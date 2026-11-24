using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch;

public sealed class DefaultJobMetadataExtractor : IJobMetadataExtractor
{
    private static readonly ConcurrentDictionary<(Type Type, string PropertyName), Func<object, object?>?> PropertyGetterCache =
        new ConcurrentDictionary<(Type, string), Func<object, object?>?>();

    private readonly JobSearchOptions _options;

    public DefaultJobMetadataExtractor(JobSearchOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyDictionary<string, string?> Extract(Job job)
    {
        if (job is null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        var definition = _options.FindDefinition(job);
        if (definition is null)
        {
            return new Dictionary<string, string?>();
        }

        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in definition.Fields)
        {
            fields[field.Name] = ExtractField(job, field);
        }

        return fields;
    }

    private static string? ExtractField(Job job, JobSearchField field)
    {
        if (field.ArgumentIndex < 0 || field.ArgumentIndex >= job.Args.Count)
        {
            return null;
        }

        var value = ResolvePath(job.Args[field.ArgumentIndex], field.JsonPath);
        return ConvertToString(value);
    }

    private static object? ResolvePath(object? value, string path)
    {
        if (value is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path == "$")
        {
            return value;
        }

        if (!path.StartsWith("$.", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var segment in path.Substring(2).Split('.').Where(segment => segment.Length > 0))
        {
            value = ResolveSegment(value, segment);
            if (value is null)
            {
                return null;
            }
        }

        return value;
    }

    private static object? ResolveSegment(object value, string segment)
    {
        if (value is IDictionary<string, object?> objectDictionary
            && objectDictionary.TryGetValue(segment, out var objectDictionaryValue))
        {
            return objectDictionaryValue;
        }

        if (value is IDictionary stringDictionary && stringDictionary.Contains(segment))
        {
            return stringDictionary[segment];
        }

        var getter = PropertyGetterCache.GetOrAdd((value.GetType(), segment), BuildPropertyGetter);
        return getter?.Invoke(value);
    }

    private static Func<object, object?>? BuildPropertyGetter((Type Type, string PropertyName) key)
    {
        var property = key.Type.GetRuntimeProperty(key.PropertyName);
        if (property is null || !property.CanRead || property.GetIndexParameters().Length > 0)
        {
            return null;
        }

        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var typedInstance = Expression.Convert(instanceParam, key.Type);
        var propertyAccess = Expression.Property(typedInstance, property);
        var boxedResult = Expression.Convert(propertyAccess, typeof(object));
        return Expression.Lambda<Func<object, object?>>(boxedResult, instanceParam).Compile();
    }

    private static string? ConvertToString(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
