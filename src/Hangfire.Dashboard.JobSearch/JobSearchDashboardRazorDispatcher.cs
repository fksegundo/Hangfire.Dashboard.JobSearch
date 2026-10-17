using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hangfire.Dashboard;

namespace Hangfire.Dashboard.JobSearch;

internal sealed class JobSearchDashboardRazorDispatcher : IDashboardDispatcher
{
    private readonly IDashboardDispatcher _innerDispatcher;

    public JobSearchDashboardRazorDispatcher(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        _innerDispatcher = CreateInnerDispatcher(serviceProvider);
    }

    public Task Dispatch(DashboardContext context)
    {
        return _innerDispatcher.Dispatch(context);
    }

    private static IDashboardDispatcher CreateInnerDispatcher(IServiceProvider serviceProvider)
    {
        var dispatcherType = typeof(DashboardContext).Assembly.GetType("Hangfire.Dashboard.RazorPageDispatcher");
        if (dispatcherType is null)
        {
            throw new InvalidOperationException("Unable to find Hangfire.Dashboard.RazorPageDispatcher.");
        }

        var pageFactory = new Func<Match, RazorPage>(_ => new JobSearchDashboardPage(serviceProvider));
        var instance = Activator.CreateInstance(
            dispatcherType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { pageFactory },
            culture: null);

        if (instance is not IDashboardDispatcher dashboardDispatcher)
        {
            throw new InvalidOperationException("Unable to create dashboard dispatcher for Job Search page.");
        }

        return dashboardDispatcher;
    }
}
