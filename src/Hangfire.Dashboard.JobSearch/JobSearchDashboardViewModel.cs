using System;
using System.Collections.Generic;
using System.Linq;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardViewModel
{
    public string PathBase { get; set; } = string.Empty;
    public JobSearchQuery Query { get; set; } = new JobSearchQuery();
    public JobSearchResultPage Result { get; set; } = new JobSearchResultPage();
    public IReadOnlyList<string> FilterableFields { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DisplayFields { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> JobTypes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> States { get; set; } = Array.Empty<string>();

    public static JobSearchDashboardViewModel From(
        string pathBase,
        JobSearchQuery query,
        JobSearchOptions options,
        JobSearchResultPage result)
    {
        var fields = options.Definitions.SelectMany(definition => definition.Fields).ToList();
        var types = options.Definitions
            .Select(definition => definition.JobType.FullName ?? definition.JobType.Name ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

        return new JobSearchDashboardViewModel
        {
            PathBase = pathBase ?? string.Empty,
            Query = query,
            Result = result,
            FilterableFields = fields
                .Where(field => field.Filterable)
                .Select(field => field.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            DisplayFields = fields
                .Where(field => field.DisplayInList)
                .Select(field => field.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            JobTypes = types,
            States = new[]
            {
                "Enqueued", "Processing", "Succeeded", "Failed", "Deleted", "Scheduled"
            },
        };
    }
}
