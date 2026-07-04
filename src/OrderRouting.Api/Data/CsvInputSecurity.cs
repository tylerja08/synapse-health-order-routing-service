namespace OrderRouting.Api.Data;

public static class CsvInputSecurity
{
    private static readonly char[] FormulaPrefixes = ['=', '+', '-', '@'];

    public static string ValidateCell(string fieldName, string value, int rowNumber)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if (FormulaPrefixes.Contains(trimmed[0]))
        {
            throw new DataLoadException($"Row {rowNumber} column '{fieldName}' starts with a spreadsheet formula prefix.");
        }

        return trimmed;
    }
}
