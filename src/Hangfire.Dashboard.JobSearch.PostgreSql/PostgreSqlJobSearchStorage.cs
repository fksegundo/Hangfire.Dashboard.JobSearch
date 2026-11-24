using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Hangfire.Dashboard.JobSearch;

public sealed class PostgreSqlJobSearchStorage : IJobSearchStorage, IJobSearchBulkChildStorage
{
    private readonly string _connectionString;
    private readonly PostgreSqlJobSearchOptions _options;
    private readonly string _jobsTable;
    private readonly string _fieldsTable;
    private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
    private volatile bool _initialized;

    public PostgreSqlJobSearchStorage(string connectionString, PostgreSqlJobSearchOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _options = options ?? new PostgreSqlJobSearchOptions();
        _options.Validate();

        _jobsTable = $"\"{_options.SchemaName}\".\"{_options.TablePrefix}_jobs\"";
        _fieldsTable = $"\"{_options.SchemaName}\".\"{_options.TablePrefix}_job_fields\"";
    }

    public async Task SaveMetadataAsync(string jobId, JobSearchMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var tx = conn.BeginTransaction();

        var upsertSql = $@"
INSERT INTO {_jobsTable} (job_id, job_type, method_name, state, created_at_utc)
VALUES (@job_id, @job_type, @method_name, @state, @created_at_utc)
ON CONFLICT (job_id) DO UPDATE
SET job_type = EXCLUDED.job_type,
    method_name = EXCLUDED.method_name,
    state = EXCLUDED.state,
    created_at_utc = EXCLUDED.created_at_utc;
DELETE FROM {_fieldsTable} WHERE job_id = @job_id;";

        using (var cmd = CreateCommand(conn, tx, upsertSql))
        {
            cmd.Parameters.AddWithValue("job_id", jobId);
            cmd.Parameters.AddWithValue("job_type", metadata.JobType ?? string.Empty);
            cmd.Parameters.AddWithValue("method_name", metadata.MethodName ?? string.Empty);
            cmd.Parameters.AddWithValue("state", metadata.State ?? string.Empty);
            cmd.Parameters.AddWithValue("created_at_utc", metadata.CreatedAt.UtcDateTime);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (metadata.Fields.Count > 0)
        {
            var insertFieldSql = $"INSERT INTO {_fieldsTable} (job_id, field_name, field_value) VALUES (@job_id, @field_name, @field_value);";
            foreach (var pair in metadata.Fields)
            {
                using var fCmd = CreateCommand(conn, tx, insertFieldSql);
                fCmd.Parameters.AddWithValue("job_id", jobId);
                fCmd.Parameters.AddWithValue("field_name", pair.Key);
                fCmd.Parameters.AddWithValue("field_value", (object?)pair.Value ?? DBNull.Value);
                await fCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateStateAsync(string jobId, string state, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));
        if (string.IsNullOrWhiteSpace(state)) throw new ArgumentException("State is required.", nameof(state));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = CreateCommand(conn, null, $"UPDATE {_jobsTable} SET state = @state WHERE job_id = @job_id;");
        cmd.Parameters.AddWithValue("job_id", jobId);
        cmd.Parameters.AddWithValue("state", state);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteMetadataAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = CreateCommand(conn, null, $"DELETE FROM {_jobsTable} WHERE job_id = @job_id;");
        cmd.Parameters.AddWithValue("job_id", jobId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobSearchResultPage> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var parameters = new List<(string Name, object Value)>();
        var whereClause = BuildWhereClause(query, includeState: true, parameters);

        int totalCount;
        using (var countCmd = CreateCommand(conn, null, $"SELECT COUNT(1) FROM {_jobsTable} j {whereClause};"))
        {
            AddParameters(countCmd, parameters);
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        if (totalCount == 0)
        {
            return new JobSearchResultPage { Items = new List<JobSearchResult>(), Page = page, PageSize = pageSize, TotalCount = 0 };
        }

        var orderBy = BuildOrderByClause(query, out var needsInMemorySort);
        var selectSql = needsInMemorySort
            ? $"SELECT j.job_id, j.job_type, j.method_name, j.state, j.created_at_utc FROM {_jobsTable} j {whereClause} ORDER BY j.created_at_utc DESC, j.job_id ASC LIMIT 5000;"
            : $"SELECT j.job_id, j.job_type, j.method_name, j.state, j.created_at_utc FROM {_jobsTable} j {whereClause} {orderBy} LIMIT @limit OFFSET @offset;";

        var rows = new List<JobSearchResult>();
        using (var selectCmd = CreateCommand(conn, null, selectSql))
        {
            AddParameters(selectCmd, parameters);
            if (!needsInMemorySort)
            {
                selectCmd.Parameters.AddWithValue("limit", pageSize);
                selectCmd.Parameters.AddWithValue("offset", (page - 1) * pageSize);
            }

            using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(ReadJobRow(reader));
            }
        }

        await LoadFieldsForResultsAsync(conn, rows, cancellationToken).ConfigureAwait(false);

        if (needsInMemorySort)
        {
            rows = ApplyInMemorySort(rows, query).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        return new JobSearchResultPage
        {
            Items = rows,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByStateAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var parameters = new List<(string Name, object Value)>();
        var whereClause = BuildWhereClause(query, includeState: false, parameters);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var cmd = CreateCommand(conn, null, $"SELECT j.state, COUNT(1) FROM {_jobsTable} j {whereClause} GROUP BY j.state;");
        AddParameters(cmd, parameters);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var state = reader.GetString(0);
            var count = Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture);
            if (count > 0)
            {
                counts[state] = count;
            }
        }

        return counts;
    }

    public async Task<Dictionary<string, List<JobSearchResult>>> SearchChildrenBulkAsync(
        string childJobType,
        string childMethodName,
        string keyField,
        IReadOnlyList<string> parentKeys,
        int maxPerKey,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, List<JobSearchResult>>(StringComparer.Ordinal);
        var distinctKeys = parentKeys.Where(k => !string.IsNullOrEmpty(k)).Distinct(StringComparer.Ordinal).ToList();
        if (distinctKeys.Count == 0)
        {
            return result;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var lowerKeys = distinctKeys.Select(k => k.ToLowerInvariant()).ToArray();

        var sb = new StringBuilder($@"
SELECT j.job_id, j.job_type, j.method_name, j.state, j.created_at_utc, f.field_value
FROM {_jobsTable} j
INNER JOIN {_fieldsTable} f ON f.job_id = j.job_id
WHERE LOWER(j.job_type) = LOWER(@child_job_type)
  AND f.field_name = @key_field
  AND LOWER(f.field_value) = ANY(@keys)");

        using var cmd = CreateCommand(conn, null, string.Empty);
        cmd.Parameters.AddWithValue("child_job_type", childJobType);
        cmd.Parameters.AddWithValue("key_field", keyField);
        cmd.Parameters.AddWithValue("keys", lowerKeys);

        if (!string.IsNullOrEmpty(childMethodName))
        {
            sb.Append(" AND j.method_name = @child_method_name");
            cmd.Parameters.AddWithValue("child_method_name", childMethodName);
        }

        sb.Append(" ORDER BY j.created_at_utc DESC;");
        cmd.CommandText = sb.ToString();

        var allChildren = new List<JobSearchResult>();
        using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var item = ReadJobRow(reader);
                var matchedKey = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                var parentKey = distinctKeys.FirstOrDefault(k => string.Equals(k, matchedKey, StringComparison.OrdinalIgnoreCase));
                if (parentKey is null) continue;

                if (!result.TryGetValue(parentKey, out var list))
                {
                    list = new List<JobSearchResult>();
                    result[parentKey] = list;
                }

                if (list.Count < maxPerKey)
                {
                    list.Add(item);
                    allChildren.Add(item);
                }
            }
        }

        await LoadFieldsForResultsAsync(conn, allChildren, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private string BuildWhereClause(JobSearchQuery query, bool includeState, List<(string Name, object Value)> parameters)
    {
        var conditions = new List<string>();
        var idx = 0;

        if (!string.IsNullOrWhiteSpace(query.JobId))
        {
            conditions.Add("j.job_id = @job_id");
            parameters.Add(("job_id", query.JobId!));
        }

        if (includeState && !string.IsNullOrWhiteSpace(query.State))
        {
            conditions.Add("LOWER(j.state) = LOWER(@state)");
            parameters.Add(("state", query.State!));
        }

        if (!string.IsNullOrWhiteSpace(query.JobType))
        {
            conditions.Add("LOWER(j.job_type) = LOWER(@job_type)");
            parameters.Add(("job_type", query.JobType!));
        }

        if (!string.IsNullOrWhiteSpace(query.MethodName))
        {
            conditions.Add("LOWER(j.method_name) = LOWER(@method_name)");
            parameters.Add(("method_name", query.MethodName!));
        }

        if (query.CreatedFrom.HasValue)
        {
            conditions.Add("j.created_at_utc >= @created_from");
            parameters.Add(("created_from", query.CreatedFrom.Value.UtcDateTime));
        }

        if (query.CreatedTo.HasValue)
        {
            conditions.Add("j.created_at_utc <= @created_to");
            parameters.Add(("created_to", query.CreatedTo.Value.UtcDateTime));
        }

        foreach (var filter in query.Filters)
        {
            var terms = JobSearchFilterMatcher.ParseTerms(filter.Value);
            if (terms.Count == 0) continue;

            var nameParam = $"fn{idx}";
            parameters.Add((nameParam, filter.Key));

            var termClauses = new List<string>(terms.Count);
            foreach (var term in terms)
            {
                var valParam = $"fv{idx}";
                idx++;
                if (JobSearchFilterMatcher.IsPrefixTerm(term, out var prefix))
                {
                    termClauses.Add($"LOWER(f.field_value) LIKE LOWER(@{valParam}) ESCAPE '\\'");
                    parameters.Add((valParam, EscapeLike(prefix) + "%"));
                }
                else
                {
                    termClauses.Add($"LOWER(f.field_value) = LOWER(@{valParam})");
                    parameters.Add((valParam, term));
                }
            }

            conditions.Add($"EXISTS (SELECT 1 FROM {_fieldsTable} f WHERE f.job_id = j.job_id AND f.field_name = @{nameParam} AND ({string.Join(" OR ", termClauses)}))");
        }

        return conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
    }

    private static string BuildOrderByClause(JobSearchQuery query, out bool needsInMemorySort)
    {
        var sorts = query.Sorts.Where(s => !string.IsNullOrWhiteSpace(s.Field)).ToList();
        if (sorts.Count == 0 && !string.IsNullOrWhiteSpace(query.SortBy))
        {
            sorts.Add(new JobSearchSort { Field = query.SortBy!, Descending = query.SortDescending });
        }

        if (sorts.Count == 0)
        {
            needsInMemorySort = false;
            return "ORDER BY j.created_at_utc DESC, j.job_id ASC";
        }

        var parts = new List<string>();
        foreach (var sort in sorts)
        {
            var dir = sort.Descending ? "DESC" : "ASC";
            switch (sort.Field.ToLowerInvariant())
            {
                case "jobid": parts.Add($"j.job_id {dir}"); break;
                case "state": parts.Add($"j.state {dir}"); break;
                case "jobtype": parts.Add($"j.job_type {dir}"); break;
                case "methodname": parts.Add($"j.method_name {dir}"); break;
                case "createdat": parts.Add($"j.created_at_utc {dir}"); break;
                default:
                    needsInMemorySort = true;
                    return string.Empty;
            }
        }

        needsInMemorySort = false;
        return "ORDER BY " + string.Join(", ", parts) + ", j.job_id ASC";
    }

    private static IEnumerable<JobSearchResult> ApplyInMemorySort(List<JobSearchResult> items, JobSearchQuery query)
    {
        var sorts = query.Sorts.Count > 0
            ? query.Sorts
            : new List<JobSearchSort> { new JobSearchSort { Field = query.SortBy ?? "CreatedAt", Descending = query.SortDescending } };

        IOrderedEnumerable<JobSearchResult>? ordered = null;
        foreach (var sort in sorts)
        {
            var field = sort.Field;
            Func<JobSearchResult, JobSearchSortValue> key = field.ToLowerInvariant() switch
            {
                "jobid" => r => JobSearchSortValue.From(r.JobId),
                "state" => r => JobSearchSortValue.From(r.State),
                "jobtype" => r => JobSearchSortValue.From(r.JobType),
                "methodname" => r => JobSearchSortValue.From(r.MethodName),
                "createdat" => r => JobSearchSortValue.From(r.CreatedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
                _ => r => JobSearchSortValue.From(r.Fields.TryGetValue(field, out var v) ? v : null),
            };

            ordered = ordered is null
                ? (sort.Descending ? items.OrderByDescending(key) : items.OrderBy(key))
                : (sort.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        }

        return (ordered ?? items.OrderByDescending(r => r.CreatedAt)).ThenBy(r => r.JobId, StringComparer.Ordinal);
    }

    private async Task LoadFieldsForResultsAsync(NpgsqlConnection conn, List<JobSearchResult> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return;

        var byId = items.ToDictionary(i => i.JobId, StringComparer.Ordinal);
        using var cmd = CreateCommand(conn, null, $"SELECT job_id, field_name, field_value FROM {_fieldsTable} WHERE job_id = ANY(@ids);");
        cmd.Parameters.AddWithValue("ids", items.Select(i => i.JobId).ToArray());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var jobId = reader.GetString(0);
            var fieldName = reader.GetString(1);
            var fieldValue = reader.IsDBNull(2) ? null : reader.GetString(2);
            if (byId.TryGetValue(jobId, out var target))
            {
                target.Fields[fieldName] = fieldValue;
            }
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized || !_options.PrepareSchemaIfNecessary) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var sql = $@"
CREATE SCHEMA IF NOT EXISTS ""{_options.SchemaName}"";

CREATE TABLE IF NOT EXISTS {_jobsTable} (
    job_id VARCHAR(128) PRIMARY KEY,
    job_type VARCHAR(512) NOT NULL,
    method_name VARCHAR(256) NOT NULL,
    state VARCHAR(64) NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_{_options.TablePrefix}_jobs_state_created ON {_jobsTable} (LOWER(state), created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_{_options.TablePrefix}_jobs_type_method ON {_jobsTable} (LOWER(job_type), LOWER(method_name));
CREATE INDEX IF NOT EXISTS ix_{_options.TablePrefix}_jobs_created ON {_jobsTable} (created_at_utc DESC);

CREATE TABLE IF NOT EXISTS {_fieldsTable} (
    job_id VARCHAR(128) NOT NULL REFERENCES {_jobsTable}(job_id) ON DELETE CASCADE,
    field_name VARCHAR(256) NOT NULL,
    field_value TEXT NULL,
    PRIMARY KEY (job_id, field_name)
);

CREATE INDEX IF NOT EXISTS ix_{_options.TablePrefix}_fields_name_val ON {_fieldsTable} (field_name, LOWER(field_value) text_pattern_ops);";

            using var cmd = CreateCommand(conn, null, sql);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return conn;
    }

    private NpgsqlCommand CreateCommand(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql)
    {
        return new NpgsqlCommand(sql, conn, tx) { CommandTimeout = _options.CommandTimeoutSeconds };
    }

    private static void AddParameters(NpgsqlCommand cmd, List<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
    }

    private static JobSearchResult ReadJobRow(NpgsqlDataReader reader)
    {
        var createdAtUtc = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc);
        return new JobSearchResult
        {
            JobId = reader.GetString(0),
            JobType = reader.GetString(1),
            MethodName = reader.GetString(2),
            State = reader.GetString(3),
            CreatedAt = new DateTimeOffset(createdAtUtc),
            Fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        };
    }

    private static string EscapeLike(string value)
    {
        return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
