using System;
using System.Linq;
using Hangfire.Client;
using Hangfire.Common;
using Microsoft.Extensions.Logging;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchClientFilter : IClientFilter
{
    private readonly JobSearchOptions _options;
    private readonly IJobMetadataExtractor _extractor;
    private readonly IJobSearchStorage _storage;
    private readonly ILogger<JobSearchClientFilter> _logger;

    public JobSearchClientFilter(
        JobSearchOptions options,
        IJobMetadataExtractor extractor,
        IJobSearchStorage storage,
        ILogger<JobSearchClientFilter> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void OnCreating(CreatingContext context)
    {
    }

    public void OnCreated(CreatedContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        ProcessCreatedJob(context.BackgroundJob?.Id, context.Job);
    }

    internal void ProcessCreatedJob(string? jobId, Job? job)
    {
        if (string.IsNullOrWhiteSpace(jobId) || job is null)
        {
            return;
        }
        var jobIdValue = jobId!;

        var definition = _options.FindDefinition(job);
        if (definition is null)
        {
            return;
        }

        try
        {
            var fields = _extractor.Extract(job);
            var metadata = new JobSearchMetadata
            {
                JobId = jobIdValue,
                JobType = job.Type.FullName ?? job.Type.Name ?? string.Empty,
                MethodName = job.Method.Name,
                State = "Enqueued",
                CreatedAt = DateTimeOffset.UtcNow,
                Fields = fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            };

            _storage.SaveMetadataAsync(jobIdValue, metadata).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error while indexing job metadata. JobId={JobId}, JobType={JobType}, Method={Method}.",
                jobId,
                job.Type.FullName ?? job.Type.Name,
                job.Method.Name);
        }
    }
}
