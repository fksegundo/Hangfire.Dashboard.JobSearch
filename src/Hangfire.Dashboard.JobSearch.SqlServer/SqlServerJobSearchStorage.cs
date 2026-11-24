using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Hangfire.Dashboard.JobSearch;

public sealed class SqlServerJobSearchStorage : IJobSearchStorage, IJobSearchBulkChildStorage
{
    private readonly string _connectionString;
    private readonly SqlServerJobSearchOptions _options;
    private readonly string _jobsTable;
    private readonly string _fieldsTable;
    private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
    private volatile bool _initialized;

    public SqlServerJobSearchStorage(string connectionString, SqlServerJobSearchOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _options = options ?? new SqlServerJobSearchOptions();
        _options.Validate();

        _jobsTable = $"[{_options.SchemaName}].[{_options.TablePrefix}_Jobs]";
        _fieldsTable = $"[{_options.SchemaName}].[{_options.TablePrefix}_JobFields]";
    }

    public async Task SaveMetadataAsync(string jobId, JobSearchMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var tx = conn.BeginTransaction();

        var upsertSql = $@"
MERGE {_jobsTable} AS target
USING (SELECT @JobId AS JobId) AS source ON (target.JobId = source.JobId)
WHEN MATCHED THEN
    UPDATE SET JobType = @JobType, MethodName = @MethodName, State = @State, CreatedAtUtc = @CreatedAtUtc
WHEN NOT MATCHED THEN
    INSERT (JobId, JobType, MethodName, State, CreatedAtUtc)
    VALUES (@JobId, @JobType, @MethodName, @State, @CreatedAtUtc);
DELETE FROM {_fieldsTable} WHERE JobId = @JobId;";

        using (var cmd = CreateCommand(conn, tx, upsertSql))
        {
            cmd.Parameters.AddWithValue("@JobId", jobId);
            cmd.Parameters.AddWithValue("@JobType", metadata.JobType ?? string.Empty);
            cmd.Parameters.AddWithValue("@MethodName", metadata.MethodName ?? string.Empty);
            cmd.Parameters.AddWithValue("@State", metadata.State ?? string.Empty);
            cmd.Parameters.AddWithValue("@CreatedAtUtc", metadata.CreatedAt.UtcDateTime);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (metadata.Fields.Count > 0)
        {
            var insertFieldSql = $"INSERT INTO {_fieldsTable} (JobId, FieldName, FieldValue) VALUES (@JobId, @FieldName, @FieldValue);";
            foreach (var pair in metadata.Fields)
            {
                using var fCmd = CreateCommand(conn, tx, insertFieldSql);
                fCmd.Parameters.AddWithValue("@JobId", jobId);
                fCmd.Parameters.AddWithValue("@FieldName", pair.Key);
                fCmd.Parameters.AddWithValue("@FieldValue", (object?)pair.Value ?? DBNull.Value);
                await fCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        tx.Commit();
    }

    public async Task UpdateStateAsync(string jobId, string state, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));
        if (string.IsNullOrWhiteSpace(state)) throw new ArgumentException("State is required.", nameof(state));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = CreateCommand(conn, null, $"UPDATE {_jobsTable} SET State = @State WHERE JobId = @JobId;");
        cmd.Parameters.AddWithValue("@JobId", jobId);
        cmd.Parameters.AddWithValue("@State", state);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteMetadataAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id is required.", nameof(jobId));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = CreateCommand(conn, null, $"DELETE FROM {_fieldsTable} WHERE JobId = @JobId; DELETE FROM {_jobsTable} WHERE JobId = @JobId;");
        cmd.Parameters.AddWithValue("@JobId", jobId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobSearchResultPage> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var parameters = new List<SqlParameter>();
        var whereClause = BuildWhereClause(query, includeState: true, parameters);

        int totalCount;
        using (var countCmd = CreateCommand(conn, null, $"SELECT COUNT(1) FROM {_jobsTable} j {whereClause};"))
        {
            AddClonedParameters(countCmd, parameters);
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        if (totalCount == 0)
        {
            return new JobSearchResultPage { Items = new List<JobSearchResult>(), Page = page, PageSize = pageSize, TotalCount = 0 };
        }

        var orderBy = BuildOrderByClause(query, out var needsInMemorySort);
        var selectSql = needsInMemorySort
            ? $"SELECT TOP (5000) j.JobId, j.JobType, j.MethodName, j.State, j.CreatedAtUtc FROM {_jobsTable} j {whereClause} ORDER BY j.CreatedAtUtc DESC, j.JobId ASC;"
            : $"SELECT j.JobId, j.JobType, j.MethodName, j.State, j.CreatedAtUtc FROM {_jobsTable} j {whereClause} {orderBy} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        var rows = new List<JobSearchResult>();
        using (var selectCmd = CreateCommand(conn, null, selectSql))
        {
            AddClonedParameters(selectCmd, parameters);
            if (!needsInMemorySort)
            {
                selectCmd.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
                selectCmd.Parameters.AddWithValue("@PageSize", pageSize);
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
        var parameters = new List<SqlParameter>();
        var whereClause = BuildWhereClause(query, includeState: false, parameters);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var cmd = CreateCommand(conn, null, $"SELECT j.State, COUNT(1) FROM {_jobsTable} j {whereClause} GROUP BY j.State;");
        AddClonedParameters(cmd, parameters);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var state = reader.GetString(0);
            var count = reader.GetInt32(1);
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
        var sb = new StringBuilder();
        var cmd = CreateCommand(conn, null, string.Empty);

        var inParams = new List<string>(distinctKeys.Count);
        for (var i = 0; i < distinctKeys.Count; i++)
        {
            var pName = $"@k{i}";
            inParams.Add(pName);
            cmd.Parameters.AddWithValue(pName, distinctKeys[i]);
        }

        sb.Append($@"
SELECT j.JobId, j.JobType, j.MethodName, j.State, j.CreatedAtUtc, f.FieldValue
FROM {_jobsTable} j
INNER JOIN {_fieldsTable} f ON f.JobId = j.JobId
WHERE LOWER(j.JobType) = LOWER(@ChildJobType)
  AND f.FieldName = @KeyField
  AND LOWER(f.FieldValue) IN ({string.Join(",", inParams.Select(p => $"LOWER({p})"))})");

        cmd.Parameters.AddWithValue("@ChildJobType", childJobType);
        cmd.Parameters.AddWithValue("@KeyField", keyField);

        if (!string.IsNullOrEmpty(childMethodName))
        {
            sb.Append(" AND j.MethodName = @ChildMethodName");
            cmd.Parameters.AddWithValue("@ChildMethodName", childMethodName);
        }

        sb.Append(" ORDER BY j.CreatedAtUtc DESC;");
        cmd.CommandText = sb.ToString();

        var allChildren = new List<JobSearchResult>();
        using (cmd)
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

    private string BuildWhereClause(JobSearchQuery query, bool includeState, List<SqlParameter> parameters)
    {
        var conditions = new List<string>();
        var idx = 0;

        if (!string.IsNullOrWhiteSpace(query.JobId))
        {
            conditions.Add("j.JobId = @JobId");
            parameters.Add(new SqlParameter("@JobId", query.JobId));
        }

        if (includeState && !string.IsNullOrWhiteSpace(query.State))
        {
            conditions.Add("LOWER(j.State) = LOWER(@State)");
            parameters.Add(new SqlParameter("@State", query.State));
        }

        if (!string.IsNullOrWhiteSpace(query.JobType))
        {
            conditions.Add("LOWER(j.JobType) = LOWER(@JobType)");
            parameters.Add(new SqlParameter("@JobType", query.JobType));
        }

        if (!string.IsNullOrWhiteSpace(query.MethodName))
        {
            conditions.Add("LOWER(j.MethodName) = LOWER(@MethodName)");
            parameters.Add(new SqlParameter("@MethodName", query.MethodName));
        }

        if (query.CreatedFrom.HasValue)
        {
            conditions.Add("j.CreatedAtUtc >= @CreatedFrom");
            parameters.Add(new SqlParameter("@CreatedFrom", query.CreatedFrom.Value.UtcDateTime));
        }

        if (query.CreatedTo.HasValue)
        {
            conditions.Add("j.CreatedAtUtc <= @CreatedTo");
            parameters.Add(new SqlParameter("@CreatedTo", query.CreatedTo.Value.UtcDateTime));
        }

        foreach (var filter in query.Filters)
        {
            var terms = JobSearchFilterMatcher.ParseTerms(filter.Value);
            if (terms.Count == 0) continue;

            var nameParam = $"@fn{idx}";
            parameters.Add(new SqlParameter(nameParam, filter.Key));

            var termClauses = new List<string>(terms.Count);
            foreach (var term in terms)
            {
                var valParam = $"@fv{idx}";
                idx++;
                if (JobSearchFilterMatcher.IsPrefixTerm(term, out var prefix))
                {
                    termClauses.Add($"LOWER(f.FieldValue) LIKE LOWER({valParam}) ESCAPE '\\'");
                    parameters.Add(new SqlParameter(valParam, EscapeLike(prefix) + "%"));
                }
                else
                {
                    termClauses.Add($"LOWER(f.FieldValue) = LOWER({valParam})");
                    parameters.Add(new SqlParameter(valParam, term));
                }
            }

            conditions.Add($"EXISTS (SELECT 1 FROM {_fieldsTable} f WHERE f.JobId = j.JobId AND f.FieldName = {nameParam} AND ({string.Join(" OR ", termClauses)}))");
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
            return "ORDER BY j.CreatedAtUtc DESC, j.JobId ASC";
        }

        var parts = new List<string>();
        foreach (var sort in sorts)
        {
            var dir = sort.Descending ? "DESC" : "ASC";
            switch (sort.Field.ToLowerInvariant())
            {
                case "jobid": parts.Add($"j.JobId {dir}"); break;
                case "state": parts.Add($"j.State {dir}"); break;
                case "jobtype": parts.Add($"j.JobType {dir}"); break;
                case "methodname": parts.Add($"j.MethodName {dir}"); break;
                case "createdat": parts.Add($"j.CreatedAtUtc {dir}"); break;
                default:
                    needsInMemorySort = true;
                    return string.Empty;
            }
        }

        needsInMemorySort = false;
        return "ORDER BY " + string.Join(", ", parts) + ", j.JobId ASC";
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

    private async Task LoadFieldsForResultsAsync(SqlConnection conn, List<JobSearchResult> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return;

        var byId = items.ToDictionary(i => i.JobId, StringComparer.Ordinal);
        using var cmd = CreateCommand(conn, null, string.Empty);
        var idParams = new List<string>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var p = $"@id{i}";
            idParams.Add(p);
            cmd.Parameters.AddWithValue(p, items[i].JobId);
        }

        cmd.CommandText = $"SELECT JobId, FieldName, FieldValue FROM {_fieldsTable} WHERE JobId IN ({string.Join(",", idParams)});";

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
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{_options.SchemaName}')
    EXEC(N'CREATE SCHEMA [{_options.SchemaName}]');

IF OBJECT_ID(N'{_jobsTable}', N'U') IS NULL
BEGIN
    CREATE TABLE {_jobsTable} (
        JobId NVARCHAR(128) NOT NULL PRIMARY KEY,
        JobType NVARCHAR(512) NOT NULL,
        MethodName NVARCHAR(256) NOT NULL,
        State NVARCHAR(64) NOT NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
    );
    CREATE INDEX IX_{_options.TablePrefix}_Jobs_State_CreatedAt ON {_jobsTable} (State, CreatedAtUtc DESC);
    CREATE INDEX IX_{_options.TablePrefix}_Jobs_Type_Method ON {_jobsTable} (JobType, MethodName);
    CREATE INDEX IX_{_options.TablePrefix}_Jobs_CreatedAt ON {_jobsTable} (CreatedAtUtc DESC);
END;

IF OBJECT_ID(N'{_fieldsTable}', N'U') IS NULL
BEGIN
    CREATE TABLE {_fieldsTable} (
        JobId NVARCHAR(128) NOT NULL,
        FieldName NVARCHAR(256) NOT NULL,
        FieldValue NVARCHAR(450) NULL,
        CONSTRAINT PK_{_options.TablePrefix}_JobFields PRIMARY KEY (JobId, FieldName),
        CONSTRAINT FK_{_options.TablePrefix}_JobFields_Jobs FOREIGN KEY (JobId) REFERENCES {_jobsTable}(JobId) ON DELETE CASCADE
    );
    CREATE INDEX IX_{_options.TablePrefix}_JobFields_Name_Value ON {_fieldsTable} (FieldName, FieldValue);
END;";

            using var cmd = CreateCommand(conn, null, sql);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return conn;
    }

    private SqlCommand CreateCommand(SqlConnection conn, SqlTransaction? tx, string sql)
    {
        return new SqlCommand(sql, conn, tx) { CommandTimeout = _options.CommandTimeoutSeconds };
    }

    private static void AddClonedParameters(SqlCommand cmd, List<SqlParameter> source)
    {
        foreach (var p in source)
        {
            cmd.Parameters.AddWithValue(p.ParameterName, p.Value);
        }
    }

    private static JobSearchResult ReadJobRow(SqlDataReader reader)
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
        return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
    }
}
