using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Hangfire.Dashboard.JobSearch;

internal static class JobSearchDashboardRenderer
{
    private static readonly string[] BaseColumns = { "JobId", "State", "JobType", "MethodName", "CreatedAt" };

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
        html.Append("<style>");
        html.Append(":root{--hfjs-bg:#ffffff;--hfjs-border:#d9dee8;--hfjs-head:#f7f9fc;--hfjs-text:#1f2937;--hfjs-muted:#4b5563;--hfjs-chip:#eef2f7;--hfjs-link:#3558a8;}");
        html.Append("@media (prefers-color-scheme: dark){:root{--hfjs-bg:#1b1f27;--hfjs-border:#364152;--hfjs-head:#252d3a;--hfjs-text:#e5e7eb;--hfjs-muted:#cbd5e1;--hfjs-chip:#2f3b4d;--hfjs-link:#9ab4ff;}}");
        html.Append(".hfjs-toolbar{display:grid;grid-template-columns:repeat(3,minmax(180px,1fr));gap:10px;align-items:end;margin-bottom:12px;}");
        html.Append(".hfjs-filters{display:grid;grid-template-columns:repeat(4,minmax(180px,1fr));gap:10px;margin-bottom:12px;}");
        html.Append(".hfjs-field label{display:block;font-weight:600;margin-bottom:4px;color:var(--hfjs-muted);font-size:12px;}");
        html.Append(".hfjs-sort-chip{display:inline-block;padding:2px 8px;background:var(--hfjs-chip);border-radius:12px;margin-right:6px;font-size:12px;color:var(--hfjs-text);}");
        html.Append(".hfjs-table-wrap{border:1px solid var(--hfjs-border);border-radius:6px;overflow:auto;background:var(--hfjs-bg);width:100%;}");
        html.Append(".hfjs-table{width:100%;min-width:1200px;border-collapse:collapse;font-size:13px;}");
        html.Append(".hfjs-table th,.hfjs-table td{padding:8px 10px;border-bottom:1px solid var(--hfjs-border);vertical-align:top;white-space:normal;color:var(--hfjs-text);}");
        html.Append(".hfjs-table th{background:var(--hfjs-head);font-weight:700;position:sticky;top:0;}");
        html.Append(".hfjs-sort-links a{margin-left:4px;font-size:12px;color:var(--hfjs-link);text-decoration:none;display:inline-block;padding:1px 3px;border-radius:4px;}");
        html.Append(".hfjs-sort-links a:hover{background:var(--hfjs-chip);}");
        html.Append(".hfjs-pager{margin-top:12px;display:flex;justify-content:space-between;align-items:center;color:var(--hfjs-text);gap:12px;flex-wrap:wrap;}");
        html.Append(".hfjs-pager .pagination{margin:0;}");
        html.Append(".hfjs-pager .pagination>li>a{color:var(--hfjs-link);background:var(--hfjs-bg);border-color:var(--hfjs-border);}");
        html.Append(".hfjs-pager .pagination>li.active>a{background:var(--hfjs-link);border-color:var(--hfjs-link);color:#fff;}");
        html.Append(".hfjs-toolbar .form-control,.hfjs-filters .form-control{max-width:100%;}");
        html.Append(".hfjs-live-bar{display:flex;align-items:center;gap:10px;font-size:12px;color:var(--hfjs-muted);margin-bottom:8px;}");
        html.Append(".hfjs-toggle-btn{cursor:pointer;background:var(--hfjs-chip);border:1px solid var(--hfjs-border);color:var(--hfjs-text);padding:2px 10px;border-radius:12px;font-size:12px;line-height:1.4;}");
        html.Append(".hfjs-toggle-btn:hover{background:var(--hfjs-border);}");
        html.Append(".hfjs-advanced{margin-bottom:12px;border:1px solid var(--hfjs-border);border-radius:6px;background:var(--hfjs-head);}");
        html.Append(".hfjs-advanced>summary{padding:8px 12px;cursor:pointer;font-weight:600;color:var(--hfjs-text);user-select:none;list-style:none;display:flex;align-items:center;}");
        html.Append(".hfjs-advanced>summary::-webkit-details-marker{display:none;}");
        html.Append(".hfjs-advanced>summary::before{content:'\\25B6';display:inline-block;margin-right:6px;font-size:9px;transition:transform 0.15s;}");
        html.Append(".hfjs-advanced[open]>summary::before{transform:rotate(90deg);}");
        html.Append(".hfjs-advanced>.hfjs-filters{padding:10px 12px;margin-bottom:0;background:var(--hfjs-bg);border-top:1px solid var(--hfjs-border);border-radius:0 0 6px 6px;}");
        html.Append(".hfjs-active-count{margin-left:8px;padding:1px 8px;background:var(--hfjs-link);color:#fff;border-radius:10px;font-size:11px;font-weight:normal;}");
        html.Append(".hfjs-expand{cursor:pointer;background:none;border:none;padding:0 4px;color:var(--hfjs-link);font-size:11px;display:inline-block;transition:transform 0.15s;}");
        html.Append(".hfjs-expand:hover{background:var(--hfjs-chip);border-radius:3px;}");
        html.Append(".hfjs-expand[data-expanded='true']{transform:rotate(90deg);}");
        html.Append(".hfjs-child-count{background:var(--hfjs-chip);padding:1px 8px;border-radius:10px;font-size:11px;margin-right:6px;color:var(--hfjs-text);}");
        html.Append(".hfjs-child-row>td{background:var(--hfjs-head);padding:6px 10px 6px 30px;font-size:12px;}");
        html.Append(".hfjs-child-arrow{color:var(--hfjs-muted);margin-right:6px;}");
        html.Append(".hfjs-child-id{background:var(--hfjs-chip);padding:1px 6px;border-radius:3px;font-family:monospace;font-size:11px;margin-right:8px;}");
        html.Append(".hfjs-child-row .hfjs-state{margin-right:8px;}");
        html.Append(".hfjs-child-method,.hfjs-child-time,.hfjs-child-field{margin-right:10px;color:var(--hfjs-muted);font-size:11px;}");
        html.Append(".hfjs-child-field b{color:var(--hfjs-text);font-weight:600;}");
        html.Append(".hfjs-child-view{margin-left:8px;color:var(--hfjs-link);}");
        html.Append(".hfjs-child-count-empty{opacity:0.55;font-style:italic;}");
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

