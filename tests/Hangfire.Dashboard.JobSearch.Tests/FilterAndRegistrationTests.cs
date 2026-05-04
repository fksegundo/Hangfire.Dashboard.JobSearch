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
            JobType = typeof(ReportWorker),
            MethodName = nameof(ReportWorker.GerarRelatorioAsync)
        };
        definition.Fields.Add(new JobSearchField
        {
            Name = "Documento",
            ArgumentIndex = 0,
            JsonPath = "$.Documento"
        });
        options.AddJobDefinition(definition);

        var storage = new InMemoryJobSearchStorage();
        var extractor = new DefaultJobMetadataExtractor(options);
        var logger = new ListLogger<JobSearchClientFilter>();
        var filter = new JobSearchClientFilter(options, extractor, storage, logger);

        filter.ProcessCreatedJob("job-1", CreateJob(new ReportArgs { Documento = "12345678900" }));

        var page = await storage.SearchAsync(new JobSearchQuery
        {
            Filters = { ["Documento"] = "12345678900" }
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
            JobType = "ReportWorker",
            MethodName = "GerarRelatorioAsync",
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
                JobType = typeof(ReportWorker),
                MethodName = nameof(ReportWorker.GerarRelatorioAsync),
                Fields =
                {
                    new JobSearchField
                    {
                        Name = "Documento",
                        ArgumentIndex = 0,
                        JsonPath = "$.Documento"
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
    public void UseJobSearch_ResolvesAndRegistersFilters()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireJobSearch(options =>
        {
            options.AddJobDefinition(new JobSearchDefinition
            {
                JobType = typeof(ReportWorker),
                MethodName = nameof(ReportWorker.GerarRelatorioAsync),
                Fields =
                {
                    new JobSearchField
                    {
                        Name = "Documento",
                        ArgumentIndex = 0,
                        JsonPath = "$.Documento"
                    }
                }
            });
        });

        using var provider = services.BuildServiceProvider();
        var configuration = GlobalConfiguration.Configuration;

        var result = configuration.UseJobSearch(provider);

        Assert.NotNull(result);
    }

    private static Job CreateJob(ReportArgs args)
    {
        var method = typeof(ReportWorker).GetMethod(
            nameof(ReportWorker.GerarRelatorioAsync),
            BindingFlags.Instance | BindingFlags.Public);
        return new Job(typeof(ReportWorker), method!, new object?[] { args });
    }

    public sealed class ReportWorker
    {
        public Task GerarRelatorioAsync(ReportArgs args)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class ReportArgs
    {
        public string? Documento { get; set; }
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

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new NullScope();

        public void Dispose()
        {
        }
    }
}
