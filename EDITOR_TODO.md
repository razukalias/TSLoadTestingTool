# DataEngine Editor TODO

This list records deferred editor work so it can be implemented later without losing the requirements.

## High priority

### 0. Fix cell highlighting

**Current status:** Deferred. The cell highlight action does not currently behave reliably and the highlight is not saved as expected.

Required behavior:

- Highlight one selected cell.
- Highlight multiple selected cells.
- Highlight selected rows and columns.
- Provide a reliable clear-highlight action.
- Preserve the highlight color after saving and reopening the workbook.
- Support choosing or changing the highlight color.
- Add regression coverage for applying, clearing, saving, and reloading highlights.

### 1. Fix Excel-style wrapping

**Current status:** Deferred. The current implementation does not reliably show wrapping for selected rows or columns.

Required behavior:

- Wrap a selected row through the row-number context menu.
- Wrap selected columns through the column-letter context menu.
- Make long text visibly wrap inside the cell.
- Automatically increase row height to display all wrapped lines.
- Preserve wrapping after saving and reopening the workbook.
- Support removing wrapping from selected rows and columns.
- Apply wrapping to multiple selected rows or columns using Ctrl/Shift selection.
- Verify the behavior on `config`, `request`, and `response` sheets.
- Add an automated regression test for save/reopen wrap persistence.

Recommended future implementation approach:

1. Store row and column wrap state separately from individual cell state.
2. Recalculate row heights after every wrap, unwrap, column resize, and text edit operation.
3. Use measured text layout rather than an estimated character count.
4. Reapply row heights and wrap style when loading the workbook.
5. Add a visible status message showing the affected rows/columns.
6. Test with long URLs, JSON, GraphQL, scripts, and multiline response values.

## Medium priority

### 2. Support `<From_...>` references for File-step outputs

**Current status:** Deferred for later testing and implementation. File outputs can currently be injected into HTTP templates with `_*fileData*_` for streamed Base64, but there is no binary-aware `<From_...>` form.

Required behavior:

- Define and document a clear File-step reference syntax, without breaking existing request/response references.
- Support large `BinaryFileReference` values without materializing the complete payload in Excel history or internal logs.
- Preserve the existing `_*variable*_` streaming syntax as a compatible alternative.
- Add an integration test covering `ReadBinary` followed by HTTP JSON injection.

### 3. Improve cell and row height behavior

- Add manual row-height resizing with the mouse.
- Add automatic row-height / best-fit behavior.
- Keep row heights stable when switching sheets or filtering columns.
- Ensure wrapped cells do not show an internal vertical scrollbar.

### 4. Complete Excel-like editing operations

- Verify copy/paste for cells, rows, and columns across filtered and unfiltered views.
- Preserve formatting, wrap state, and column widths when copying rows or columns.
- Add undo/redo coverage for insert, delete, copy, paste, wrap, and resize actions.
- Add a reliable context menu for row and column indexes.

### 5. Autocomplete improvements

- Keep autocomplete working in normal cells and directly edited headers.
- Verify `<From_...>` suggestions are filtered by the active sheet and headers.
- Verify `{assertion}` suggestions are filtered by the active response/header context.
- Add keyboard navigation with Up/Down, Enter, Tab, and Escape.
- Add regression tests for autocomplete before existing values.

## Verification checklist for the future wrapping fix

- [ ] Wrap one selected row.
- [ ] Wrap multiple selected rows.
- [ ] Wrap one selected column.
- [ ] Wrap multiple selected columns.
- [ ] Confirm visible row expansion.
- [ ] Save workbook.
- [ ] Close and reopen editor.
- [ ] Confirm wrapping persists.
- [ ] Unwrap rows and columns.
- [ ] Confirm unwrapped rows return to normal height.
- [ ] Run automated regression tests.
- [ ] Test the packaged Windows build.

## UI and instance-management backlog

The following requirements were captured from the latest UI review and are intentionally recorded before implementation.

### High priority: create and configure instances

**Current status:** Implemented in the current source; manual UI verification remains.

