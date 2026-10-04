using System.Diagnostics;
using System.Text.Json;
using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.UI;
using LoadTestingTool.Validation;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
void Reject(Action action, string name)
{
    try { action(); } catch (InvalidDataException) { Check(true, name); return; }
    throw new Exception("FAIL: expected rejection: " + name);
}
var steps = new[]
{
    new RequestStep { TestcaseIndex = 1, StepName = "Dev", Enabled = true, Environments = ["dev"], Sequence = 3 },
    new RequestStep { TestcaseIndex = 1, StepName = "Prod", Enabled = true, Environments = ["prod"], Sequence = 1 },
    new RequestStep { TestcaseIndex = 1, StepName = "Shared", Enabled = true, Environments = [], Sequence = 2 },
    new RequestStep { TestcaseIndex = 1, StepName = "Disabled", Enabled = false, Environments = ["dev"], Sequence = 4 }
};
var model = new WorkbookModel { Testcases = [new TestcaseDefinition { TestcaseIndex = 1, Testcase = "First", Steps = steps },
    new TestcaseDefinition { TestcaseIndex = 2, Testcase = "Second", Steps = [new RequestStep { TestcaseIndex = 2, StepName = "Dev", Environments = ["dev"] }] }] };
var planner = new RunSelectionPlanner();
var dev = planner.Build(model, [1], null, "dev");
Check(dev.Count == 1 && dev[0].Testcase.Steps.Select(x => x.StepName).SequenceEqual(new[] { "Shared", "Dev" }), "Environment excludes prod/disabled steps and keeps workbook sequence");
var one = planner.Build(model, [1], "{\"1\":[\"dEv\"]}", "DEV");
Check(one.Count == 1 && one[0].Testcase.Steps.Count == 1 && one[0].Testcase.Steps[0].StepName == "Dev", "Named step selection is testcase-scoped and case-insensitive");
Check(planner.Build(model, [1, 2], null, "prod").All(x => x.Testcase.TestcaseIndex == 1), "Testcase with no matching environment is omitted");
Check(planner.Build(model, [1], null, "dev,dev").Count == 1, "Duplicate environments do not duplicate runs");
Reject(() => planner.Build(model, [], null, "dev"), "Empty testcase selection rejected");
Reject(() => planner.Build(model, [1], "{}", "dev"), "Empty explicit step selection rejected");
Reject(() => planner.Build(model, [1], "{\"1\":[\"Disabled\"]}", "dev"), "Disabled step cannot be forced on");
Reject(() => planner.Build(model, [1], "{\"1\":[\"Missing\"]}", "dev"), "Unknown step rejected");
Reject(() => planner.Build(model, [1, 2], "{\"1\":[\"Dev\"]}", "dev"), "Explicit step JSON must cover all selected testcases");
Reject(() => planner.Build(model, [1], "{\"2\":[\"Dev\"]}", "dev"), "Unselected testcase step filter rejected");
Reject(() => planner.Build(model, [1], "{\"1\":[\"Dev\"]}", "prod"), "Incompatible step/environment selection rejected");
Reject(() => planner.Build(model, [1], "{\"1\":[\"Dev\"],\"01\":[\"Dev\"]}", "dev"), "Duplicate numeric testcase keys rejected");
Reject(() => planner.Build(model, [1], "not json", "dev"), "Malformed step JSON rejected");
var punctuation = new WorkbookModel { Testcases = [new TestcaseDefinition { TestcaseIndex = 1, Steps = [new RequestStep { TestcaseIndex = 1, StepName = "A, B: \"quote\"", Enabled = true }] }] };
Check(planner.Build(punctuation, [1], JsonSerializer.Serialize(new Dictionary<int, string[]> { [1] = ["A, B: \"quote\""] }), "").Count == 1, "JSON supports spaces, commas, colons and quotes in step names");

