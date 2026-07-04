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
        if (!File.Exists(path))
        {
            throw new DataLoadException($"CSV file '{path}' does not exist.");
        }

        var rows = Parse(File.ReadAllText(path));
        if (rows.Count == 0)
        {
            throw new DataLoadException($"CSV file '{path}' is empty.");
        }

        var headers = rows[0].Select(header => header.Trim()).ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace))
        {
            throw new DataLoadException($"CSV file '{path}' has a blank header.");
        }

        var duplicateHeaders = headers.GroupBy(header => header, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateHeaders is not null)
        {
            throw new DataLoadException($"CSV file '{path}' has duplicate header '{duplicateHeaders.Key}'.");
        }

        var dataRows = new List<CsvRow>();
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0]))
            {
                continue;
            }

            if (row.Count != headers.Length)
            {
                throw new DataLoadException($"CSV file '{path}' row {index + 1} has {row.Count} fields, expected {headers.Length}.");
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var fieldIndex = 0; fieldIndex < headers.Length; fieldIndex++)
            {
                values[headers[fieldIndex]] = row[fieldIndex];
            }

            dataRows.Add(new CsvRow(index + 1, values));
        }

        return new CsvTable(headers, dataRows);
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
