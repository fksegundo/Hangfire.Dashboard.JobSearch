using System;
using System.Collections.Generic;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchResult
{
    public string JobId { get; set; } = string.Empty;

    public string JobType { get; set; } = string.Empty;

    public string MethodName { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Dictionary<string, string?> Fields { get; set; } = new Dictionary<string, string?>();
}
