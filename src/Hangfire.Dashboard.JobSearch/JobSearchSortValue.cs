using System;
using System.Globalization;

namespace Hangfire.Dashboard.JobSearch;

internal readonly struct JobSearchSortValue : IComparable<JobSearchSortValue>
{
    private readonly decimal _number;
    private readonly string _text;

    private JobSearchSortValue(bool hasNumber, decimal number, string text)
    {
        HasNumber = hasNumber;
        _number = number;
        _text = text;
    }

    public bool HasNumber { get; }

    public static JobSearchSortValue From(string? value)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return new JobSearchSortValue(true, number, string.Empty);
        }

        return new JobSearchSortValue(false, 0, value ?? string.Empty);
    }

    public int CompareTo(JobSearchSortValue other)
    {
        var numberComparison = HasNumber.CompareTo(other.HasNumber);
        if (numberComparison != 0)
        {
            return numberComparison;
        }

        return HasNumber
            ? _number.CompareTo(other._number)
            : string.Compare(_text, other._text, StringComparison.Ordinal);
    }
}
