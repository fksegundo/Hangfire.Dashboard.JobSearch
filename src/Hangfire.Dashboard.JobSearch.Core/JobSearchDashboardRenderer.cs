using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Hangfire.Dashboard.JobSearch;

internal static class JobSearchDashboardRenderer
{
    private static readonly int[] PageSizeOptions = { 20, 50, 100 };

    public static string Render(JobSearchDashboardViewModel model)
    {
        return RenderBody(model);
    }

    public static string RenderBody(JobSearchDashboardViewModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var html = new StringBuilder();
        RenderStyles(html);

        html.Append("<h1 class=\"page-header\">").Append(E(model.MenuTitle)).Append("</h1>");
        RenderLiveBar(html, model);
        RenderSortSummary(html, model);
        RenderFilterForm(html, model);

        html.Append("<div id=\"hfjs-results-region\">");
        RenderStateTabs(html, model);
        RenderBulkActionsBar(html, model);
        RenderResultsTable(html, model);
        RenderPager(html, model);
        html.Append("</div>");

        RenderClientScript(html, model);
        return html.ToString();
    }

    private static void RenderStyles(StringBuilder html)
    {
        html.Append("<style>");
        html.Append(":root{--hfjs-bg:#ffffff;--hfjs-border:#d9dee8;--hfjs-head:#f7f9fc;--hfjs-text:#1f2937;--hfjs-muted:#4b5563;--hfjs-chip:#eef2f7;--hfjs-link:#3558a8;--hfjs-danger:#dc2626;}");
        html.Append("@media (prefers-color-scheme: dark){:root{--hfjs-bg:#1b1f27;--hfjs-border:#364152;--hfjs-head:#252d3a;--hfjs-text:#e5e7eb;--hfjs-muted:#cbd5e1;--hfjs-chip:#2f3b4d;--hfjs-link:#9ab4ff;--hfjs-danger:#f87171;}}");
        html.Append(".hfjs-toolbar{display:grid;grid-template-columns:repeat(3,minmax(180px,1fr));gap:10px;align-items:end;margin-bottom:12px;}");
        html.Append(".hfjs-filters{display:grid;grid-template-columns:repeat(4,minmax(180px,1fr));gap:10px;margin-bottom:12px;}");
        html.Append(".hfjs-field label{display:block;font-weight:600;margin-bottom:4px;color:var(--hfjs-muted);font-size:12px;}");
        html.Append(".hfjs-sort-chip{display:inline-block;padding:2px 8px;background:var(--hfjs-chip);border-radius:12px;margin-right:6px;font-size:12px;color:var(--hfjs-text);}");
        html.Append(".hfjs-state-tabs{display:flex;flex-wrap:wrap;gap:6px;margin:14px 0 10px 0;}");
        html.Append(".hfjs-state-tab{display:inline-flex;align-items:center;gap:6px;padding:5px 12px;border-radius:16px;border:1px solid var(--hfjs-border);background:var(--hfjs-bg);color:var(--hfjs-text);text-decoration:none;font-size:12px;font-weight:600;}");
        html.Append(".hfjs-state-tab:hover{background:var(--hfjs-chip);text-decoration:none;color:var(--hfjs-text);}");
        html.Append(".hfjs-state-tab.active{background:var(--hfjs-link);border-color:var(--hfjs-link);color:#fff;}");
        html.Append(".hfjs-state-tab-count{padding:1px 6px;border-radius:10px;background:rgba(0,0,0,0.08);font-size:11px;}");
        html.Append(".hfjs-state-tab.active .hfjs-state-tab-count{background:rgba(255,255,255,0.22);color:#fff;}");
        html.Append(".hfjs-form-actions{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:8px;margin-bottom:8px;}");
        html.Append(".hfjs-export-group{display:flex;gap:6px;align-items:center;}");
        html.Append(".hfjs-bulk-bar{display:flex;align-items:center;gap:8px;margin-bottom:8px;padding:6px 10px;border:1px solid var(--hfjs-border);border-radius:6px;background:var(--hfjs-head);font-size:12px;color:var(--hfjs-text);}");
        html.Append(".hfjs-table-wrap{border:1px solid var(--hfjs-border);border-radius:6px;overflow:auto;background:var(--hfjs-bg);width:100%;}");
        html.Append(".hfjs-table{width:100%;min-width:1200px;border-collapse:collapse;font-size:13px;}");
        html.Append(".hfjs-table th,.hfjs-table td{padding:8px 10px;border-bottom:1px solid var(--hfjs-border);vertical-align:top;color:var(--hfjs-text);}");
        html.Append(".hfjs-table th{background:var(--hfjs-head);font-weight:700;position:sticky;top:0;white-space:nowrap;}");
        html.Append(".hfjs-sort-links{display:inline-flex;align-items:center;gap:1px;margin-left:4px;white-space:nowrap;}");
        html.Append(".hfjs-sort-links a{font-size:11px;color:var(--hfjs-link);text-decoration:none;display:inline-block;padding:1px 2px;border-radius:4px;}");
        html.Append(".hfjs-sort-links a:hover{background:var(--hfjs-chip);}");
        html.Append(".hfjs-nowrap{white-space:nowrap;}");
        html.Append(".hfjs-details-cell{display:flex;align-items:center;gap:6px;flex-wrap:wrap;white-space:nowrap;}");
        html.Append(".hfjs-pager{margin-top:12px;display:flex;justify-content:space-between;align-items:center;color:var(--hfjs-text);gap:12px;flex-wrap:wrap;}");
        html.Append(".hfjs-pager .pagination{margin:0;}");
        html.Append(".hfjs-pager .pagination>li>a{color:var(--hfjs-link);background:var(--hfjs-bg);border-color:var(--hfjs-border);}");
        html.Append(".hfjs-pager .pagination>li.active>a{background:var(--hfjs-link);border-color:var(--hfjs-link);color:#fff;}");
        html.Append(".hfjs-toolbar .form-control,.hfjs-filters .form-control{max-width:100%;}");
        html.Append(".hfjs-live-bar{display:flex;align-items:center;gap:10px;font-size:12px;color:var(--hfjs-muted);margin-bottom:8px;}");
        html.Append(".hfjs-toggle-btn,.hfjs-action-btn{cursor:pointer;background:var(--hfjs-chip);border:1px solid var(--hfjs-border);color:var(--hfjs-text);padding:2px 10px;border-radius:12px;font-size:12px;line-height:1.4;}");
        html.Append(".hfjs-toggle-btn:hover,.hfjs-action-btn:hover{background:var(--hfjs-border);}");
        html.Append(".hfjs-action-btn-danger{color:var(--hfjs-danger);}");
        html.Append(".hfjs-row-actions{display:inline-flex;gap:4px;margin-left:6px;}");
        html.Append(".hfjs-advanced{margin-bottom:12px;border:1px solid var(--hfjs-border);border-radius:6px;background:var(--hfjs-head);}");
        html.Append(".hfjs-advanced>summary{padding:8px 12px;cursor:pointer;font-weight:600;color:var(--hfjs-text);user-select:none;list-style:none;display:flex;align-items:center;}");
        html.Append(".hfjs-advanced>summary::-webkit-details-marker{display:none;}");
        html.Append(".hfjs-advanced>summary::before{content:'\\25B6';display:inline-block;margin-right:6px;font-size:9px;transition:transform 0.15s;}");
        html.Append(".hfjs-advanced[open]>summary::before{transform:rotate(90deg);}");
        html.Append(".hfjs-advanced>.hfjs-filters{padding:10px 12px;margin-bottom:0;background:var(--hfjs-bg);border-top:1px solid var(--hfjs-border);border-radius:0 0 6px 6px;}");
        html.Append(".hfjs-active-count{margin-left:8px;padding:1px 8px;background:var(--hfjs-link);color:#fff;border-radius:10px;font-size:11px;font-weight:normal;}");
        html.Append(".hfjs-expand{cursor:pointer;background:none;border:none;padding:0 4px;color:var(--hfjs-link);font-size:11px;display:inline-block;transition:transform 0.15s;outline:none;}");
        html.Append(".hfjs-expand:focus-visible{outline:2px solid var(--hfjs-link);border-radius:3px;}");
        html.Append(".hfjs-expand:hover{background:var(--hfjs-chip);border-radius:3px;}");
        html.Append(".hfjs-expand[data-expanded='true']{transform:rotate(90deg);}");
        html.Append(".hfjs-child-count{background:var(--hfjs-chip);padding:1px 8px;border-radius:10px;font-size:11px;margin-right:4px;color:var(--hfjs-text);}");
        html.Append(".hfjs-child-summary{font-size:10px;padding:1px 6px;border-radius:8px;margin-right:4px;}");
        html.Append(".hfjs-child-row>td{background:var(--hfjs-head);padding:6px 10px 6px 30px;font-size:12px;}");
        html.Append(".hfjs-child-arrow{color:var(--hfjs-muted);margin-right:6px;}");
        html.Append(".hfjs-child-id{background:var(--hfjs-chip);padding:1px 6px;border-radius:3px;font-family:monospace;font-size:11px;margin-right:8px;}");
        html.Append(".hfjs-child-row .hfjs-state{margin-right:8px;}");
        html.Append(".hfjs-child-method,.hfjs-child-time,.hfjs-child-field{margin-right:10px;color:var(--hfjs-muted);font-size:11px;}");
        html.Append(".hfjs-child-field b{color:var(--hfjs-text);font-weight:600;}");
        html.Append(".hfjs-child-view{margin-left:8px;color:var(--hfjs-link);}");
        html.Append(".hfjs-child-count-empty{opacity:0.55;font-style:italic;}");
        html.Append(".hfjs-error-snippet{display:block;margin-top:3px;font-size:11px;color:var(--hfjs-danger);max-width:260px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}");
        html.Append(".hfjs-duration{display:inline-block;margin-left:4px;font-size:11px;color:var(--hfjs-muted);}");
        html.Append(".hfjs-state{display:inline-block;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:600;line-height:1.4;text-transform:uppercase;letter-spacing:0.3px;}");
        html.Append(".hfjs-state-processing{background:#dbeafe;color:#1e40af;}");
        html.Append(".hfjs-state-enqueued{background:#e5e7eb;color:#374151;}");
        html.Append(".hfjs-state-succeeded{background:#d1fae5;color:#065f46;}");
        html.Append(".hfjs-state-failed{background:#fee2e2;color:#991b1b;}");
        html.Append(".hfjs-state-scheduled{background:#fed7aa;color:#9a3412;}");
        html.Append(".hfjs-state-deleted{background:#d1d5db;color:#1f2937;}");
        html.Append(".hfjs-state-awaiting{background:#fef3c7;color:#92400e;}");
        html.Append(".hfjs-state-unknown{background:var(--hfjs-chip);color:var(--hfjs-text);}");
        html.Append("@media (prefers-color-scheme: dark){");
        html.Append(".hfjs-state-processing{background:#1e3a8a;color:#bfdbfe;}");
        html.Append(".hfjs-state-enqueued{background:#374151;color:#e5e7eb;}");
        html.Append(".hfjs-state-succeeded{background:#064e3b;color:#a7f3d0;}");
        html.Append(".hfjs-state-failed{background:#7f1d1d;color:#fecaca;}");
        html.Append(".hfjs-state-scheduled{background:#7c2d12;color:#fed7aa;}");
        html.Append(".hfjs-state-deleted{background:#374151;color:#9ca3af;}");
        html.Append(".hfjs-state-awaiting{background:#78350f;color:#fde68a;}");
        html.Append("}");
        html.Append("</style>");
    }

