using System;
using System.Text.RegularExpressions;

namespace Hangfire.Dashboard.JobSearch;

public sealed class SqlServerJobSearchOptions
{
    private static readonly Regex SafeIdentifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public string SchemaName { get; set; } = "HangFire";

    public string TablePrefix { get; set; } = "JobSearch";

    public bool PrepareSchemaIfNecessary { get; set; } = true;

    public int CommandTimeoutSeconds { get; set; } = 30;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SchemaName) || !SafeIdentifier.IsMatch(SchemaName))
        {
            throw new ArgumentException("SchemaName must be a valid SQL identifier.", nameof(SchemaName));
        }

        if (string.IsNullOrWhiteSpace(TablePrefix) || !SafeIdentifier.IsMatch(TablePrefix))
        {
            throw new ArgumentException("TablePrefix must be a valid SQL identifier.", nameof(TablePrefix));
        }
    }
}
