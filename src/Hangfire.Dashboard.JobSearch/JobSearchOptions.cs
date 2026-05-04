using System;
using System.Collections.Generic;
using System.Linq;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchOptions
{
    private readonly List<JobSearchDefinition> _definitions = new List<JobSearchDefinition>();

    public IReadOnlyList<JobSearchDefinition> Definitions => _definitions;

    public void AddJobDefinition(JobSearchDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        _definitions.Add(definition);
    }

    public JobSearchDefinition? FindDefinition(Job job)
    {
        if (job is null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        return _definitions.FirstOrDefault(definition => definition.Matches(job));
    }
}
