using Hangfire;
using Hangfire.Dashboard.JobSearch;
using Hangfire.MemoryStorage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHangfire(configuration => configuration.UseMemoryStorage());
builder.Services.AddHangfireServer();

builder.Services.AddHangfireJobSearch(options =>
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
                JsonPath = "$.Field1",
                DisplayInList = true,
                Filterable = true,
                Sortable = true
            },
            new JobSearchField
            {
                Name = "Field2",
                ArgumentIndex = 0,
                JsonPath = "$.Field2",
                DisplayInList = true,
                Filterable = true,
                Sortable = true
            },
            new JobSearchField
            {
                Name = "Field3",
                ArgumentIndex = 0,
                JsonPath = "$.Field3",
                DisplayInList = true,
                Filterable = true
            }
        }
    });

    options.AddJobDefinition(new JobSearchDefinition
    {
        JobType = typeof(WorkerB),
        MethodName = nameof(WorkerB.RunJobAsync),
        Fields =
        {
            new JobSearchField
            {
                Name = "Option1",
                ArgumentIndex = 0,
                JsonPath = "$.Option1",
                DisplayInList = true,
                Filterable = true,
                Sortable = true
            },
            new JobSearchField
            {
                Name = "Option2",
                ArgumentIndex = 0,
                JsonPath = "$.Option2",
                DisplayInList = true,
                Filterable = true,
                Sortable = true
            },
            new JobSearchField
            {
                Name = "Option3",
                ArgumentIndex = 0,
                JsonPath = "$.Option3",
                DisplayInList = true,
                Filterable = true
            }
        }
    });
});

var app = builder.Build();

app.UseHangfireDashboard("/hangfire");
GlobalConfiguration.Configuration.UseJobSearch(app.Services);

app.MapGet("/", () => Results.Redirect("/hangfire/job-search"));

app.MapPost("/enqueue-job-a", () =>
{
    BackgroundJob.Enqueue<WorkerA>(worker => worker.RunJobAsync(new JobAArgs
    {
        Field1 = $"v-{Random.Shared.Next(1000, 9999)}",
        Field2 = Random.Shared.Next(1, 100),
        Field3 = "group-a",
        StartDate = DateTime.Today.AddDays(-30),
        EndDate = DateTime.Today
    }));

    return Results.Ok();
});

app.MapPost("/enqueue-job-b", () =>
{
    var options = new[] { "A", "B", "C" };
    var selectedOption = options[Random.Shared.Next(0, options.Length)];

    BackgroundJob.Enqueue<WorkerB>(worker => worker.RunJobAsync(new JobBArgs
    {
        Option1 = selectedOption,
        Option2 = $"value-{Random.Shared.Next(1, 500)}",
        Option3 = $"key-{Random.Shared.Next(1000, 9999)}",
        Priority = Random.Shared.Next(1, 5)
    }));

    return Results.Ok();
});

app.Run();

public sealed class WorkerA
{
    public async Task RunJobAsync(JobAArgs args)
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
    }
}

public sealed class JobAArgs
{
    public string Field1 { get; set; } = string.Empty;
    public int Field2 { get; set; }
    public string Field3 { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public sealed class WorkerB
{
    public async Task RunJobAsync(JobBArgs args)
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
    }
}

public sealed class JobBArgs
{
    public string Option1 { get; set; } = string.Empty;
    public string Option2 { get; set; } = string.Empty;
    public string Option3 { get; set; } = string.Empty;
    public int Priority { get; set; }
}
