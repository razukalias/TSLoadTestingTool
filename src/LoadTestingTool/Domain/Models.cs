namespace LoadTestingTool.Domain;

public sealed class AppConfig
{
    public string ExcelFilePath { get; set; } = "DataEngine.xlsx";
    public string TemplatesFolder { get; set; } = "Templates";
    public string LogsFolder { get; set; } = "Logs";
    public bool InternalLoggingEnabled { get; set; }
    public string HistoryFolder { get; set; } = "History";
    public string ResultsFolder { get; set; } = "Results";
    public string InstanceId { get; set; } = "default";
    public string RunId { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 300;
    public bool RunScenariosInParallel { get; set; }
    public string ExecutionMode { get; set; } = "threaded";
    public string TestcaseSelection { get; set; } = "0";
    public string EnvironmentSelection { get; set; } = "";
    public bool PromptForTestcaseSelection { get; set; }
    public bool SaveRequestBodyToHistory { get; set; } = true;
    public bool SaveResponseBodyToHistory { get; set; } = true;
    public bool MaskSensitiveData { get; set; } = true;
    public string WorkspaceRoot { get; set; } = ".";
    public Dictionary<string, SqlConnectionProfile> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AllowTrustedScripts { get; set; }
    public int ScriptTimeoutSeconds { get; set; } = 30;
}

public sealed class TestcaseDefinition
{
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public IReadOnlyList<RequestStep> Steps { get; init; } = [];
}

public sealed class RequestStep
{
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public StepType StepType { get; init; } = StepType.Http;
    public ExecutionStage Stage { get; init; } = ExecutionStage.Test;
    public bool Enabled { get; init; } = true;
    public int TimeoutSeconds { get; init; }
    public string StepConfig { get; init; } = "{}";
    public int Sequence { get; init; }
    public string TemplateSource { get; init; } = string.Empty;
    public string TargetUrl { get; init; } = string.Empty;
    public string Verb { get; init; } = "POST";
    public string ContentType { get; init; } = string.Empty;
    public int Threads { get; init; } = 1;
    public int Iterations { get; init; } = 1;
    public int ThreadIntervalMs { get; init; }
    public int IterationIntervalMs { get; init; }
    public int WaitMs { get; init; }
    public string ExpectedStatus { get; init; } = string.Empty;
    public bool StopOnFailure { get; init; } = true;
    public bool IgnoreEmpty { get; init; }
    public bool AssertEnabled { get; init; } = true;
    public bool AssertOnlyResponse { get; init; }
    public IReadOnlyList<string> Environments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class RequestDataRow
{
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public IReadOnlyList<string> Dontrun { get; init; } = [];
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class AssertionDefinition
{
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string ResponsePath { get; init; } = string.Empty;
    public string AssertionVerb { get; init; } = "eq";
    public string HeaderAssertionVerb { get; init; } = "eq";
    public string ExpectedValue { get; init; } = string.Empty;
    public string? ExtractVariable { get; init; }
    public int ExcelRowNumber { get; init; }
    public int ExpectedValueColumn { get; init; }
    public int ResponseColumn { get; init; }
}

public sealed class WorkbookModel
{
    public IReadOnlyList<TestcaseDefinition> Testcases { get; init; } = [];
    public IReadOnlyDictionary<(int Index, string DataId), RequestDataRow> RequestData { get; init; } = new Dictionary<(int, string), RequestDataRow>();
    public IReadOnlyDictionary<(int Index, string DataId, string StepName), IReadOnlyList<AssertionDefinition>> Assertions { get; init; } = new Dictionary<(int, string, string), IReadOnlyList<AssertionDefinition>>();
}

public sealed class RuntimeSelection
{
    public string Input { get; init; } = "0";
    public IReadOnlyList<int> Indexes { get; init; } = [];
    public string Source { get; init; } = "appsettings.json";
}

public sealed class RunResult
{
    public string RunId { get; init; } = string.Empty;
    public RuntimeSelection Selection { get; init; } = new();
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; set; }
    public string Environment { get; init; } = string.Empty;
    public List<TestcaseResult> Testcases { get; } = [];
}

public sealed class TestcaseResult
{
    public string RunId { get; init; } = string.Empty;
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public int Thread { get; init; }
    public int Iteration { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; set; }
    public string Environment { get; init; } = string.Empty;
    public List<RequestResult> Requests { get; } = [];
    public string Result => Requests.Any(r => r.Result == "FAIL") ? "FAILED" : Requests.Count == 0 || Requests.All(r => r.Result == "SKIPPED") ? "SKIPPED" : "PASSED";
}

public sealed class RequestResult
{
    public string RunId { get; init; } = string.Empty;
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string StepType { get; init; } = string.Empty;
    public int Thread { get; init; }
    public int Iteration { get; init; }
    public int SequenceOrder { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; set; }
    public int WaitMs { get; init; }
    public string Verb { get; init; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string TemplateSource { get; init; } = string.Empty;
    public string StepConfig { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string RequestHeaders { get; set; } = string.Empty;
    public string RequestBody { get; set; } = string.Empty;
    public int HttpStatus { get; set; }
    public string ResponseHeaders { get; set; } = string.Empty;
    public string ResponseBody { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<CorrelationRecord> Correlations { get; } = [];
    public List<AssertionResult> Assertions { get; } = [];
    public string ErrorMessage { get; set; } = string.Empty;
    public string FailureCategory { get; set; } = string.Empty;
    public string RuntimeReference { get; set; } = string.Empty;
    public string RuntimeReferenceKey { get; set; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string Result => ErrorMessage.Length > 0 || Assertions.Any(a => a.Result == "FAIL") || (HttpStatus is < 200 or >= 300) ? "FAIL" : "PASS";
}

public sealed class CorrelationRecord
{
    public string DataId { get; init; } = string.Empty;
    public string SourceStepName { get; init; } = string.Empty;
    public string ResponsePath { get; init; } = string.Empty;
    public string Variable { get; init; } = string.Empty;
    public string ExtractedValue { get; init; } = string.Empty;
    public string UsedByStepName { get; set; } = string.Empty;
}

public sealed class AssertionResult
{
    public string RunId { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string ResponsePath { get; init; } = string.Empty;
    public string AssertionVerb { get; init; } = "eq";
    public string ExpectedValue { get; init; } = string.Empty;
    public string ActualValue { get; init; } = string.Empty;
    public string Result { get; init; } = "FAIL";
    public string FailureMessage { get; init; } = string.Empty;
    public string? ExtractVariable { get; init; }
    public int ExcelRowNumber { get; init; }
    public int ExpectedValueColumn { get; init; }
    public string Environment { get; init; } = string.Empty;
}
