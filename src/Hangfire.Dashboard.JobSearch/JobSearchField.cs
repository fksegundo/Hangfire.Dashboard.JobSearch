namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchField
{
    public string Name { get; set; } = string.Empty;

    public int ArgumentIndex { get; set; }

    public string JsonPath { get; set; } = string.Empty;

    public bool DisplayInList { get; set; }

    public bool Filterable { get; set; }

    public bool Sortable { get; set; }
}
