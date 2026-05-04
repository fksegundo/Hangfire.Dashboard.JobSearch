using System;
using System.Threading.Tasks;
using Hangfire.Dashboard;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardDispatcher : IDashboardDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public JobSearchDashboardDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task Dispatch(DashboardContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var storage = (IJobSearchStorage?)_serviceProvider.GetService(typeof(IJobSearchStorage));
        if (storage is null)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("IJobSearchStorage is not registered.");
            return;
        }

        var options = (JobSearchOptions?)_serviceProvider.GetService(typeof(JobSearchOptions));
        if (options is null)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("JobSearchOptions is not registered.");
            return;
        }

        var query = JobSearchDashboardQueryMapper.Map(context.Request, options);
        var result = await storage.SearchAsync(query);

        var model = JobSearchDashboardViewModel.From(
            pathBase: context.Request.PathBase,
            query: query,
            options: options,
            result: result);

        var html = JobSearchDashboardRenderer.Render(model);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html);
    }
}
