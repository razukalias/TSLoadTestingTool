using ClosedXML.Excel;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Reporting;

public sealed class HistoryWriter
{
    private readonly AppConfig _config;
    public HistoryWriter(AppConfig config) => _config = config;

    public string Write(RunResult run, string? outputFolder = null, string? fileName = null, WorkbookModel? workbookModel = null)
    {
        var folder = outputFolder ?? _config.HistoryFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName ?? $"Execution_History_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        using var workbook = new XLWorkbook();
        var summary = workbook.AddWorksheet("RunSummary");
        WriteTable(summary, "runid", "starttime", "endtime", "durationms", "selectionSource", "selectionInput", "selectedIndexes", "totalTestcases", "totalRequests", "passedRequests", "failedRequests", "p95DurationMs", "p99DurationMs", "requestsPerSecond", "overallResult");
        var requests = run.Testcases.SelectMany(t => t.Requests).ToList();
        var metrics = run.Metrics;
        var passed = requests.Count(r => r.Result == "PASS"); var failed = requests.Count(r => r.Result == "FAIL");
        summary.AddRow(run.RunId, run.StartedAt, run.EndedAt, (run.EndedAt - run.StartedAt).TotalMilliseconds, run.Selection.Source, run.Selection.Input, string.Join(",", run.Selection.Indexes), run.Testcases.Count, requests.Count, passed, failed, metrics.P95DurationMs, metrics.P99DurationMs, metrics.RequestsPerSecond, run.Cancelled ? "CANCELLED" : failed == 0 ? "PASSED" : "FAILED");

        var metricsSheet = workbook.AddWorksheet("Metrics");
        WriteTable(metricsSheet, "metric", "value");
        metricsSheet.AddRow("totalRequests", metrics.TotalRequests);
        metricsSheet.AddRow("passedRequests", metrics.PassedRequests);
        metricsSheet.AddRow("failedRequests", metrics.FailedRequests);
        metricsSheet.AddRow("cancelledRequests", metrics.CancelledRequests);
        metricsSheet.AddRow("assertionFailures", metrics.AssertionFailures);
        metricsSheet.AddRow("averageDurationMs", metrics.AverageDurationMs);
        metricsSheet.AddRow("p50DurationMs", metrics.P50DurationMs);
        metricsSheet.AddRow("p95DurationMs", metrics.P95DurationMs);
        metricsSheet.AddRow("p99DurationMs", metrics.P99DurationMs);
        metricsSheet.AddRow("maxDurationMs", metrics.MaxDurationMs);
        metricsSheet.AddRow("requestsPerSecond", metrics.RequestsPerSecond);

        var tc = workbook.AddWorksheet("TestcaseSummary");
        WriteTable(tc, "runid", "testcaseindex", "testcase", "dataid", "thread", "iteration", "starttime", "endtime", "durationms", "totalsteps", "passedsteps", "failedsteps", "skippedsteps", "result");
        foreach (var t in run.Testcases) tc.AddRow(run.RunId, t.TestcaseIndex, t.Testcase, t.DataId, t.Thread, t.Iteration, t.StartedAt, t.EndedAt, (t.EndedAt - t.StartedAt).TotalMilliseconds, t.Requests.Count, t.Requests.Count(r => r.Result == "PASS"), t.Requests.Count(r => r.Result == "FAIL"), t.Requests.Count(r => r.Result == "SKIPPED"), t.Result);

        var rh = workbook.AddWorksheet("RequestHistory");
        WriteTable(rh, "runid", "requestid", "testcaseindex", "testcase", "dataid", "stepname", "steptype", "thread", "iteration", "sequenceorder", "starttime", "endtime", "waitms", "verb", "targeturl", "templetsource", "stepconfig", "contenttype", "requestheaders", "requestbody", "httpstatus", "responseheaders", "responsebody", "durationms", "assertioncount", "passedassertions", "failedassertions", "assertionresult", "requestresult", "failurecategory", "runtimereference", "runtimereferencekey", "errormessage", "failureaction");
        foreach (var r in requests) rh.AddRow(run.RunId, r.RequestId, r.TestcaseIndex, r.Testcase, r.DataId, r.StepName, r.StepType, r.Thread, r.Iteration, r.SequenceOrder, r.StartedAt, r.EndedAt, r.WaitMs, r.Verb, r.TargetUrl, r.TemplateSource, Mask(r.StepConfig), r.ContentType, Mask(r.RequestHeaders), _config.SaveRequestBodyToHistory ? Mask(r.RequestBody) : "[omitted]", r.HttpStatus, Mask(r.ResponseHeaders), _config.SaveResponseBodyToHistory ? Mask(r.ResponseBody) : "[omitted]", r.DurationMs, r.Assertions.Count, r.Assertions.Count(a => a.Result == "PASS"), r.Assertions.Count(a => a.Result == "FAIL"), r.Assertions.Count == 0 || r.Assertions.All(a => a.Result == "PASS") ? "PASS" : "FAIL", r.Result, r.FailureCategory, r.RuntimeReference, r.RuntimeReferenceKey, r.ErrorMessage, r.Result == "FAIL" ? (r.FailureCategory == "RuntimeReference" ? "Current data row stopped; remaining rows continued" : "Sequence stopped when configured") : "");

        var referenceErrors = workbook.AddWorksheet("RuntimeReferenceErrors");
        WriteTable(referenceErrors, "runid", "testcaseindex", "testcase", "dataid", "thread", "iteration", "sequenceorder", "stepname", "steptype", "environment", "requestid", "reference", "referencekey", "error", "timestamp");
        foreach (var r in requests.Where(r => r.FailureCategory.Equals("RuntimeReference", StringComparison.OrdinalIgnoreCase)))
            referenceErrors.AddRow(run.RunId, r.TestcaseIndex, r.Testcase, r.DataId, r.Thread, r.Iteration, r.SequenceOrder, r.StepName, r.StepType, r.Environment, r.RequestId, r.RuntimeReference, r.RuntimeReferenceKey, r.ErrorMessage, r.EndedAt);

        var ah = workbook.AddWorksheet("AssertionHistory");
        WriteTable(ah, "runid", "requestid", "testcaseindex", "testcase", "dataid", "stepname", "responsepath", "assertionverb", "expectedvalue", "actualvalue", "result", "failuremessage");
        foreach (var a in requests.SelectMany(r => r.Assertions)) ah.AddRow(run.RunId, a.RequestId, a.TestcaseIndex, a.Testcase, a.DataId, a.StepName, a.ResponsePath, a.AssertionVerb, a.ExpectedValue, a.ActualValue, a.Result, a.FailureMessage);

        var vars = workbook.AddWorksheet("Variables"); WriteTable(vars, "runid", "requestid", "testcaseindex", "testcase", "dataid", "stepname", "variable", "value", "source");
        var corr = workbook.AddWorksheet("CorrelationHistory"); WriteTable(corr, "runid", "requestid", "testcaseindex", "dataid", "sourceStepName", "responsepath", "variable", "extractedvalue", "usedByStepName");
        foreach (var r in requests)
        {
            foreach (var variable in r.Variables)
                vars.AddRow(run.RunId, r.RequestId, r.TestcaseIndex, r.Testcase, r.DataId, r.StepName, variable.Key, Mask($"{variable.Key}={variable.Value}"), "request/runtime");
            foreach (var correlation in r.Correlations)
                corr.AddRow(run.RunId, r.RequestId, r.TestcaseIndex, r.DataId, correlation.SourceStepName, correlation.ResponsePath, correlation.Variable, Mask($"{correlation.Variable}={correlation.ExtractedValue}"), correlation.UsedByStepName);
        }
        if (workbookModel is not null)
        {
            var data = workbook.AddWorksheet("DataEngineRequestRows");
            WriteTable(data, "testcaseindex", "testcase", "dataid", "dontrun", "variable", "value", "source");
            foreach (var row in workbookModel.RequestData.Values.OrderBy(x => x.TestcaseIndex).ThenBy(x => x.DataId))
            {
                if (row.Variables.Count == 0) data.AddRow(row.TestcaseIndex, row.Testcase, row.DataId, string.Join(',', row.Dontrun), "", "", "DataEngine request row");
                foreach (var variable in row.Variables.OrderBy(x => x.Key)) data.AddRow(row.TestcaseIndex, row.Testcase, row.DataId, string.Join(',', row.Dontrun), variable.Key, Mask(variable.Value), "DataEngine request row");
            }
            var config = workbook.AddWorksheet("DataEngineConfig");
            WriteTable(config, "testcaseindex", "testcase", "stepname", "steptype", "stage", "enabled", "sequence", "templetsource", "targeturl", "verb", "contenttype", "stepconfig", "headers", "stoponfailure", "assertonlyresponse", "environments");
            foreach (var testcase in workbookModel.Testcases)
                foreach (var step in testcase.Steps)
                    config.AddRow(step.TestcaseIndex, step.Testcase, step.StepName, step.StepType.WireName(), step.Stage.ToString(), step.Enabled, step.Sequence, step.TemplateSource, step.TargetUrl, step.Verb, step.ContentType, Mask(step.StepConfig), Mask(string.Join("; ", step.Headers.Select(h => $"{h.Key}={h.Value}"))), step.StopOnFailure, step.AssertOnlyResponse, string.Join(',', step.Environments));
        }
        foreach (var sheet in workbook.Worksheets) { sheet.Columns().AdjustToContents(1, 80); sheet.SheetView.FreezeRows(1); sheet.Row(1).Style.Font.Bold = true; sheet.Row(1).Style.Fill.SetBackgroundColor(XLColor.LightBlue); }
        workbook.SaveAs(path); return path;
    }

    private string Mask(string value) => InternalLogger.MaskSensitiveData(value, _config.MaskSensitiveData);
    private static void WriteTable(IXLWorksheet sheet, params string[] headers) { for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i]; }
}

internal static class WorksheetExtensions
{
    public static void AddRow(this IXLWorksheet sheet, params object?[] values)
    { var row = sheet.LastRowUsed()?.RowNumber() + 1 ?? 2; for (var i = 0; i < values.Length; i++) sheet.Cell(row, i + 1).Value = XLCellValue.FromObject(values[i]); }
}
