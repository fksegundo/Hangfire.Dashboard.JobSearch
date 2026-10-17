using System;
using System.Linq;

namespace Hangfire.Dashboard.JobSearch;

internal static class JobSearchOptionsValidator
{
    public static void Validate(JobSearchOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        foreach (var definition in options.Definitions)
        {
            if (definition.JobType is null)
            {
                throw new InvalidOperationException("JobType is required for every job definition.");
            }

            if (string.IsNullOrWhiteSpace(definition.MethodName))
            {
                throw new InvalidOperationException("MethodName is required for every job definition.");
            }

            if (definition.Fields.Count == 0)
            {
                continue;
            }

            if (definition.Fields.Any(field => string.IsNullOrWhiteSpace(field.Name)))
            {
                throw new InvalidOperationException("Field name is required for every job field.");
            }

            if (definition.Fields.Any(field => string.IsNullOrWhiteSpace(field.JsonPath)))
            {
                throw new InvalidOperationException("JsonPath is required for every job field.");
            }
        }
    }
}
