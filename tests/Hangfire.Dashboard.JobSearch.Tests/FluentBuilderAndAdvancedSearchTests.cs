using System.Reflection;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class FluentBuilderAndAdvancedSearchTests
{
    [Fact]
    public void FluentBuilder_ConfiguresTypedFieldsAndCustomExtractorAndAttributes()
    {
        var options = new JobSearchOptions();
        options.AddJob<OrderWorker>(w => w.ProcessAsync(default!), job => job
            .Field<OrderPayload>(p => p.Customer.TenantId, sortable: true)
            .Field("FirstItemSku", 0, "$.Items[0].Sku", displayInList: true, filterable: true)
            .Field("Computed", j => $"args:{j.Args.Count}", displayInList: true, filterable: true)
            .DiscoverFieldsFromAttributes());

        JobSearchOptionsValidator.Validate(options);

        var def = Assert.Single(options.Definitions);
        Assert.Equal(typeof(OrderWorker), def.JobType);
        Assert.Equal(nameof(OrderWorker.ProcessAsync), def.MethodName);
        Assert.Contains(def.Fields, f => f.Name == "TenantId" && f.JsonPath == "$.Customer.TenantId" && f.Sortable);
        Assert.Contains(def.Fields, f => f.Name == "OrderNumber" && f.JsonPath == "$.OrderId");

        var extractor = new DefaultJobMetadataExtractor(options);
        var method = typeof(OrderWorker).GetMethod(nameof(OrderWorker.ProcessAsync), BindingFlags.Instance | BindingFlags.Public)!;
        var job = new Job(typeof(OrderWorker), method, new object?[]
        {
            new OrderPayload
            {
                OrderId = "ORD-900",
                Customer = new CustomerInfo { TenantId = "acme" },
                Items = new List<OrderItem> { new OrderItem { Sku = "SKU-42" } }
            }
        });

        var extracted = extractor.Extract(job);
        Assert.Equal("acme", extracted["TenantId"]);
        Assert.Equal("SKU-42", extracted["FirstItemSku"]);
        Assert.Equal("args:1", extracted["Computed"]);
        Assert.Equal("ORD-900", extracted["OrderNumber"]);
    }

    [Fact]
    public async Task InMemoryStorage_SupportsDateRangeMultiValueAndPrefix()
    {
        var storage = new InMemoryJobSearchStorage();
        var t0 = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        await storage.SaveMetadataAsync("1", Make("1", t0, ("OrderId", "ORD-100"), ("Tenant", "acme")));
        await storage.SaveMetadataAsync("2", Make("2", t0.AddHours(2), ("OrderId", "ORD-105"), ("Tenant", "globex")));
        await storage.SaveMetadataAsync("3", Make("3", t0.AddHours(5), ("OrderId", "INV-200"), ("Tenant", "initech")));

        var prefixPage = await storage.SearchAsync(new JobSearchQuery
        {
            Filters = { ["OrderId"] = "ORD-10*" }
        });
        Assert.Equal(2, prefixPage.TotalCount);

        var multiPage = await storage.SearchAsync(new JobSearchQuery
        {
            Filters = { ["Tenant"] = "acme,initech" }
        });
        Assert.Equal(2, multiPage.TotalCount);
        Assert.Contains(multiPage.Items, i => i.JobId == "1");
        Assert.Contains(multiPage.Items, i => i.JobId == "3");

        var datePage = await storage.SearchAsync(new JobSearchQuery
        {
            CreatedFrom = t0.AddHours(1),
            CreatedTo = t0.AddHours(3)
        });
        var single = Assert.Single(datePage.Items);
        Assert.Equal("2", single.JobId);

        var counts = await storage.CountByStateAsync(new JobSearchQuery
        {
            CreatedFrom = t0.AddHours(1),
            CreatedTo = t0.AddHours(3)
        });
        Assert.Equal(1, counts["Enqueued"]);
    }

    [Fact]
    public void QueryMapper_ParsesFromAndToDates()
    {
        var values = new Dictionary<string, string>
        {
            ["from"] = "2026-05-01T00:00:00Z",
            ["to"] = "2026-05-02T00:00:00Z"
        };

        var query = JobSearchDashboardQueryMapper.Map(
            k => values.TryGetValue(k, out var v) ? v : null,
            selectedDefinition: null);

        Assert.Equal(DateTimeOffset.Parse("2026-05-01T00:00:00Z"), query.CreatedFrom);
        Assert.Equal(DateTimeOffset.Parse("2026-05-02T00:00:00Z"), query.CreatedTo);
    }

    private static JobSearchMetadata Make(string id, DateTimeOffset createdAt, params (string Key, string Val)[] fields) =>
        new JobSearchMetadata
        {
            JobId = id,
            JobType = "OrderWorker",
            MethodName = "ProcessAsync",
            State = "Enqueued",
            CreatedAt = createdAt,
            Fields = fields.ToDictionary(x => x.Key, x => (string?)x.Val)
        };

    public sealed class OrderWorker
    {
        public Task ProcessAsync(OrderPayload payload) => Task.CompletedTask;
    }

    public sealed class OrderPayload
    {
        [JobSearchField("OrderNumber", Sortable = true)]
        public string OrderId { get; set; } = string.Empty;

        public CustomerInfo Customer { get; set; } = new CustomerInfo();

        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    public sealed class CustomerInfo
    {
        public string TenantId { get; set; } = string.Empty;
    }

    public sealed class OrderItem
    {
        public string Sku { get; set; } = string.Empty;
    }
}
