namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchSort
{
    public string Field { get; set; } = string.Empty;
    public bool Descending { get; set; }
}
