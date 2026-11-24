using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Hangfire.Common;

namespace Hangfire.Dashboard.JobSearch;

public sealed class JobSearchDefinitionBuilder
{
    private readonly JobSearchDefinition _definition;
    private readonly MethodInfo? _method;

    internal JobSearchDefinitionBuilder(JobSearchDefinition definition, MethodInfo? method = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _method = method;
    }

    public JobSearchDefinitionBuilder Field(
        string name,
        int argumentIndex,
        string jsonPath,
        bool displayInList = true,
        bool filterable = true,
        bool sortable = false)
    {
        _definition.Fields.Add(new JobSearchField
        {
            Name = name,
            ArgumentIndex = argumentIndex,
            JsonPath = jsonPath,
            DisplayInList = displayInList,
            Filterable = filterable,
            Sortable = sortable
        });
        return this;
    }

    public JobSearchDefinitionBuilder Field<TArg>(
        Expression<Func<TArg, object?>> propertyExpression,
        string? name = null,
        int argumentIndex = 0,
        bool displayInList = true,
        bool filterable = true,
        bool sortable = false)
    {
        if (propertyExpression is null)
        {
            throw new ArgumentNullException(nameof(propertyExpression));
        }

        var pathSegments = ExtractMemberPath(propertyExpression.Body);
        if (pathSegments.Count == 0)
        {
            throw new ArgumentException("Expression must refer to a property or nested property path.", nameof(propertyExpression));
        }

        var fieldName = string.IsNullOrWhiteSpace(name) ? pathSegments[pathSegments.Count - 1] : name!;
        var jsonPath = "$." + string.Join(".", pathSegments);

        return Field(fieldName, argumentIndex, jsonPath, displayInList, filterable, sortable);
    }

    public JobSearchDefinitionBuilder Field(
        string name,
        Func<Job, string?> valueExtractor,
        bool displayInList = true,
        bool filterable = true,
        bool sortable = false)
    {
        if (valueExtractor is null)
        {
            throw new ArgumentNullException(nameof(valueExtractor));
        }

        _definition.Fields.Add(new JobSearchField
        {
            Name = name,
            ArgumentIndex = 0,
            JsonPath = string.Empty,
            ValueExtractor = valueExtractor,
            DisplayInList = displayInList,
            Filterable = filterable,
            Sortable = sortable
        });
        return this;
    }

    public JobSearchDefinitionBuilder DiscoverFieldsFromAttributes(int argumentIndex = 0)
    {
        if (_method is null)
        {
            throw new InvalidOperationException("Method metadata is required to discover fields from attributes.");
        }

        var parameters = _method.GetParameters();
        if (argumentIndex < 0 || argumentIndex >= parameters.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(argumentIndex));
        }

        DiscoverProperties(parameters[argumentIndex].ParameterType, argumentIndex, "$");
        return this;
    }

    private void DiscoverProperties(Type type, int argumentIndex, string parentPath)
    {
        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var attr = prop.GetCustomAttribute<JobSearchFieldAttribute>();
            if (attr is null)
            {
                continue;
            }

            var fieldName = string.IsNullOrWhiteSpace(attr.Name) ? prop.Name : attr.Name!;
            _definition.Fields.Add(new JobSearchField
            {
                Name = fieldName,
                ArgumentIndex = argumentIndex,
                JsonPath = $"{parentPath}.{prop.Name}",
                DisplayInList = attr.DisplayInList,
                Filterable = attr.Filterable,
                Sortable = attr.Sortable
            });
        }
    }

    private static List<string> ExtractMemberPath(Expression expression)
    {
        var segments = new List<string>();
        var current = expression;

        while (current is UnaryExpression unary && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
        {
            current = unary.Operand;
        }

        while (current is MemberExpression member)
        {
            segments.Add(member.Member.Name);
            current = member.Expression!;
        }

        segments.Reverse();
        return segments;
    }
}

public static class JobSearchOptionsFluentExtensions
{
    public static JobSearchOptions AddJob<TWorker>(
        this JobSearchOptions options,
        Expression<Action<TWorker>> methodCall,
        Action<JobSearchDefinitionBuilder>? configure = null)
    {
        return AddJobCore(options, typeof(TWorker), ExtractMethod(methodCall), configure);
    }

    public static JobSearchOptions AddJob<TWorker>(
        this JobSearchOptions options,
        Expression<Func<TWorker, Task>> methodCall,
        Action<JobSearchDefinitionBuilder>? configure = null)
    {
        return AddJobCore(options, typeof(TWorker), ExtractMethod(methodCall), configure);
    }

    private static JobSearchOptions AddJobCore(
        JobSearchOptions options,
        Type workerType,
        MethodInfo method,
        Action<JobSearchDefinitionBuilder>? configure)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var definition = new JobSearchDefinition
        {
            JobType = workerType,
            MethodName = method.Name
        };

        var builder = new JobSearchDefinitionBuilder(definition, method);
        configure?.Invoke(builder);

        options.AddJobDefinition(definition);
        return options;
    }

    private static MethodInfo ExtractMethod(LambdaExpression expression)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        if (expression.Body is MethodCallExpression call)
        {
            return call.Method;
        }

        throw new ArgumentException("Expression must be a method call on the worker type.", nameof(expression));
    }
}
