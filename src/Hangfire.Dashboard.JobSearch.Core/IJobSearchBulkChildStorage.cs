using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hangfire.Dashboard.JobSearch;

/// <summary>
/// Optional interface implemented by storages that support batch-fetching child jobs
/// for multiple parent keys in a single round-trip.
/// </summary>
public interface IJobSearchBulkChildStorage
{
    Task<Dictionary<string, List<JobSearchResult>>> SearchChildrenBulkAsync(
        string childJobType,
        string childMethodName,
        string keyField,
        IReadOnlyList<string> parentKeys,
        int maxPerKey,
        CancellationToken cancellationToken = default);
}
