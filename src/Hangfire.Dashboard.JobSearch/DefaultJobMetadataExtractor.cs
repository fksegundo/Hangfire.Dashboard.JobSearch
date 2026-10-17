using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch;

public sealed class DefaultJobMetadataExtractor : IJobMetadataExtractor
{
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

        var property = value.GetType().GetRuntimeProperty(segment);
        return property?.GetValue(value);
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
