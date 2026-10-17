using System.Collections.Generic;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchQuery
{
    public string? JobId { get; set; }

    public string? State { get; set; }

    public string? JobType { get; set; }

    public Dictionary<string, string> Filters { get; set; } = new Dictionary<string, string>();

    public string? SortBy { get; set; }

    public bool SortDescending { get; set; }

    public List<JobSearchSort> Sorts { get; set; } = new List<JobSearchSort>();

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
