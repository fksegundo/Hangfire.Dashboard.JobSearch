using System.Reflection;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class FilterAndRegistrationTests
{
    [Fact]
    public async Task ClientFilter_IndexesMetadataForMappedJob()
    {
        var options = new JobSearchOptions();
        var definition = new JobSearchDefinition
        {
            JobType = typeof(WorkerA),
            MethodName = nameof(WorkerA.RunJobAsync)
        };
        definition.Fields.Add(new JobSearchField
        {
            Name = "Field1",
            ArgumentIndex = 0,
            JsonPath = "$.Field1"
        });
        options.AddJobDefinition(definition);

        var storage = new InMemoryJobSearchStorage();
        var extractor = new DefaultJobMetadataExtractor(options);
        var logger = new ListLogger<JobSearchClientFilter>();
        var filter = new JobSearchClientFilter(options, extractor, storage, logger);

        filter.ProcessCreatedJob("job-1", CreateJob(new JobAArgs { Field1 = "12345678900" }));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Filters = { ["Field1"] = "12345678900" }
        });

        var result = Assert.Single(page.Items);
        Assert.Equal("job-1", result.JobId);
        Assert.Equal("Enqueued", result.State);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task StateFilter_UpdatesJobState()
    {
        var storage = new InMemoryJobSearchStorage();
        await storage.SaveMetadataAsync("job-1", new JobSearchMetadata
        {
            JobId = "job-1",
            JobType = "WorkerA",
            MethodName = "RunJobAsync",
            State = "Enqueued",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var logger = new ListLogger<JobSearchStateFilter>();
        var filter = new JobSearchStateFilter(storage, logger);

        filter.ProcessStateApplied("job-1", "Succeeded");

        var page = await storage.SearchAsync(new JobSearchQuery { State = "Succeeded" });
        var result = Assert.Single(page.Items);
        Assert.Equal("job-1", result.JobId);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void AddHangfireJobSearch_RegistersRequiredServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireJobSearch(options =>
        {
            options.AddJobDefinition(new JobSearchDefinition
            {
                JobType = typeof(WorkerA),
                MethodName = nameof(WorkerA.RunJobAsync),
                Fields =
                {
                    new JobSearchField
                    {
                        Name = "Field1",
                        ArgumentIndex = 0,
                        JsonPath = "$.Field1"
                    }
                }
            });
        });

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<JobSearchOptions>());
        Assert.NotNull(provider.GetService<IJobMetadataExtractor>());
        Assert.NotNull(provider.GetService<IJobSearchStorage>());
        Assert.NotNull(provider.GetService<JobSearchClientFilter>());
        Assert.NotNull(provider.GetService<JobSearchStateFilter>());
    }

    [Fact]
    public void AddHangfireJobSearch_UsesInMemoryStorageByDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireJobSearch(options =>
        {
            options.AddJobDefinition(new JobSearchDefinition
            {
                JobType = typeof(WorkerA),
                MethodName = nameof(WorkerA.RunJobAsync)
            });
        });

        var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IJobSearchStorage>();
        Assert.IsType<InMemoryJobSearchStorage>(storage);
    }

    [Fact]
    public void AddHangfireJobSearch_UsesCustomStorageType()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireJobSearch(options =>
        {
            options.AddJobDefinition(new JobSearchDefinition
            {
                JobType = typeof(WorkerA),
                MethodName = nameof(WorkerA.RunJobAsync)
            });
            options.UseStorage<TestStorage>();
        });

        var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IJobSearchStorage>();
        Assert.IsType<TestStorage>(storage);
    }

    [Fact]
    public void UseJobSearch_ResolvesAndRegistersFilters()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireJobSearch(options =>
        {
            options.AddJobDefinition(new JobSearchDefinition
            {
                JobType = typeof(WorkerA),
                MethodName = nameof(WorkerA.RunJobAsync),
                Fields =
                {
                    new JobSearchField
                    {
                        Name = "Field1",
                        ArgumentIndex = 0,
                        JsonPath = "$.Field1"
                    }
                }
            });
        });

        using var provider = services.BuildServiceProvider();
        var configuration = GlobalConfiguration.Configuration;

        var result = configuration.UseJobSearch(provider);

        Assert.NotNull(result);
    }

    private static Job CreateJob(JobAArgs args)
    {
        var method = typeof(WorkerA).GetMethod(
            nameof(WorkerA.RunJobAsync),
            BindingFlags.Instance | BindingFlags.Public);
        return new Job(typeof(WorkerA), method!, new object?[] { args });
    }

    public sealed class WorkerA
    {
        public Task RunJobAsync(JobAArgs args)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class JobAArgs
    {
        public string? Field1 { get; set; }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Errors { get; } = new List<string>();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }

    private sealed class TestStorage : IJobSearchStorage
    {
        public Task DeleteMetadataAsync(string jobId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<JobSearchResultPage> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new JobSearchResultPage());
        }

        public Task SaveMetadataAsync(string jobId, JobSearchMetadata metadata, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateStateAsync(string jobId, string state, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, int>> CountByStateAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new NullScope();

        public void Dispose()
        {
        }
    }
}
