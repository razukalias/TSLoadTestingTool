using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using LoadTestingTool.Domain;
using LoadTestingTool.Excel;
using LoadTestingTool.Execution;
using LoadTestingTool.Reporting;
using LoadTestingTool.Validation;
using Microsoft.Extensions.Configuration;

namespace LoadTestingTool;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        AppConfig? loadedConfig = null;
        string? loadedRunId = null;
        try
        {
            var config = LoadConfig();
            loadedConfig = config;
            var workbookPath = GetCommandLineValue(args, "--excel", "--workbook") ?? config.ExcelFilePath;
            if (string.IsNullOrWhiteSpace(workbookPath)) throw new InvalidOperationException("No Excel workbook was specified.");
            if (Path.GetFileName(workbookPath).StartsWith("~$", StringComparison.Ordinal)) throw new InvalidOperationException($"The selected workbook is an Excel temporary lock file: '{workbookPath}'. Close Excel or select the real workbook file.");
            config.TemplatesFolder = GetCommandLineValue(args, "--templates") ?? config.TemplatesFolder;
            config.ResultsFolder = GetCommandLineValue(args, "--results") ?? config.ResultsFolder;
            if (bool.TryParse(GetCommandLineValue(args, "--internal-log"), out var internalLogging)) config.InternalLoggingEnabled = internalLogging;
            config.InstanceId = GetCommandLineValue(args, "--instance-id") ?? config.InstanceId;
            config.RunId = GetCommandLineValue(args, "--run-id") ?? config.RunId;
            var uiLaunchId = GetCommandLineValue(args, "--ui-launch-id") ?? "not-provided";
            var uiLaunchAt = GetCommandLineValue(args, "--ui-launch-at") ?? "not-provided";
            config.EnvironmentSelection = GetCommandLineValue(args, "--environments") ?? config.EnvironmentSelection;
            config.ExecutionMode = GetCommandLineValue(args, "--execution-mode") ?? config.ExecutionMode;
            var dataIdSelection = GetCommandLineValue(args, "--dataids");
            if (!config.ExecutionMode.Equals("threaded", StringComparison.OrdinalIgnoreCase) && !config.ExecutionMode.Equals("loop", StringComparison.OrdinalIgnoreCase) && !config.ExecutionMode.Equals("sequential", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("execution-mode must be 'threaded' or 'loop'.");
            var runId = string.IsNullOrWhiteSpace(config.RunId) ? $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..25] : config.RunId;
            loadedRunId = runId;
            using var http = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true, AutomaticDecompression = DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan };
            WorkbookModel workbook;
            try
            {
                workbook = new WorkbookReader().Read(workbookPath);
            }
            catch (Exception ex)
            {
                var validationLog = WriteStartupFailureLog(config, runId, workbookPath, args, ex);
                Console.Error.WriteLine($"[Fatal] {ex.Message}\n[Internal log] {validationLog}");
                return 1;
            }
            var expression = GetCommandLineValue(args, "--testcases") ?? config.TestcaseSelection;
            if (string.IsNullOrWhiteSpace(expression) && config.PromptForTestcaseSelection) { Console.Write("Testcase selection (0=all): "); expression = Console.ReadLine(); }
            var indexes = new TestcaseSelectionParser().Parse(expression, workbook.Testcases.Select(t => t.TestcaseIndex));
            Console.WriteLine($"Selected testcase indexes: {string.Join(", ", indexes)}");
            var selection = new RuntimeSelection { Input = string.IsNullOrWhiteSpace(expression) ? "0" : expression!, Indexes = indexes, Source = GetCommandLineValue(args, "--testcases") is not null ? "command-line" : "appsettings.json" };
            var overall = new RunResult { RunId = runId, Selection = selection, StartedAt = DateTimeOffset.Now };
            var selected = workbook.Testcases.Where(t => indexes.Contains(t.TestcaseIndex)).OrderBy(t => t.TestcaseIndex).ToList();
            var availableEnvironments = selected.SelectMany(t => t.Steps).SelectMany(s => s.Environments).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var environments = string.IsNullOrWhiteSpace(config.EnvironmentSelection) ? (availableEnvironments.Count == 0 ? new[] { "" } : availableEnvironments.ToArray()) : config.EnvironmentSelection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var filters = ParseDataIdSelection(dataIdSelection);
            var work = environments.SelectMany(environment => selected.Select(testcase => (testcase, environment)));
            if (config.RunScenariosInParallel) await Task.WhenAll(work.Select(x => RunTestcaseAsync(x.testcase, x.environment, workbook, overall, http, config, runId, selection, filters, uiLaunchId, uiLaunchAt)));
            else foreach (var item in work) await RunTestcaseAsync(item.testcase, item.environment, workbook, overall, http, config, runId, selection, filters, uiLaunchId, uiLaunchAt);
            overall.EndedAt = DateTimeOffset.Now;
            return overall.Testcases.Any(t => t.Result == "FAILED") ? 2 : 0;
        }
        catch (Exception ex)
        {
            var location = loadedConfig is null || string.IsNullOrWhiteSpace(loadedRunId) ? null : WriteStartupFailureLog(loadedConfig, loadedRunId, "<startup>", args, ex);
            Console.Error.WriteLine(location is null ? $"[Fatal] {ex.Message}" : $"[Fatal] {ex.Message}\n[Internal log] {location}"); return 1;
        }
    }

    private static string WriteStartupFailureLog(AppConfig config, string runId, string workbookPath, string[] args, Exception exception)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var folderName = $"runner_startup_{stamp}_{Guid.NewGuid():N}";
        var folder = Path.Combine(config.ResultsFolder, "failed", folderName, "log");
        using var logger = new InternalLogger(folder, config.InstanceId, runId, true);
        logger.Info($"Runner startup/preflight failure. Workbook={workbookPath}; arguments={string.Join(' ', args)}");
        logger.Error("Runner did not reach testcase execution.", exception);
        return logger.FilePath ?? Path.Combine(folder, "internal.log");
    }

    private static async Task RunTestcaseAsync(TestcaseDefinition testcase, string environment, WorkbookModel workbook, RunResult overall, HttpClient http, AppConfig config, string runId, RuntimeSelection selection, IReadOnlyDictionary<int, IReadOnlySet<string>> filters, string uiLaunchId, string uiLaunchAt)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var folderName = InternalLogger.SafeFolderName(testcase.Testcase) + (string.IsNullOrWhiteSpace(environment) ? "" : "_" + InternalLogger.SafeFolderName(environment)) + "_" + stamp;
        var staging = Path.Combine(config.ResultsFolder, ".running", folderName + "_" + Guid.NewGuid().ToString("N"));
        var logFolder = Path.Combine(staging, "log"); Directory.CreateDirectory(logFolder);
        using var logger = new InternalLogger(logFolder, config.InstanceId, runId, config.InternalLoggingEnabled);
        using var processLogger = new InternalLogger(logFolder, config.InstanceId, runId, config.InternalLoggingEnabled, null, "process.log");
        var eventSuffix = $"_{InternalLogger.SafeFolderName(testcase.Testcase)}_{Guid.NewGuid():N}";
        using var events = new LiveEventWriter(logFolder, config.InstanceId, runId, eventSuffix);
        var local = new RunResult { RunId = runId, Selection = selection, StartedAt = DateTimeOffset.Now, Environment = environment };
        logger.Info($"UI diagnostic correlation. uiLaunchId={uiLaunchId}; uiLaunchAt={uiLaunchAt}; runnerStartedAt={DateTimeOffset.Now:O}; processId={Environment.ProcessId}");
        processLogger.Info($"UI diagnostic correlation. uiLaunchId={uiLaunchId}; uiLaunchAt={uiLaunchAt}; runnerStartedAt={DateTimeOffset.Now:O}; processId={Environment.ProcessId}");
        logger.Info($"Testcase started. Index={testcase.TestcaseIndex}; name={testcase.Testcase}; environment={environment}");
        processLogger.Info($"Testcase process started. Index={testcase.TestcaseIndex}; name={testcase.Testcase}; environment={environment}");
        var runner = new TestRunner(http, new AssertionEngine(), config.TemplatesFolder, config.RequestTimeoutSeconds, events.WriteRequest, logger, new FeatureConfig
        {
            WorkspaceRoot = Path.GetFullPath(config.WorkspaceRoot),
            Connections = config.Connections,
            AllowTrustedScripts = config.AllowTrustedScripts,
            ScriptTimeoutSeconds = config.ScriptTimeoutSeconds
        });
        filters.TryGetValue(testcase.TestcaseIndex, out var selectedDataIds);
        try
        {
            await runner.RunAsync(testcase, workbook, local, CancellationToken.None, environment, config.ExecutionMode, selectedDataIds);
        }
        catch (Exception ex)
        {
            logger.Error($"Unhandled testcase execution exception. testcase={testcase.Testcase}; environment={environment}", ex);
            processLogger.Error("Testcase process failed before normal completion.", ex);
            local.Testcases.Add(new TestcaseResult
            {
                RunId = runId, TestcaseIndex = testcase.TestcaseIndex, Testcase = testcase.Testcase,
                DataId = selectedDataIds?.FirstOrDefault() ?? "", Thread = 0, Iteration = 0,
                StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now, Environment = environment,
                Requests = { new RequestResult
                {
                    RunId = runId, RequestId = $"FATAL-{Guid.NewGuid():N}", TestcaseIndex = testcase.TestcaseIndex,
                    Testcase = testcase.Testcase, DataId = selectedDataIds?.FirstOrDefault() ?? "", StepName = "<runner>",
                    StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now, HttpStatus = 500,
                    ErrorMessage = ex.Message, Environment = environment
                } }
            });
        }
        local.EndedAt = DateTimeOffset.Now;
        var failed = local.Testcases.Any(t => t.Result == "FAILED");
        var destination = Path.Combine(config.ResultsFolder, failed ? "failed" : "pass", folderName); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var historyDataIds = local.Testcases.Select(t => t.DataId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var historyName = DataIdFileName(historyDataIds);
        string history = string.Empty;
        try
        {
            history = new HistoryWriter(config).Write(local, staging, historyName);
        }
        catch (Exception ex)
        {
            logger.Error($"History workbook generation failed. The run folder will still be promoted so the complete log is preserved. target={Path.Combine(destination, historyName)}", ex);
        }
        var finalHistoryPath = Path.Combine(destination, historyName);
        events.WriteRunCompleted(local, finalHistoryPath);
        logger.Info($"Testcase completed. Result={(failed ? "FAILED" : "PASSED")}; history={(string.IsNullOrWhiteSpace(history) ? "NOT_CREATED" : finalHistoryPath)}"); processLogger.Info($"Testcase process completed. Result={(failed ? "FAILED" : "PASSED")}"); logger.Dispose(); processLogger.Dispose(); events.Dispose();
        if (Directory.Exists(destination)) destination += "_" + Guid.NewGuid().ToString("N")[..6];
        Directory.Move(staging, destination);
        lock (overall.Testcases) overall.Testcases.AddRange(local.Testcases);
        Console.WriteLine($"[Done] {testcase.Testcase} [{(string.IsNullOrWhiteSpace(environment) ? "default" : environment)}] -> {destination}");
    }

    private static string DataIdFileName(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return "dataid_unknown.xlsx";
        var tokens = ids.Select(InternalLogger.SafeFolderName).ToList(); var parsed = tokens.Select(ParseId).ToList();
        if (parsed.Any(x => x is null) || parsed.Select(x => x!.Value.Prefix).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) return "dataid_" + string.Join("_", tokens) + ".xlsx";
        var prefix = parsed[0]!.Value.Prefix; var numbers = parsed.Select(x => x!.Value.Number).ToList(); var parts = new List<string>();
        for (var i = 0; i < numbers.Count;)
        {
            var start = i; while (i + 1 < numbers.Count && numbers[i + 1] == numbers[i] + 1) i++;
            var width = parsed[start]!.Value.Width;
            parts.Add(start == i ? $"{prefix}{numbers[start].ToString().PadLeft(width, '0')}" : $"{prefix}{numbers[start].ToString().PadLeft(width, '0')}-{prefix}{numbers[i].ToString().PadLeft(width, '0')}"); i++;
        }
        return "dataid_" + string.Join("_", parts) + ".xlsx";
    }
    private static (string Prefix, int Number, int Width)? ParseId(string value) { var match = Regex.Match(value, "^(.*?)(\\d+)$"); return match.Success && int.TryParse(match.Groups[2].Value, out var n) ? (match.Groups[1].Value, n, match.Groups[2].Value.Length) : null; }
    private static IReadOnlyDictionary<int, IReadOnlySet<string>> ParseDataIdSelection(string? value)
    {
        var result = new Dictionary<int, IReadOnlySet<string>>();
        if (string.IsNullOrWhiteSpace(value) || value == "0") return result;
        foreach (var group in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = group.Split(':', 2); if (parts.Length != 2 || !int.TryParse(parts[0], out var index)) throw new InvalidDataException($"Invalid data ID selection group '{group}'. Expected testcase:dataid1,dataid2.");
            var ids = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (ids.Count > 0) result[index] = ids;
        }
        return result;
    }
    private static AppConfig LoadConfig() { var root = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: true, reloadOnChange: false).Build(); var config = new AppConfig(); root.Bind(config); return config; }
    private static string? GetCommandLineValue(string[] args, params string[] names) { for (var i = 0; i < args.Length - 1; i++) if (names.Any(name => args[i].Equals(name, StringComparison.OrdinalIgnoreCase))) return args[i + 1]; return null; }
}
