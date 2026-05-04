using System;
using System.Collections.Generic;
using System.Linq;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardViewModel
{
    public sealed class DefinitionOption
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public sealed class StateOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public string PathBase { get; set; } = string.Empty;
    public JobSearchQuery Query { get; set; } = new JobSearchQuery();
    public JobSearchResultPage Result { get; set; } = new JobSearchResultPage();
    public string SelectedDefinitionKey { get; set; } = string.Empty;
    public IReadOnlyList<DefinitionOption> Definitions { get; set; } = Array.Empty<DefinitionOption>();
    public IReadOnlyList<string> FilterableFields { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DisplayFields { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> JobTypes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<StateOption> States { get; set; } = Array.Empty<StateOption>();

    public static JobSearchDashboardViewModel From(
        string pathBase,
        JobSearchQuery query,
        JobSearchOptions options,
        JobSearchResultPage result,
        JobSearchDefinition? selectedDefinition,
        IReadOnlyDictionary<string, int> stateCounts)
    {
        var fields = (selectedDefinition?.Fields ?? new List<JobSearchField>()).ToList();
        var types = options.Definitions
            .Select(definition => definition.JobType.FullName ?? definition.JobType.Name ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var filterableFields = fields
            .Where(field => field.Filterable)
            .Select(field => field.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new JobSearchDashboardViewModel
        {
            PathBase = pathBase ?? string.Empty,
            Query = query,
            Result = result,
            SelectedDefinitionKey = selectedDefinition is null
                ? string.Empty
                : JobSearchDashboardDefinitionSelector.ToKey(selectedDefinition),
            Definitions = options.Definitions
                .Select(definition => new DefinitionOption
                {
                    Key = JobSearchDashboardDefinitionSelector.ToKey(definition),
                    Label = JobSearchDashboardDefinitionSelector.ToLabel(definition)
                })
                .ToList(),
            FilterableFields = filterableFields,
            DisplayFields = fields
                .Where(field => field.DisplayInList)
                .Select(field => field.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            JobTypes = types,
            States = BuildStateOptions(stateCounts),
        };
    }

    private static IReadOnlyList<StateOption> BuildStateOptions(IReadOnlyDictionary<string, int> stateCounts)
    {
        var order = new[] { "Processing", "Enqueued", "Succeeded", "Failed", "Scheduled", "Deleted" };
        var options = new List<StateOption>();

        foreach (var state in order)
        {
            stateCounts.TryGetValue(state, out var count);
            options.Add(new StateOption
            {
                Value = state,
                Label = $"{state} ({count})"
            });
        }

        return options;
    }

}
