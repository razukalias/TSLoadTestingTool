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

### 2. Improve cell and row height behavior

- Add manual row-height resizing with the mouse.
- Add automatic row-height / best-fit behavior.
- Keep row heights stable when switching sheets or filtering columns.
- Ensure wrapped cells do not show an internal vertical scrollbar.

### 3. Complete Excel-like editing operations

- Verify copy/paste for cells, rows, and columns across filtered and unfiltered views.
- Preserve formatting, wrap state, and column widths when copying rows or columns.
- Add undo/redo coverage for insert, delete, copy, paste, wrap, and resize actions.
- Add a reliable context menu for row and column indexes.

### 4. Autocomplete improvements

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
