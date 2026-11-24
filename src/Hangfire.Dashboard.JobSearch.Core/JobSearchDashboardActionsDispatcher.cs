using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire.Dashboard;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardActionsDispatcher : IDashboardDispatcher
{
    public async Task Dispatch(DashboardContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 405;
            return;
        }

        if (context.IsReadOnly)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Dashboard is read-only.").ConfigureAwait(false);
            return;
        }

        var actionValues = await context.Request.GetFormValuesAsync("action").ConfigureAwait(false);
        var action = actionValues?.FirstOrDefault()?.Trim();
        var rawIds = await context.Request.GetFormValuesAsync("jobIds").ConfigureAwait(false);

        var jobIds = new List<string>();
        if (rawIds is not null)
        {
            foreach (var entry in rawIds)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                foreach (var part in entry.Split(','))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 0)
                    {
                        jobIds.Add(trimmed);
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(action) || jobIds.Count == 0)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Missing action or jobIds.").ConfigureAwait(false);
            return;
        }

        var client = new BackgroundJobClient(context.Storage);
        var processed = 0;

        foreach (var jobId in jobIds.Distinct(StringComparer.Ordinal))
        {
            if (string.Equals(action, "requeue", StringComparison.OrdinalIgnoreCase))
            {
                if (client.Requeue(jobId))
                {
                    processed++;
                }
            }
            else if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
            {
                if (client.Delete(jobId))
                {
                    processed++;
                }
            }
            else
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Unsupported action.").ConfigureAwait(false);
                return;
            }
        }

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = 200;
        await context.Response.WriteAsync($"{{\"processed\":{processed}}}").ConfigureAwait(false);
    }
}
