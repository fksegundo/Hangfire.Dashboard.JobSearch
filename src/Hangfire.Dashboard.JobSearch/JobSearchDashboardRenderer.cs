using System;
using System.Linq;
using System.Net;
using System.Text;

namespace Hangfire.Dashboard.JobSearch;

internal static class JobSearchDashboardRenderer
{
    public static string Render(JobSearchDashboardViewModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Job Search</title>");
        html.Append("<style>");
        html.Append("body{font-family:Arial,sans-serif;margin:20px;} .row{margin-bottom:12px;} label{display:inline-block;min-width:120px;} ");
        html.Append("input,select{padding:4px 6px;margin-right:8px;} table{width:100%;border-collapse:collapse;margin-top:16px;} ");
        html.Append("th,td{border:1px solid #ddd;padding:8px;text-align:left;} th{background:#f5f5f5;} .pager{margin-top:12px;}");
        html.Append("</style></head><body>");
        html.Append("<h1>Job Search</h1>");
        RenderFilterForm(html, model);
        RenderResultsTable(html, model);
        RenderPager(html, model);
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void RenderFilterForm(StringBuilder html, JobSearchDashboardViewModel model)
    {
        html.Append("<form method=\"get\" class=\"row\">");
        html.Append("<div class=\"row\"><label>Job Type</label><select name=\"jobType\"><option value=\"\">All</option>");
        foreach (var type in model.JobTypes)
        {
            AppendOption(html, type, model.Query.JobType);
        }

        html.Append("</select><label>State</label><select name=\"state\"><option value=\"\">All</option>");
        foreach (var state in model.States)
        {
            AppendOption(html, state, model.Query.State);
        }

        html.Append("</select></div>");

        foreach (var field in model.FilterableFields)
        {
            model.Query.Filters.TryGetValue(field, out var value);
            html.Append("<div class=\"row\"><label>")
                .Append(E(field))
                .Append("</label><input name=\"f_")
                .Append(E(field))
                .Append("\" value=\"")
                .Append(E(value))
                .Append("\"></div>");
        }

        html.Append("<div class=\"row\">");
        html.Append("<label>Page Size</label><input name=\"pageSize\" value=\"").Append(model.Query.PageSize).Append("\">");
        html.Append("<button type=\"submit\">Search</button>");
        html.Append("</div>");
        html.Append("</form>");
    }

    private static void RenderResultsTable(StringBuilder html, JobSearchDashboardViewModel model)
    {
        html.Append("<table><thead><tr>");
        html.Append("<th>Job Id</th><th>State</th><th>Job Type</th><th>Method</th><th>Created At</th>");

        foreach (var field in model.DisplayFields)
        {
            html.Append("<th>").Append(E(field)).Append("</th>");
        }

        html.Append("<th>Details</th></tr></thead><tbody>");
        foreach (var item in model.Result.Items)
        {
            html.Append("<tr>")
                .Append("<td>").Append(E(item.JobId)).Append("</td>")
                .Append("<td>").Append(E(item.State)).Append("</td>")
                .Append("<td>").Append(E(item.JobType)).Append("</td>")
                .Append("<td>").Append(E(item.MethodName)).Append("</td>")
                .Append("<td>").Append(E(item.CreatedAt.ToString("u"))).Append("</td>");

            foreach (var field in model.DisplayFields)
            {
                item.Fields.TryGetValue(field, out var value);
                html.Append("<td>").Append(E(value)).Append("</td>");
            }

            var detailsUrl = $"{NormalizePathBase(model.PathBase)}/jobs/details/{item.JobId}";
            html.Append("<td><a href=\"").Append(E(detailsUrl)).Append("\">View</a></td></tr>");
        }

        if (!model.Result.Items.Any())
        {
            html.Append("<tr><td colspan=\"")
                .Append(6 + model.DisplayFields.Count)
                .Append("\">No jobs found.</td></tr>");
        }

        html.Append("</tbody></table>");
    }

    private static void RenderPager(StringBuilder html, JobSearchDashboardViewModel model)
    {
        var totalPages = model.Result.PageSize <= 0
            ? 1
            : Math.Max(1, (int)Math.Ceiling((double)model.Result.TotalCount / model.Result.PageSize));

        html.Append("<div class=\"pager\">");
        html.Append("Page ").Append(model.Result.Page).Append(" of ").Append(totalPages);

        if (model.Result.Page > 1)
        {
            html.Append(" <a href=\"").Append(E(BuildPageUrl(model, model.Result.Page - 1))).Append("\">Prev</a>");
        }

        if (model.Result.Page < totalPages)
        {
            html.Append(" <a href=\"").Append(E(BuildPageUrl(model, model.Result.Page + 1))).Append("\">Next</a>");
        }

        html.Append("</div>");
    }

    private static string BuildPageUrl(JobSearchDashboardViewModel model, int page)
    {
        var query = new StringBuilder();
        AppendQuery(query, "page", page.ToString());
        AppendQuery(query, "pageSize", model.Query.PageSize.ToString());
        AppendQuery(query, "jobType", model.Query.JobType);
        AppendQuery(query, "state", model.Query.State);

        foreach (var filter in model.Query.Filters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            AppendQuery(query, $"f_{filter.Key}", filter.Value);
        }

        if (!string.IsNullOrWhiteSpace(model.Query.SortBy))
        {
            AppendQuery(query, "sortBy", model.Query.SortBy);
            AppendQuery(query, "desc", model.Query.SortDescending ? "1" : "0");
        }

        return $"{NormalizePathBase(model.PathBase)}/job-search{query}";
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

    private static void AppendOption(StringBuilder html, string value, string? selected)
    {
        var isSelected = string.Equals(value, selected, StringComparison.Ordinal);
        html.Append("<option value=\"")
            .Append(E(value))
            .Append("\"")
            .Append(isSelected ? " selected" : string.Empty)
            .Append(">")
            .Append(E(value))
            .Append("</option>");
    }

    private static string E(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty) ?? string.Empty;
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
