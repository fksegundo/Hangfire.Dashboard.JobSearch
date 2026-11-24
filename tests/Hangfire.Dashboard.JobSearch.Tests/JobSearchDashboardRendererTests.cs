namespace Hangfire.Dashboard.JobSearch.Tests;

public sealed class JobSearchDashboardRendererTests
{
    [Fact]
    public void Render_IncludesFilterableFieldInputs()
    {
        var model = CreateModel();

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("name=\"f_Field1\"", html);
        Assert.Contains("name=\"f_Field2\"", html);
    }

    [Fact]
    public void Render_IncludesDisplayColumns()
    {
        var model = CreateModel();

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("<th>Field1", html);
        Assert.Contains("<th>Field2", html);
    }

    [Fact]
    public void Render_IncludesNativeJobDetailsLink()
    {
        var model = CreateModel();

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("/hangfire/jobs/details/123", html);
    }

    [Fact]
    public void Render_PreservesFiltersInPaginationUrl()
    {
        var model = CreateModel();
        model.Query.Page = 1;
        model.Result.Page = 1;
        model.Result.PageSize = 1;
        model.Result.TotalCount = 2;
        model.Query.Sorts.Add(new JobSearchSort { Field = "CreatedAt", Descending = true });

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("f_Field1=12345678900", html);
        Assert.Contains("page=2", html);
        Assert.Contains("definition=WorkerA%7CRunJobAsync", html);
        Assert.Contains("name=\"sort\"", html);
    }

    [Fact]
    public void QueryMapper_MapsFieldsAndDefaults()
    {
        var options = new JobSearchOptions();
        var definition = new JobSearchDefinition
        {
            JobType = typeof(object),
            MethodName = "ToString"
        };
        definition.Fields.Add(new JobSearchField
        {
            Name = "Field1",
            Filterable = true,
            JsonPath = "$.Field1"
        });
        options.AddJobDefinition(definition);

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobId"] = "abc-123",
            ["f_Field1"] = "12345678900",
            ["page"] = "-1",
            ["pageSize"] = "0",
            ["state"] = "Succeeded"
        };

        var query = JobSearchDashboardQueryMapper.Map(
            key => values.TryGetValue(key, out var value) ? value : null,
            definition);