    private static void RenderLiveBar(StringBuilder html, JobSearchDashboardViewModel model)
    {
        var interval = model.AutoRefreshIntervalSeconds;
        var statusText = interval > 0 ? $"every {interval}s" : "paused";
        var btnText = interval > 0 ? "Pause" : "Resume";
        html.Append("<div class=\"hfjs-live-bar\"><span>Live refresh: <span id=\"hfjs-status\">")
            .Append(E(statusText))
            .Append("</span></span><button type=\"button\" id=\"hfjs-toggle\" class=\"hfjs-toggle-btn\" data-interval=\"")
            .Append(interval)
            .Append("\">")
            .Append(btnText)
            .Append("</button></div>");
    }

    private static void RenderStateTabs(StringBuilder html, JobSearchDashboardViewModel model)
    {
        if (model.States.Count == 0)
        {
            return;
        }

        var activeState = string.IsNullOrWhiteSpace(model.Query.State) ? "all" : model.Query.State!;
        html.Append("<div class=\"hfjs-state-tabs\" role=\"tablist\">");
        foreach (var state in model.States)
        {
            var isActive = string.Equals(state.Value, activeState, StringComparison.OrdinalIgnoreCase);
            var tabUrl = BuildStateUrl(model, state.Value);
            html.Append("<a class=\"hfjs-state-tab")
                .Append(isActive ? " active" : string.Empty)
                .Append("\" href=\"")
                .Append(E(tabUrl))
                .Append("\">")
                .Append(E(state.Label))
                .Append("</a>");
        }
        html.Append("</div>");
    }