        html.Append("<h1 class=\"page-header\">Job Search</h1>");
        html.Append("<div class=\"hfjs-live-bar\"><span>Live refresh: <span id=\"hfjs-status\">every 5s</span></span><button type=\"button\" id=\"hfjs-toggle\" class=\"hfjs-toggle-btn\">Pause</button></div>");
        RenderSortSummary(html, model);
        RenderFilterForm(html, model);
        RenderResultsTable(html, model);
        RenderPager(html, model);
        RenderAutoRefreshScript(html);
        RenderExpandScript(html);
        return html.ToString();
    }

    private static void RenderExpandScript(StringBuilder html)
    {
        html.Append("<script>(function(){try{");
        html.Append("function esc(s){return s.replace(/(['\"\\\\])/g,'\\\\$1');}");
        html.Append("var btns=document.querySelectorAll('.hfjs-expand');");
        html.Append("for(var i=0;i<btns.length;i++){(function(btn){");
        html.Append("btn.addEventListener('click',function(){");
        html.Append("var target=btn.getAttribute('data-target');");
        html.Append("var expanded=btn.getAttribute('data-expanded')==='true';");
        html.Append("var rows=document.querySelectorAll('.hfjs-child-row[data-parent=\"'+esc(target)+'\"]');");
        html.Append("for(var j=0;j<rows.length;j++){rows[j].style.display=expanded?'none':'';}");
        html.Append("btn.setAttribute('data-expanded',expanded?'false':'true');");
        html.Append("});");
        html.Append("})(btns[i]);}");
        html.Append("}catch(e){}})();</script>");
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
        RenderSelect(html, "State", "state", model.States.Select(s => (s.Value, s.Label)), model.Query.State, includeAll: false);
        html.Append("</div>");

        if (model.FilterableFields.Count > 0)
        {
            var activeCount = model.FilterableFields.Count(f =>
                model.Query.Filters.TryGetValue(f, out var v) && !string.IsNullOrEmpty(v));
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
                RenderInput(html, field, $"f_{field}", value ?? string.Empty);
            }

            html.Append("</div></details>");
        }

        RenderHiddenInput(html, "sort", BuildSortQuery(model.Query.Sorts));
        html.Append("<button class=\"btn btn-primary\" type=\"submit\">Apply Filters</button> ");
        html.Append("<a class=\"btn btn-default\" href=\"").Append(E(BuildResetUrl(model))).Append("\">Reset</a>");
        html.Append("</form>");
    }

    private static void RenderResultsTable(StringBuilder html, JobSearchDashboardViewModel model)
    {
        html.Append("<div class=\"hfjs-table-wrap\"><table class=\"hfjs-table\"><thead><tr>");
        RenderHeader(html, model, "Job Id", "JobId");
        RenderHeader(html, model, "State", "State");
        RenderHeader(html, model, "Method", "MethodName");
        RenderHeader(html, model, "Created At", "CreatedAt");

        foreach (var field in model.DisplayFields)
        {
            RenderHeader(html, model, field, field);
        }

        html.Append("<th>Details</th></tr></thead><tbody>");

        var columnCount = 5 + model.DisplayFields.Count;

        foreach (var item in model.Result.Items)
        {
            var hasJoin = item.Children is not null;
            var hasChildren = hasJoin && item.Children!.Count > 0;
            var parentId = E(item.JobId);

            html.Append("<tr>");
            html.Append("<td>");
            if (hasChildren)
            {
                html.Append("<button type=\"button\" class=\"hfjs-expand\" data-target=\"")
                    .Append(parentId)
                    .Append("\" data-expanded=\"false\" title=\"Toggle children\">&#9654;</button> ");
            }
            html.Append(parentId).Append("</td>");
            html.Append("<td>").Append(RenderStateBadge(item.State)).Append("</td>")
                .Append("<td>").Append(E(item.MethodName)).Append("</td>")
                .Append("<td>").Append(E(item.CreatedAt.ToString("u"))).Append("</td>");

            foreach (var field in model.DisplayFields)
            {
                item.Fields.TryGetValue(field, out var value);
                html.Append("<td>").Append(E(value)).Append("</td>");
            }

            html.Append("<td>");
            if (hasJoin)
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
            }
            html.Append("<a href=\"")
                .Append(E($"{NormalizePathBase(model.PathBase)}/jobs/details/{item.JobId}"))
                .Append("\">View</a></td></tr>");

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

    private static void RenderHeader(StringBuilder html, JobSearchDashboardViewModel model, string label, string field)
    {
        html.Append("<th>").Append(E(label));
        html.Append("<span class=\"hfjs-sort-links\">");
        html.Append("<a title=\"Sort ascending\" href=\"").Append(E(BuildSortUrl(model, field, false, append: false))).Append("\">↑</a>");
        html.Append("<a title=\"Sort descending\" href=\"").Append(E(BuildSortUrl(model, field, true, append: false))).Append("\">↓</a>");
        html.Append("<a title=\"Add ascending sort\" href=\"").Append(E(BuildSortUrl(model, field, false, append: true))).Append("\">⇧↑</a>");
        html.Append("<a title=\"Add descending sort\" href=\"").Append(E(BuildSortUrl(model, field, true, append: true))).Append("\">⇧↓</a>");
        html.Append("</span></th>");
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
            .Append("\">");
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
        string value)
    {
        html.Append("<div class=\"hfjs-field\"><label>")
            .Append(E(label))
            .Append("</label><input class=\"form-control\" name=\"")
            .Append(E(name))
            .Append("\" value=\"")
            .Append(E(value))
            .Append("\"/></div>");
    }

    private static string BuildSortUrl(JobSearchDashboardViewModel model, string field, bool desc, bool append)
    {
        var sorts = append ? new List<JobSearchSort>(model.Query.Sorts) : new List<JobSearchSort>();
        sorts.RemoveAll(s => string.Equals(s.Field, field, StringComparison.Ordinal));
        sorts.Add(new JobSearchSort { Field = field, Descending = desc });
        return BuildPageUrl(model, 1, sorts);
    }

    private static string BuildPageUrl(JobSearchDashboardViewModel model, int page, List<JobSearchSort>? sortsOverride = null)
    {
        var query = new StringBuilder();
        AppendQuery(query, "page", page.ToString());
        AppendQuery(query, "jobId", model.Query.JobId);
        AppendQuery(query, "definition", model.SelectedDefinitionKey);
        AppendQuery(query, "state", model.Query.State);
        AppendQuery(query, "sort", BuildSortQuery(sortsOverride ?? model.Query.Sorts));

        foreach (var filter in model.Query.Filters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            AppendQuery(query, $"f_{filter.Key}", filter.Value);
        }

        return $"{NormalizePathBase(model.PathBase)}/job-search{query}";
    }

    private static string BuildResetUrl(JobSearchDashboardViewModel model)
    {
        var query = new StringBuilder();
        AppendQuery(query, "definition", model.SelectedDefinitionKey);
        AppendQuery(query, "state", "Processing");
        AppendQuery(query, "page", "1");
        return $"{NormalizePathBase(model.PathBase)}/job-search{query}";
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

    private static void RenderAutoRefreshScript(StringBuilder html)
    {
        html.Append("<script>(function(){try{");
        html.Append("var KEY='hfjs.autoRefresh';");
        html.Append("var btn=document.getElementById('hfjs-toggle');");
        html.Append("var statusEl=document.getElementById('hfjs-status');");
        html.Append("if(!btn||!statusEl)return;");
        html.Append("var enabled;try{enabled=localStorage.getItem(KEY)!=='false';}catch(e){enabled=true;}");
        html.Append("var timer=null;");
        html.Append("function apply(){");
        html.Append("if(timer){clearTimeout(timer);timer=null;}");
        html.Append("if(enabled){btn.textContent='Pause';statusEl.textContent='every 5s';");
        html.Append("timer=setTimeout(function(){window.location.reload();},5000);");
        html.Append("}else{btn.textContent='Resume';statusEl.textContent='paused';}");
        html.Append("}");
        html.Append("btn.addEventListener('click',function(){enabled=!enabled;");
        html.Append("try{localStorage.setItem(KEY,enabled?'true':'false');}catch(e){}");
        html.Append("apply();});");
        html.Append("apply();");
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
