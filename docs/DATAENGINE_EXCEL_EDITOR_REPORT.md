# DataEngine Excel-Style Editor

## Implemented

The DataEngine editor now uses a grid-oriented workflow rather than toolbar-only editing. Cells, rows, and columns can be selected with the mouse; Shift extends a range and Ctrl adds to the current selection. The status line reports the selected cell, row, and column counts.

Column borders use draggable resize grips. Resizing applies to the selected columns, and the column context menu provides auto-fit based on the longest header or value. The editor also supports zoom/font-size changes and horizontal/vertical grid scrolling without creating an internal scrollbar in each cell.

Right-click menus now provide row operations (insert above/below, copy, duplicate, delete, wrap), column operations (insert before/after, copy, paste, delete, auto-fit, wrap, select all), and cell operations (copy, paste, wrap, auto-fit, highlight, clear highlight, font-size change, clear contents). Header cells are editable and use the column menu.

Wrap settings, highlights, widths, and font size are preserved in the workbook on save. The existing timestamped backup and workbook validation behavior remains active.

## Autocomplete

Autocomplete works in data cells and editable headers. It evaluates the text before the caret, so suggestions work before, after, or between existing text. Suggestions are available for `<`, `<From_`, sheet/header references, supported functions, and `{` assertion verbs. Enter, Tab, Ctrl+Space, and mouse selection are supported, including the first suggestion.

## Verification

The full solution builds successfully. The regression suite passes all 40 checks. Warnings remain for the pre-existing `Tmds.DBus.Protocol` advisory and Avalonia's obsolete `Popup.PlacementMode` API; there are no build errors.

## Scope note

Copy/paste is maintained as an editor-internal clipboard so row and column structures remain safe and predictable. The editor keeps the DataEngine workbook values and runner contract intact.
