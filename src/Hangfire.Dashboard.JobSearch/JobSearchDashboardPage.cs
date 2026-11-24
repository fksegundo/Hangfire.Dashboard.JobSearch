using Hangfire.Dashboard;
using Hangfire.Dashboard.Pages;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardPage : RazorPage
{
    private readonly string _bodyHtml;

    public JobSearchDashboardPage(string bodyHtml)
    {
        _bodyHtml = bodyHtml ?? string.Empty;
    }

    public override void Execute()
    {
        Layout = new LayoutPage("Job Search");
        WriteLiteral(_bodyHtml);
    }
}
