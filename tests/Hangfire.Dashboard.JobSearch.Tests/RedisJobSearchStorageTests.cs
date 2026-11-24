using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class RedisJobSearchStorageTests : IAsyncLifetime
{
    private RedisContainer _container = null!;
    private IConnectionMultiplexer _connection = null!;
    private RedisJobSearchStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _container = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await _container.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        _storage = new RedisJobSearchStorage(_connection, expiry: TimeSpan.FromMinutes(5));
    }

    public async Task DisposeAsync()
    {
        _connection.Dispose();
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static JobSearchMetadata Meta(
        string jobId,
        string jobType = "TestJob",
        string methodName = "Run",
        string state = "Enqueued",
        DateTimeOffset? createdAt = null,
        Dictionary<string, string?>? fields = null) =>
        new JobSearchMetadata
        {
            JobId = jobId,
            JobType = jobType,
            MethodName = methodName,
            State = state,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            Fields = fields ?? new Dictionary<string, string?>(),
        };

    private static Dictionary<string, string?> F(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => (string?)p.Value);

    // ── save + search (no filters) ────────────────────────────────────────────

    [Fact]
    public async Task SaveAndSearch_NoFilters_ReturnsJob()
    {
        await _storage.SaveMetadataAsync("j1", Meta("j1"));

        var page = await _storage.SearchAsync(new JobSearchQuery());

        var item = Assert.Single(page.Items);
        Assert.Equal("j1", item.JobId);
        Assert.Equal(1, page.TotalCount);
    }

    // ── filter by state ───────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ByState_ReturnsOnlyMatchingState()
    {
        await _storage.SaveMetadataAsync("e1", Meta("e1", state: "Enqueued"));
        await _storage.SaveMetadataAsync("s1", Meta("s1", state: "Succeeded"));

        var page = await _storage.SearchAsync(new JobSearchQuery { State = "Succeeded" });

        var item = Assert.Single(page.Items);
        Assert.Equal("s1", item.JobId);
    }

    // ── filter by job type ────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ByJobType_ReturnsOnlyMatchingType()
    {
        await _storage.SaveMetadataAsync("a1", Meta("a1", jobType: "TypeA"));
        await _storage.SaveMetadataAsync("b1", Meta("b1", jobType: "TypeB"));

        var page = await _storage.SearchAsync(new JobSearchQuery { JobType = "TypeA" });

        var item = Assert.Single(page.Items);
        Assert.Equal("a1", item.JobId);
    }

    // ── multi-field filter (ZINTERSTORE) ─────────────────────────────────────

    [Fact]
    public async Task Search_MultiField_IntersectsCorrectly()
    {
        await _storage.SaveMetadataAsync("j1", Meta("j1", fields: F(("F1", "v1"), ("F2", "v2"))));
        await _storage.SaveMetadataAsync("j2", Meta("j2", fields: F(("F1", "v1"), ("F2", "other"))));
        await _storage.SaveMetadataAsync("j3", Meta("j3", fields: F(("F1", "other"), ("F2", "v2"))));

        var page = await _storage.SearchAsync(new JobSearchQuery
        {
            Filters = new Dictionary<string, string> { ["F1"] = "v1", ["F2"] = "v2" },
        });

        var item = Assert.Single(page.Items);
        Assert.Equal("j1", item.JobId);
    }

    // ── filter by jobId ───────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ByJobId_ReturnsExactJob()
    {
        await _storage.SaveMetadataAsync("x1", Meta("x1"));
        await _storage.SaveMetadataAsync("x2", Meta("x2"));

        var page = await _storage.SearchAsync(new JobSearchQuery { JobId = "x2" });

        var item = Assert.Single(page.Items);
        Assert.Equal("x2", item.JobId);
        Assert.Equal(1, page.TotalCount);
    }

    // ── pagination + order ────────────────────────────────────────────────────

    [Fact]
    public async Task Search_Pagination_RespectsPageSizeAndDescendingOrder()
    {
        var base_ = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 5; i++)
            await _storage.SaveMetadataAsync($"p{i}", Meta($"p{i}", createdAt: base_.AddMinutes(i)));

        var page1 = await _storage.SearchAsync(new JobSearchQuery { Page = 1, PageSize = 2 });
        var page2 = await _storage.SearchAsync(new JobSearchQuery { Page = 2, PageSize = 2 });

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal("p5", page1.Items[0].JobId); // newest first
        Assert.Equal("p4", page1.Items[1].JobId);
        Assert.Equal("p3", page2.Items[0].JobId);
        Assert.Equal("p2", page2.Items[1].JobId);
    }

    // ── state transition ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateState_MovesJobBetweenStateIndexes()
    {
        await _storage.SaveMetadataAsync("t1", Meta("t1", state: "Enqueued"));
        await _storage.UpdateStateAsync("t1", "Processing");

        var enqueued = await _storage.SearchAsync(new JobSearchQuery { State = "Enqueued" });
        var processing = await _storage.SearchAsync(new JobSearchQuery { State = "Processing" });

        Assert.Empty(enqueued.Items);
        var item = Assert.Single(processing.Items);
        Assert.Equal("t1", item.JobId);
    }

    [Fact]
    public async Task UpdateState_IsAtomic_NoDuplicateInStateIndexes()
    {
        await _storage.SaveMetadataAsync("t2", Meta("t2", state: "Enqueued"));
        await _storage.UpdateStateAsync("t2", "Succeeded");

        var counts = await _storage.CountByStateAsync(new JobSearchQuery());

        var total = counts.Values.Sum();
        Assert.Equal(1, total);
        Assert.True(counts.TryGetValue("Succeeded", out var cnt) && cnt == 1);
    }

    // ── delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesFromAllIndexes()
    {
        await _storage.SaveMetadataAsync("d1", Meta("d1", state: "Failed", fields: F(("K", "v"))));
        await _storage.DeleteMetadataAsync("d1");

        var global = await _storage.SearchAsync(new JobSearchQuery());
        var byState = await _storage.SearchAsync(new JobSearchQuery { State = "Failed" });
        var byField = await _storage.SearchAsync(new JobSearchQuery
        {
            Filters = new Dictionary<string, string> { ["K"] = "v" },
        });

        Assert.Empty(global.Items);
        Assert.Empty(byState.Items);
        Assert.Empty(byField.Items);
    }

    // ── CountByStateAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CountByState_NoFilters_UsesZCARDFastPath()
    {
        await _storage.SaveMetadataAsync("c1", Meta("c1", state: "Enqueued"));
        await _storage.SaveMetadataAsync("c2", Meta("c2", state: "Enqueued"));
        await _storage.SaveMetadataAsync("c3", Meta("c3", state: "Succeeded"));

        var counts = await _storage.CountByStateAsync(new JobSearchQuery());

        Assert.Equal(2, counts["Enqueued"]);
        Assert.Equal(1, counts["Succeeded"]);
        Assert.False(counts.ContainsKey("Failed"));
    }

    [Fact]
    public async Task CountByState_WithTypeFilter_UsesZINTERSTORE()
    {
        await _storage.SaveMetadataAsync("f1", Meta("f1", jobType: "TypeA", state: "Enqueued"));
        await _storage.SaveMetadataAsync("f2", Meta("f2", jobType: "TypeA", state: "Failed"));
        await _storage.SaveMetadataAsync("f3", Meta("f3", jobType: "TypeB", state: "Enqueued"));

        var counts = await _storage.CountByStateAsync(new JobSearchQuery { JobType = "TypeA" });

        Assert.Equal(1, counts["Enqueued"]);
        Assert.Equal(1, counts["Failed"]);
        Assert.False(counts.ContainsKey("Succeeded"));
    }

    // ── TTL ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveMetadata_AppliesTTL_KeyHasExpiry()
    {
        var db = _connection.GetDatabase();
        await _storage.SaveMetadataAsync("ttl1", Meta("ttl1"));

        var ttl = await db.KeyTimeToLiveAsync($"hangfire:jobsearch:v2:meta:ttl1");

        Assert.NotNull(ttl);
        Assert.True(ttl!.Value.TotalSeconds > 0);
        Assert.True(ttl!.Value.TotalMinutes <= 5);
    }

    // ── SearchChildrenBulkAsync ───────────────────────────────────────────────

    [Fact]
    public async Task SearchChildrenBulkAsync_ReturnsMappedByParentKey()
    {
        await _storage.SaveMetadataAsync("parent-1", Meta("parent-1", jobType: "Parent"));
        await _storage.SaveMetadataAsync("child-1a", Meta("child-1a", jobType: "Child", methodName: "Execute",
            fields: F(("ReportId", "r1"))));
        await _storage.SaveMetadataAsync("child-1b", Meta("child-1b", jobType: "Child", methodName: "Execute",
            fields: F(("ReportId", "r1"))));
        await _storage.SaveMetadataAsync("child-2a", Meta("child-2a", jobType: "Child", methodName: "Execute",
            fields: F(("ReportId", "r2"))));

        var result = await _storage.SearchChildrenBulkAsync(
            "Child", "Execute", "ReportId", new[] { "r1", "r2" }, 100);

        Assert.Equal(2, result["r1"].Count);
        Assert.Single(result["r2"]);
        Assert.Contains(result["r1"], c => c.JobId == "child-1a");
        Assert.Contains(result["r1"], c => c.JobId == "child-1b");
        Assert.Equal("child-2a", result["r2"][0].JobId);
    }

    // ── non-createdAt sort (maxSortWindow fallback) ───────────────────────────

    [Fact]
    public async Task Search_NonCreatedAtSort_OrdersByField()
    {
        var storageSmallWindow = new RedisJobSearchStorage(_connection, "hangfire:jobsearch:sw",
            expiry: TimeSpan.FromMinutes(5), maxSortWindow: 10);

        await storageSmallWindow.SaveMetadataAsync("sw1", Meta("sw1", fields: F(("Name", "charlie"))));
        await storageSmallWindow.SaveMetadataAsync("sw2", Meta("sw2", fields: F(("Name", "alpha"))));
        await storageSmallWindow.SaveMetadataAsync("sw3", Meta("sw3", fields: F(("Name", "bravo"))));

        var page = await storageSmallWindow.SearchAsync(new JobSearchQuery
        {
            SortBy = "Name",
            SortDescending = false,
        });

        Assert.Equal(3, page.Items.Count);
        Assert.Equal("sw2", page.Items[0].JobId); // alpha first
        Assert.Equal("sw3", page.Items[1].JobId); // bravo
        Assert.Equal("sw1", page.Items[2].JobId); // charlie
    }

    [Fact]
    public async Task Search_ByMethodName_ReturnsOnlyMatchingMethod()
    {
        var storage = new RedisJobSearchStorage(_connection, "hangfire:jobsearch:method", expiry: TimeSpan.FromMinutes(5));
        await storage.SaveMetadataAsync("m1", Meta("m1", jobType: "SharedWorker", methodName: "CreateAsync"));
        await storage.SaveMetadataAsync("m2", Meta("m2", jobType: "SharedWorker", methodName: "CancelAsync"));

        var page = await storage.SearchAsync(new JobSearchQuery { JobType = "SharedWorker", MethodName = "CreateAsync" });
        var item = Assert.Single(page.Items);
        Assert.Equal("m1", item.JobId);

        var counts = await storage.CountByStateAsync(new JobSearchQuery { JobType = "SharedWorker", MethodName = "CreateAsync" });
        Assert.Equal(1, counts["Enqueued"]);
    }

    [Fact]
    public async Task Search_NumericSort_OrdersNumerically()
    {
        var storage = new RedisJobSearchStorage(_connection, "hangfire:jobsearch:num", expiry: TimeSpan.FromMinutes(5));
        await storage.SaveMetadataAsync("n1", Meta("n1", fields: F(("Amount", "100"))));
        await storage.SaveMetadataAsync("n2", Meta("n2", fields: F(("Amount", "7"))));
        await storage.SaveMetadataAsync("n3", Meta("n3", fields: F(("Amount", "42"))));

        var page = await storage.SearchAsync(new JobSearchQuery { SortBy = "Amount", SortDescending = false });

        Assert.Equal(new[] { "n2", "n3", "n1" }, page.Items.Select(x => x.JobId));
    }

    [Fact]
    public async Task UpdateState_UpdatesReverseIndexForCleanExpiration()
    {
        var prefix = "hangfire:jobsearch:rev";
        var storage = new RedisJobSearchStorage(_connection, prefix, expiry: TimeSpan.FromMinutes(5));
        await storage.SaveMetadataAsync("r1", Meta("r1", state: "Enqueued"));
        await storage.UpdateStateAsync("r1", "Succeeded");

        var db = _connection.GetDatabase();
        var revMembers = (await db.SetMembersAsync($"{prefix}:v2:rev:r1")).Select(v => (string)v!).ToHashSet();

        Assert.DoesNotContain($"{prefix}:v2:idx:state:enqueued", revMembers);
        Assert.Contains($"{prefix}:v2:idx:state:succeeded", revMembers);
    }

    [Fact]
    public async Task ClusterHashTag_WrapsPrefixInBraces()
    {
        var storage = new RedisJobSearchStorage(
            _connection,
            keyPrefix: "hangfire:cluster",
            expiry: TimeSpan.FromMinutes(5),
            useClusterHashTag: true);

        await storage.SaveMetadataAsync("c1", Meta("c1", state: "Enqueued", fields: F(("Tenant", "t1"))));

        var db = _connection.GetDatabase();
        Assert.True(await db.KeyExistsAsync("{hangfire:cluster}:v2:meta:c1"));

        var counts = await storage.CountByStateAsync(new JobSearchQuery
        {
            JobType = "TestJob",
            Filters = { ["Tenant"] = "t1" }
        });
        Assert.Equal(1, counts["Enqueued"]);
    }

    [Fact]
    public async Task Methods_RespectCancellationToken()
    {
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _storage.SearchAsync(new JobSearchQuery(), cts.Token));
    }

    [Fact]
    public async Task Search_DateRangeMultiValueAndPrefix_WorkServerSide()
    {
        var storage = new RedisJobSearchStorage(_connection, "hangfire:jobsearch:adv", expiry: TimeSpan.FromMinutes(5));
        var t0 = DateTimeOffset.UtcNow.AddHours(-5);
        await storage.SaveMetadataAsync("a1", Meta("a1", createdAt: t0, fields: F(("OrderId", "ORD-100"), ("Tenant", "acme"))));
        await storage.SaveMetadataAsync("a2", Meta("a2", createdAt: t0.AddHours(2), fields: F(("OrderId", "ORD-109"), ("Tenant", "globex"))));
        await storage.SaveMetadataAsync("a3", Meta("a3", createdAt: t0.AddHours(4), fields: F(("OrderId", "INV-500"), ("Tenant", "initech"))));

        var prefixPage = await storage.SearchAsync(new JobSearchQuery { Filters = { ["OrderId"] = "ORD-10*" } });
        Assert.Equal(2, prefixPage.TotalCount);

        var multiPage = await storage.SearchAsync(new JobSearchQuery { Filters = { ["Tenant"] = "acme,initech" } });
        Assert.Equal(2, multiPage.TotalCount);

        var datePage = await storage.SearchAsync(new JobSearchQuery
        {
            CreatedFrom = t0.AddHours(1),
            CreatedTo = t0.AddHours(3)
        });
        var single = Assert.Single(datePage.Items);
        Assert.Equal("a2", single.JobId);

        var counts = await storage.CountByStateAsync(new JobSearchQuery
        {
            CreatedFrom = t0.AddHours(1),
            CreatedTo = t0.AddHours(3)
        });
        Assert.Equal(1, counts["Enqueued"]);
    }
}
