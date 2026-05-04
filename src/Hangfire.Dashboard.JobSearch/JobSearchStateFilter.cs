using System;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchStateFilter : IApplyStateFilter
{
    private readonly IJobSearchStorage _storage;
    private readonly ILogger<JobSearchStateFilter> _logger;

    public JobSearchStateFilter(
        IJobSearchStorage storage,
        ILogger<JobSearchStateFilter> logger)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        ProcessStateApplied(context.BackgroundJob?.Id, context.NewState?.Name);
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }

    internal void ProcessStateApplied(string? jobId, string? stateName)
    {
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(stateName))
        {
            return;
        }
        var jobIdValue = jobId!;
        var stateValue = stateName!;

        try
        {
            _storage.UpdateStateAsync(jobIdValue, stateValue).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error while updating indexed job state. JobId={JobId}, State={State}.",
                jobId,
                stateName);
        }
    }
}
