using System.Text.Json;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Reporting;

public sealed class LiveEventWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _sync = new();
    private readonly string _instanceId;

    public LiveEventWriter(string folder, string instanceId, string runId, string? fileSuffix = null)
    {
        Directory.CreateDirectory(folder);
        _instanceId = instanceId;
        var path = Path.Combine(folder, $"events_{runId}{fileSuffix ?? string.Empty}.ndjson");
        _writer = new StreamWriter(path, append: false) { AutoFlush = true };
        EventPath = path;
    }

    public string EventPath { get; }

    public void WriteRequest(RequestResult request)
    {
        var message = new
        {
            eventType = "request-completed",
            instanceId = _instanceId,
            runId = request.RunId,
            requestId = request.RequestId,
            testcaseIndex = request.TestcaseIndex,
            testcase = request.Testcase,
            dataId = request.DataId,
            stepName = request.StepName,
            stepType = request.StepType,
            template = request.TemplateSource,
            targetUrl = request.TargetUrl,
            thread = request.Thread,
            iteration = request.Iteration,
            result = request.Result,
            httpStatus = request.HttpStatus,
            durationMs = request.DurationMs,
            assertionsPassed = request.Assertions.Count(a => a.Result == "PASS"),
            assertionsFailed = request.Assertions.Count(a => a.Result == "FAIL"),
            errorMessage = request.ErrorMessage,
            failureCategory = request.FailureCategory,
            runtimeReference = request.RuntimeReference,
            runtimeReferenceKey = request.RuntimeReferenceKey
            ,environment = request.Environment
        };
        Write(message);
        foreach (var assertion in request.Assertions) Write(new
        {
            eventType = "assertion-completed",
            instanceId = _instanceId,
            runId = request.RunId,
            requestId = request.RequestId,
            testcaseIndex = assertion.TestcaseIndex,
            testcase = assertion.Testcase,
            dataId = assertion.DataId,
            stepName = assertion.StepName,
            responsePath = assertion.ResponsePath,
            assertionVerb = assertion.AssertionVerb,
            expectedValue = assertion.ExpectedValue,
            actualValue = assertion.ActualValue,
            result = assertion.Result,
            failureMessage = assertion.FailureMessage
            ,sourceSheet = "response"
            ,excelRowNumber = assertion.ExcelRowNumber
            ,expectedValueColumn = assertion.ExpectedValueColumn
            // The assertion engine does not own the execution environment;
            // the request does. Use the request environment so UI filters
            // and cross-environment comparisons receive a reliable value.
            ,environment = request.Environment
        });
    }

    public void WriteRunCompleted(RunResult run, string historyFile)
    {
        Write(new
        {
            eventType = "run-completed",
            instanceId = _instanceId,
            runId = run.RunId,
            result = run.Testcases.Any(t => t.Result == "FAILED") ? "FAILED" : "PASSED",
            totalTestcases = run.Testcases.Count,
            totalRequests = run.Testcases.Sum(t => t.Requests.Count),
            historyFile
            ,environment = run.Environment
        });
    }

    private void Write(object value)
    {
        lock (_sync) _writer.WriteLine(JsonSerializer.Serialize(value));
    }

    public void Dispose() => _writer.Dispose();
}
