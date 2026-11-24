using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hangfire.Dashboard;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardRazorDispatcher : IDashboardDispatcher
{
    private const int MaxChildrenPerJoin = 1000;

    private static readonly AsyncLocal<string?> CurrentBodyHtml = new AsyncLocal<string?>();

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
            var selectedDefinition = JobSearchDashboardDefinitionSelector.Resolve(
                options.Definitions,
                context.Request.GetQuery("definition"));

            var query = JobSearchDashboardQueryMapper.Map(context.Request, selectedDefinition);
            var dashboardBasePath = GetDashboardBasePath(context);

            var searchTask = storage.SearchAsync(query);
            var countTask = storage.CountByStateAsync(query);
            await Task.WhenAll(searchTask, countTask).ConfigureAwait(false);

            var result = await searchTask.ConfigureAwait(false);
            var stateCounts = await countTask.ConfigureAwait(false);

            await AttachChildrenAsync(storage, options, result).ConfigureAwait(false);

            var model = JobSearchDashboardViewModel.From(
                pathBase: dashboardBasePath,
                query: query,
                options: options,
                result: result,
                selectedDefinition: selectedDefinition,
                stateCounts: stateCounts);

            bodyHtml = JobSearchDashboardRenderer.RenderBody(model);
        }

        var previous = CurrentBodyHtml.Value;
        CurrentBodyHtml.Value = bodyHtml;
        try
        {
            await _innerDispatcher.Dispatch(context).ConfigureAwait(false);
        }
        finally
        {
            CurrentBodyHtml.Value = previous;
        }
    }

    private static IDashboardDispatcher CreateInnerDispatcher()
    {
        var dispatcherType = typeof(DashboardContext).Assembly.GetType("Hangfire.Dashboard.RazorPageDispatcher");
        if (dispatcherType is null)
        {
            throw new InvalidOperationException("Unable to find Hangfire.Dashboard.RazorPageDispatcher.");
        }

        var pageFactory = new Func<Match, RazorPage>(_ => new JobSearchDashboardPage(CurrentBodyHtml.Value ?? string.Empty));
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

        if (storage is RedisJobSearchStorage redisStorage)
        {
            await AttachChildrenBulkAsync(redisStorage, options, result).ConfigureAwait(false);
            return;
        }

        await AttachChildrenSequentialAsync(storage, options, result).ConfigureAwait(false);
    }

    private static async Task AttachChildrenBulkAsync(
        RedisJobSearchStorage storage,
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

    private static string GetDashboardBasePath(DashboardContext context)
    {
        var pathBase = context.Request?.PathBase;
        if (!string.IsNullOrWhiteSpace(pathBase))
        {
            return pathBase!;
        }

        var requestPath = context.Request?.Path ?? string.Empty;
        const string suffix = "/job-search";
        if (requestPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return requestPath.Substring(0, requestPath.Length - suffix.Length);
        }

        return context.Options?.AppPath ?? string.Empty;
    }
}
