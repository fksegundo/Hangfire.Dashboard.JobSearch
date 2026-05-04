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
                Name = "Documento",
                ArgumentIndex = 0,
                JsonPath = "$.Documento"
            });

        var job = CreateReportJob(new ReportArgs { Documento = "12345678900" });

        var fields = extractor.Extract(job);

        Assert.Equal("12345678900", fields["Documento"]);
    }

    [Fact]
    public void Extract_ReturnsNestedPropertyFromArgument()
    {
        var extractor = CreateExtractor(
            new JobSearchField
            {
                Name = "Documento",
                ArgumentIndex = 0,
                JsonPath = "$.Filtro.Documento"
            });

        var job = CreateReportJob(new ReportArgs
        {
            Filtro = new ReportFilter { Documento = "98765432100" }
        });

        var fields = extractor.Extract(job);

        Assert.Equal("98765432100", fields["Documento"]);
    }

    [Fact]
    public void Extract_ReturnsEmptyMetadataWhenTypeDoesNotMatch()
    {
        var extractor = CreateExtractor(
            new JobSearchField
            {
                Name = "Documento",
                ArgumentIndex = 0,
                JsonPath = "$.Documento"
            });

        var job = CreateJob<OtherWorker>(
            nameof(OtherWorker.GerarRelatorioAsync),
            new ReportArgs { Documento = "12345678900" });

        var fields = extractor.Extract(job);

        Assert.Empty(fields);
    }

    [Fact]
    public void Extract_ReturnsEmptyMetadataWhenMethodDoesNotMatch()
    {
        var extractor = CreateExtractor(
            new JobSearchField
            {
                Name = "Documento",
                ArgumentIndex = 0,
                JsonPath = "$.Documento"
            });

        var job = CreateJob<ReportWorker>(
            nameof(ReportWorker.OutroMetodoAsync),
            new ReportArgs { Documento = "12345678900" });

        var fields = extractor.Extract(job);

        Assert.Empty(fields);
    }

    [Fact]
    public void Extract_ReturnsNullFieldWhenArgumentIsNull()
    {
        var extractor = CreateExtractor(
            new JobSearchField
            {
                Name = "Documento",
                ArgumentIndex = 0,
                JsonPath = "$.Documento"
            });

        var job = CreateReportJob(null);

        var fields = extractor.Extract(job);

        Assert.Null(fields["Documento"]);
    }

    private static DefaultJobMetadataExtractor CreateExtractor(params JobSearchField[] fields)
    {
        var options = new JobSearchOptions();
        var definition = new JobSearchDefinition
        {
            JobType = typeof(ReportWorker),
            MethodName = nameof(ReportWorker.GerarRelatorioAsync)
        };

        definition.Fields.AddRange(fields);
        options.AddJobDefinition(definition);

        return new DefaultJobMetadataExtractor(options);
    }

    private static Job CreateReportJob(ReportArgs? args)
    {
        return CreateJob<ReportWorker>(nameof(ReportWorker.GerarRelatorioAsync), args);
    }

    private static Job CreateJob<TWorker>(string methodName, ReportArgs? args)
    {
        var method = typeof(TWorker).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

        return new Job(typeof(TWorker), method!, new object?[] { args });
    }

    public sealed class ReportWorker
    {
        public Task GerarRelatorioAsync(ReportArgs? args)
        {
            return Task.CompletedTask;
        }

        public Task OutroMetodoAsync(ReportArgs args)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class OtherWorker
    {
        public Task GerarRelatorioAsync(ReportArgs args)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class ReportArgs
    {
        public string? Documento { get; set; }

        public ReportFilter? Filtro { get; set; }
    }

    public sealed class ReportFilter
    {
        public string? Documento { get; set; }
    }
}
