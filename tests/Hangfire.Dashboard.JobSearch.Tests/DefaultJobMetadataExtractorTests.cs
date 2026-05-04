using System.Reflection;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class DefaultJobMetadataExtractorTests
{
    [Fact]
    public void Extract_ReturnsSimplePropertyFromArgument()
    {
        var extractor = CreateExtractor(
            new JobSearchField
            {
                Name = "Field1",
                ArgumentIndex = 0,
                JsonPath = "$.Field1"
            });

        var job = CreateJobA(new JobAArgs { Field1 = "12345678900" });

        var fields = extractor.Extract(job);

        Assert.Equal("12345678900", fields["Field1"]);
    }

    [Fact]
    public void Extract_ReturnsNestedPropertyFromArgument()
    {
        var extractor = CreateExtractor(
            new JobSearchField
            {
                Name = "Field1",
                ArgumentIndex = 0,
                JsonPath = "$.Nested.Field1"
            });

        var job = CreateJobA(new JobAArgs
        {
            Nested = new NestedData { Field1 = "98765432100" }
        });

        var fields = extractor.Extract(job);

        Assert.Equal("98765432100", fields["Field1"]);
    }

    [Fact]
    public void Extract_ReturnsEmptyMetadataWhenTypeDoesNotMatch()
    {
        var extractor = CreateExtractor(
            new JobSearchField
            {
                Name = "Field1",
                ArgumentIndex = 0,
                JsonPath = "$.Field1"
            });

        var job = CreateJob<WorkerB>(
            nameof(WorkerB.RunJobAsync),
            new JobAArgs { Field1 = "12345678900" });

        var fields = extractor.Extract(job);

        Assert.Empty(fields);
    }

    [Fact]
    public void Extract_ReturnsEmptyMetadataWhenMethodDoesNotMatch()
    {
        var extractor = CreateExtractor(
            new JobSearchField
            {
                Name = "Field1",
                ArgumentIndex = 0,
                JsonPath = "$.Field1"
            });

        var job = CreateJob<WorkerA>(
            nameof(WorkerA.RunAnotherAsync),
            new JobAArgs { Field1 = "12345678900" });

        var fields = extractor.Extract(job);

        Assert.Empty(fields);
    }

    [Fact]
    public void Extract_ReturnsNullFieldWhenArgumentIsNull()
    {
        var extractor = CreateExtractor(
            new JobSearchField
            {
                Name = "Field1",
                ArgumentIndex = 0,
                JsonPath = "$.Field1"
            });

        var job = CreateJobA(null);

        var fields = extractor.Extract(job);

        Assert.Null(fields["Field1"]);
    }

    private static DefaultJobMetadataExtractor CreateExtractor(params JobSearchField[] fields)
    {
        var options = new JobSearchOptions();
        var definition = new JobSearchDefinition
        {
            JobType = typeof(WorkerA),
            MethodName = nameof(WorkerA.RunJobAsync)
        };

        definition.Fields.AddRange(fields);
        options.AddJobDefinition(definition);

        return new DefaultJobMetadataExtractor(options);
    }

    private static Job CreateJobA(JobAArgs? args)
    {
        return CreateJob<WorkerA>(nameof(WorkerA.RunJobAsync), args);
    }

    private static Job CreateJob<TWorker>(string methodName, JobAArgs? args)
    {
        var method = typeof(TWorker).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

        return new Job(typeof(TWorker), method!, new object?[] { args });
    }

    public sealed class WorkerA
    {
        public Task RunJobAsync(JobAArgs? args)
        {
            return Task.CompletedTask;
        }

        public Task RunAnotherAsync(JobAArgs args)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class WorkerB
    {
        public Task RunJobAsync(JobAArgs args)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class JobAArgs
    {
        public string? Field1 { get; set; }

        public NestedData? Nested { get; set; }
    }

    public sealed class NestedData
    {
        public string? Field1 { get; set; }
    }
}
