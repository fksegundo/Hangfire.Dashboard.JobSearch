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

            var options = (JobSearchOptions?)serviceProvider.GetService(typeof(JobSearchOptions));
            var routePath = NormalizeRoutePath(options?.RoutePath);
            var menuTitle = string.IsNullOrWhiteSpace(options?.MenuTitle) ? "Job Search" : options!.MenuTitle;

            DashboardRoutes.Routes.Add(routePath, new JobSearchDashboardRazorDispatcher(serviceProvider));
            DashboardRoutes.Routes.Add($"{routePath}/actions", new JobSearchDashboardActionsDispatcher());

            NavigationMenu.Items.Add(page => new MenuItem(menuTitle, page.Url.To(routePath))
            {
                Active = page.RequestPath.StartsWith(routePath, StringComparison.OrdinalIgnoreCase)
            });

            _dashboardRegistered = true;
        }
    }

    internal static string NormalizeRoutePath(string? routePath)
    {
        if (string.IsNullOrWhiteSpace(routePath))
        {
            return "/job-search";
        }

        var trimmed = routePath!.Trim().TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return "/job-search";
        }

        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
    }
}
