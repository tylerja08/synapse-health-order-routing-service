using System.Text;

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

        return value.Trim();
    }

    public string RequiredAny(params string[] headers)
    {
        foreach (var header in headers)
        {
            if (_values.TryGetValue(header, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        throw new DataLoadException($"Row {RowNumber} is missing required column '{string.Join("' or '", headers)}'.");
    }
}

public static class CsvTableReader
{
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

    private static IEnumerable<IReadOnlyList<string>> EnumerateRecords(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStartedWithQuote = false;

        while (reader.Read() is var current && current != -1)
        {
            var ch = (char)current;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            if (ch == '"' && field.Length == 0 && !fieldStartedWithQuote)
            {
                inQuotes = true;
                fieldStartedWithQuote = true;
                continue;
            }

            if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStartedWithQuote = false;
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }

                row.Add(field.ToString());
                yield return row;
                row = new List<string>();
                field.Clear();
                fieldStartedWithQuote = false;
                continue;
            }

            field.Append(ch);
        }

        if (inQuotes)
        {
            throw new DataLoadException("CSV input ended inside a quoted field.");
        }

        if (field.Length > 0 || fieldStartedWithQuote || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }

    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStartedWithQuote = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            if (ch == '"' && field.Length == 0 && !fieldStartedWithQuote)
            {
                inQuotes = true;
                fieldStartedWithQuote = true;
                continue;
            }

            if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStartedWithQuote = false;
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                rows.Add(row);
                row = new List<string>();
                field.Clear();
                fieldStartedWithQuote = false;
                continue;
            }

            field.Append(ch);
        }

        if (inQuotes)
        {
            throw new DataLoadException("CSV input ended inside a quoted field.");
        }

        if (field.Length > 0 || fieldStartedWithQuote || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
