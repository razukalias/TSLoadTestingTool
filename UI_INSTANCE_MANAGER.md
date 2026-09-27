# Multi-Instance UI Manager

## Overview

The solution now contains two applications:

| Project | Purpose |
|---|---|
| `LoadTestingTool.csproj` | Console runner that executes one workbook instance. |
| `src/LoadTestingTool.UI/LoadTestingTool.UI.csproj` | Avalonia desktop UI that discovers and runs multiple instances. |

The UI discovers instance folders whose names start with:

```text
DataEngine_
```

Example:

```text
Instances/
├── DataEngine_CustomerRegression/
│   ├── customer-tests.xlsx
│   ├── Templates/
│   ├── History/
│   ├── Logs/
│   └── Results/
└── DataEngine_SmokeTests/
    ├── smoke-tests.xlsx
    ├── Templates/
    ├── History/
    ├── Logs/
    └── Results/
```

The UI scans only the direct child folders of `InstancesRoot`. It displays a folder as a runnable instance when the folder starts with `DataEngine_` and contains at least one `.xlsx` workbook.

## UI configuration

`src/LoadTestingTool.UI/appsettings.json` contains:

```json
{
  "InstancesRoot": "Instances",
  "RunnerDll": "../LoadTestingTool/bin/Debug/net8.0/LoadTestingTool.dll",
  "InstancePrefix": "DataEngine_"
}
```

Change `InstancesRoot` to the directory containing your instance folders. `RunnerDll` points to the console runner that the UI launches for each selected instance.

## Running the UI

From the solution directory:

```bash
dotnet run --project src/LoadTestingTool.UI/LoadTestingTool.UI.csproj
```

The UI provides:

- Refresh instance discovery.
- Select all instances.
- Clear selection.
- Choose testcase selection text such as `0`, `1,3`, or `1-5`.
- Run selected instances.
- View per-instance status and latest request/assertion result.

## Process isolation

Each selected instance launches a separate runner process with its own paths:

```bash
dotnet LoadTestingTool.dll \
  --excel "<instance>/workbook.xlsx" \
  --templates "<instance>/Templates" \
  --history "<instance>/History" \
  --logs "<instance>/Logs" \
  --results "<instance>/Results" \
  --instance-id "DataEngine_CustomerRegression" \
  --testcases "1-5"
```

The runner writes its execution history beneath the instance folder:

```text
<instance>/History/Execution_History_yyyyMMdd_HHmmss.xlsx
```

It also writes live NDJSON events beneath:

```text
<instance>/Results/events_<runId>.ndjson
```

The UI reads these event files while the process is running. Request completion and assertion completion events are shown in the instance row. The final `run-completed` event updates the instance status to `PASSED` or `FAILED`.

## Fresh runs and assertion correction

Every run receives a unique run ID and a dedicated results folder:

```text
Results/Run_<runId>/events_<runId>.ndjson
```

This prevents a new run from reading assertion events from an earlier run. The workbook is loaded by a new runner process each time **Run selected** is pressed.

Selecting an instance displays its assertion results. Failed assertions can be selected individually, or all failed assertions can be selected at once. **Apply selected actual values** creates a timestamped workbook backup and updates only the selected cells in the `response` sheet. The live assertion event carries the exact response-sheet row and expected-value column, so the UI does not need to guess which Excel row to change.

## Runner command-line options

| Option | Description |
|---|---|
| `--excel <path>` | Workbook path. |
| `--workbook <path>` | Alias for `--excel`. |
| `--templates <path>` | Template folder. |
| `--history <path>` | History output folder. |
| `--logs <path>` | Log output folder. |
| `--results <path>` | Live event output folder. |
| `--instance-id <id>` | Instance identifier written to live events. |
| `--run-id <id>` | Optional run identifier. |
| `--testcases <expression>` | Selection such as `0`, `1,3`, or `1-5`. |

## Important behavior

The UI does not modify workbooks automatically. It launches isolated runner processes and displays their results. The original workbook remains unchanged during execution. Workbook correction should be implemented as a separate reviewed workflow so a failed assertion is not silently converted into a new expected value.
