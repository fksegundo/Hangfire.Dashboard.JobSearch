using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hangfire.Dashboard.JobSearch;

public sealed class InMemoryJobSearchStorage : IJobSearchStorage
{
    private readonly ConcurrentDictionary<string, JobSearchMetadata> _metadata = new ConcurrentDictionary<string, JobSearchMetadata>(StringComparer.Ordinal);

    public Task SaveMetadataAsync(
        string jobId,
        JobSearchMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        cancellationToken.ThrowIfCancellationRequested();
        _metadata[jobId] = CloneMetadata(metadata);

        return Task.CompletedTask;
    }

    public Task<JobSearchResultPage> SearchAsync(
        JobSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var results = _metadata.Values
            .Select(ToResult)
            .Where(result => MatchesQuery(result, query));

        results = ApplyOrdering(results, query);

        var totalCount = results.Count();
        var items = results
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new JobSearchResultPage
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public Task DeleteMetadataAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        _metadata.TryRemove(jobId, out _);

        return Task.CompletedTask;
    }

    public Task UpdateStateAsync(
        string jobId,
        string state,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ArgumentException("State is required.", nameof(state));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_metadata.TryGetValue(jobId, out var metadata))
        {
            metadata.State = state;
            _metadata[jobId] = CloneMetadata(metadata);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, int>> CountByStateAsync(
        JobSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var baseQuery = new JobSearchQuery
        {
            JobId = query.JobId,
            JobType = query.JobType,
            State = null,
            Filters = new Dictionary<string, string>(query.Filters, StringComparer.Ordinal)
        };

        var counts = _metadata.Values
            .Select(ToResult)
            .Where(result => MatchesQuery(result, baseQuery))
            .GroupBy(result => result.State ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return Task.FromResult<IReadOnlyDictionary<string, int>>(counts);
    }

    private static bool MatchesQuery(JobSearchResult result, JobSearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.JobId)
            && !string.Equals(result.JobId, query.JobId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.State)
            && !string.Equals(result.State, query.State, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.JobType)
            && !string.Equals(result.JobType, query.JobType, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var filter in query.Filters.Where(filter => !string.IsNullOrWhiteSpace(filter.Value)))
        {
            if (!result.Fields.TryGetValue(filter.Key, out var value)
                || !string.Equals(value, filter.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<JobSearchResult> ApplyOrdering(
        IEnumerable<JobSearchResult> results,
        JobSearchQuery query)
    {
        var sorts = query.Sorts.Where(sort => !string.IsNullOrWhiteSpace(sort.Field)).ToList();
        if (sorts.Count == 0 && !string.IsNullOrWhiteSpace(query.SortBy))
        {
            sorts.Add(new JobSearchSort { Field = query.SortBy!, Descending = query.SortDescending });
        }

        if (sorts.Count > 0)
        {
            IOrderedEnumerable<JobSearchResult>? ordered = null;

            foreach (var sort in sorts)
            {
                var field = sort.Field;
                if (ordered is null)
                {
                    ordered = sort.Descending
                        ? results.OrderByDescending(result => GetSortValue(result, field))
                        : results.OrderBy(result => GetSortValue(result, field));
                }
                else
                {
                    ordered = sort.Descending
                        ? ordered.ThenByDescending(result => GetSortValue(result, field))
                        : ordered.ThenBy(result => GetSortValue(result, field));
                }
            }

            return (ordered ?? results.OrderByDescending(result => result.CreatedAt))
                .ThenBy(result => result.JobId, StringComparer.Ordinal);
        }

        return results.OrderByDescending(result => result.CreatedAt).ThenBy(result => result.JobId, StringComparer.Ordinal);
    }

    private static SortValue GetSortValue(JobSearchResult result, string fieldName)
    {
        if (string.Equals(fieldName, "JobId", StringComparison.Ordinal))
        {
            return SortValue.From(result.JobId);
        }

        if (string.Equals(fieldName, "State", StringComparison.Ordinal))
        {
            return SortValue.From(result.State);
        }

        if (string.Equals(fieldName, "JobType", StringComparison.Ordinal))
        {
            return SortValue.From(result.JobType);
        }

        if (string.Equals(fieldName, "MethodName", StringComparison.Ordinal))
        {
            return SortValue.From(result.MethodName);
        }

        if (string.Equals(fieldName, "CreatedAt", StringComparison.Ordinal))
        {
            return SortValue.From(result.CreatedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }

        result.Fields.TryGetValue(fieldName, out var value);
        return SortValue.From(value);
    }

    private static JobSearchResult ToResult(JobSearchMetadata metadata)
    {
        return new JobSearchResult
        {
            JobId = metadata.JobId,
            JobType = metadata.JobType,
            MethodName = metadata.MethodName,
            State = metadata.State,
            CreatedAt = metadata.CreatedAt,
            Fields = new Dictionary<string, string?>(metadata.Fields, StringComparer.Ordinal)
        };
    }

    private static JobSearchMetadata CloneMetadata(JobSearchMetadata metadata)
    {
        return new JobSearchMetadata
        {
            JobId = metadata.JobId,
            JobType = metadata.JobType,
            MethodName = metadata.MethodName,
            State = metadata.State,
            CreatedAt = metadata.CreatedAt,
            Fields = new Dictionary<string, string?>(metadata.Fields, StringComparer.Ordinal)
        };
    }

    private readonly struct SortValue : IComparable<SortValue>
    {
        private readonly decimal _number;
        private readonly string _text;

        private SortValue(bool hasNumber, decimal number, string text)
        {
            HasNumber = hasNumber;
            _number = number;
            _text = text;
        }

        public bool HasNumber { get; }

        public static SortValue From(string? value)
        {
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            {
                return new SortValue(true, number, string.Empty);
            }

            return new SortValue(false, 0, value ?? string.Empty);
        }

        public int CompareTo(SortValue other)
        {
            var numberComparison = HasNumber.CompareTo(other.HasNumber);
            if (numberComparison != 0)
            {
                return numberComparison;
            }

            return HasNumber
                ? _number.CompareTo(other._number)
                : string.Compare(_text, other._text, StringComparison.Ordinal);
        }
    }
}
