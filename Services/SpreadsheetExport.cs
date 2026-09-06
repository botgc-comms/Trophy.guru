namespace Trophy.Catalogue.Services;

public static class SpreadsheetExport
{
    public static string Cell(string value)
    {
        var first = value.AsSpan().TrimStart();
        // Quoting alone does not stop a spreadsheet evaluating a formula.
        if (first.Length > 0 && first[0] is '=' or '+' or '-' or '@' || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