var catalog = new[] { new TestcaseOption(1, "First", [new("Dev", "File", true, ["dev"]), new("Prod", "File", true, ["prod"])]),
    new TestcaseOption(2, "Second", [new("Disabled", "File", false, [])]) };
var draft = new RunSelectionDraft(catalog, "0", "", "");
Check(draft.Cases.SetEquals([1]), "UI does not preselect testcases with no enabled steps");
draft.SetStep(1, "Prod", false);
Check(draft.AvailableEnvironments.SequenceEqual(new[] { "dev" }), "Environment choices follow selected steps");
Check(draft.TryBuild(out var selection, out _) && selection!.StepsJson.Contains("Dev") && !selection.StepsJson.Contains("Prod"), "UI serializes only checked steps");
draft.ClearEnvironments(); Check(!draft.TryBuild(out _, out _), "Clearing environments is NOT interpreted as Run all");
draft.SelectAllEnvironments(); draft.SetStep(1, "Dev", false); Check(!draft.TryBuild(out _, out _), "Clearing all test steps disables Apply");
draft.SetStep(1, "Dev", true); draft.SetCase(1, false); Check(!draft.TryBuild(out _, out _), "Clearing all testcases disables Apply");
var snapshotDraft = new RunSelectionDraft([new TestcaseOption(1, "First", [new("Dev", "File", true, ["dev"])])], "0", "", "");
Check(snapshotDraft.TryBuild(out var snapshot, out _) && snapshot!.Testcases == "1" && snapshot.StepsJson.Length > 0 && snapshot.EnvironmentsJson == "[\"dev\"]", "Checked-all choices stay explicit rather than dynamic Run all sentinels");
var changedCatalog = new[] { new TestcaseOption(1, "First", [new("Dev", "File", true, ["dev", "prod"]), new("New step", "File", true, ["dev"])]),
    new TestcaseOption(3, "New testcase", [new("Other", "File", true, ["prod"])]) };
var restored = new RunSelectionDraft(changedCatalog, snapshot!.Testcases, snapshot.StepsJson, snapshot.Environments, snapshot.EnvironmentsJson);
Check(restored.Cases.SetEquals([1]) && restored.Steps[1].SetEquals(["Dev"]) && restored.Environments.SetEquals(["dev"]), "Saved selection does not widen when cases, steps or environments are added");
var defaultDraft = new RunSelectionDraft([new TestcaseOption(1, "Default", [new("Shared", "File", true, [])])], "0", "", "");
Check(defaultDraft.TryBuild(out var defaultSelection, out _) && defaultSelection!.EnvironmentsJson == "[\"\"]", "Default environment is explicit in the saved selection");
Check(planner.Build(model, [1], null, null, "[\"\"]").Single().Testcase.Steps.Single().StepName == "Shared", "Explicit default environment does not expand to tagged environments");
Reject(() => planner.Build(model, [1], null, null, "[]"), "Empty explicit environment array rejected");
Reject(() => planner.Build(model, [1], null, null, "[null]"), "Invalid explicit environment values rejected");

foreach (var extension in new[] { ".xlsx", ".pdf", ".log", ".json", ".txt" })
{
    var info = SystemDefaultFileOpener.CreateStartInfo(Path.Combine(Path.GetTempPath(), "a path with spaces" + extension));
    Check(info.UseShellExecute && info.Arguments.Length == 0 && info.ArgumentList.Count == 0 && info.FileName.EndsWith(extension), "System default association used for " + extension);
}