- [x] Fix **Create new instance** so the instance name is editable before creation.
- [x] Use a clear unique default name beginning with `DataEngine_`.
- Keep the instance display name, instance folder name, workbook name, and configuration paths synchronized.
- Allow renaming an existing instance safely, with validation for invalid or duplicate folder names.
- Prevent the create-instance flow from unexpectedly opening the configure-instance form.
- [x] Add **Select all instances** and **Clear all instances** actions.
- [x] Add suitable per-instance **Run**, **Stop**, and, where applicable, **Force stop** actions.
- Preserve the legacy configuration route only where it is still needed internally; remove the obsolete legacy configuration panel from the right-side dashboard UI.

### High priority: test-step editor and wizard

**Current status:** Partially implemented; manual field-by-field UI verification remains.

- [x] In **Edit / Add new test step**, allow double-clicking a list item to open the corresponding editor.
- Show only fields relevant to the selected step type; switching types must not collapse or corrupt the window.
- Review every supported step type and verify its complete field set, including at least:
  - HTTP
  - GraphQL
  - Script
  - SQL
  - File
  - Wait
  - assertion-only-response behavior
- [x] Add dropdowns for HTTP verbs, content types, File actions/modes, and SQL command type.
- Add validation, required-field indicators, and useful defaults for each step type.
- [x] Keep template editing step-aware and select the format from the step type/file extension.
- [x] Add wizard Help guidance with field descriptions, valid values, examples, variable syntax, and common errors.
- [x] Add guidance for Script, GraphQL, SQL, File, Wait, and assertion-related settings.
- Validate template variables such as `_variable_`, references, and step-specific configuration before saving.

### High priority: run behavior and instance actions

**Current status:** Partially implemented; manual run/stop and artifact-opening verification remains.

- Diagnose and fix why clicking **Run instance** opens the instance configuration form instead of starting the selected run or opening the intended run-selection flow.
- Keep run selection separate from instance configuration.
- [x] Make Run, Stop, and Force stop state-aware and disable actions when they are not applicable.
- [x] Show the active run status on the instance item and retain the active run ID in the selected detail/artifact view.
- [x] Add instance-item actions to open:
  - The internal log for the selected run.
  - History results for the selected run.
  - Filtered testcase/environment results for that run ID.
- Use a compact action area or overflow menu so instance rows remain readable.

### Medium priority: dashboard detail panel

**Current status:** Implemented in the current source; manual interaction and copy verification remains.

- [x] Make the right-side detail panel resizable by dragging its divider with the mouse.
- [x] Add horizontal scrolling where detail content is wider than the panel.
- [x] Add Copy overview and Copy assertions actions for long detail content.
- [x] Add a text filter for Overview items with `&` support for multiple terms.
- [x] Add a text filter for Assertion items with `&` support for multiple terms.
- Define multi-term behavior consistently: `term1 & term2` matches items containing any supplied term unless an explicit all-terms mode is later added.
- Keep long values wrapped/readable without collapsing the dashboard.
- [x] Remove the obsolete **Legacy config** section from the right-side panel.

### Verification checklist

- [ ] Create an instance and confirm the editable default name starts with `DataEngine_`.
- [ ] Rename an instance and confirm folder, workbook, and UI references remain consistent.
- [ ] Select all instances and clear all selections.
- [ ] Double-click each step type and confirm the correct wizard opens.
- [ ] Verify fields and dropdowns for HTTP, GraphQL, Script, SQL, File, Wait, and assertion-only-response configurations.
- [ ] Confirm irrelevant template editors and fields are hidden.
- [ ] Open each step-type Help view and verify examples.
- [ ] Confirm Run instance starts the intended run instead of opening configuration unexpectedly.
- [ ] Run, stop, and force-stop from an instance item where applicable.
- [ ] Open internal log and history filtered by run ID, testcase, and environment.
- [ ] Resize and horizontally scroll the right-side detail panel.
- [ ] Copy long Overview and Assertion values.
- [ ] Verify `&` filtering for Overview and Assertions.
- [ ] Confirm Legacy config is absent from the right-side panel.
- [ ] Build and test the packaged Windows version.
