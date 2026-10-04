using System.Text.Json;
using ClosedXML.Excel;

namespace LoadTestingTool.UI;

public sealed record TestStepOption(string Name, string Type, bool Enabled, IReadOnlyList<string> Environments);
public sealed record TestcaseOption(int Index, string Name, IReadOnlyList<TestStepOption> Steps);
public sealed record RunSelectionResult(string Testcases, string StepsJson, string Environments, string EnvironmentsJson);

public static class WorkbookSelectionCatalog
{
    public static IReadOnlyList<TestcaseOption> Read(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The workbook has no config sheet.");
        var headers = sheet.FirstRowUsed()?.CellsUsed().ToDictionary(x => x.GetString().Trim(), x => x.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase)
            ?? throw new InvalidDataException("The config sheet has no headers.");
        foreach (var name in new[] { "testcaseindex", "testcase", "stepname" })
            if (!headers.ContainsKey(name)) throw new InvalidDataException($"Missing config column: {name}.");
        string Text(IXLRow row, string key, string fallback = "") => headers.TryGetValue(key, out var col) ? row.Cell(col).GetString().Trim() : fallback;
        var groups = new Dictionary<int, (string Name, List<TestStepOption> Steps)>();
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            if (row.CellsUsed().All(x => string.IsNullOrWhiteSpace(x.GetString()))) continue;
            if (!int.TryParse(Text(row, "testcaseindex"), out var index)) throw new InvalidDataException($"Invalid testcase index in config row {row.RowNumber()}.");
            var name = Text(row, "stepname");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"Missing step name in config row {row.RowNumber()}.");
            var enabled = !bool.TryParse(Text(row, "enabled"), out var flag) || flag;
            var env = new[] { "environments", "environment", "enviromests", "enviromments" }
                .Select(key => Text(row, key)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
            var environments = env.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!groups.TryGetValue(index, out var group)) groups[index] = group = (Text(row, "testcase"), []);
            if (group.Steps.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Duplicate step name in testcase {index}: {name}.");
            group.Steps.Add(new TestStepOption(name, Text(row, "steptype", "HTTP"), enabled, environments));
        }
        return groups.OrderBy(x => x.Key).Select(x => new TestcaseOption(x.Key, x.Value.Name, x.Value.Steps)).ToList();
    }
}

public sealed class RunSelectionDraft
{
    public IReadOnlyList<TestcaseOption> Catalog { get; }
    public HashSet<int> Cases { get; } = [];
    public Dictionary<int, HashSet<string>> Steps { get; } = [];
    public HashSet<string> Environments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AllEnvironments { get; private set; }
    public IReadOnlyList<string> AvailableEnvironments
    {
        get
        {
            var values = Catalog.Where(c => Cases.Contains(c.Index)).SelectMany(c => c.Steps.Where(s => s.Enabled && Steps[c.Index].Contains(s.Name)))
                .SelectMany(s => s.Environments).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            return values.Count == 0 ? [""] : values;
        }
    }
    public RunSelectionDraft(IReadOnlyList<TestcaseOption> catalog, string testcases, string stepsJson, string environments, string? environmentsJson = null)
    {
        Catalog = catalog;
        if (string.IsNullOrWhiteSpace(testcases) || testcases == "0") Cases.UnionWith(catalog.Where(c => c.Steps.Any(s => s.Enabled)).Select(c => c.Index));
        else foreach (var token in testcases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split('-');
            if (parts.Length == 1 && int.TryParse(parts[0], out var index)) Cases.Add(index);
            else if (parts.Length == 2 && int.TryParse(parts[0], out var start) && int.TryParse(parts[1], out var end) && start <= end)
                Cases.UnionWith(catalog.Where(c => c.Index >= start && c.Index <= end).Select(c => c.Index));
        }
        Cases.IntersectWith(catalog.Select(c => c.Index));
        var saved = string.IsNullOrWhiteSpace(stepsJson) ? null : JsonSerializer.Deserialize<Dictionary<int, string[]>>(stepsJson);
        foreach (var c in catalog)
        {
            var available = c.Steps.Where(s => s.Enabled).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (saved is not null && saved.TryGetValue(c.Index, out var selected)) available.IntersectWith(selected);
            Steps[c.Index] = available;
        }
        AllEnvironments = string.IsNullOrWhiteSpace(environments) && string.IsNullOrWhiteSpace(environmentsJson);
        if (!string.IsNullOrWhiteSpace(environmentsJson)) Environments.UnionWith(JsonSerializer.Deserialize<string[]>(environmentsJson) ?? []);
        else if (AllEnvironments) Environments.UnionWith(AvailableEnvironments);
        else Environments.UnionWith(environments.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
    public void SetCase(int index, bool selected) { if (selected) Cases.Add(index); else Cases.Remove(index); SyncEnvironments(); }
    public void SetStep(int index, string name, bool selected) { if (selected) Steps[index].Add(name); else Steps[index].Remove(name); SyncEnvironments(); }
    public void SetEnvironment(string name, bool selected)
    {
        AllEnvironments = false;
        if (selected) Environments.Add(name); else Environments.Remove(name);
    }
    public void SelectAllEnvironments() { AllEnvironments = true; SyncEnvironments(); }
    public void ClearEnvironments() { AllEnvironments = false; Environments.Clear(); }
    private void SyncEnvironments() { if (AllEnvironments) { Environments.Clear(); Environments.UnionWith(AvailableEnvironments); } }
    public bool TryBuild(out RunSelectionResult? result, out string error)
    {
        result = null;
        var cases = Catalog.Where(c => Cases.Contains(c.Index)).ToList();
        if (cases.Count == 0) { error = "Choose at least one testcase."; return false; }
        if (cases.Any(c => !c.Steps.Any(s => s.Enabled && Steps[c.Index].Contains(s.Name))))
        { error = "Choose at least one enabled test step in each selected testcase."; return false; }
        var environments = AvailableEnvironments.Where(e => Environments.Contains(e)).ToList();
        if (environments.Count == 0) { error = "Choose at least one environment (or Default for untagged steps)."; return false; }
        var selectedSteps = cases.ToDictionary(c => c.Index, c => c.Steps.Where(s => s.Enabled && Steps[c.Index].Contains(s.Name)).Select(s => s.Name).ToArray());
        result = new RunSelectionResult(string.Join(',', cases.Select(c => c.Index)), JsonSerializer.Serialize(selectedSteps),
            string.Join(',', environments.Select(e => string.IsNullOrEmpty(e) ? "Default" : e)), JsonSerializer.Serialize(environments));
        error = ""; return true;
    }
}
