using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using LoadTestingTool.Domain;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.Execution;

internal sealed class RuntimeReferenceException : Exception
{
    public RuntimeReferenceException(string key, string reference)
        : base($"Missing runtime response value '{key}' for From_ reference '{reference}'.")
    {
        Key = key;
        Reference = reference;
    }

    public string Key { get; }
    public string Reference { get; }
}

public sealed class TestRunner
{
    private readonly HttpClient _client; private readonly AssertionEngine _assertions; private readonly string _templatesFolder; private readonly int _timeoutSeconds; private readonly Action<RequestResult>? _requestCompleted; private readonly InternalLogger _logger; private readonly FeatureConfig _features; private readonly ExecutionPipeline _pipeline; private int _sequence;
    public TestRunner(HttpClient client, AssertionEngine assertions, string templatesFolder, int timeoutSeconds, Action<RequestResult>? requestCompleted = null, InternalLogger? logger = null, FeatureConfig? features = null)
    {
        _client = client; _assertions = assertions; _templatesFolder = templatesFolder; _timeoutSeconds = Math.Max(1, timeoutSeconds); _requestCompleted = requestCompleted; _logger = logger ?? new InternalLogger(string.Empty, "disabled", "disabled", false); _features = features ?? new FeatureConfig { WorkspaceRoot = Directory.GetCurrentDirectory() };
        _pipeline = new ExecutionPipeline(new StepExecutorRegistry([new GraphQlStepExecutor(_client), new GraphQlStepExecutor(_client, flattenCaseQl: true), new FileStepExecutor(), new SqlStepExecutor(_features), new CSharpScriptStepExecutor(_features)]));
    }

