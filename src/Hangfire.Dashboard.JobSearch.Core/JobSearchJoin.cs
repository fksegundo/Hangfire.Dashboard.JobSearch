using System;

namespace Hangfire.Dashboard.JobSearch;

/// <summary>
/// Declares a parent-child relationship between two job definitions.
/// When a parent job carries a value for <see cref="KeyField"/>, the dashboard
/// looks up child jobs of the configured type that carry the same value in the
/// same field and renders them as expandable sub-rows under the parent.
/// </summary>
public sealed class JobSearchJoin
{
    /// <summary>Field name present on both parent and child jobs (e.g. "ReportId").</summary>
    public string KeyField { get; set; } = string.Empty;

    public Type ParentJobType { get; set; } = null!;

    public string ParentMethodName { get; set; } = string.Empty;

    public Type ChildJobType { get; set; } = null!;

    public string ChildMethodName { get; set; } = string.Empty;

    /// <summary>
    /// Optional label rendered before the children count (e.g. "partitions").
    /// Defaults to empty (renders just "N children").
    /// </summary>
    public string ChildLabel { get; set; } = string.Empty;

    /// <summary>
    /// Optional resolver for the parent's join value. Receives the parent's indexed
    /// result and returns the value to use when searching for children.
    /// <para>
    /// Defaults to looking up <c>parent.Fields[KeyField]</c>. Override when the parent
    /// worker defaults the correlation ID at runtime (after enqueue/index time) — e.g.
    /// a dispatch that falls back to its own JobId when the caller passed an empty key.
    /// </para>
    /// </summary>
    public Func<JobSearchResult, string?>? ParentValueResolver { get; set; }

    public bool MatchesParent(string jobType, string methodName)
    {
        return string.Equals(jobType, ParentJobType?.FullName ?? ParentJobType?.Name, StringComparison.Ordinal)
            && string.Equals(methodName, ParentMethodName, StringComparison.Ordinal);
    }

    public string ChildJobTypeName => ChildJobType?.FullName ?? ChildJobType?.Name ?? string.Empty;

    internal string? ResolveParentValue(JobSearchResult parent)
    {
        if (ParentValueResolver is not null)
        {
            return ParentValueResolver(parent);
        }

        if (parent.Fields.TryGetValue(KeyField, out var value))
        {
            return value;
        }

        return null;
    }
}