var repo = Path.GetFullPath(args.FirstOrDefault() ?? Directory.GetCurrentDirectory());
var runner = Path.Combine(repo, "bin", "Debug", "net10.0", "LoadTestingTool.dll");
if (!File.Exists(runner)) throw new FileNotFoundException("Build the solution first to run integration checks.", runner);
var temporary = Path.Combine(Path.GetTempPath(), "selection regression " + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var workbookPath = Path.Combine(temporary, "test workbook.xlsx");
    using (var book = new XLWorkbook())
    {
        var config = book.Worksheets.Add("config");
        var headers = new[] { "testcaseindex", "testcase", "stepname", "steptype", "enabled", "stepconfig", "environments", "assert" };
        for (var i = 0; i < headers.Length; i++) config.Cell(1, i + 1).Value = headers[i];
        void Row(int row, int index, string name, string filename, string env, bool enabled = true)
        {
            config.Cell(row, 1).Value = index; config.Cell(row, 2).Value = "Case " + index; config.Cell(row, 3).Value = name;
            config.Cell(row, 4).Value = "File"; config.Cell(row, 5).Value = enabled;
            config.Cell(row, 6).Value = JsonSerializer.Serialize(new { action = "write", path = "out/" + filename, content = name });
            config.Cell(row, 7).Value = env; config.Cell(row, 8).Value = false;
        }
        Row(2, 1, "Write dev", "dev.txt", "dev"); Row(3, 1, "Write prod", "prod.txt", "prod"); Row(4, 1, "Global", "global.txt", "");
        Row(5, 2, "Write dev", "other-case.txt", "dev"); Row(6, 1, "Disabled", "disabled.txt", "dev", false);
        foreach (var sheetName in new[] { "request", "response" })
        {
            var sheet = book.Worksheets.Add(sheetName); sheet.Cell(1, 1).Value = "testcaseindex"; sheet.Cell(1, 2).Value = "testcase"; sheet.Cell(1, 3).Value = "dataid";
            for (var index = 1; index <= 2; index++) { sheet.Cell(index + 1, 1).Value = index; sheet.Cell(index + 1, 2).Value = "Case " + index; sheet.Cell(index + 1, 3).Value = "ROW" + index; }
        }
        Check(WorkbookSelectionCatalog.Read(book).Count == 2, "UI catalog reads named testcases and steps from the real workbook");
        book.SaveAs(workbookPath);
    }
    File.WriteAllText(Path.Combine(temporary, "appsettings.json"), JsonSerializer.Serialize(new { WorkspaceRoot = ".", PromptForTestcaseSelection = false, RequestTimeoutSeconds = 5 }));
    int Run(string cases, string? stepsJson, string env, string runId)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = temporary, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(runner);
        void Arg(string key, string value) { start.ArgumentList.Add(key); start.ArgumentList.Add(value); }
        Arg("--excel", workbookPath); Arg("--testcases", cases); Arg("--environments", env); Arg("--run-id", runId);
        if (stepsJson is not null) Arg("--steps", stepsJson);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new Exception("Test subprocess did not finish."); }
        Task.WaitAll(stdout, stderr); Console.WriteLine(stdout.Result.Trim()); if (process.ExitCode != 0) Console.WriteLine(stderr.Result.Trim());
        return process.ExitCode;
    }
    Check(Run("1", "{\"1\":[\"Write dev\"]}", "dev", "only-dev") == 0, "Real runner accepts selected step through ArgumentList");
    Check(File.Exists(Path.Combine(temporary, "out/dev.txt")) && Directory.GetFiles(Path.Combine(temporary, "out")).Length == 1, "Only selected testcase/step executed, no dependency or other environment added");
    Check(Run("1", null, "dev", "all-dev") == 0, "Real runner accepts environment-only selection");
    Check(File.Exists(Path.Combine(temporary, "out/global.txt")) && !File.Exists(Path.Combine(temporary, "out/prod.txt")), "Real runner excludes prod-tagged steps from dev run");
    Check(Run("1", "{\"1\":[\"Missing\"]}", "dev", "invalid-step") == 1, "Invalid real-run step fails preflight instead of silently running all");
    Check(Run("0", null, "prod", "all-prod") == 0 && !File.Exists(Path.Combine(temporary, "out/other-case.txt")), "Environment with no matching steps does not execute unrelated testcase");
    Console.WriteLine($"\nAll {count} regression checks passed.");
}
finally { Directory.Delete(temporary, recursive: true); }