    public async Task RunAsync(TestcaseDefinition testcase, WorkbookModel workbook, RunResult run, CancellationToken cancellationToken, string environment = "", string executionMode = "threaded", IReadOnlySet<string>? selectedDataIds = null)
    {
        var dataRows = workbook.RequestData.Values.Where(r => r.TestcaseIndex == testcase.TestcaseIndex && (selectedDataIds is null || selectedDataIds.Contains(r.DataId))).OrderBy(r => r.DataId, StringComparer.OrdinalIgnoreCase).ToList();
        _logger.Section($"TESTCASE {testcase.TestcaseIndex}: {testcase.Testcase} - START");
        _logger.Info($"Testcase summary | dataRows={dataRows.Count} | steps={testcase.Steps.Count} | environment={environment}");
        var sequential = executionMode.Equals("loop", StringComparison.OrdinalIgnoreCase) || executionMode.Equals("sequential", StringComparison.OrdinalIgnoreCase);
        var threads = sequential ? 1 : testcase.Steps.Max(s => s.Threads);
        _logger.Info($"Execution mode: {(sequential ? "sequential loop" : "threaded")}; workers={threads}");
        var tasks = Enumerable.Range(1, threads).Select(async threadId =>
        {
            if (threadId > 1 && testcase.Steps.Max(s => s.ThreadIntervalMs) > 0) await Task.Delay((threadId - 1) * testcase.Steps.Max(s => s.ThreadIntervalMs), cancellationToken);
            foreach (var data in dataRows)
            for (var iteration = 1; iteration <= testcase.Steps.Max(s => s.Iterations); iteration++)
            {
                _logger.Section($"DATA ROW {data.DataId} | thread={threadId} | iteration={iteration}");
                var testcaseResult = new TestcaseResult { RunId = run.RunId, TestcaseIndex = testcase.TestcaseIndex, Testcase = testcase.Testcase, DataId = data.DataId, Thread = threadId, Iteration = iteration, Environment = environment, StartedAt = DateTimeOffset.Now };
                var correlation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var correlationSources = new Dictionary<string, CorrelationRecord>(StringComparer.OrdinalIgnoreCase); var requestReferences = data.Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase); var stepOrder = 0;
                var executionContext = new LoadTestingTool.Domain.ExecutionContext();
                foreach (var request in testcase.Steps)
                {
                    _logger.Info($"---- STEP {stepOrder + 1}: {request.StepName} | type={request.StepType.WireName()} | dataid={data.DataId} | iteration={iteration} ----");
                    if (data.Dontrun.Contains(request.StepName, StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.Info($"STEP SKIPPED | reason=request.dontrun | step={request.StepName}");
                        continue;
                    }
                    if (!request.Enabled) { _logger.Info($"STEP SKIPPED | reason=disabled | step={request.StepName}"); continue; }
                    if (request.Iterations < iteration) { _logger.Info($"STEP SKIPPED | reason=iteration-limit | configured={request.Iterations} | current={iteration}"); continue; }
                    if (request.WaitMs > 0) { _logger.Info($"Waiting {request.WaitMs} ms before dataid={data.DataId}, step {request.StepName}."); await Task.Delay(request.WaitMs, cancellationToken); }
                    Dictionary<string, string> variables;
                    try
                    {
                        variables = StepVariables(data, request.StepName);
                        if (!string.IsNullOrWhiteSpace(environment)) variables["env"] = environment;
                        foreach (var pair in correlation) variables[pair.Key] = pair.Value;
                        foreach (var key in variables.Keys.ToList()) variables[key] = ResolveRuntimeReferences(variables[key], correlation, requestReferences);
                    }
                    catch (RuntimeReferenceException ex)
                    {
                        var failure = CreateRuntimeReferenceFailure(run, testcase, data.DataId, request, ++stepOrder, threadId, iteration, environment, ex);
                        testcaseResult.Requests.Add(failure);
                        _requestCompleted?.Invoke(failure);
                        _logger.Warn($"Stopping current data row after missing runtime reference. testcase={testcase.TestcaseIndex}; dataid={data.DataId}; stepName={request.StepName}; iteration={iteration}; reference={ex.Reference}");
                        break;
                    }
                    foreach (var variable in variables) executionContext.Set(variable.Key, variable.Value, VariableScope.Iteration);
                    foreach (var source in correlationSources.Values) if (string.IsNullOrWhiteSpace(source.UsedByStepName) || !source.UsedByStepName.Split(',', StringSplitOptions.TrimEntries).Contains(request.StepName, StringComparer.OrdinalIgnoreCase)) source.UsedByStepName = string.IsNullOrWhiteSpace(source.UsedByStepName) ? request.StepName : $"{source.UsedByStepName},{request.StepName}";
                    var definitions = workbook.Assertions.GetValueOrDefault((request.TestcaseIndex, data.DataId, request.StepName)) ?? [];
                    var result = await ExecuteRequestAsync(run, testcase, data.DataId, request, variables, definitions, requestReferences, ++stepOrder, threadId, iteration, correlation, correlationSources, executionContext, cancellationToken, environment);
                    testcaseResult.Requests.Add(result);
                    if (result.FailureCategory.Equals("RuntimeReference", StringComparison.OrdinalIgnoreCase)) { _logger.Warn($"Stopping current data row after missing runtime reference. dataid={data.DataId}; step={request.StepName}; iteration={iteration}; remaining rows will continue."); break; }
                    if (result.Result == "FAIL" && request.StopOnFailure) { _logger.Warn($"Stopping testcase sequence after failed dataid={data.DataId}, step {request.StepName} because stopOnFailure=true."); break; }
                }
                testcaseResult.EndedAt = DateTimeOffset.Now; lock (run.Testcases) run.Testcases.Add(testcaseResult);
                if (iteration < testcase.Steps.Max(s => s.Iterations) && testcase.Steps.Max(s => s.IterationIntervalMs) > 0) await Task.Delay(testcase.Steps.Max(s => s.IterationIntervalMs), cancellationToken);
            }
        });
        await Task.WhenAll(tasks); _logger.Section($"TESTCASE {testcase.TestcaseIndex}: {testcase.Testcase} - END");
    }

