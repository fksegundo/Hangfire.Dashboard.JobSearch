using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hangfire.Dashboard;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardRazorDispatcher : IDashboardDispatcher
{
    private const int MaxChildrenPerJoin = 1000;
    private const int MaxExportItems = 1000;

    private static readonly AsyncLocal<string?> CurrentBodyHtml = new AsyncLocal<string?>();
    private static readonly AsyncLocal<string?> CurrentPageTitle = new AsyncLocal<string?>();

    private readonly IServiceProvider _serviceProvider;
    private readonly IDashboardDispatcher _innerDispatcher;

    public JobSearchDashboardRazorDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _innerDispatcher = CreateInnerDispatcher();
    }

    public async Task Dispatch(DashboardContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        string bodyHtml;
        var pageTitle = "Job Search";

        var storage = (IJobSearchStorage?)_serviceProvider.GetService(typeof(IJobSearchStorage));
        var options = (JobSearchOptions?)_serviceProvider.GetService(typeof(JobSearchOptions));

        if (storage is null)
        {
            bodyHtml = "<div class=\"alert alert-danger\">IJobSearchStorage is not registered.</div>";
        }
        else if (options is null)
        {
            bodyHtml = "<div class=\"alert alert-danger\">JobSearchOptions is not registered.</div>";
        }
        else
        {
            pageTitle = string.IsNullOrWhiteSpace(options.MenuTitle) ? "Job Search" : options.MenuTitle;
            var routePath = GlobalConfigurationExtensions.NormalizeRoutePath(options.RoutePath);

            var selectedDefinition = JobSearchDashboardDefinitionSelector.Resolve(
                options.Definitions,
                context.Request.GetQuery("definition"));

            var query = JobSearchDashboardQueryMapper.Map(context.Request, selectedDefinition);
            var exportFormat = context.Request.GetQuery("export");
            if (!string.IsNullOrWhiteSpace(exportFormat))
            {
                await HandleExportAsync(context, storage, query, selectedDefinition, exportFormat!).ConfigureAwait(false);
                return;
            }

            var dashboardBasePath = GetDashboardBasePath(context, routePath);

            var searchTask = storage.SearchAsync(query);
            var countTask = storage.CountByStateAsync(query);
            await Task.WhenAll(searchTask, countTask).ConfigureAwait(false);

            var result = await searchTask.ConfigureAwait(false);
            var stateCounts = await countTask.ConfigureAwait(false);

            await AttachChildrenAsync(storage, options, result).ConfigureAwait(false);
            EnrichFromHangfireStorage(context, result);

            var model = JobSearchDashboardViewModel.From(
                pathBase: dashboardBasePath,
                query: query,
                options: options,
                result: result,
                selectedDefinition: selectedDefinition,
                stateCounts: stateCounts,
                isReadOnly: context.IsReadOnly);

            bodyHtml = JobSearchDashboardRenderer.RenderBody(model);
        }

        var previousBody = CurrentBodyHtml.Value;
        var previousTitle = CurrentPageTitle.Value;
        CurrentBodyHtml.Value = bodyHtml;
        CurrentPageTitle.Value = pageTitle;
        try
        {
            await _innerDispatcher.Dispatch(context).ConfigureAwait(false);
        }
        finally
        {
            CurrentBodyHtml.Value = previousBody;
            CurrentPageTitle.Value = previousTitle;
        }
    }

    private static async Task HandleExportAsync(
        DashboardContext context,
        IJobSearchStorage storage,
        JobSearchQuery query,
        JobSearchDefinition? selectedDefinition,
        string exportFormat)
    {
        query.Page = 1;
        query.PageSize = MaxExportItems;
        var page = await storage.SearchAsync(query).ConfigureAwait(false);
        var displayFields = (selectedDefinition?.Fields ?? new List<JobSearchField>())
            .Where(f => f.DisplayInList)
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (string.Equals(exportFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.SetExpire(DateTimeOffset.UtcNow.AddSeconds(-1));
            var json = BuildJsonExport(page.Items);
            await context.Response.WriteAsync(json).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = "text/csv; charset=utf-8";
        context.Response.SetExpire(DateTimeOffset.UtcNow.AddSeconds(-1));
        var csv = BuildCsvExport(page.Items, displayFields);
        await context.Response.WriteAsync(csv).ConfigureAwait(false);
    }

    internal static string BuildCsvExport(IReadOnlyList<JobSearchResult> items, IReadOnlyList<string> displayFields)
    {
        var sb = new StringBuilder();
        sb.Append("JobId,State,JobType,MethodName,CreatedAt");
        foreach (var field in displayFields)
        {
            sb.Append(',').Append(CsvEscape(field));
        }
        sb.AppendLine();

        foreach (var item in items)
        {
            sb.Append(CsvEscape(item.JobId)).Append(',')
                .Append(CsvEscape(item.State)).Append(',')
                .Append(CsvEscape(item.JobType)).Append(',')
                .Append(CsvEscape(item.MethodName)).Append(',')
                .Append(CsvEscape(item.CreatedAt.ToString("O", CultureInfo.InvariantCulture)));

            foreach (var field in displayFields)
            {
                item.Fields.TryGetValue(field, out var val);
                sb.Append(',').Append(CsvEscape(val));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    internal static string BuildJsonExport(IReadOnlyList<JobSearchResult> items)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var item = items[i];
            sb.Append('{');
            sb.Append("\"jobId\":").Append(JsonString(item.JobId)).Append(',');
            sb.Append("\"state\":").Append(JsonString(item.State)).Append(',');
            sb.Append("\"jobType\":").Append(JsonString(item.JobType)).Append(',');
            sb.Append("\"methodName\":").Append(JsonString(item.MethodName)).Append(',');
            sb.Append("\"createdAt\":").Append(JsonString(item.CreatedAt.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            sb.Append("\"fields\":{");
            var firstField = true;
            foreach (var pair in item.Fields)
            {
                if (!firstField) sb.Append(',');
                firstField = false;
                sb.Append(JsonString(pair.Key)).Append(':').Append(pair.Value is null ? "null" : JsonString(pair.Value));
            }
            sb.Append("}}");
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var v = value!;
        if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
        {
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
        return v;
    }

    private static string JsonString(string? value)
    {
        if (value is null) return "null";
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < ' ')
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static void EnrichFromHangfireStorage(DashboardContext context, JobSearchResultPage result)
    {
        if (context.Storage is null || result.Items.Count == 0)
        {
            return;
        }

        try
        {
            var monitoring = context.Storage.GetMonitoringApi();
            if (monitoring is null)
            {
                return;
            }

            foreach (var item in result.Items)
            {
                if (!string.Equals(item.State, "Failed", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(item.State, "Succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var details = monitoring.JobDetails(item.JobId);
                var latest = details?.History?.FirstOrDefault();
                if (latest?.Data is null)
                {
                    continue;
                }

                if (string.Equals(item.State, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    if (latest.Data.TryGetValue("ExceptionMessage", out var msg) && !string.IsNullOrWhiteSpace(msg))
                    {
                        item.LastError = msg;
                    }
                }
                else if (string.Equals(item.State, "Succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    if (latest.Data.TryGetValue("PerformanceDuration", out var durStr)
                        && long.TryParse(durStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
                        && ms >= 0)
                    {
                        item.Duration = TimeSpan.FromMilliseconds(ms);
                    }
                }
            }
        }
        catch
        {
            // Best-effort enrichment; never fail dashboard rendering.
        }
    }

    private static IDashboardDispatcher CreateInnerDispatcher()
    {
        var dispatcherType = typeof(DashboardContext).Assembly.GetType("Hangfire.Dashboard.RazorPageDispatcher");
        if (dispatcherType is null)
        {
            throw new InvalidOperationException("Unable to find Hangfire.Dashboard.RazorPageDispatcher.");
        }

        var pageFactory = new Func<Match, RazorPage>(_ => new JobSearchDashboardPage(
            CurrentBodyHtml.Value ?? string.Empty,
            CurrentPageTitle.Value ?? "Job Search"));
        var instance = Activator.CreateInstance(
            dispatcherType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { pageFactory },
            culture: null);

        if (instance is not IDashboardDispatcher dashboardDispatcher)
        {
            throw new InvalidOperationException("Unable to create dashboard dispatcher for Job Search page.");
        }

        return dashboardDispatcher;
    }

    private static async Task AttachChildrenAsync(
        IJobSearchStorage storage,
        JobSearchOptions options,
        JobSearchResultPage result)
    {
        if (options.Joins.Count == 0 || result.Items.Count == 0)
        {
            return;
        }

        if (storage is IJobSearchBulkChildStorage bulkStorage)
        {
            await AttachChildrenBulkAsync(bulkStorage, options, result).ConfigureAwait(false);
            return;
        }

        await AttachChildrenSequentialAsync(storage, options, result).ConfigureAwait(false);
    }

    private static async Task AttachChildrenBulkAsync(
        IJobSearchBulkChildStorage storage,
        JobSearchOptions options,
        JobSearchResultPage result)
    {
        foreach (var join in options.Joins)
        {
            var parentItems = result.Items
                .Where(item => join.MatchesParent(item.JobType, item.MethodName))
                .ToList();

            if (parentItems.Count == 0)
            {
                continue;
            }

            var resolvedKeys = parentItems
                .Select(item => new { Parent = item, Key = join.ResolveParentValue(item) })
                .ToList();

            var distinctKeys = resolvedKeys
                .Select(x => x.Key)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.Ordinal)
                .Select(v => v!)
                .ToList();

            var childrenByKey = await storage.SearchChildrenBulkAsync(
                join.ChildJobTypeName,
                join.ChildMethodName,
                join.KeyField,
                distinctKeys,
                MaxChildrenPerJoin).ConfigureAwait(false);

            foreach (var entry in resolvedKeys)
            {
                var parent = entry.Parent;
                var parentKey = entry.Key;

                if (parent.Children is null)
                {
                    parent.Children = new List<JobSearchResult>();
                    parent.ChildrenLabel = join.ChildLabel;
                    parent.ChildrenLookupField = join.KeyField;
                    parent.ChildrenLookupValue = parentKey;
                }

                if (!string.IsNullOrEmpty(parentKey)
                    && childrenByKey.TryGetValue(parentKey!, out var children)
                    && children.Count > 0)
                {
                    parent.Children = parent.Children.Concat(children).ToList();
                }
            }
        }
    }

    private static async Task AttachChildrenSequentialAsync(
        IJobSearchStorage storage,
        JobSearchOptions options,
        JobSearchResultPage result)
    {
        foreach (var join in options.Joins)
        {
            var parentItems = result.Items
                .Where(item => join.MatchesParent(item.JobType, item.MethodName))
                .ToList();

            if (parentItems.Count == 0)
            {
                continue;
            }

            var resolvedKeys = parentItems
                .Select(item => new { Parent = item, Key = join.ResolveParentValue(item) })
                .ToList();

            var distinctKeys = resolvedKeys
                .Select(x => x.Key)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var childrenByKey = new Dictionary<string, List<JobSearchResult>>(StringComparer.Ordinal);
            foreach (var key in distinctKeys)
            {
                if (key is null)
                {
                    continue;
                }

                var childQuery = new JobSearchQuery
                {
                    JobType = join.ChildJobTypeName,
                    MethodName = string.IsNullOrEmpty(join.ChildMethodName) ? null : join.ChildMethodName,
                    Page = 1,
                    PageSize = MaxChildrenPerJoin,
                    Filters = new Dictionary<string, string> { [join.KeyField] = key },
                };

                var childPage = await storage.SearchAsync(childQuery).ConfigureAwait(false);
                if (childPage.Items.Count > 0)
                {
                    childrenByKey[key] = childPage.Items
                        .Where(c => string.IsNullOrEmpty(join.ChildMethodName)
                            || string.Equals(c.MethodName, join.ChildMethodName, StringComparison.Ordinal))
                        .ToList();
                }
            }

            foreach (var entry in resolvedKeys)
            {
                var parent = entry.Parent;
                var parentKey = entry.Key;

                if (parent.Children is null)
                {
                    parent.Children = new List<JobSearchResult>();
                    parent.ChildrenLabel = join.ChildLabel;
                    parent.ChildrenLookupField = join.KeyField;
                    parent.ChildrenLookupValue = parentKey;
                }

                if (!string.IsNullOrEmpty(parentKey)
                    && childrenByKey.TryGetValue(parentKey!, out var children)
                    && children.Count > 0)
                {
                    parent.Children = parent.Children.Concat(children).ToList();
                }
            }
        }
    }

    private static string GetDashboardBasePath(DashboardContext context, string routePath)
    {
        var pathBase = context.Request?.PathBase;
        if (!string.IsNullOrWhiteSpace(pathBase))
        {
            return pathBase!;
        }

        var requestPath = context.Request?.Path ?? string.Empty;
        if (requestPath.EndsWith(routePath, StringComparison.OrdinalIgnoreCase))
        {
            return requestPath.Substring(0, requestPath.Length - routePath.Length);
        }

        return context.Options?.AppPath ?? string.Empty;
    }
}
