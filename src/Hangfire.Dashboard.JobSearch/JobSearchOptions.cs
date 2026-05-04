using System;
using System.Collections.Generic;
using System.Linq;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchOptions
{
    private readonly List<JobSearchDefinition> _definitions = new List<JobSearchDefinition>();
    private Type? _storageType;

    public IReadOnlyList<JobSearchDefinition> Definitions => _definitions;
    public Func<IServiceProvider, IJobSearchStorage>? StorageFactory { get; private set; }

    internal Type? StorageType => _storageType;

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

    public void UseStorage<TStorage>() where TStorage : class, IJobSearchStorage
    {
        _storageType = typeof(TStorage);
        StorageFactory = null;
    }

    public void UseStorage(Func<IServiceProvider, IJobSearchStorage> storageFactory)
    {
        StorageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
        _storageType = null;
    }
}