    private static void RenderBulkActionsBar(StringBuilder html, JobSearchDashboardViewModel model)
    {
        if (model.IsReadOnly || !model.Result.Items.Any())
        {
            return;
        }

        var actionsUrl = $"{NormalizePathBase(model.PathBase)}{model.RoutePath}/actions";
        html.Append("<div class=\"hfjs-bulk-bar\" data-actions-url=\"")
            .Append(E(actionsUrl))
            .Append("\">")
            .Append("<span id=\"hfjs-selected-count\">0 selected</span>")
            .Append("<button type=\"button\" class=\"hfjs-action-btn hfjs-bulk-action\" data-action=\"requeue\">Requeue Selected</button>")
            .Append("<button type=\"button\" class=\"hfjs-action-btn hfjs-action-btn-danger hfjs-bulk-action\" data-action=\"delete\">Delete Selected</button>")
            .Append("</div>");
    }

    private static void RenderSortSummary(StringBuilder html, JobSearchDashboardViewModel model)
    {
        if (model.Query.Sorts.Count == 0)
        {
            return;
        }

        html.Append("<div class=\"mb-2\">");
        foreach (var sort in model.Query.Sorts)
        {
            html.Append("<span class=\"hfjs-sort-chip\">")
                .Append(E(sort.Field))
                .Append(sort.Descending ? " desc" : " asc")
                .Append("</span>");
        }

        html.Append("</div>");
    }

