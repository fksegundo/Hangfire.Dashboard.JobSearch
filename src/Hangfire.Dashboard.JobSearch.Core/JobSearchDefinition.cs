using System;
using System.Collections.Generic;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchDefinition
{
    public Type JobType { get; set; } = default!;

    public string MethodName { get; set; } = string.Empty;

    public List<JobSearchField> Fields { get; } = new List<JobSearchField>();

    public bool Matches(Job job)
    {
        if (job is null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        return job.Type == JobType
            && string.Equals(job.Method.Name, MethodName, StringComparison.Ordinal);
    }
}
