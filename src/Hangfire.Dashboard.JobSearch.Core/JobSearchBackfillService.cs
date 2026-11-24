using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchBackfillResult
{
    public int Scanned { get; set; }
    public int Indexed { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
}

/// <summary>
/// Utility service that scans existing jobs from Hangfire's <see cref="JobStorage"/>
/// and indexes matching jobs into <see cref="IJobSearchStorage"/>.
/// Useful when adopting JobSearch on an application with existing job history.
/// </summary>
public sealed class JobSearchBackfillService
{
    private readonly JobSearchOptions _options;
    private readonly IJobMetadataExtractor _extractor;
    private readonly IJobSearchStorage _storage;
    private readonly ILogger<JobSearchBackfillService>? _logger;

    public JobSearchBackfillService(
        JobSearchOptions options,
        IJobMetadataExtractor extractor,
        IJobSearchStorage storage,
        ILogger<JobSearchBackfillService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger;
    }

    public async Task<JobSearchBackfillResult> BackfillAsync(
        JobStorage hangfireStorage,
        int maxPerState = 500,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (hangfireStorage is null)
        {
            throw new ArgumentNullException(nameof(hangfireStorage));
        }

        if (maxPerState <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPerState));
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var monitoring = hangfireStorage.GetMonitoringApi();
        var result = new JobSearchBackfillResult();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        await ScanPagesAsync(
            (from, count) => monitoring.ProcessingJobs(from, count)
                .Select(p => (p.Key, p.Value?.Job, "Processing", (DateTime?)p.Value?.StartedAt)),
            maxPerState, batchSize, seen, result, cancellationToken).ConfigureAwait(false);

        await ScanPagesAsync(
            (from, count) => monitoring.ScheduledJobs(from, count)
                .Select(p => (p.Key, p.Value?.Job, "Scheduled", (DateTime?)p.Value?.ScheduledAt)),
            maxPerState, batchSize, seen, result, cancellationToken).ConfigureAwait(false);

        await ScanPagesAsync(
            (from, count) => monitoring.SucceededJobs(from, count)
                .Select(p => (p.Key, p.Value?.Job, "Succeeded", p.Value?.SucceededAt)),
            maxPerState, batchSize, seen, result, cancellationToken).ConfigureAwait(false);

        await ScanPagesAsync(
            (from, count) => monitoring.FailedJobs(from, count)
                .Select(p => (p.Key, p.Value?.Job, "Failed", p.Value?.FailedAt)),
            maxPerState, batchSize, seen, result, cancellationToken).ConfigureAwait(false);

        await ScanPagesAsync(
            (from, count) => monitoring.DeletedJobs(from, count)
                .Select(p => (p.Key, p.Value?.Job, "Deleted", p.Value?.DeletedAt)),
            maxPerState, batchSize, seen, result, cancellationToken).ConfigureAwait(false);

        foreach (var queue in monitoring.Queues())
        {
            var queueName = queue.Name;
            await ScanPagesAsync(
                (from, count) => monitoring.EnqueuedJobs(queueName, from, count)
                    .Select(p => (p.Key, p.Value?.Job, "Enqueued", p.Value?.EnqueuedAt)),
                maxPerState, batchSize, seen, result, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task ScanPagesAsync(
        Func<int, int, IEnumerable<(string JobId, Job? Job, string State, DateTime? Timestamp)>> fetcher,
        int maxPerState,
        int batchSize,
        HashSet<string> seen,
        JobSearchBackfillResult result,
        CancellationToken cancellationToken)
    {
        var fetched = 0;
        while (fetched < maxPerState)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = Math.Min(batchSize, maxPerState - fetched);
            var batch = fetcher(fetched, take).ToList();
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var entry in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Scanned++;

                if (string.IsNullOrWhiteSpace(entry.JobId) || entry.Job is null || !seen.Add(entry.JobId))
                {
                    result.Skipped++;
                    continue;
                }

                var definition = _options.FindDefinition(entry.Job);
                if (definition is null)
                {
                    result.Skipped++;
                    continue;
                }

                try
                {
                    var fields = _extractor.Extract(entry.Job);
                    var createdAt = entry.Timestamp.HasValue
                        ? new DateTimeOffset(DateTime.SpecifyKind(entry.Timestamp.Value, DateTimeKind.Utc))
                        : DateTimeOffset.UtcNow;

                    var metadata = new JobSearchMetadata
                    {
                        JobId = entry.JobId,
                        JobType = entry.Job.Type.FullName ?? entry.Job.Type.Name ?? string.Empty,
                        MethodName = entry.Job.Method.Name,
                        State = entry.State,
                        CreatedAt = createdAt,
                        Fields = fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
                    };

                    await _storage.SaveMetadataAsync(entry.JobId, metadata, cancellationToken).ConfigureAwait(false);
                    result.Indexed++;
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    _logger?.LogError(ex, "Error while backfilling job {JobId}.", entry.JobId);
                }
            }

            fetched += batch.Count;
            if (batch.Count < take)
            {
                break;
            }
        }
    }
}
