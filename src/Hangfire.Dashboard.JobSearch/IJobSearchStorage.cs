using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

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

    Task UpdateStateAsync(
        string jobId,
        string state,
        CancellationToken cancellationToken = default);

    Task DeleteMetadataAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, int>> CountByStateAsync(
        JobSearchQuery query,
        CancellationToken cancellationToken = default);
}
