using OrderRouting.Api.Data;

namespace OrderRouting.Api.Routing;

public sealed class ZipCoverage
{
    private readonly IReadOnlyList<ZipRange> _ranges;

    public ZipCoverage(IReadOnlyList<ZipRange> ranges)
    {
        _ranges = ranges;
    }

    public bool Contains(string zip)
    {
        var normalized = NormalizeZip(zip);
        return _ranges.Any(range => string.CompareOrdinal(normalized, range.Start) >= 0 && string.CompareOrdinal(normalized, range.End) <= 0);
    }

    public static ZipCoverage Parse(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DataLoadException($"{fieldName} must include at least one ZIP or ZIP range.");
        }

        var ranges = new List<ZipRange>();
        foreach (var rawToken in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawToken.Contains('-', StringComparison.Ordinal))
            {
                var parts = rawToken.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    throw new DataLoadException($"{fieldName} contains malformed ZIP range '{rawToken}'.");
                }

                var start = NormalizeZip(parts[0]);
                var end = NormalizeZip(parts[1]);
                if (string.CompareOrdinal(start, end) > 0)
                {
                    throw new DataLoadException($"{fieldName} contains reversed ZIP range '{rawToken}'.");
                }

                ranges.Add(new ZipRange(start, end));
            }
            else
            {
                var zip = NormalizeZip(rawToken);
                ranges.Add(new ZipRange(zip, zip));
            }
        }

        if (ranges.Count == 0)
        {
            throw new DataLoadException($"{fieldName} must include at least one ZIP or ZIP range.");
        }

        return new ZipCoverage(ranges);
    }

    public static string NormalizeZip(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is < 1 or > 5 || trimmed.Any(ch => !char.IsDigit(ch)))
        {
            throw new DataLoadException($"ZIP value '{value}' must contain one to five digits.");
        }

        return trimmed.PadLeft(5, '0');
    }
}

public sealed record ZipRange(string Start, string End);
