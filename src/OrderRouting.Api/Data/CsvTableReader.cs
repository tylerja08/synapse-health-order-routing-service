using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace OrderRouting.Api.Data;

public sealed class CsvTable
{
    public CsvTable(IReadOnlyList<string> headers, IReadOnlyList<CsvRow> rows)
    {
        Headers = headers;
        Rows = rows;
    }

    public IReadOnlyList<string> Headers { get; }

    public IReadOnlyList<CsvRow> Rows { get; }
}

public sealed class CsvRow
{
    private readonly Dictionary<string, string> _values;

    public CsvRow(int rowNumber, Dictionary<string, string> values)
    {
        RowNumber = rowNumber;
        _values = values;
    }

    public int RowNumber { get; }

    public string Required(string header)
    {
        if (!_values.TryGetValue(header, out var value))
        {
            throw new DataLoadException($"Row {RowNumber} is missing required column '{header}'.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DataLoadException($"Row {RowNumber} has blank required column '{header}'.");
        }

        return CsvInputSecurity.ValidateCell(header, value, RowNumber);
    }

    public string RequiredAny(params string[] headers)
    {
        foreach (var header in headers)
        {
            if (_values.TryGetValue(header, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return CsvInputSecurity.ValidateCell(header, value, RowNumber);
            }
        }

        throw new DataLoadException($"Row {RowNumber} is missing required column '{string.Join("' or '", headers)}'.");
    }
}

public static class CsvTableReader
{
    private static readonly CsvConfiguration Configuration = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = false,
        IgnoreBlankLines = true,
        TrimOptions = TrimOptions.None,
        BadDataFound = args => throw new DataLoadException($"CSV input contains malformed data near row {args.Context?.Parser?.Row ?? 0}."),
        MissingFieldFound = null
    };

    public static CsvTable Read(string path)
    {
        var rows = ReadRows(path).ToArray();
        var headers = ReadHeaders(path);
        return new CsvTable(headers, rows);
    }

    public static IReadOnlyList<string> ReadHeaders(string path)
    {
        if (!File.Exists(path))
        {
            throw new DataLoadException($"CSV file '{path}' does not exist.");
        }

        using var reader = File.OpenText(path);
        var records = EnumerateRecords(reader).GetEnumerator();
        if (!records.MoveNext())
        {
            throw new DataLoadException($"CSV file '{path}' is empty.");
        }

        return ValidateHeaders(records.Current, path);
    }

    public static IEnumerable<CsvRow> ReadRows(string path)
    {
        if (!File.Exists(path))
        {
            throw new DataLoadException($"CSV file '{path}' does not exist.");
        }

        using var reader = File.OpenText(path);
        var recordNumber = 0;
        string[]? headers = null;

        foreach (var record in EnumerateRecords(reader))
        {
            recordNumber++;
            if (recordNumber == 1)
            {
                headers = ValidateHeaders(record, path);
                continue;
            }

            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
            {
                continue;
            }

            if (headers is null)
            {
                throw new DataLoadException($"CSV file '{path}' is empty.");
            }

            if (record.Count != headers.Length)
            {
                throw new DataLoadException($"CSV file '{path}' row {recordNumber} has {record.Count} fields, expected {headers.Length}.");
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var fieldIndex = 0; fieldIndex < headers.Length; fieldIndex++)
            {
                values[headers[fieldIndex]] = record[fieldIndex];
            }

            yield return new CsvRow(recordNumber, values);
        }

        if (recordNumber == 0)
        {
            throw new DataLoadException($"CSV file '{path}' is empty.");
        }
    }

    public static int CountDataRows(string path)
    {
        var count = 0;
        foreach (var _ in ReadRows(path))
        {
            count++;
        }

        return count;
    }

    private static string[] ValidateHeaders(IReadOnlyList<string> headerRecord, string path)
    {
        var headers = headerRecord.Select(header => header.Trim()).ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace))
        {
            throw new DataLoadException($"CSV file '{path}' has a blank header.");
        }

        var duplicateHeaders = headers.GroupBy(header => header, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateHeaders is not null)
        {
            throw new DataLoadException($"CSV file '{path}' has duplicate header '{duplicateHeaders.Key}'.");
        }

        return headers;
    }

    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        using var reader = new StringReader(text);
        return EnumerateRecords(reader).ToArray();
    }

    private static IEnumerable<IReadOnlyList<string>> EnumerateRecords(TextReader reader)
    {
        using var csv = new CsvReader(reader, Configuration);

        while (Read(csv))
        {
            yield return (csv.Parser.Record ?? []).ToArray();
        }
    }

    private static bool Read(CsvReader csv)
    {
        try
        {
            return csv.Read();
        }
        catch (DataLoadException)
        {
            throw;
        }
        catch (CsvHelperException exception)
        {
            throw new DataLoadException($"CSV input could not be parsed near row {csv.Context?.Parser?.Row ?? 0}.", exception);
        }
    }
}
