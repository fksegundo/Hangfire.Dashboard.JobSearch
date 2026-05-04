using System;
using Microsoft.Extensions.DependencyInjection;

namespace Hangfire.Dashboard.JobSearch;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHangfireJobSearch(
        this IServiceCollection services,
        Action<JobSearchOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new JobSearchOptions();
        configure(options);
        JobSearchOptionsValidator.Validate(options);

        services.AddSingleton(options);
        services.AddSingleton<IJobMetadataExtractor, DefaultJobMetadataExtractor>();
        services.AddSingleton<IJobSearchStorage, InMemoryJobSearchStorage>();
        services.AddSingleton<JobSearchClientFilter>();
        services.AddSingleton<JobSearchStateFilter>();

        return services;
    }
}
