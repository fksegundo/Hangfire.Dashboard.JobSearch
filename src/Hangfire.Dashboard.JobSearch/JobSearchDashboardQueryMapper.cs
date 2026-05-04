using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hangfire.Dashboard;

namespace Hangfire.Dashboard.JobSearch;

internal static class JobSearchDashboardQueryMapper
{
    public static JobSearchQuery Map(
        DashboardRequest request,
        JobSearchDefinition? selectedDefinition)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return Map(request.GetQuery, selectedDefinition);
    }

    internal static JobSearchQuery Map(
        Func<string, string?> queryValueProvider,
        JobSearchDefinition? selectedDefinition)
    {
        if (queryValueProvider is null)
        {
            throw new ArgumentNullException(nameof(queryValueProvider));
        }

        var filterable = (selectedDefinition?.Fields ?? Enumerable.Empty<JobSearchField>())
            .Where(field => field.Filterable)
            .Select(field => field.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fieldName in filterable)
        {
            var value = queryValueProvider($"f_{fieldName}");
            if (!string.IsNullOrWhiteSpace(value))
            {
                filters[fieldName] = value!;
            }
        }

        return new JobSearchQuery
        {
            JobId = EmptyToNull(queryValueProvider("jobId")),
            JobType = selectedDefinition is null
                ? EmptyToNull(queryValueProvider("jobType"))
                : (selectedDefinition.JobType.FullName ?? selectedDefinition.JobType.Name),
            State = EmptyToNull(queryValueProvider("state")) ?? "Processing",
            SortBy = EmptyToNull(queryValueProvider("sortBy")),
            SortDescending = ParseBool(queryValueProvider("desc")),
            Sorts = ParseSorts(queryValueProvider("sort")),
            Page = ParsePositiveInt(queryValueProvider("page"), 1),
            PageSize = ParsePositiveInt(queryValueProvider("pageSize"), 20),
            Filters = filters
        };
    }

    private static List<JobSearchSort> ParseSorts(string? value)
    {
        var sorts = new List<JobSearchSort>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return sorts;
        }

        var raw = value!;
        foreach (var segment in raw.Split(','))
        {
            var part = segment.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            var split = part.Split(':');
            var field = split[0].Trim();
            if (field.Length == 0)
            {
                continue;
            }

            var direction = split.Length > 1 ? split[1].Trim() : "asc";
            sorts.Add(new JobSearchSort
            {
                Field = field,
                Descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase)
            });
        }

        return sorts;
    }

    private static int ParsePositiveInt(string? value, int defaultValue)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return defaultValue;
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
