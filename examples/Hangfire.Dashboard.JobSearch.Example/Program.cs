using Hangfire;
using Hangfire.Dashboard.JobSearch;
using Hangfire.MemoryStorage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHangfire(configuration => configuration.UseMemoryStorage());
builder.Services.AddHangfireServer();

builder.Services.AddHangfireJobSearch(options =>
{
    options.AddJob<OrderWorker>(w => w.ProcessOrderAsync(default!), job => job
        .Field<OrderJobArgs>(a => a.TenantId, sortable: true)
        .Field<OrderJobArgs>(a => a.OrderId, sortable: true)
        .Field<OrderJobArgs>(a => a.Amount, sortable: true)
        .Field<OrderJobArgs>(a => a.Region, sortable: false));

    options.AddJob<InvoiceWorker>(w => w.GenerateInvoiceAsync(default!), job => job
        .DiscoverFieldsFromAttributes());

    options.AddJoin(new JobSearchJoin
    {
        KeyField = "OrderId",
        ParentJobType = typeof(OrderWorker),
        ParentMethodName = nameof(OrderWorker.ProcessOrderAsync),
        ChildJobType = typeof(InvoiceWorker),
        ChildMethodName = nameof(InvoiceWorker.GenerateInvoiceAsync),
        ChildLabel = "invoices"
    });
});

var app = builder.Build();

app.UseHangfireDashboard("/hangfire");
GlobalConfiguration.Configuration.UseJobSearch(app.Services);

// Seed realistic sample jobs on startup so the dashboard is immediately populated
SeedSampleJobs();

app.MapGet("/", () => Results.Redirect("/hangfire/job-search?state=all"));

app.MapPost("/enqueue-order", () =>
{
    var tenants = new[] { "acme", "globex", "initech", "umbrella" };
    var regions = new[] { "us-east", "eu-west", "sa-east" };
    var orderId = $"ORD-{Random.Shared.Next(1000, 9999)}";
    var tenant = tenants[Random.Shared.Next(tenants.Length)];
    var region = regions[Random.Shared.Next(regions.Length)];

    BackgroundJob.Enqueue<OrderWorker>(w => w.ProcessOrderAsync(new OrderJobArgs
    {
        TenantId = tenant,
        OrderId = orderId,
        Amount = Math.Round((decimal)(Random.Shared.NextDouble() * 1500 + 25), 2),
        Region = region
    }));

    return Results.Ok(new { orderId, tenant, region });
});

app.Run();

static void SeedSampleJobs()
{
    var orders = new[]
    {
        new OrderJobArgs { TenantId = "acme", OrderId = "ORD-1001", Amount = 1250.50m, Region = "us-east", ShouldHold = false },
        new OrderJobArgs { TenantId = "acme", OrderId = "ORD-1002", Amount = 480.00m, Region = "eu-west", ShouldFail = true },
        new OrderJobArgs { TenantId = "globex", OrderId = "ORD-1003", Amount = 2990.90m, Region = "us-east", ShouldHold = true },
        new OrderJobArgs { TenantId = "initech", OrderId = "ORD-1004", Amount = 150.00m, Region = "sa-east", ShouldHold = false },
        new OrderJobArgs { TenantId = "umbrella", OrderId = "ORD-1005", Amount = 875.25m, Region = "eu-west", ShouldHold = true },
    };

    foreach (var order in orders)
    {
        BackgroundJob.Enqueue<OrderWorker>(w => w.ProcessOrderAsync(order));

        // Enqueue child invoice jobs correlated by OrderId
        for (var i = 1; i <= 3; i++)
        {
            var inv = new InvoiceJobArgs
            {
                OrderId = order.OrderId,
                InvoiceNumber = $"INV-{order.OrderId.Substring(4)}-{i}",
                Installment = i,
                ShouldFail = order.OrderId == "ORD-1001" && i == 3
            };
            BackgroundJob.Enqueue<InvoiceWorker>(w => w.GenerateInvoiceAsync(inv));
        }
    }

    BackgroundJob.Schedule<OrderWorker>(
        w => w.ProcessOrderAsync(new OrderJobArgs
        {
            TenantId = "globex",
            OrderId = "ORD-1006",
            Amount = 640.00m,
            Region = "sa-east"
        }),
        TimeSpan.FromHours(2));
}

public sealed class OrderWorker
{
    [AutomaticRetry(Attempts = 0)]
    public async Task ProcessOrderAsync(OrderJobArgs args)
    {
        if (args.ShouldFail)
        {
            throw new InvalidOperationException($"Payment gateway timeout while processing {args.OrderId}.");
        }

        if (args.ShouldHold)
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(150));
    }
}

public sealed class InvoiceWorker
{
    [AutomaticRetry(Attempts = 0)]
    public async Task GenerateInvoiceAsync(InvoiceJobArgs args)
    {
        if (args.ShouldFail)
        {
            throw new InvalidOperationException($"Tax service rejected invoice {args.InvoiceNumber}.");
        }

        await Task.Delay(TimeSpan.FromMilliseconds(120));
    }
}

public sealed class OrderJobArgs
{
    public string TenantId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Region { get; set; } = string.Empty;
    public bool ShouldFail { get; set; }
    public bool ShouldHold { get; set; }
}

public sealed class InvoiceJobArgs
{
    [JobSearchField(Sortable = true)]
    public string OrderId { get; set; } = string.Empty;

    [JobSearchField(Sortable = true)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [JobSearchField(Sortable = true)]
    public int Installment { get; set; }

    public bool ShouldFail { get; set; }
}
