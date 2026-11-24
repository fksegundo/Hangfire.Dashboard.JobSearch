using Hangfire.Dashboard;
using Hangfire.Dashboard.Pages;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardPage : RazorPage
{
    private readonly string _bodyHtml;
    private readonly string _title;

    public JobSearchDashboardPage(string bodyHtml, string title = "Job Search")
    {
        _bodyHtml = bodyHtml ?? string.Empty;
        _title = string.IsNullOrWhiteSpace(title) ? "Job Search" : title;
    }

    public override void Execute()
    {
        Layout = new LayoutPage(_title);
        WriteLiteral(_bodyHtml);
    }
}
