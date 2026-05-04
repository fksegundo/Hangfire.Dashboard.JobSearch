using System.Threading;
using System.Threading.Tasks;

namespace Hangfire.Dashboard.JobSearch;

public interface IJobSearchStorage
{
    Task SaveMetadataAsync(
        string jobId,
        JobSearchMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<JobSearchResultPage> SearchAsync(
        JobSearchQuery query,
        CancellationToken cancellationToken = default);

    Task DeleteMetadataAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}
