using System;
using Hangfire.Dashboard;
using Hangfire.Dashboard.Pages;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardPage : RazorPage
{
    private readonly IServiceProvider _serviceProvider;

    public JobSearchDashboardPage(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public override void Execute()
    {
        Layout = new LayoutPage("Job Search");

        var storage = (IJobSearchStorage?)_serviceProvider.GetService(typeof(IJobSearchStorage));
        if (storage is null)
        {
            WriteLiteral("<div class=\"alert alert-danger\">IJobSearchStorage is not registered.</div>");
            return;
        }

        var options = (JobSearchOptions?)_serviceProvider.GetService(typeof(JobSearchOptions));
        if (options is null)
        {
            WriteLiteral("<div class=\"alert alert-danger\">JobSearchOptions is not registered.</div>");
            return;
        }

        var selectedDefinition = JobSearchDashboardDefinitionSelector.Resolve(
            options.Definitions,
            Query("definition"));

        var query = JobSearchDashboardQueryMapper.Map(Query, selectedDefinition);
        var result = storage.SearchAsync(query).GetAwaiter().GetResult();
        var stateCounts = storage.CountByStateAsync(query).GetAwaiter().GetResult();
        var dashboardBasePath = GetDashboardBasePath();

        var model = JobSearchDashboardViewModel.From(
            pathBase: dashboardBasePath,
            query: query,
            options: options,
            result: result,
            selectedDefinition: selectedDefinition,
            stateCounts: stateCounts);

        WriteLiteral(JobSearchDashboardRenderer.RenderBody(model));
    }

    private string GetDashboardBasePath()
    {
        var pathBase = Context?.Request?.PathBase;
        if (!string.IsNullOrWhiteSpace(pathBase))
        {
            return pathBase!;
        }

        var requestPath = RequestPath ?? string.Empty;
        const string suffix = "/job-search";
        if (requestPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return requestPath.Substring(0, requestPath.Length - suffix.Length);
        }

        return AppPath ?? string.Empty;
    }
}
