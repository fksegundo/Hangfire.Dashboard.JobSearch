using System;
using Hangfire;

namespace Hangfire.Dashboard.JobSearch;

public static class GlobalConfigurationExtensions
{
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

        return configuration.UseFilter(clientFilter).UseFilter(stateFilter);
    }
}
