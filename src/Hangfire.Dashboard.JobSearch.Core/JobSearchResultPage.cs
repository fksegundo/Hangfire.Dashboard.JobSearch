using System.Collections.Generic;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchResultPage
{
    public IReadOnlyList<JobSearchResult> Items { get; set; } = new List<JobSearchResult>();

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }
}
