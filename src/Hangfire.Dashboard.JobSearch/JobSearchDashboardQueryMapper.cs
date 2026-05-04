using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hangfire.Dashboard;

namespace Hangfire.Dashboard.JobSearch;

internal static class JobSearchDashboardQueryMapper
{
    public static JobSearchQuery Map(DashboardRequest request, JobSearchOptions options)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return Map(request.GetQuery, options);
    }

    internal static JobSearchQuery Map(Func<string, string?> queryValueProvider, JobSearchOptions options)
    {
        if (queryValueProvider is null)
        {
            throw new ArgumentNullException(nameof(queryValueProvider));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var filterable = options.Definitions
            .SelectMany(definition => definition.Fields)
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
            JobType = EmptyToNull(queryValueProvider("jobType")),
            State = EmptyToNull(queryValueProvider("state")),
            SortBy = EmptyToNull(queryValueProvider("sortBy")),
            SortDescending = ParseBool(queryValueProvider("desc")),
            Page = ParsePositiveInt(queryValueProvider("page"), 1),
            PageSize = ParsePositiveInt(queryValueProvider("pageSize"), 20),
            Filters = filters
        };
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
