using System;
using System.Collections.Generic;
using System.Linq;

namespace Hangfire.Dashboard.JobSearch;

internal static class JobSearchFilterMatcher
{
    public static IReadOnlyList<string> ParseTerms(string filterValue)
    {
        if (string.IsNullOrWhiteSpace(filterValue))
        {
            return Array.Empty<string>();
        }

        if (filterValue.IndexOf(',') < 0)
        {
            return new[] { filterValue.Trim() };
        }

        return filterValue
            .Split(',')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsPrefixTerm(string term, out string prefix)
    {
        if (term.Length > 1 && term[term.Length - 1] == '*')
        {
            prefix = term.Substring(0, term.Length - 1);
            return true;
        }

        prefix = term;
        return false;
    }

    public static bool Matches(string? actualValue, string filterValue)
    {
        var terms = ParseTerms(filterValue);
        if (terms.Count == 0)
        {
            return true;
        }

        if (actualValue is null)
        {
            return false;
        }

        foreach (var term in terms)
        {
            if (IsPrefixTerm(term, out var prefix))
            {
                if (actualValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(actualValue, term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
