using System;
using System.Text.RegularExpressions;

namespace Hangfire.Dashboard.JobSearch;

public sealed class PostgreSqlJobSearchOptions
{
    private static readonly Regex SafeIdentifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public string SchemaName { get; set; } = "hangfire";

    public string TablePrefix { get; set; } = "job_search";

    public bool PrepareSchemaIfNecessary { get; set; } = true;

    public int CommandTimeoutSeconds { get; set; } = 30;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SchemaName) || !SafeIdentifier.IsMatch(SchemaName))
        {
            throw new ArgumentException("SchemaName must be a valid PostgreSQL identifier.", nameof(SchemaName));
        }

        if (string.IsNullOrWhiteSpace(TablePrefix) || !SafeIdentifier.IsMatch(TablePrefix))
        {
            throw new ArgumentException("TablePrefix must be a valid PostgreSQL identifier.", nameof(TablePrefix));
        }
    }
}
