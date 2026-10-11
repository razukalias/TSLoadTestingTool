# Instance-row actions and wizard help

The main instance list now exposes the most common actions directly on each instance row: Configure, Add step, Edit workbook, and Open workbook. The Config tab remains useful for run details but no longer duplicates the workbook actions.

The Add step action opens the guided wizard without first opening the workbook editor. The wizard writes a compatible config row and creates a timestamped workbook backup. Edit workbook opens the Excel-style editor, while Open workbook uses the system-default application.

The wizard now includes a Help button with explanations and examples for testcase identity, stage, timeout, environments, HTTP and GraphQL fields, file and binary references, scripts, SQL, and StepConfig JSON.

StepConfig has type-specific guided fields:

| Step type | Guided fields |
|---|---|
| GraphQL | Query, operation name, variables JSON |
| File | Action, path, output variable, output mode |
| Script | Script source |
| SQL | Connection and query |

Each supported type has a Generate StepConfig command. Advanced users can still edit the generated JSON directly.

The workbook editor also has a Help button describing sheets, Excel-style selection, autocomplete, row/column operations, imports, and Ctrl+S backup behavior.

Validation completed with a successful solution build and all 40 existing regression checks.
