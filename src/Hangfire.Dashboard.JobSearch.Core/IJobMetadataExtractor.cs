using System.Collections.Generic;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch;

public interface IJobMetadataExtractor
{
    IReadOnlyDictionary<string, string?> Extract(Job job);
}