    private static void RenderFilterForm(StringBuilder html, JobSearchDashboardViewModel model)
    {
        html.Append("<form method=\"get\">");
        html.Append("<div class=\"hfjs-toolbar\">");
        RenderInput(html, "Job ID", "jobId", model.Query.JobId ?? string.Empty);
        RenderSelect(html, "Definition", "definition", model.Definitions.Select(d => (d.Key, d.Label)), model.SelectedDefinitionKey, autoSubmit: true);
        RenderSelect(
            html,
            "Page Size",
            "pageSize",
            PageSizeOptions.Select(s => (s.ToString(CultureInfo.InvariantCulture), $"{s} per page")),
            (model.Query.PageSize > 0 ? model.Query.PageSize : 20).ToString(CultureInfo.InvariantCulture),
            autoSubmit: true);
        html.Append("</div>");

        var activeFieldCount = model.FilterableFields.Count(f =>
            model.Query.Filters.TryGetValue(f, out var v) && !string.IsNullOrEmpty(v));
        var activeDateCount = (model.Query.CreatedFrom.HasValue ? 1 : 0) + (model.Query.CreatedTo.HasValue ? 1 : 0);
        var activeCount = activeFieldCount + activeDateCount;
        var isOpen = activeCount > 0;

        html.Append("<details class=\"hfjs-advanced\"");
        if (isOpen) html.Append(" open");
        html.Append("><summary>Filters");
        if (activeCount > 0)
        {
            html.Append("<span class=\"hfjs-active-count\">")
                .Append(activeCount)
                .Append(" active</span>");
        }
        html.Append("</summary>");

        html.Append("<div class=\"hfjs-filters\">");
        foreach (var field in model.FilterableFields)
        {
            model.Query.Filters.TryGetValue(field, out var value);
            RenderInput(html, field, $"f_{field}", value ?? string.Empty, placeholder: "exact, a,b, or prefix*");
        }

        RenderInput(
            html,
            "Created From (UTC)",
            "from",
            model.Query.CreatedFrom.HasValue ? model.Query.CreatedFrom.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) : string.Empty,
            inputType: "datetime-local");
        RenderInput(
            html,
            "Created To (UTC)",
            "to",
            model.Query.CreatedTo.HasValue ? model.Query.CreatedTo.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) : string.Empty,
            inputType: "datetime-local");

        html.Append("</div></details>");

        RenderHiddenInput(html, "state", string.IsNullOrWhiteSpace(model.Query.State) ? "all" : model.Query.State!);
        RenderHiddenInput(html, "sort", BuildSortQuery(model.Query.Sorts));

        html.Append("<div class=\"hfjs-form-actions\"><div>");
        html.Append("<button class=\"btn btn-primary\" type=\"submit\">Apply Filters</button> ");
        html.Append("<a class=\"btn btn-default\" href=\"").Append(E(BuildResetUrl(model))).Append("\">Reset</a>");
        html.Append("</div><div class=\"hfjs-export-group\">");
        html.Append("<a class=\"btn btn-default btn-sm\" href=\"").Append(E(BuildExportUrl(model, "csv"))).Append("\">Export CSV</a>");
        html.Append("<a class=\"btn btn-default btn-sm\" href=\"").Append(E(BuildExportUrl(model, "json"))).Append("\">Export JSON</a>");
        html.Append("</div></div>");
        html.Append("</form>");
    }

    private static void RenderResultsTable(StringBuilder html, JobSearchDashboardViewModel model)
    {
        html.Append("<div class=\"hfjs-table-wrap\"><table class=\"hfjs-table\"><thead><tr>");
        if (!model.IsReadOnly)
        {
            html.Append("<th style=\"width:30px;\"><input type=\"checkbox\" id=\"hfjs-select-all\" title=\"Select all\"/></th>");
        }
        RenderHeader(html, model, "Job Id", "JobId");
        RenderHeader(html, model, "State", "State");
        RenderHeader(html, model, "Method", "MethodName");
        RenderHeader(html, model, "Created At", "CreatedAt");

        foreach (var field in model.DisplayFields)
        {
            var sortable = model.SortableFields.Contains(field);
            RenderHeader(html, model, field, field, sortable);
        }

        html.Append("<th>Details</th></tr></thead><tbody>");

        var columnCount = (model.IsReadOnly ? 5 : 6) + model.DisplayFields.Count;

        foreach (var item in model.Result.Items)
        {
            var hasJoin = item.Children is not null;
            var hasChildren = hasJoin && item.Children!.Count > 0;
            var parentId = E(item.JobId);

            html.Append("<tr>");
            if (!model.IsReadOnly)
            {
                html.Append("<td><input type=\"checkbox\" class=\"hfjs-row-select\" value=\"")
                    .Append(parentId)
                    .Append("\"/></td>");
            }

            html.Append("<td>");
            if (hasChildren)
            {
                html.Append("<button type=\"button\" class=\"hfjs-expand\" data-target=\"")
                    .Append(parentId)
                    .Append("\" data-expanded=\"false\" title=\"Toggle children\">&#9654;</button> ");
            }
            html.Append(parentId).Append("</td>");

            html.Append("<td>").Append(RenderStateBadge(item.State));
            if (item.Duration.HasValue)
            {
                html.Append("<span class=\"hfjs-duration\">").Append(E(FormatDuration(item.Duration.Value))).Append("</span>");
            }
            if (!string.IsNullOrWhiteSpace(item.LastError))
            {
                html.Append("<span class=\"hfjs-error-snippet\" title=\"")
                    .Append(E(item.LastError))
                    .Append("\">")
                    .Append(E(item.LastError))
                    .Append("</span>");
            }
            html.Append("</td>")
                .Append("<td class=\"hfjs-nowrap\">").Append(E(item.MethodName)).Append("</td>")
                .Append("<td class=\"hfjs-nowrap\">").Append(E(item.CreatedAt.ToString("u"))).Append("</td>");

            foreach (var field in model.DisplayFields)
            {
                item.Fields.TryGetValue(field, out var value);
                html.Append("<td class=\"hfjs-nowrap\">").Append(E(value)).Append("</td>");
            }

            html.Append("<td><div class=\"hfjs-details-cell\">");
            if (hasJoin)
            {
                RenderJoinBadgeAndSummary(html, item, hasChildren);
            }
            html.Append("<a href=\"")
                .Append(E($"{NormalizePathBase(model.PathBase)}/jobs/details/{item.JobId}"))
                .Append("\">View</a>");

            if (!model.IsReadOnly)
            {
                html.Append("<span class=\"hfjs-row-actions\">")
                    .Append("<button type=\"button\" class=\"hfjs-action-btn hfjs-row-action\" data-action=\"requeue\" data-job-id=\"")
                    .Append(parentId)
                    .Append("\" title=\"Requeue job\">↻</button>")
                    .Append("<button type=\"button\" class=\"hfjs-action-btn hfjs-action-btn-danger hfjs-row-action\" data-action=\"delete\" data-job-id=\"")
                    .Append(parentId)
                    .Append("\" title=\"Delete job\">✕</button>")
                    .Append("</span>");
            }

            html.Append("</div></td></tr>");

            if (hasChildren)
            {
                RenderChildRows(html, model, item, columnCount);
            }
        }

        if (!model.Result.Items.Any())
        {
            html.Append("<tr><td colspan=\"")
                .Append(columnCount)
                .Append("\">No jobs found.</td></tr>");
        }

        html.Append("</tbody></table></div>");
    }

    private static void RenderJoinBadgeAndSummary(StringBuilder html, JobSearchResult item, bool hasChildren)
    {
        var label = string.IsNullOrEmpty(item.ChildrenLabel) ? "children" : item.ChildrenLabel;
        var countClass = hasChildren ? "hfjs-child-count" : "hfjs-child-count hfjs-child-count-empty";
        var lookupTitle = item.ChildrenLookupField is null
            ? string.Empty
            : string.IsNullOrEmpty(item.ChildrenLookupValue)
                ? $"Join field '{item.ChildrenLookupField}' is empty on this job"
                : $"Searched by {item.ChildrenLookupField}={item.ChildrenLookupValue}";

        html.Append("<span class=\"").Append(countClass).Append("\"");
        if (!string.IsNullOrEmpty(lookupTitle))
        {
            html.Append(" title=\"").Append(E(lookupTitle)).Append("\"");
        }
        html.Append(">")
            .Append(item.Children!.Count)
            .Append(' ')
            .Append(E(label))
            .Append("</span>");

        if (hasChildren)
        {
            var grouped = item.Children!
                .GroupBy(c => c.State ?? "Unknown", StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count());
            foreach (var group in grouped)
            {
                html.Append("<span class=\"hfjs-child-summary ")
                    .Append(StateCssClass(group.Key))
                    .Append("\">")
                    .Append(group.Count())
                    .Append(' ')
                    .Append(E(group.Key.ToLowerInvariant()))
                    .Append("</span>");
            }
        }
    }

    private static void RenderChildRows(
        StringBuilder html,
        JobSearchDashboardViewModel model,
        JobSearchResult parent,
        int columnCount)
    {
        var parentId = E(parent.JobId);
        var basePath = NormalizePathBase(model.PathBase);

        foreach (var child in parent.Children!)
        {
            html.Append("<tr class=\"hfjs-child-row\" data-parent=\"")
                .Append(parentId)
                .Append("\" style=\"display:none;\"><td colspan=\"")
                .Append(columnCount)
                .Append("\">");

            html.Append("<span class=\"hfjs-child-arrow\">&#8627;</span>");
            html.Append("<code class=\"hfjs-child-id\">").Append(E(child.JobId)).Append("</code>");
            html.Append(RenderStateBadge(child.State));
            html.Append("<span class=\"hfjs-child-method\">").Append(E(child.MethodName)).Append("</span>");
            html.Append("<span class=\"hfjs-child-time\">").Append(E(child.CreatedAt.ToString("u"))).Append("</span>");

            foreach (var pair in child.Fields)
            {
                if (string.IsNullOrEmpty(pair.Value))
                {
                    continue;
                }

                html.Append("<span class=\"hfjs-child-field\"><b>")
                    .Append(E(pair.Key))
                    .Append(":</b> ")
                    .Append(E(pair.Value))
                    .Append("</span>");
            }

            html.Append("<a class=\"hfjs-child-view\" href=\"")
                .Append(E($"{basePath}/jobs/details/{child.JobId}"))
                .Append("\">View</a>");
            html.Append("</td></tr>");
        }
    }

    private static void RenderPager(StringBuilder html, JobSearchDashboardViewModel model)
    {
        var totalPages = model.Result.PageSize <= 0
            ? 1
            : Math.Max(1, (int)Math.Ceiling((double)model.Result.TotalCount / model.Result.PageSize));

        html.Append("<div class=\"hfjs-pager\">");
        html.Append("<div>Total: ").Append(model.Result.TotalCount).Append(" jobs</div>");
        html.Append("<ul class=\"pagination\">");
        html.Append("<li").Append(model.Result.Page <= 1 ? " class=\"disabled\"" : string.Empty).Append(">");
        html.Append("<a href=\"").Append(E(BuildPageUrl(model, Math.Max(1, model.Result.Page - 1)))).Append("\">&laquo;</a></li>");
        foreach (var page in GetPageWindow(model.Result.Page, totalPages))
        {
            html.Append("<li").Append(page == model.Result.Page ? " class=\"active\"" : string.Empty).Append(">");
            html.Append("<a href=\"").Append(E(BuildPageUrl(model, page))).Append("\">").Append(page).Append("</a></li>");
        }

        html.Append("<li").Append(model.Result.Page >= totalPages ? " class=\"disabled\"" : string.Empty).Append(">");
        html.Append("<a href=\"").Append(E(BuildPageUrl(model, Math.Min(totalPages, model.Result.Page + 1)))).Append("\">&raquo;</a></li>");
        html.Append("</ul></div>");
    }

    private static void RenderHeader(StringBuilder html, JobSearchDashboardViewModel model, string label, string field, bool sortable = true)
    {
        html.Append("<th>").Append(E(label));
        if (sortable)
        {
            html.Append("<span class=\"hfjs-sort-links\">");
            html.Append("<a title=\"Sort ascending\" href=\"").Append(E(BuildSortUrl(model, field, false, append: false))).Append("\">↑</a>");
            html.Append("<a title=\"Sort descending\" href=\"").Append(E(BuildSortUrl(model, field, true, append: false))).Append("\">↓</a>");
            html.Append("<a title=\"Add ascending sort\" href=\"").Append(E(BuildSortUrl(model, field, false, append: true))).Append("\">⇧↑</a>");
            html.Append("<a title=\"Add descending sort\" href=\"").Append(E(BuildSortUrl(model, field, true, append: true))).Append("\">⇧↓</a>");
            html.Append("</span>");
        }
        html.Append("</th>");
    }

    private static void RenderSelect(
        StringBuilder html,
        string label,
        string name,
        IEnumerable<(string Value, string Label)> options,
        string? selected,
        bool includeAll = false,
        bool autoSubmit = false)
    {
        html.Append("<div class=\"hfjs-field\"><label>")
            .Append(E(label))
            .Append("</label><select class=\"form-control\" name=\"")
            .Append(E(name))
            .Append("\"")
            .Append(autoSubmit ? " onchange=\"this.form.submit()\"" : string.Empty)
            .Append(">");
        if (includeAll)
        {
            AppendOption(html, string.Empty, selected, "All");
        }

        foreach (var option in options)
        {
            AppendOption(html, option.Value, selected, option.Label);
        }

        html.Append("</select></div>");
    }

    private static void RenderInput(
        StringBuilder html,
        string label,
        string name,
        string value,
        string inputType = "text",
        string? placeholder = null)
    {
        html.Append("<div class=\"hfjs-field\"><label>")
            .Append(E(label))
            .Append("</label><input type=\"")
            .Append(E(inputType))
            .Append("\" class=\"form-control\" name=\"")
            .Append(E(name))
            .Append("\" value=\"")
            .Append(E(value))
            .Append("\"");
        if (!string.IsNullOrEmpty(placeholder))
        {
            html.Append(" placeholder=\"").Append(E(placeholder)).Append("\"");
        }
        html.Append("/></div>");
    }

    private static string BuildSortUrl(JobSearchDashboardViewModel model, string field, bool desc, bool append)
    {
        var sorts = append ? new List<JobSearchSort>(model.Query.Sorts) : new List<JobSearchSort>();
        sorts.RemoveAll(s => string.Equals(s.Field, field, StringComparison.Ordinal));
        sorts.Add(new JobSearchSort { Field = field, Descending = desc });
        return BuildPageUrl(model, 1, sorts);
    }

    private static string BuildStateUrl(JobSearchDashboardViewModel model, string stateValue)
    {
        return BuildPageUrl(model, 1, stateOverride: stateValue);
    }

    private static string BuildExportUrl(JobSearchDashboardViewModel model, string format)
    {
        var baseUrl = BuildPageUrl(model, 1);
        var sep = baseUrl.IndexOf('?') >= 0 ? "&" : "?";
        return $"{baseUrl}{sep}export={Uri.EscapeDataString(format)}";
    }

    private static string BuildPageUrl(
        JobSearchDashboardViewModel model,
        int page,
        List<JobSearchSort>? sortsOverride = null,
        string? stateOverride = null)
    {
        var query = new StringBuilder();
        AppendQuery(query, "page", page.ToString(CultureInfo.InvariantCulture));
        if (model.Query.PageSize > 0 && model.Query.PageSize != 20)
        {
            AppendQuery(query, "pageSize", model.Query.PageSize.ToString(CultureInfo.InvariantCulture));
        }
        AppendQuery(query, "jobId", model.Query.JobId);
        AppendQuery(query, "definition", model.SelectedDefinitionKey);
        var stateValue = stateOverride ?? (string.IsNullOrWhiteSpace(model.Query.State) ? "all" : model.Query.State);
        AppendQuery(query, "state", stateValue);
        AppendQuery(query, "sort", BuildSortQuery(sortsOverride ?? model.Query.Sorts));
        if (model.Query.CreatedFrom.HasValue)
        {
            AppendQuery(query, "from", model.Query.CreatedFrom.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture));
        }
        if (model.Query.CreatedTo.HasValue)
        {
            AppendQuery(query, "to", model.Query.CreatedTo.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture));
        }

        foreach (var filter in model.Query.Filters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            AppendQuery(query, $"f_{filter.Key}", filter.Value);
        }

        return $"{NormalizePathBase(model.PathBase)}{model.RoutePath}{query}";
    }

    private static string BuildResetUrl(JobSearchDashboardViewModel model)
    {
        var query = new StringBuilder();
        AppendQuery(query, "definition", model.SelectedDefinitionKey);
        AppendQuery(query, "state", "Processing");
        AppendQuery(query, "page", "1");
        return $"{NormalizePathBase(model.PathBase)}{model.RoutePath}{query}";
    }

    private static string BuildSortQuery(IReadOnlyList<JobSearchSort> sorts)
    {
        return string.Join(",", sorts
            .Where(s => !string.IsNullOrWhiteSpace(s.Field))
            .Select(s => $"{s.Field}:{(s.Descending ? "desc" : "asc")}"));
    }

    private static void AppendQuery(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(builder.Length == 0 ? "?" : "&")
            .Append(Uri.EscapeDataString(key))
            .Append("=")
            .Append(Uri.EscapeDataString(value));
    }

    private static void AppendOption(StringBuilder html, string value, string? selected, string text)
    {
        var isSelected = string.Equals(value, selected, StringComparison.Ordinal);
        html.Append("<option value=\"")
            .Append(E(value))
            .Append("\"")
            .Append(isSelected ? " selected" : string.Empty)
            .Append(">")
            .Append(E(text))
            .Append("</option>");
    }

    private static void RenderHiddenInput(StringBuilder html, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        html.Append("<input type=\"hidden\" name=\"")
            .Append(E(name))
            .Append("\" value=\"")
            .Append(E(value))
            .Append("\"/>");
    }

    private static void RenderClientScript(StringBuilder html, JobSearchDashboardViewModel model)
    {
        var intervalSec = model.AutoRefreshIntervalSeconds > 0 ? model.AutoRefreshIntervalSeconds : 5;
        var defaultEnabled = model.AutoRefreshIntervalSeconds > 0 ? "true" : "false";

        html.Append("<script>(function(){try{");
        html.Append("function esc(s){return s.replace(/(['\"\\\\])/g,'\\\\$1');}");
        html.Append("var expandedIds={};");
        html.Append("var selectedIds={};");

        html.Append("function updateSelectedCount(){var el=document.getElementById('hfjs-selected-count');if(el){var c=Object.keys(selectedIds).length;el.textContent=c+' selected';}}");

        html.Append("function sendAction(action,ids){if(!ids.length)return;var bar=document.querySelector('.hfjs-bulk-bar');var url=bar?bar.getAttribute('data-actions-url'):'';if(!url)return;");
        html.Append("var body=new URLSearchParams();body.set('action',action);body.set('jobIds',ids.join(','));");
        html.Append("fetch(url,{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:body.toString()}).then(function(){ids.forEach(function(id){delete selectedIds[id];});refreshPartial();});}");

        html.Append("function bindRegion(){");
        html.Append("var btns=document.querySelectorAll('.hfjs-expand');");
        html.Append("for(var i=0;i<btns.length;i++){(function(btn){var target=btn.getAttribute('data-target');");
        html.Append("if(expandedIds[target]){btn.setAttribute('data-expanded','true');var r=document.querySelectorAll('.hfjs-child-row[data-parent=\"'+esc(target)+'\"]');for(var k=0;k<r.length;k++)r[k].style.display='';}");
        html.Append("btn.addEventListener('click',function(){var exp=btn.getAttribute('data-expanded')==='true';var rows=document.querySelectorAll('.hfjs-child-row[data-parent=\"'+esc(target)+'\"]');for(var j=0;j<rows.length;j++){rows[j].style.display=exp?'none':'';}btn.setAttribute('data-expanded',exp?'false':'true');if(exp)delete expandedIds[target];else expandedIds[target]=true;});");
        html.Append("})(btns[i]);}");

        html.Append("var checks=document.querySelectorAll('.hfjs-row-select');");
        html.Append("for(var i=0;i<checks.length;i++){(function(cb){if(selectedIds[cb.value])cb.checked=true;cb.addEventListener('change',function(){if(cb.checked)selectedIds[cb.value]=true;else delete selectedIds[cb.value];updateSelectedCount();});})(checks[i]);}");
        html.Append("var selAll=document.getElementById('hfjs-select-all');if(selAll){selAll.addEventListener('change',function(){var all=document.querySelectorAll('.hfjs-row-select');for(var i=0;i<all.length;i++){all[i].checked=selAll.checked;if(selAll.checked)selectedIds[all[i].value]=true;else delete selectedIds[all[i].value];}updateSelectedCount();});}");

        html.Append("var rowBtns=document.querySelectorAll('.hfjs-row-action');for(var i=0;i<rowBtns.length;i++){(function(b){b.addEventListener('click',function(){sendAction(b.getAttribute('data-action'),[b.getAttribute('data-job-id')]);});})(rowBtns[i]);}");
        html.Append("var bulkBtns=document.querySelectorAll('.hfjs-bulk-action');for(var i=0;i<bulkBtns.length;i++){(function(b){b.addEventListener('click',function(){sendAction(b.getAttribute('data-action'),Object.keys(selectedIds));});})(bulkBtns[i]);}");
        html.Append("updateSelectedCount();}");

        html.Append("function refreshPartial(){var ae=document.activeElement;if(ae&&(ae.tagName==='INPUT'||ae.tagName==='SELECT'))return Promise.resolve();");
        html.Append("return fetch(window.location.href,{headers:{'X-Requested-With':'XMLHttpRequest'}}).then(function(res){return res.text();}).then(function(txt){var doc=new DOMParser().parseFromString(txt,'text/html');var next=doc.getElementById('hfjs-results-region');var cur=document.getElementById('hfjs-results-region');if(next&&cur){cur.innerHTML=next.innerHTML;bindRegion();}}).catch(function(){});}");

        html.Append("var KEY='hfjs.autoRefresh';var btn=document.getElementById('hfjs-toggle');var statusEl=document.getElementById('hfjs-status');");
        html.Append("var intervalMs=").Append(intervalSec * 1000).Append(";");
        html.Append("var enabled=").Append(defaultEnabled).Append(";try{var saved=localStorage.getItem(KEY);if(saved!==null)enabled=saved!=='false';}catch(e){}");
        html.Append("var timer=null;");
        html.Append("function schedule(){if(timer){clearTimeout(timer);timer=null;}if(enabled&&btn&&statusEl){btn.textContent='Pause';statusEl.textContent='every '+(intervalMs/1000)+'s';timer=setTimeout(function(){refreshPartial().then(schedule);},intervalMs);}else if(btn&&statusEl){btn.textContent='Resume';statusEl.textContent='paused';}}");
        html.Append("if(btn){btn.addEventListener('click',function(){enabled=!enabled;try{localStorage.setItem(KEY,enabled?'true':'false');}catch(e){}schedule();});}");
        html.Append("bindRegion();schedule();");
        html.Append("}catch(e){}})();</script>");
    }

    private static IReadOnlyList<int> GetPageWindow(int currentPage, int totalPages)
    {
        var start = Math.Max(1, currentPage - 2);
        var end = Math.Min(totalPages, start + 4);
        if (end - start < 4)
        {
            start = Math.Max(1, end - 4);
        }

        var pages = new List<int>();
        for (var page = start; page <= end; page++)
        {
            pages.Add(page);
        }

        return pages;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1000)
        {
            return $"{(int)duration.TotalMilliseconds}ms";
        }

        if (duration.TotalSeconds < 60)
        {
            return $"{duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s";
        }

        return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
    }

    private static string E(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty) ?? string.Empty;
    }

    private static string RenderStateBadge(string? state)
    {
        var safe = E(state);
        var cls = StateCssClass(state);
        return $"<span class=\"hfjs-state {cls}\">{safe}</span>";
    }

    private static string StateCssClass(string? state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return "hfjs-state-unknown";
        }

        switch (state!.ToLowerInvariant())
        {
            case "processing": return "hfjs-state-processing";
            case "enqueued": return "hfjs-state-enqueued";
            case "succeeded": return "hfjs-state-succeeded";
            case "failed": return "hfjs-state-failed";
            case "scheduled": return "hfjs-state-scheduled";
            case "deleted": return "hfjs-state-deleted";
            case "awaiting": return "hfjs-state-awaiting";
            default: return "hfjs-state-unknown";
        }
    }

    private static string NormalizePathBase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value!.TrimEnd('/');
    }
}
