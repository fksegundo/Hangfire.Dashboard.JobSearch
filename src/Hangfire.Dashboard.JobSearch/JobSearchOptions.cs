using System;
using System.Collections.Generic;
using System.Linq;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchOptions
{
    private readonly List<JobSearchDefinition> _definitions = new List<JobSearchDefinition>();
    private readonly List<JobSearchJoin> _joins = new List<JobSearchJoin>();
    private Type? _storageType;

    public IReadOnlyList<JobSearchDefinition> Definitions => _definitions;
    public IReadOnlyList<JobSearchJoin> Joins => _joins;
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

    public void AddJoin(JobSearchJoin join)
    {
        if (join is null)
        {
            throw new ArgumentNullException(nameof(join));
        }

        if (string.IsNullOrWhiteSpace(join.KeyField))
        {
            throw new ArgumentException("Join must declare a KeyField.", nameof(join));
        }

        if (join.ParentJobType is null || join.ChildJobType is null)
        {
            throw new ArgumentException("Join must declare ParentJobType and ChildJobType.", nameof(join));
        }

        _joins.Add(join);
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
