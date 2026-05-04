using System;
using Hangfire;
using Hangfire.Dashboard;

namespace Hangfire.Dashboard.JobSearch;

public static class GlobalConfigurationExtensions
{
    private static readonly object SyncRoot = new object();
    private static bool _dashboardRegistered;

    public static IGlobalConfiguration UseJobSearch(
        this IGlobalConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        var clientFilter = (JobSearchClientFilter?)serviceProvider.GetService(typeof(JobSearchClientFilter));
        if (clientFilter is null)
        {
            throw new InvalidOperationException("JobSearchClientFilter is not registered in the service provider.");
        }

        var stateFilter = (JobSearchStateFilter?)serviceProvider.GetService(typeof(JobSearchStateFilter));
        if (stateFilter is null)
        {
            throw new InvalidOperationException("JobSearchStateFilter is not registered in the service provider.");
        }

        configuration.UseFilter(clientFilter).UseFilter(stateFilter);
        RegisterDashboard(serviceProvider);

        return configuration;
    }

    private static void RegisterDashboard(IServiceProvider serviceProvider)
    {
        lock (SyncRoot)
        {
            if (_dashboardRegistered)
            {
                return;
            }

            var dispatcher = new JobSearchDashboardDispatcher(serviceProvider);
            DashboardRoutes.Routes.Add("/job-search", dispatcher);

            NavigationMenu.Items.Add(page => new MenuItem("Job Search", page.Url.To("/job-search"))
            {
                Active = page.RequestPath.StartsWith("/job-search", StringComparison.OrdinalIgnoreCase)
            });

            _dashboardRegistered = true;
        }
    }
}
