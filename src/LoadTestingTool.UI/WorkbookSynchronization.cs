using ClosedXML.Excel;
using System.Text.RegularExpressions;

namespace LoadTestingTool.UI;

internal static class WorkbookSynchronization
{
    internal sealed record StepChange(string TestcaseIndex, string OldName, string? NewName);

    public static void SynchronizeRequestResponse(IXLWorkbook workbook)
    {
        var request = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase));
        var response = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("response", StringComparison.OrdinalIgnoreCase));
        if (request is null || response is null) return;

        var requestHeaders = Headers(request);
        var responseHeaders = Headers(response);
        if (!TryColumn(requestHeaders, "testcaseindex", out var requestTestcase) || !TryColumn(requestHeaders, "dataid", out var requestDataId)
            || !TryColumn(responseHeaders, "testcaseindex", out var responseTestcase) || !TryColumn(responseHeaders, "dataid", out var responseDataId)) return;

        var requestRows = new List<(string Testcase, string DataId)>();
        foreach (var row in request.RowsUsed().Skip(1))
        {
            var key = (row.Cell(requestTestcase).GetString().Trim(), row.Cell(requestDataId).GetString().Trim());
            requestRows.Add(key);
        }

        var width = Math.Max(responseHeaders.Values.DefaultIfEmpty(1).Max(), response.LastColumnUsed()?.ColumnNumber() ?? 1);
        var existing = new Dictionary<(string Testcase, string DataId), string[]>(new RowKeyComparer());
        var responseRows = response.RowsUsed().Skip(1).ToList();
        foreach (var row in responseRows)
        {
            var key = (row.Cell(responseTestcase).GetString().Trim(), row.Cell(responseDataId).GetString().Trim());
            if (!string.IsNullOrWhiteSpace(key.Item1) && !string.IsNullOrWhiteSpace(key.Item2) && !existing.ContainsKey(key))
                existing[key] = Enumerable.Range(1, width).Select(row.Cell).Select(x => x.GetString()).ToArray();
        }

        var oldLastRow = response.LastRowUsed()?.RowNumber() ?? 1;
        var oldLastColumn = response.LastColumnUsed()?.ColumnNumber() ?? width;
        if (oldLastRow > 1 || oldLastColumn > 0) response.Range(1, 1, Math.Max(1, oldLastRow), Math.Max(1, oldLastColumn)).Clear(XLClearOptions.Contents);
        foreach (var pair in responseHeaders) response.Cell(1, pair.Value).Value = pair.Key;

        var outputRow = 2;
        for (var requestIndex = 0; requestIndex < requestRows.Count; requestIndex++)
        {
            var key = requestRows[requestIndex];
            if (existing.TryGetValue(key, out var values) || (requestIndex < responseRows.Count && !string.IsNullOrWhiteSpace(responseRows[requestIndex].Cell(responseTestcase).GetString())))
            {
                values ??= Enumerable.Range(1, width).Select(responseRows[requestIndex].Cell).Select(x => x.GetString()).ToArray();
                for (var column = 1; column <= width; column++) response.Cell(outputRow, column).Value = values.ElementAtOrDefault(column - 1) ?? "";
            }
            response.Cell(outputRow, responseTestcase).Value = key.Testcase;
            response.Cell(outputRow, responseDataId).Value = key.DataId;
            outputRow++;
        }
        for (var row = 1; row < outputRow; row++) response.Row(row).Height = 22;
        response.Row(1).Style.Font.Bold = true;
        response.SheetView.FreezeRows(1);
    }

    public static IReadOnlyList<string> SynchronizeStepColumns(IXLWorkbook workbook, IEnumerable<StepChange> changes)
    {
        var warnings = new List<string>();
        var sheets = workbook.Worksheets.Where(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("response", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var change in changes.Where(x => !string.IsNullOrWhiteSpace(x.OldName)))
        {
            foreach (var sheet in sheets)
            {
                var headers = Headers(sheet);
                var matching = headers.Where(x => IsStepHeader(x.Key, change.OldName)).OrderByDescending(x => x.Value).ToList();
                if (change.NewName is null)
                {
                    foreach (var column in matching) sheet.Column(column.Value).Delete();
                }
                else
                {
                    foreach (var column in matching)
                    {
                        var updated = RenameStepHeader(column.Key, change.OldName, change.NewName);
                        sheet.Cell(1, column.Value).Value = updated;
                    }
                    ReplaceReferences(sheet, change.OldName, change.NewName);
                }
            }
            if (change.NewName is null)
                foreach (var sheet in sheets) ReplaceReferences(sheet, change.OldName, null);
        }
        return warnings;
    }

    private static Dictionary<string, int> Headers(IXLWorksheet sheet) => sheet.FirstRowUsed()?.CellsUsed().ToDictionary(x => x.GetString().Trim(), x => x.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
    private static bool TryColumn(Dictionary<string, int> headers, string name, out int column) => headers.TryGetValue(name, out column);
    private static bool IsStepHeader(string header, string stepName)
    {
        var clean = Regex.Replace(header.Trim(), @"^\{[^{}]*\}", "").Trim();
        return clean.Equals(stepName, StringComparison.OrdinalIgnoreCase) || clean.StartsWith(stepName + ".", StringComparison.OrdinalIgnoreCase);
    }
    private static string RenameStepHeader(string header, string oldName, string newName)
    {
        var marker = Regex.Match(header, @"^\{[^{}]*\}");
        var prefix = marker.Success ? marker.Value : "";
        var clean = marker.Success ? header[marker.Length..] : header;
        return prefix + (clean.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : newName + clean[oldName.Length..]);
    }
    private static void ReplaceReferences(IXLWorksheet sheet, string oldName, string? newName)
    {
        foreach (var cell in sheet.CellsUsed())
        {
            var value = cell.GetString();
            if (string.IsNullOrEmpty(value)) continue;
            var pattern = $"(<from_){Regex.Escape(oldName)}(?=[_.>])";
            var replacement = newName is null ? "$1[deleted-step]" : "$1" + newName;
            var updated = Regex.Replace(value, pattern, replacement, RegexOptions.IgnoreCase);
            if (!ReferenceEquals(updated, value) && updated != value) cell.Value = updated;
        }
    }

    private sealed class RowKeyComparer : IEqualityComparer<(string Testcase, string DataId)>
    {
        public bool Equals((string Testcase, string DataId) x, (string Testcase, string DataId) y) => string.Equals(x.Testcase, y.Testcase, StringComparison.OrdinalIgnoreCase) && string.Equals(x.DataId, y.DataId, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Testcase, string DataId) value) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Testcase), StringComparer.OrdinalIgnoreCase.GetHashCode(value.DataId));
    }
}