    private static Dictionary<string, string> StepVariables(RequestDataRow data, string stepName)
    {
        var prefix = stepName + ".";
        var variables = new Dictionary<string, string>(data.Variables.Where(x => !x.Key.Contains('.', StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var variable in data.Variables.Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            variables[variable.Key[prefix.Length..]] = variable.Value;
        return variables;
    }

    private RequestResult CreateRuntimeReferenceFailure(RunResult run, TestcaseDefinition testcase, string dataId, RequestStep step, int sequence, int thread, int iteration, string environment, RuntimeReferenceException exception)
    {
        var now = DateTimeOffset.Now;
        var requestId = $"REF-{Interlocked.Increment(ref _sequence):D8}";
        var message = exception.Message;
        _logger.Error($"RUNTIME REFERENCE FAILURE [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; thread={thread}; iteration={iteration}; reference={exception.Reference}; key={exception.Key}", exception);
        return new RequestResult
        {
            RunId = run.RunId,
            RequestId = requestId,
            TestcaseIndex = step.TestcaseIndex,
            Testcase = testcase.Testcase,
            DataId = dataId,
            StepName = step.StepName,
            StepType = step.StepType.WireName(),
            Thread = thread,
            Iteration = iteration,
            SequenceOrder = sequence,
            StartedAt = now,
            EndedAt = now,
            Verb = step.StepType.WireName(),
            TemplateSource = step.TemplateSource,
            StepConfig = step.StepConfig,
            HttpStatus = 0,
            ErrorMessage = message,
            FailureCategory = "RuntimeReference",
            RuntimeReference = exception.Reference,
            RuntimeReferenceKey = exception.Key,
            Environment = environment
        };
    }

    private async Task<RequestResult> ExecuteRequestAsync(RunResult run, TestcaseDefinition testcase, string dataId, RequestStep step, Dictionary<string, string> variables, IReadOnlyList<AssertionDefinition> definitions, IReadOnlyDictionary<string, string> requestReferences, int sequence, int thread, int iteration, Dictionary<string, string> correlation, Dictionary<string, CorrelationRecord> correlationSources, LoadTestingTool.Domain.ExecutionContext executionContext, CancellationToken cancellationToken, string environment)
    {
        if (step.StepType != StepType.Http) return await ExecuteNonHttpAsync(run, testcase, dataId, step, variables, definitions, requestReferences, sequence, thread, iteration, correlation, correlationSources, executionContext, cancellationToken, environment);
        var started = DateTimeOffset.Now; var sw = Stopwatch.StartNew(); var requestId = $"REQ-{Interlocked.Increment(ref _sequence):D8}"; var templatePath = Path.Combine(_templatesFolder, step.TemplateSource);
        _logger.Info($"Request starting. RequestId={requestId}; testcase={testcase.TestcaseIndex}; dataid={dataId}; stepName={step.StepName}; thread={thread}; iteration={iteration}; verb={step.Verb}; url={step.TargetUrl}; template={templatePath}; ignoreempty={step.IgnoreEmpty}");
        var result = new RequestResult { RunId = run.RunId, RequestId = requestId, TestcaseIndex = step.TestcaseIndex, Testcase = testcase.Testcase, DataId = dataId, StepName = step.StepName, StepType = step.StepType.WireName(), Thread = thread, Iteration = iteration, SequenceOrder = sequence, StartedAt = started, WaitMs = step.WaitMs, Verb = step.Verb, TargetUrl = step.TargetUrl, TemplateSource = step.TemplateSource, StepConfig = step.StepConfig, ContentType = ResolveContentType(step), RequestHeaders = string.Join("; ", step.Headers.Select(h => $"{h.Key}={h.Value}")), Variables = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase), Environment = environment };
        try
        {
            result.TargetUrl = Substitute(step.TargetUrl.Replace("{env}", environment, StringComparison.OrdinalIgnoreCase), variables); var body = TemplateRenderer.Render(ResolveRuntimeReferences(File.ReadAllText(templatePath), correlation, requestReferences), result.ContentType, variables); result.RequestBody = body;
            using var request = new HttpRequestMessage(new HttpMethod(step.Verb), result.TargetUrl); if (!step.Verb.Equals("HEAD", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(body)) request.Content = new StringContent(body, Encoding.UTF8, result.ContentType.Equals("xml", StringComparison.OrdinalIgnoreCase) ? "application/xml" : "application/json");
            var resolvedHeaders = step.Headers.ToDictionary(h => h.Key, h => Substitute(h.Value, variables), StringComparer.OrdinalIgnoreCase); result.RequestHeaders = string.Join("; ", resolvedHeaders.Select(h => $"{h.Key}={h.Value}")); foreach (var h in resolvedHeaders) if (!request.Headers.TryAddWithoutValidation(h.Key, h.Value)) request.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value);
            _logger.Info($"REQUEST SENT [{requestId}]\nURL: {result.TargetUrl}\nMETHOD: {step.Verb}\nHEADERS:\n{result.RequestHeaders}\nBODY:\n{body}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds)); using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            result.HttpStatus = (int)response.StatusCode; result.ResponseHeaders = string.Join("; ", response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}={string.Join(",", h.Value)}")); result.ResponseBody = await response.Content.ReadAsStringAsync(timeout.Token);
            PublishResponseValues(step.StepName, result.ResponseBody, result.ContentType, correlation);
            _logger.Info($"RESPONSE RECEIVED [{requestId}]\nSTATUS: {result.HttpStatus}\nHEADERS:\n{result.ResponseHeaders}\nBODY:\n{result.ResponseBody}");
            if (step.AssertEnabled)
            {
                var resolvedDefinitions = ResolveAssertionReferences(definitions, correlation, requestReferences);
                result.Assertions.AddRange(_assertions.Evaluate(result.ResponseBody, result.ContentType, resolvedDefinitions, run.RunId, requestId, result.HttpStatus, step.IgnoreEmpty, step.AssertOnlyResponse));
            }
            var failedAssertions = result.Assertions.Where(a => a.Result == "FAIL").ToList();
            _logger.Info($"ASSERTIONS COMPLETED [{requestId}]\nPASSED: {result.Assertions.Count(a => a.Result == "PASS")}\nFAILED: {failedAssertions.Count}");
            foreach (var assertion in failedAssertions)
                _logger.Warn($"ASSERTION FAILED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; responsePath={assertion.ResponsePath}; operator={assertion.AssertionVerb}; expected={assertion.ExpectedValue}; actual={assertion.ActualValue}; message={assertion.FailureMessage}");
            foreach (var assertion in result.Assertions.Where(a => a.Result == "PASS" && !string.IsNullOrWhiteSpace(a.ExtractVariable))) { var record = new CorrelationRecord { DataId = dataId, SourceStepName = step.StepName, ResponsePath = assertion.ResponsePath, Variable = assertion.ExtractVariable!, ExtractedValue = assertion.ActualValue }; result.Correlations.Add(record); correlation[record.Variable] = record.ExtractedValue; correlationSources[record.Variable] = record; }
            var hasExpectedStatus = !string.IsNullOrWhiteSpace(step.ExpectedStatus);
            var expectedStatusMatches = hasExpectedStatus && StatusMatches(result.HttpStatus, step.ExpectedStatus);
            if ((hasExpectedStatus && !expectedStatusMatches) || (!hasExpectedStatus && !response.IsSuccessStatusCode))
            {
                result.ErrorMessage = hasExpectedStatus
                    ? $"Expected status '{step.ExpectedStatus}' but received '{result.HttpStatus}'."
                    : $"Unexpected HTTP status {(int)response.StatusCode} {response.StatusCode}.";
                _logger.Error($"HTTP STATUS FAILURE [{requestId}] | testcase={testcase.TestcaseIndex} | dataid={dataId} | step={step.StepName} | actual={result.HttpStatus} | expected={(hasExpectedStatus ? step.ExpectedStatus : "2xx/3xx")}");
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = FormatException("HTTP request preparation/execution failed", ex);
            if (ex is RuntimeReferenceException referenceException)
            {
                result.FailureCategory = "RuntimeReference";
                result.RuntimeReference = referenceException.Reference;
                result.RuntimeReferenceKey = referenceException.Key;
            }
            result.HttpStatus = 0;
            _logger.Error($"REQUEST FAILED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; template={templatePath}; templateExists={File.Exists(templatePath)}; url={result.TargetUrl}; verb={step.Verb}; contentType={result.ContentType}; requestBody={result.RequestBody}; requestHeaders={result.RequestHeaders}", ex);
        }
        finally
        {
            sw.Stop(); result.DurationMs = sw.ElapsedMilliseconds; result.EndedAt = DateTimeOffset.Now;
            _logger.Info($"REQUEST COMPLETED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; result={result.Result}; status={result.HttpStatus}; durationMs={result.DurationMs}; error={result.ErrorMessage}");
        }
        _requestCompleted?.Invoke(result); return result;
    }

    private async Task<RequestResult> ExecuteNonHttpAsync(RunResult run, TestcaseDefinition testcase, string dataId, RequestStep step, Dictionary<string, string> variables, IReadOnlyList<AssertionDefinition> definitions, IReadOnlyDictionary<string, string> requestReferences, int sequence, int thread, int iteration, Dictionary<string, string> correlation, Dictionary<string, CorrelationRecord> correlationSources, LoadTestingTool.Domain.ExecutionContext context, CancellationToken cancellationToken, string environment)
    {
        var started = DateTimeOffset.Now; var requestId = $"STEP-{Interlocked.Increment(ref _sequence):D8}";
        var templatePath = string.IsNullOrWhiteSpace(step.TemplateSource) ? string.Empty : Path.Combine(_templatesFolder, step.TemplateSource);
        var result = new RequestResult { RunId = run.RunId, RequestId = requestId, TestcaseIndex = step.TestcaseIndex, Testcase = testcase.Testcase, DataId = dataId, StepName = step.StepName, StepType = step.StepType.WireName(), Thread = thread, Iteration = iteration, SequenceOrder = sequence, StartedAt = started, WaitMs = step.WaitMs, Verb = step.StepType.WireName(), TargetUrl = step.TargetUrl, TemplateSource = step.TemplateSource, StepConfig = step.StepConfig, ContentType = step.ContentType, Variables = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase), Environment = environment };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(step.TimeoutSeconds > 0 ? step.TimeoutSeconds : _timeoutSeconds));
            var templateText = string.Empty;
            result.TargetUrl = context.ResolveUrl(step.TargetUrl);
            _logger.Info($"STEP TEMPLATE LOAD [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; environment={environment}; template={templatePath}; templateExists={(!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))}; configuredTargetUrl={step.TargetUrl}; resolvedTargetUrl={result.TargetUrl}; ignoreempty={step.IgnoreEmpty}; stepConfig={step.StepConfig}");
            if (!string.IsNullOrWhiteSpace(step.TemplateSource))
            {
                templateText = context.Resolve(ResolveRuntimeReferences(await File.ReadAllTextAsync(templatePath, timeout.Token), correlation, requestReferences));
            }
            _logger.Info($"STEP REQUEST [{requestId}]\nTYPE: {step.StepType.WireName()}\nSTEP: {step.StepName}\nENVIRONMENT: {environment}\nCONFIGURED URL: {step.TargetUrl}\nRESOLVED URL: {result.TargetUrl}\nTEMPLATE: {step.TemplateSource}\nREQUEST:\n{templateText}\nMETADATA:\n{step.StepConfig}");
            var execution = await _pipeline.ExecuteAsync(new StepExecutionRequest { Step = step, Context = context, WorkspaceRoot = _features.WorkspaceRoot, TemplateText = templateText, CancellationToken = timeout.Token });
            StepExecutionResultMapper.PublishOutputs(execution, context);
            result.RequestBody = execution.RequestText; result.ResponseBody = execution.ResponseText; result.HttpStatus = execution.StatusCode == 0 && execution.Succeeded ? 200 : execution.StatusCode; result.ErrorMessage = execution.ErrorMessage;
            PublishResponseValues(step.StepName, result.ResponseBody, "json", correlation);
            _logger.Info($"STEP RESPONSE [{requestId}]\nTYPE: {step.StepType.WireName()}\nSTATUS: {result.HttpStatus}\nREQUEST PAYLOAD:\n{execution.RequestText}\nRESPONSE PAYLOAD:\n{execution.ResponseText}\nERROR: {execution.ErrorMessage}");
            if (!execution.Succeeded || !string.IsNullOrWhiteSpace(execution.ErrorMessage))
                _logger.Error($"STEP EXECUTION FAILURE [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; template={templatePath}; templateExists={(!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))}; targetUrl={step.TargetUrl}; status={result.HttpStatus}; error={execution.ErrorMessage}");
            foreach (var output in execution.Outputs) { var text = output.Value is string s ? s : JsonSerializer.Serialize(output.Value); var record = new CorrelationRecord { DataId = dataId, SourceStepName = step.StepName, ResponsePath = output.Key, Variable = output.Key, ExtractedValue = text }; correlation[output.Key] = text; correlationSources[output.Key] = record; result.Correlations.Add(record); }
            if (step.AssertEnabled)
            {
                var resolvedDefinitions = ResolveAssertionReferences(definitions, correlation, requestReferences);
                result.Assertions.AddRange(_assertions.Evaluate(result.ResponseBody, "json", resolvedDefinitions, run.RunId, requestId, result.HttpStatus, step.IgnoreEmpty, step.AssertOnlyResponse));
            }
            foreach (var assertion in result.Assertions.Where(a => a.Result == "FAIL"))
                _logger.Warn($"ASSERTION FAILED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; responsePath={assertion.ResponsePath}; operator={assertion.AssertionVerb}; expected={assertion.ExpectedValue}; actual={assertion.ActualValue}; message={assertion.FailureMessage}");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = FormatException("Step preparation/execution failed", ex);
            if (ex is RuntimeReferenceException referenceException)
            {
                result.FailureCategory = "RuntimeReference";
                result.RuntimeReference = referenceException.Reference;
                result.RuntimeReferenceKey = referenceException.Key;
            }
            _logger.Error($"STEP FAILED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; template={templatePath}; templateExists={(!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))}; targetUrl={step.TargetUrl}; verb={step.Verb}; contentType={step.ContentType}; stepConfig={step.StepConfig}; requestPayload={result.RequestBody}; responsePayload={result.ResponseBody}; status={result.HttpStatus}", ex);
        }
        finally
        {
            result.EndedAt = DateTimeOffset.Now; result.DurationMs = (long)(result.EndedAt - result.StartedAt).TotalMilliseconds;
            _logger.Info($"STEP COMPLETED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; result={result.Result}; status={result.HttpStatus}; durationMs={result.DurationMs}; error={result.ErrorMessage}");
        }
        _requestCompleted?.Invoke(result); return result;
    }
    private static string FormatException(string context, Exception exception) => $"{context}: {exception.GetType().Name}: {exception.Message}";
    private static string ResolveRuntimeReferences(string value, IReadOnlyDictionary<string, string> correlation, IReadOnlyDictionary<string, string>? requestReferences = null)
    {
        return Regex.Replace(value ?? string.Empty, @"<From_(?:(request|response)_)?([^_>]+)_(r|\d+)>", match =>
        {
            var source = match.Groups[1].Value;
            var key = match.Groups[2].Value;
            var values = source.Equals("request", StringComparison.OrdinalIgnoreCase) ? requestReferences : correlation;
            return values is not null && values.TryGetValue(key, out var actual)
                ? actual
                : throw new RuntimeReferenceException(key, match.Value);
        }, RegexOptions.IgnoreCase);
    }
    private static IReadOnlyList<AssertionDefinition> ResolveAssertionReferences(IReadOnlyList<AssertionDefinition> definitions, IReadOnlyDictionary<string, string> correlation, IReadOnlyDictionary<string, string>? requestReferences = null)
    {
        return definitions.Select(definition => new AssertionDefinition
        {
            TestcaseIndex = definition.TestcaseIndex,
            Testcase = definition.Testcase,
            DataId = definition.DataId,
            StepName = definition.StepName,
            ResponsePath = definition.ResponsePath,
            AssertionVerb = definition.AssertionVerb,
            HeaderAssertionVerb = definition.HeaderAssertionVerb,
            ExpectedValue = ResolveRuntimeReferences(definition.ExpectedValue, correlation, requestReferences),
            ExtractVariable = definition.ExtractVariable,
            ExcelRowNumber = definition.ExcelRowNumber,
            ExpectedValueColumn = definition.ExpectedValueColumn,
            ResponseColumn = definition.ResponseColumn
        }).ToList();
    }
    private static void PublishResponseValues(string stepName, string body, string contentType, Dictionary<string, string> correlation)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        try
        {
            if (contentType.Equals("json", StringComparison.OrdinalIgnoreCase) || body.TrimStart().StartsWith("{"))
            {
                using var document = JsonDocument.Parse(body);
                PublishJsonRuntimeNodes(stepName, document.RootElement, string.Empty, correlation);
            }
            else
            {
                var values = new AssertionEngine().Normalize(body, contentType);
                foreach (var pair in values) correlation[$"{stepName}.{pair.Key}"] = pair.Value;
            }
        }
        catch (Exception) { /* Assertion evaluation records malformed payloads; preserve the original step result. */ }
    }

    private static void PublishJsonRuntimeNodes(string stepName, JsonElement element, string path, Dictionary<string, string> correlation)
    {
        if (!string.IsNullOrEmpty(path))
        {
            var value = element.ValueKind switch
            {
                JsonValueKind.Null => string.Empty,
                JsonValueKind.String => element.GetString() ?? string.Empty,
                _ => element.GetRawText()
            };
            correlation[$"{stepName}.{path}"] = value;
        }
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                PublishJsonRuntimeNodes(stepName, property.Value, string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}", correlation);
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray()) PublishJsonRuntimeNodes(stepName, item, $"{path}[{index++}]", correlation);
        }
    }
    private static string Substitute(string value, IReadOnlyDictionary<string, string> vars) => Regex.Replace(value ?? "", @"_([\p{L}\p{N}._-]+)_", m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : throw new InvalidDataException($"Missing request variable '{m.Groups[1].Value}'."));
    private static string ResolveContentType(RequestStep step) => !string.IsNullOrWhiteSpace(step.ContentType) ? step.ContentType : Path.GetExtension(step.TemplateSource).TrimStart('.').ToLowerInvariant() switch { "xml" => "xml", _ => "json" };
    private static bool StatusMatches(int actual, string expected) => expected.Contains('-') ? int.TryParse(expected.Split('-')[0], out var min) && int.TryParse(expected.Split('-')[1], out var max) && actual >= min && actual <= max : expected.EndsWith("xx", StringComparison.OrdinalIgnoreCase) && int.TryParse(expected[..1], out var family) ? actual / 100 == family : int.TryParse(expected, out var exact) && actual == exact;
}