        Assert.Equal("12345678900", query.Filters["Field1"]);
        Assert.Equal("abc-123", query.JobId);
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Equal("Succeeded", query.State);
        Assert.Equal(typeof(object).FullName, query.JobType);
        Assert.Equal("ToString", query.MethodName);
        Assert.Empty(query.Sorts);
    }

    [Fact]
    public void QueryMapper_DefaultsStateToProcessingWhenMissing()
    {
        var definition = new JobSearchDefinition
        {
            JobType = typeof(string),
            MethodName = "ToString"
        };

        var query = JobSearchDashboardQueryMapper.Map(_ => null, definition);

        Assert.Equal("Processing", query.State);
    }

    [Fact]
    public void Render_UsesIconSortLinks()
    {
        var model = CreateModel();
        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("title=\"Sort ascending\"", html);
        Assert.Contains("title=\"Add descending sort\"", html);
    }

    [Fact]
    public void Render_RespectsSortableFieldsAndValidSelectHtmlAndPageSize()
    {
        var model = CreateModel();
        model.Query.PageSize = 50;
        model.Result.PageSize = 50;
        model.Result.TotalCount = 100;
        model.SortableFields = new HashSet<string>(StringComparer.Ordinal) { "Field1" };

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("<th>Field1<span class=\"hfjs-sort-links\">", html);
        Assert.Contains("<th>Field2</th>", html);
        Assert.DoesNotContain("\"\" onchange=", html);
        Assert.DoesNotContain("name=\"state\"\">", html);
        Assert.Contains("pageSize=50", html);
    }

    [Fact]
    public void ViewModel_IncludesAwaitingStateAndSortableFields()
    {
        var options = new JobSearchOptions();
        var definition = new JobSearchDefinition
        {
            JobType = typeof(object),
            MethodName = "ToString",
            Fields =
            {
                new JobSearchField { Name = "A", JsonPath = "$.A", DisplayInList = true, Sortable = true },
                new JobSearchField { Name = "B", JsonPath = "$.B", DisplayInList = true, Sortable = false }
            }
        };
        options.AddJobDefinition(definition);

        var vm = JobSearchDashboardViewModel.From(
            "/hangfire",
            new JobSearchQuery(),
            options,
            new JobSearchResultPage(),
            definition,
            new Dictionary<string, int> { ["Awaiting"] = 3 });

        Assert.Contains(vm.States, s => s.Value == "Awaiting" && s.Label == "Awaiting (3)");
        Assert.Contains(vm.States, s => s.Value == "all" && s.Label == "All (3)");
        Assert.Contains("A", vm.SortableFields);
        Assert.DoesNotContain("B", vm.SortableFields);
    }

    [Fact]
    public void QueryMapper_MapsAllStateToNull()
    {
        var values = new Dictionary<string, string> { ["state"] = "all" };
        var query = JobSearchDashboardQueryMapper.Map(
            k => values.TryGetValue(k, out var v) ? v : null,
            selectedDefinition: null);

        Assert.Null(query.State);
    }

    [Fact]
    public void Render_IncludesStateTabsExportLinksQuickActionsAndJoinSummary()
    {
        var model = CreateModel();
        model.Result.Items[0].LastError = "TimeoutException: upstream timed out";
        model.Result.Items[0].Duration = TimeSpan.FromMilliseconds(450);
        model.Result.Items[0].ChildrenLabel = "invoices";
        model.Result.Items[0].Children = new[]
        {
            new JobSearchResult { JobId = "c1", State = "Succeeded", MethodName = "Run" },
            new JobSearchResult { JobId = "c2", State = "Failed", MethodName = "Run" }
        };

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.Contains("class=\"hfjs-state-tabs\"", html);
        Assert.Contains("export=csv", html);
        Assert.Contains("export=json", html);
        Assert.Contains("Requeue Selected", html);
        Assert.Contains("Delete Selected", html);
        Assert.Contains("hfjs-results-region", html);
        Assert.Contains("TimeoutException: upstream timed out", html);
        Assert.Contains("450ms", html);
        Assert.Contains("1 succeeded", html);
        Assert.Contains("1 failed", html);
    }

    [Fact]
    public void Render_HidesQuickActionsWhenReadOnly()
    {
        var model = CreateModel();
        model.IsReadOnly = true;

        var html = JobSearchDashboardRenderer.Render(model);

        Assert.DoesNotContain("Requeue Selected", html);
        Assert.DoesNotContain("data-job-id=", html);
        Assert.DoesNotContain("id=\"hfjs-select-all\"", html);
    }

    [Fact]
    public void ExportHelpers_GenerateValidCsvAndJson()
    {
        var items = new[]
        {
            new JobSearchResult
            {
                JobId = "j1",
                State = "Succeeded",
                JobType = "WorkerA",
                MethodName = "Run",
                CreatedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
                Fields = new Dictionary<string, string?> { ["Tenant"] = "Acme, Inc." }
            }
        };

        var csv = JobSearchDashboardRazorDispatcher.BuildCsvExport(items, new[] { "Tenant" });
        Assert.Contains("JobId,State,JobType,MethodName,CreatedAt,Tenant", csv);
        Assert.Contains("\"Acme, Inc.\"", csv);

        var json = JobSearchDashboardRazorDispatcher.BuildJsonExport(items);
        Assert.Contains("\"jobId\":\"j1\"", json);
        Assert.Contains("\"Tenant\":\"Acme, Inc.\"", json);
    }

    private static JobSearchDashboardViewModel CreateModel()
    {
        return new JobSearchDashboardViewModel
        {
            PathBase = "/hangfire",
            Query = new JobSearchQuery
            {
                Page = 1,
                PageSize = 20,
                State = "Processing",
                Filters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Field1"] = "12345678900",
                    ["Field2"] = "42"
                }
            },
            SelectedDefinitionKey = "WorkerA|RunJobAsync",
            Definitions = new[]
            {
                new JobSearchDashboardViewModel.DefinitionOption
                {
                    Key = "WorkerA|RunJobAsync",
                    Label = "WorkerA.RunJobAsync"
                }
            },
            FilterableFields = new[] { "Field1", "Field2" },
            DisplayFields = new[] { "Field1", "Field2" },
            JobTypes = new[] { "WorkerA" },
            States = new[]
            {
                new JobSearchDashboardViewModel.StateOption { Value = "Processing", Label = "Processing (5)" },
                new JobSearchDashboardViewModel.StateOption { Value = "Succeeded", Label = "Succeeded (10)" }
            },
            Result = new JobSearchResultPage
            {
                Page = 1,
                PageSize = 20,
                TotalCount = 1,
                Items = new[]
                {
                    new JobSearchResult
                    {
                        JobId = "123",
                        State = "Succeeded",
                        JobType = "WorkerA",
                        MethodName = "RunJobAsync",
                        CreatedAt = DateTimeOffset.Parse("2026-05-03T12:00:00Z"),
                        Fields = new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            ["Field1"] = "12345678900",
                            ["Field2"] = "42"
                        }
                    }
                }
            }
        };
    }
}
