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

    /// <summary>
    /// Children resolved via configured <see cref="JobSearchJoin"/>s.
    /// Null when no join was configured. May be empty when a join applies
    /// but no matching child jobs were found (diagnostic signal).
    /// Populated by the dashboard pipeline; not part of stored metadata.
    /// </summary>
    public IReadOnlyList<JobSearchResult>? Children { get; set; }

    /// <summary>
    /// Optional label describing the children relationship (e.g. "partitions").
    /// Set by the dashboard pipeline together with <see cref="Children"/>.
    /// </summary>
    public string? ChildrenLabel { get; set; }

    /// <summary>
    /// Name of the field used as the join key (set by the dashboard pipeline, diagnostic).
    /// </summary>
    public string? ChildrenLookupField { get; set; }

    /// <summary>
    /// Value that was actually used to search for children (set by the dashboard pipeline, diagnostic).
    /// </summary>
    public string? ChildrenLookupValue { get; set; }
}
