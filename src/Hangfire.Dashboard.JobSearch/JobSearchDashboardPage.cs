using System;
using System.Collections.Generic;
using System.Linq;
using Hangfire.Dashboard;
using Hangfire.Dashboard.Pages;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardPage : RazorPage
{
    private const int MaxChildrenPerJoin = 1000;

    private readonly IServiceProvider _serviceProvider;

    public JobSearchDashboardPage(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public override void Execute()
    {
        Layout = new LayoutPage("Job Search");

        var storage = (IJobSearchStorage?)_serviceProvider.GetService(typeof(IJobSearchStorage));
        if (storage is null)
        {
            WriteLiteral("<div class=\"alert alert-danger\">IJobSearchStorage is not registered.</div>");
            return;
        }

        var options = (JobSearchOptions?)_serviceProvider.GetService(typeof(JobSearchOptions));
        if (options is null)
        {
            WriteLiteral("<div class=\"alert alert-danger\">JobSearchOptions is not registered.</div>");
            return;
        }

        var selectedDefinition = JobSearchDashboardDefinitionSelector.Resolve(
            options.Definitions,
            Query("definition"));

        var query = JobSearchDashboardQueryMapper.Map(Query, selectedDefinition);
        var dashboardBasePath = GetDashboardBasePath();

        var searchTask = storage.SearchAsync(query);
        var countTask = storage.CountByStateAsync(query);
        System.Threading.Tasks.Task.WhenAll(searchTask, countTask).GetAwaiter().GetResult();

        var result = searchTask.Result;
        var stateCounts = countTask.Result;

        AttachChildren(storage, options, result);

        var model = JobSearchDashboardViewModel.From(
            pathBase: dashboardBasePath,
            query: query,
            options: options,
            result: result,
            selectedDefinition: selectedDefinition,
            stateCounts: stateCounts);

        WriteLiteral(JobSearchDashboardRenderer.RenderBody(model));
    }

    private static void AttachChildren(
        IJobSearchStorage storage,
        JobSearchOptions options,
        JobSearchResultPage result)
    {
        if (options.Joins.Count == 0 || result.Items.Count == 0)
            return;

        if (storage is RedisJobSearchStorage redisStorage)
        {
            AttachChildrenBulk(redisStorage, options, result);
            return;
        }

        AttachChildrenSequential(storage, options, result);
    }

    private static void AttachChildrenBulk(
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
                continue;

            var resolvedKeys = parentItems
                .Select(item => new { Parent = item, Key = join.ResolveParentValue(item) })
                .ToList();

            var distinctKeys = resolvedKeys
                .Select(x => x.Key)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.Ordinal)
                .Select(v => v!)
                .ToList();

            var childrenByKey = storage.SearchChildrenBulkAsync(
                join.ChildJobTypeName,
                join.ChildMethodName,
                join.KeyField,
                distinctKeys,
                MaxChildrenPerJoin).GetAwaiter().GetResult();

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

    private static void AttachChildrenSequential(
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
                continue;

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
                    continue;

                var childQuery = new JobSearchQuery
                {
                    JobType = join.ChildJobTypeName,
                    Page = 1,
                    PageSize = MaxChildrenPerJoin,
                    Filters = new Dictionary<string, string> { [join.KeyField] = key },
                };

                var childPage = storage.SearchAsync(childQuery).GetAwaiter().GetResult();
                if (childPage.Items.Count > 0)
                {
                    childrenByKey[key] = childPage.Items
                        .Where(c => string.Equals(c.MethodName, join.ChildMethodName, StringComparison.Ordinal))
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

    private string GetDashboardBasePath()
    {
        var pathBase = Context?.Request?.PathBase;
        if (!string.IsNullOrWhiteSpace(pathBase))
        {
            return pathBase!;
        }

        var requestPath = RequestPath ?? string.Empty;
        const string suffix = "/job-search";
        if (requestPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return requestPath.Substring(0, requestPath.Length - suffix.Length);
        }

        return AppPath ?? string.Empty;
    }
}
