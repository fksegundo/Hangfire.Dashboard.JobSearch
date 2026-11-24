using System;
using System.Collections.Generic;
using System.Linq;

namespace Hangfire.Dashboard.JobSearch;

internal static class JobSearchDashboardDefinitionSelector
{
    public static JobSearchDefinition? Resolve(
        IReadOnlyList<JobSearchDefinition> definitions,
        string? requestedKey)
    {
        if (definitions is null || definitions.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(requestedKey))
        {
            var matched = definitions.FirstOrDefault(definition =>
                string.Equals(ToKey(definition), requestedKey, StringComparison.Ordinal));
            if (matched is not null)
            {
                return matched;
            }
        }

        return definitions[0];
    }

    public static string ToKey(JobSearchDefinition definition)
    {
        var typeName = definition.JobType.FullName ?? definition.JobType.Name ?? string.Empty;
        return $"{typeName}|{definition.MethodName}";
    }

    public static string ToLabel(JobSearchDefinition definition)
    {
        var typeName = definition.JobType.Name ?? definition.JobType.FullName ?? string.Empty;
        return $"{typeName}.{definition.MethodName}";
    }
}
