using System;

namespace Hangfire.Dashboard.JobSearch;

/// <summary>
/// Marks a property on a job argument DTO to be automatically indexed by JobSearch.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class JobSearchFieldAttribute : Attribute
{
    public JobSearchFieldAttribute(string? name = null)
    {
        Name = name;
    }

    public string? Name { get; }

    public bool DisplayInList { get; set; } = true;

    public bool Filterable { get; set; } = true;

    public bool Sortable { get; set; }
}
