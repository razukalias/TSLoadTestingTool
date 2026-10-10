using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClosedXML.Excel;

namespace LoadTestingTool.UI;

public sealed class WorkbookEditorWindow : Window
{
    private readonly string _path;
    private readonly Dictionary<string, SheetDraft> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _save;
    public string? SavedBackup { get; private set; }

    public WorkbookEditorWindow(string workbookPath)
    {
        _path = workbookPath;
        Title = $"DataEngine Editor — {Path.GetFileName(workbookPath)}";
        Width = 1550; Height = 920; MinWidth = 1100; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        using var workbook = new XLWorkbook(workbookPath);
        var sheetHeaders = workbook.Worksheets.ToDictionary(x => x.Name,
            x => x.FirstRowUsed()?.CellsUsed().Select(c => c.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? [], StringComparer.OrdinalIgnoreCase);
        var tabs = new TabControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        foreach (var name in new[] { "config", "request", "response", "README" })
        {
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (sheet is null) continue;
            var draft = new SheetDraft(sheet, name, sheetHeaders, SetStatus);
            _sheets[name] = draft; tabs.Items.Add(new TabItem { Header = name, Content = draft.View });
        }
        _save = DashboardStyle.Action("Save workbook", Save, "primary");
        var top = new StackPanel { Spacing = 4, Children = {
            DashboardStyle.Text("DataEngine Editor", 21, true),
            DashboardStyle.Text("Select cells, rows, or columns with the mouse. Right-click for operations. Drag column borders to resize. Type < or { for autocomplete; Ctrl+Space opens it. For a header, right-click and select Edit header.", 12, false, DashboardStyle.Muted) } };
        _status.Foreground = DashboardStyle.Red;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Children = { DashboardStyle.Action("Open raw workbook", () => SystemDefaultFileOpener.Open(_path)), DashboardStyle.Action("Cancel", () => Close(null)), _save } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(18) };
        root.Children.Add(top); Grid.SetRow(tabs, 1); root.Children.Add(tabs); Grid.SetRow(_status, 2); root.Children.Add(_status); Grid.SetRow(buttons, 3); root.Children.Add(buttons); Content = root;
        tabs.SelectedIndex = 0;
    }

    private void SetStatus(string message, bool error = false) { _status.Text = message; _status.Foreground = error ? DashboardStyle.Red : DashboardStyle.Muted; }

    private void Save()
    {
        try
        {
            foreach (var draft in _sheets.Values) draft.Validate();
            using var workbook = new XLWorkbook(_path);
            foreach (var (name, draft) in _sheets) draft.WriteTo(workbook.Worksheet(name));
            SavedBackup = $"{_path}.backup.{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            File.Copy(_path, SavedBackup, overwrite: false); workbook.Save(); SetStatus($"Saved workbook. Backup: {Path.GetFileName(SavedBackup)}"); Close();
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    private sealed class SheetDraft
    {
        private static readonly string[] AssertionVerbs = { "eq", "ne", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "gt", "gte", "lt", "lte", "in", "size" };
        private static readonly string[] Functions = { "<From_", "<datum>", "<currentdatum>", "<currenttime>", "<currentdatetime>", "<currenttimestamp>", "<randomnumber>", "<randomnumber:1-100>", "<guid>", "<guid:N>" };
        private readonly string _sheetName;
        private readonly Dictionary<string, string[]> _allHeaders;
        private readonly Action<string, bool> _status;
        private readonly List<List<string>> _values = [];
        private readonly List<double> _widths = [];
        private readonly StackPanel _rows = new() { Spacing = 1 };
        private readonly TextBox _columnFilter = new() { Watermark = "Filter columns by header…", Width = 280 };
        private readonly TextBlock _position = DashboardStyle.Text("Select a cell, row, or column", 11, false, DashboardStyle.Muted);
        private readonly ComboBox _zoom = new() { Width = 90, ItemsSource = new[] { "80%", "90%", "100%", "110%", "125%", "150%" }, SelectedIndex = 2 };
        private readonly ComboBox _selectionMode = new() { Width = 136, ItemsSource = new[] { "Cell", "Add cells", "Cell range", "Row", "Add rows", "Row range", "Column", "Add columns", "Column range" }, SelectedIndex = 0 };
        private readonly Popup _suggestionPopup = new() { PlacementMode = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = false };
        private readonly ListBox _suggestions = new() { MaxHeight = 220, MinWidth = 280 };
        private readonly HashSet<int> _selectedRows = [];
        private readonly HashSet<int> _selectedColumns = [];
        private readonly HashSet<(int Row, int Column)> _selectedCells = [];
        private readonly Dictionary<(int Row, int Column), bool> _wrap = [];
        private readonly Dictionary<(int Row, int Column), string?> _cellColors = [];
        private readonly Dictionary<(int Row, int Column), TextBox> _cellBoxes = [];
        private readonly Dictionary<int, TextBlock> _rowHeaders = [];
        private int[] _resizeColumns = [];
        private bool _resizing;
        private double _resizeLastX;
        private int _selectedRow = -1, _selectedColumn = -1, _anchorRow = -1, _anchorColumn = -1;
        private bool _updatingSuggestions, _selectingSuggestion;
        private TextBox? _activeBox;
        private IReadOnlyList<int> _visibleColumns = [];
        private List<List<string>>? _copiedRows;
        private List<List<string>>? _copiedColumns;
        private List<List<string>>? _copiedCells;
        private List<List<List<string>>> _undo = [];
        private List<List<List<string>>> _redo = [];
        private double _fontSize = 11;
        public Control View { get; }

        public SheetDraft(IXLWorksheet sheet, string name, IReadOnlyDictionary<string, string[]> headers, Action<string, bool> status)
        {
            _sheetName = name; _allHeaders = new Dictionary<string, string[]>(headers, StringComparer.OrdinalIgnoreCase); _status = status;
            var used = sheet.RangeUsed(); var lastRow = used?.RangeAddress.LastAddress.RowNumber ?? 1; var lastColumn = used?.RangeAddress.LastAddress.ColumnNumber ?? 1;
            for (var r = 1; r <= lastRow; r++) _values.Add(Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(r, c).GetString()).ToList());
            if (_values.Count == 0) _values.Add(Enumerable.Repeat("", Math.Max(1, lastColumn)).ToList());
            for (var c = 0; c < _values[0].Count; c++)
            {
                var existingWidth = sheet.Column(c + 1).Width;
                _widths.Add(existingWidth > 0 ? Math.Clamp(existingWidth * 7.5, 100, 700) : 190d);
            }
            _columnFilter.TextChanged += (_, _) => Render();
            _zoom.SelectionChanged += (_, _) => { _fontSize = double.TryParse((_zoom.SelectedItem?.ToString() ?? "100%").TrimEnd('%'), out var z) ? 11 * z / 100 : 11; Render(); };
            _suggestions.SelectionChanged += (_, _) => { if (!_updatingSuggestions && _suggestionPopup.IsOpen && _suggestions.SelectedItem is string value) AcceptSuggestion(value); };
            _suggestions.PointerReleased += (_, _) => { if (_suggestions.SelectedItem is string value) AcceptSuggestion(value); };
            _suggestionPopup.Child = _suggestions;
            var help = DashboardStyle.Text("Mouse: drag column borders to resize; Shift/Ctrl selects ranges. Right-click cells, row numbers, or headers for actions. Ctrl+C/V copies internal ranges. Wrap, format, and auto-fit are available in the context menu.", 11, false, DashboardStyle.Muted);
            var zoomOut = DashboardStyle.Action("A−", () => ChangeFont(-1)); zoomOut.Padding = new Thickness(7, 5); var zoomIn = DashboardStyle.Action("A+", () => ChangeFont(1)); zoomIn.Padding = new Thickness(7, 5); var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _columnFilter, DashboardStyle.Text("Select", 11, false, DashboardStyle.Muted), _selectionMode, DashboardStyle.Text("Zoom", 11, false, DashboardStyle.Muted), _zoom, zoomOut, zoomIn, _position } };
            var gridScroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var table = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Children = { toolbar, help, gridScroll } }; Grid.SetRow(help, 1); Grid.SetRow(gridScroll, 2);
            var editorHost = new Grid { Children = { table, _suggestionPopup } };
            View = new Border { BorderBrush = DashboardStyle.Line, BorderThickness = new Thickness(1), Child = editorHost };
            Render();
        }

        private string[] Headers => _values.Count == 0 ? [] : _values[0].ToArray();
        private string ScaleWidth(double width) => Math.Clamp(width, 100, 700).ToString(System.Globalization.CultureInfo.InvariantCulture);
        private void Render()
        {
            _rows.Children.Clear(); _cellBoxes.Clear(); _rowHeaders.Clear(); var filter = _columnFilter.Text?.Trim() ?? "";
            _visibleColumns = Enumerable.Range(0, Headers.Length).Where(i => string.IsNullOrWhiteSpace(filter) || Headers[i].Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (_visibleColumns.Count == 0) { _rows.Children.Add(DashboardStyle.Text("No columns match the filter. Clear the filter to restore the grid.", color: DashboardStyle.Muted)); return; }
            AddRowView(0, true); for (var r = 1; r < _values.Count; r++) AddRowView(r, false);
        }

        private void AddRowView(int row, bool header)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("52," + string.Join(',', _visibleColumns.Select(i => ScaleWidth(_widths[i])))), Background = header ? DashboardStyle.Brush("#173559") : Brushes.Transparent };
            var number = DashboardStyle.Text(header ? "#" : row.ToString(), 11, header);
            number.Background = (_selectedRows.Contains(row) || (header && _selectedRows.Count == _values.Count - 1)) ? DashboardStyle.Brush("#275B8F") : Brushes.Transparent;
            number.PointerPressed += (_, e) => { if (!header) SelectRow(row, e); else SelectAllRows(); }; number.ContextMenu = RowMenu(row, header); _rowHeaders[row] = number; Grid.SetColumn(number, 0); grid.Children.Add(number);
            for (var n = 0; n < _visibleColumns.Count; n++)
            {
                var column = _visibleColumns[n];
                var cell = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10") };
                var box = new TextBox { Text = _values[row][column], FontSize = _fontSize, MinWidth = 80, Margin = new Thickness(1), AcceptsReturn = !header, IsReadOnly = header, Focusable = !header, TextWrapping = TextWrapping.NoWrap };
                ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Hidden); ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Hidden);
                if (_wrap.TryGetValue((row, column), out var wrapped) && wrapped) box.TextWrapping = TextWrapping.Wrap;
                box.Background = _selectedCells.Contains((row, column)) || _selectedColumns.Contains(column) || _selectedRows.Contains(row) ? DashboardStyle.Brush("#173559") : (_cellColors.TryGetValue((row, column), out var cellColor) && cellColor is not null ? DashboardStyle.Brush(cellColor) : Brushes.Transparent);
                box.CaretIndex = box.Text?.Length ?? 0;
                _cellBoxes[(row, column)] = box;
                box.TextChanged += (_, _) => { _values[row][column] = box.Text ?? ""; if (ReferenceEquals(box, _activeBox)) UpdateSuggestions(box); };
                box.GotFocus += (_, _) => { _activeBox = box; };
                if (header)
                {
                    box.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
                    {
                        if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                        _activeBox = box;
                        if (e.ClickCount >= 2)
                        {
                            box.Focusable = true; box.IsReadOnly = false; box.Focus();
                            Dispatcher.UIThread.Post(() => UpdateSuggestions(box, true), DispatcherPriority.Background);
                        }
                        else
                        {
                            _selectedColumn = column;
                            _selectedCells.Clear();
                            _selectedRows.Clear();
                            _selectedColumns.Clear();
                            UpdateSelectionText();
                        }
                        e.Handled = true;
                    }, RoutingStrategies.Tunnel);
                }
                else box.AddHandler(InputElement.PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) { _activeBox = box; SelectCell(row, column, box, e); } }, RoutingStrategies.Tunnel);
                box.AddHandler(InputElement.KeyDownEvent, (_, e) => HandleKey(box, e), RoutingStrategies.Tunnel);
                box.ContextMenu = header ? ColumnMenu(column) : CellMenu(row, column);
                if (header) { box.Foreground = DashboardStyle.Brush("#E3EDF9"); ToolTip.SetTip(box, "Click to select the column. Right-click and choose Edit header to edit it or use column operations."); }
                Grid.SetColumn(box, 0); cell.Children.Add(box);
                var grip = new Border { Width = 10, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Cursor = new Cursor(StandardCursorType.SizeWestEast), Background = DashboardStyle.Brush("#45698E"), Opacity = 0.75, IsHitTestVisible = true };
                grip.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
                    BeginResize(column); _resizing = true; _resizeLastX = e.GetPosition(null).X; e.Pointer.Capture(grip); e.Handled = true;
                };
                grip.PointerMoved += (_, e) =>
                {
                    if (!_resizing) return; var x = e.GetPosition(null).X; AccumulateResize(x - _resizeLastX); _resizeLastX = x; e.Handled = true;
                };
                grip.PointerReleased += (_, e) =>
                {
                    if (!_resizing) return; _resizing = false; e.Pointer.Capture(null); EndResize(); e.Handled = true;
                };
                Grid.SetColumn(grip, 1); cell.Children.Add(grip);
                Grid.SetColumn(cell, n + 1); grid.Children.Add(cell);
            }
            _rows.Children.Add(grid);
        }

        private string SelectionMode => _selectionMode.SelectedItem?.ToString() ?? "Cell";
        private (bool Add, bool Range) SelectionIntent(PointerEventArgs? e, string scope)
        {
            var add = e is not null && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
            var range = e is not null && (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
            var configured = SelectionMode;
            if (configured.Equals($"Add {scope}", StringComparison.OrdinalIgnoreCase)) add = true;
            if (configured.Equals($"{scope[..^1]} range", StringComparison.OrdinalIgnoreCase)) range = true;
            return (add, range);
        }
        private void SelectCell(int row, int column, TextBox box, PointerEventArgs? args)
        {
            if (SelectionMode.Contains("row", StringComparison.OrdinalIgnoreCase)) { SelectRow(row, args); return; }
            if (SelectionMode.Contains("column", StringComparison.OrdinalIgnoreCase)) { SelectColumn(column, args); return; }
            var (add, range) = SelectionIntent(args, "cells");
            _selectedRow = row; _selectedColumn = column; _activeBox = box; _selectedRows.Clear(); _selectedColumns.Clear();
            if (range && _anchorRow >= 0)
            {
                _selectedCells.Clear(); for (var r = Math.Min(_anchorRow, row); r <= Math.Max(_anchorRow, row); r++) for (var c = Math.Min(_anchorColumn, column); c <= Math.Max(_anchorColumn, column); c++) _selectedCells.Add((r, c));
            }
            else if (!add) { _selectedCells.Clear(); _selectedCells.Add((row, column)); _anchorRow = row; _anchorColumn = column; }
            else _selectedCells.Add((row, column));
            UpdateSelectionText(); RefreshSelectionVisuals(); if (!box.IsReadOnly) UpdateSuggestions(box);
        }
        private void SelectRow(int row, PointerEventArgs? e)
        {
            var (add, range) = SelectionIntent(e, "rows"); _selectedCells.Clear(); _selectedColumns.Clear();
            if (!add && !range) _selectedRows.Clear();
            if (range && _anchorRow >= 1) for (var r = Math.Min(_anchorRow, row); r <= Math.Max(_anchorRow, row); r++) _selectedRows.Add(r); else _selectedRows.Add(row);
            _selectedRow = row; if (!range) _anchorRow = row; UpdateSelectionText(); Render();
        }
        private void SelectRowDirect(int row, bool add) { _selectedCells.Clear(); _selectedColumns.Clear(); if (!add) _selectedRows.Clear(); _selectedRows.Add(row); _selectedRow = row; _anchorRow = row; UpdateSelectionText(); Render(); }
        private void SelectAllRows() { _selectedRows.Clear(); for (var r = 1; r < _values.Count; r++) _selectedRows.Add(r); UpdateSelectionText(); Render(); }
        private void SelectColumn(int column, PointerEventArgs? e)
        {
            var (add, range) = SelectionIntent(e, "columns"); _selectedCells.Clear(); _selectedRows.Clear();
            if (!add && !range) _selectedColumns.Clear();
            if (range && _anchorColumn >= 0) for (var c = Math.Min(_anchorColumn, column); c <= Math.Max(_anchorColumn, column); c++) _selectedColumns.Add(c); else _selectedColumns.Add(column);
            _selectedColumn = column; if (!range) _anchorColumn = column; UpdateSelectionText(); Render();
        }
        private void SelectColumnDirect(int column, bool add) { _selectedCells.Clear(); _selectedRows.Clear(); if (!add) _selectedColumns.Clear(); _selectedColumns.Add(column); _selectedColumn = column; _anchorColumn = column; UpdateSelectionText(); Render(); }
        private void UpdateSelectionText() => _position.Text = $"Sheet: {_sheetName} | {_selectedCells.Count} cells | {_selectedRows.Count} rows | {_selectedColumns.Count} columns";

        private void HandleKey(TextBox box, KeyEventArgs e)
        {
            if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { UpdateSuggestions(box, true); e.Handled = true; return; }
            if (e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { SelectAllCells(); e.Handled = true; return; }
            if (e.Key == Key.Escape) { CloseSuggestions(); return; }
            if ((e.Key == Key.Enter || e.Key == Key.Tab) && _suggestionPopup.IsOpen && _suggestions.SelectedItem is string value) { AcceptSuggestion(value); e.Handled = true; return; }
            if (e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { Undo(); e.Handled = true; }
            else if (e.Key == Key.Y && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { Redo(); e.Handled = true; }
        }

        private void UpdateSuggestions(TextBox box, bool force = false)
        {
            var value = box.Text ?? ""; var caret = Math.Clamp(box.CaretIndex, 0, value.Length); var before = value[..caret];
            var lt = before.LastIndexOf('<'); var brace = before.LastIndexOf('{'); var tokenStart = Math.Max(lt, brace);
            var token = tokenStart >= 0 ? before[tokenStart..] : ""; var suggestions = new List<string>();
            if (brace > lt && !before[(brace + 1)..].Contains('}')) suggestions.AddRange(AssertionVerbs.Select(x => "{" + x + "}"));
            else if (lt >= 0 && !before[(lt + 1)..].Contains('>'))
            {
                var from = before[(lt + 1)..];
                if (from.StartsWith("From_", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = from[5..].Split('_'); var sheet = parts[0]; var prefix = parts.Length > 1 ? string.Join('_', parts.Skip(1)) : "";
                    if (parts.Length <= 1) suggestions.AddRange(_allHeaders.Keys.Select(x => $"<From_{x}_"));
                    else if (_allHeaders.TryGetValue(sheet, out var headers)) suggestions.AddRange(headers.Where(x => x.Contains(prefix, StringComparison.OrdinalIgnoreCase)).Select(x => $"<From_{sheet}_{x}_r>"));
                }
                else suggestions.AddRange(Functions);
            }
            else if (force) suggestions.AddRange(Functions.Concat(AssertionVerbs.Select(x => "{" + x + "}")));
            suggestions = suggestions.Where(x => force || x.Contains(token, StringComparison.OrdinalIgnoreCase)).Distinct().Take(80).ToList();
            if (suggestions.Count == 0) { CloseSuggestions(); return; }
            _updatingSuggestions = true; _suggestions.ItemsSource = suggestions; _suggestions.SelectedIndex = 0; _updatingSuggestions = false;
            _suggestionPopup.PlacementTarget = box; _suggestionPopup.IsOpen = true;
        }
        private void AcceptSuggestion(string suggestion)
        {
            if (_activeBox is null || _selectingSuggestion) return; _selectingSuggestion = true;
            var value = _activeBox.Text ?? ""; var caret = Math.Clamp(_activeBox.CaretIndex, 0, value.Length); var before = value[..caret]; var lt = before.LastIndexOf('<'); var brace = before.LastIndexOf('{'); var start = Math.Max(lt, brace); if (start < 0) start = caret;
            _activeBox.Text = value[..start] + suggestion + value[caret..]; _activeBox.CaretIndex = start + suggestion.Length; _activeBox.Focus(); CloseSuggestions(); _selectingSuggestion = false;
        }
        private void CloseSuggestions() { _suggestionPopup.IsOpen = false; }

        private ContextMenu CellMenu(int row, int column) => MakeMenu(
            Menu("Select this row", () => SelectRowDirect(row, false)), Menu("Add this row", () => SelectRowDirect(row, true)),
            Menu("Select this column", () => SelectColumnDirect(column, false)), Menu("Add this column", () => SelectColumnDirect(column, true)),
            Menu("Copy selection", CopySelection), Menu("Paste", PasteSelection), Menu("Undo", Undo), Menu("Redo", Redo),
            Menu("Insert row above", () => InsertRows(row, false)), Menu("Insert row below", () => InsertRows(row, true)), Menu("Delete selected rows", DeleteRows),
            Menu("Insert column before", () => InsertColumns(column, false)), Menu("Insert column after", () => InsertColumns(column, true)), Menu("Delete selected columns", DeleteColumns),
            Menu("Wrap selected cells", () => SetWrap(true)), Menu("No wrap", () => SetWrap(false)), Menu("Auto-fit selected columns", AutoFitSelectedColumns),
            Menu("Highlight selected cells", () => SetCellColor("#284D70")), Menu("Clear highlight", () => SetCellColor(null)), Menu("Increase font", () => ChangeFont(1)), Menu("Decrease font", () => ChangeFont(-1)), Menu("Delete cell contents", () => ClearSelection()));
        private ContextMenu RowMenu(int row, bool header) => header ? MakeMenu(Menu("Select all rows", SelectAllRows), Menu("Select all columns", SelectAllColumns), Menu("Select all cells", SelectAllCells)) : MakeMenu(Menu("Select row", () => SelectRowDirect(row, false)), Menu("Add row", () => SelectRowDirect(row, true)), Menu("Insert row above", () => InsertRows(row, false)), Menu("Insert row below", () => InsertRows(row, true)), Menu("Copy rows", CopyRows), Menu("Duplicate rows", PasteRows), Menu("Delete selected rows", DeleteRows), Menu("Wrap selected rows", () => SetWrap(true)));
        private ContextMenu ColumnMenu(int column) => MakeMenu(Menu("Edit header", () => BeginHeaderEdit(column)), Menu("Select column", () => SelectColumnDirect(column, false)), Menu("Add column", () => InsertColumns(column, true)), Menu("Insert column before", () => InsertColumns(column, false)), Menu("Insert column after", () => InsertColumns(column, true)), Menu("Copy columns", CopyColumns), Menu("Paste columns", PasteColumns), Menu("Delete selected columns", DeleteColumns), Menu("Auto-fit selected columns", AutoFitSelectedColumns), Menu("Set selected width (280)", () => SetSelectedColumnsWidth(280)), Menu("Reset selected width", () => SetSelectedColumnsWidth(190)), Menu("Wrap selected columns", () => SetWrapForColumns(true)), Menu("No wrap selected columns", () => SetWrapForColumns(false)), Menu("Select all columns", SelectAllColumns), Menu("Select all cells", SelectAllCells));
        private void BeginHeaderEdit(int column)
        {
            if (!_cellBoxes.TryGetValue((0, column), out var box)) return;
            _activeBox = box; box.Focusable = true; box.IsReadOnly = false; box.Focus(); box.CaretIndex = box.Text?.Length ?? 0;
            Dispatcher.UIThread.Post(() => { if ((box.Text ?? "").Contains('<') || (box.Text ?? "").Contains('{')) UpdateSuggestions(box, true); }, DispatcherPriority.Background);
        }
        private static ContextMenu MakeMenu(params MenuItem[] items) { var menu = new ContextMenu(); foreach (var item in items) menu.Items.Add(item); return menu; }
        private static MenuItem Menu(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); return item; }

        private void SelectAllColumns() { _selectedColumns.Clear(); for (var c = 0; c < Headers.Length; c++) _selectedColumns.Add(c); UpdateSelectionText(); Render(); }
        private void SelectAllCells() { _selectedCells.Clear(); for (var r = 1; r < _values.Count; r++) for (var c = 0; c < Headers.Length; c++) _selectedCells.Add((r, c)); SelectAllRows(); SelectAllColumns(); }
        private IEnumerable<int> TargetColumns() => _selectedColumns.Count > 0 ? _selectedColumns : (_selectedColumn >= 0 ? new[] { _selectedColumn } : Enumerable.Range(0, Headers.Length));
        private IEnumerable<int> TargetRows() => _selectedRows.Count > 0 ? _selectedRows : (_selectedRow > 0 ? new[] { _selectedRow } : Enumerable.Range(1, _values.Count - 1));
        private void SaveUndo() { _undo.Add(_values.Select(x => x.ToList()).ToList()); if (_undo.Count > 30) _undo.RemoveAt(0); _redo.Clear(); }
        private void Undo() { if (_undo.Count == 0) return; _redo.Add(_values.Select(x => x.ToList()).ToList()); _values.Clear(); _values.AddRange(_undo[^1].Select(x => x.ToList())); _undo.RemoveAt(_undo.Count - 1); Render(); }
        private void Redo() { if (_redo.Count == 0) return; _undo.Add(_values.Select(x => x.ToList()).ToList()); _values.Clear(); _values.AddRange(_redo[^1].Select(x => x.ToList())); _redo.RemoveAt(_redo.Count - 1); Render(); }
        private void InsertRows(int row, bool below) { SaveUndo(); var at = Math.Clamp(row + (below ? 1 : 0), 1, _values.Count); _values.Insert(at, Enumerable.Repeat("", Headers.Length).ToList()); Render(); }
        private void DeleteRows() { SaveUndo(); var rows = TargetRows().Where(r => r > 0 && r < _values.Count).OrderByDescending(x => x).ToArray(); foreach (var r in rows) _values.RemoveAt(r); _selectedRows.Clear(); Render(); }
        private void CopyRows() { _copiedRows = TargetRows().Select(r => _values[r].ToList()).ToList(); _status("Copied rows internally. Select a row and choose Duplicate rows or press Ctrl+V.", false); }
        private void PasteRows() { SaveUndo(); if (_copiedRows is null) return; var at = _selectedRow > 0 ? _selectedRow : _values.Count; foreach (var row in _copiedRows.Select(x => x.ToList())) _values.Insert(Math.Min(at, _values.Count), row); Render(); }
        private void InsertColumns(int column, bool after) { SaveUndo(); var cols = TargetColumns().OrderBy(x => x).ToArray(); var at = Math.Clamp(column + (after ? 1 : 0), 0, Headers.Length); foreach (var row in _values) row.Insert(at, ""); _widths.Insert(at, 190); Render(); }
        private void DeleteColumns() { SaveUndo(); var cols = TargetColumns().Where(c => c >= 0 && c < Headers.Length).OrderByDescending(x => x).ToArray(); if (cols.Length >= Headers.Length) return; foreach (var row in _values) foreach (var c in cols) row.RemoveAt(c); foreach (var c in cols) _widths.RemoveAt(c); _selectedColumns.Clear(); Render(); }
        private void CopyColumns() { _copiedColumns = TargetColumns().OrderBy(x => x).Select(c => _values.Select(row => row[c]).ToList()).ToList(); _status("Copied columns internally. Choose Paste columns from a column menu.", false); }
        private void PasteColumns() { SaveUndo(); if (_copiedColumns is null) return; var at = _selectedColumn >= 0 ? _selectedColumn : Headers.Length; for (var i = 0; i < _copiedColumns.Count; i++) { var c = Math.Min(at + i, Headers.Length); for (var r = 0; r < _values.Count; r++) _values[r].Insert(c, _copiedColumns[i][r]); _widths.Insert(c, 190); } Render(); }
        private void CopySelection()
        {
            if (_selectedCells.Count > 0)
            {
                var minR = _selectedCells.Min(x => x.Row); var maxR = _selectedCells.Max(x => x.Row); var minC = _selectedCells.Min(x => x.Column); var maxC = _selectedCells.Max(x => x.Column);
                _copiedCells = Enumerable.Range(minR, maxR - minR + 1).Select(r => Enumerable.Range(minC, maxC - minC + 1).Select(c => _values[r][c]).ToList()).ToList();
                _status("Copied selected cells internally. Ctrl+V pastes the range at the active cell.", false);
            }
            else CopyRows();
        }
        private void PasteSelection()
        {
            if (_copiedCells is not null && _selectedRow >= 0 && _selectedColumn >= 0)
            {
                SaveUndo(); var r0 = _selectedRow; var c0 = _selectedColumn;
                for (var r = 0; r < _copiedCells.Count && r0 + r < _values.Count; r++) for (var c = 0; c < _copiedCells[r].Count && c0 + c < Headers.Length; c++) _values[r0 + r][c0 + c] = _copiedCells[r][c]; Render();
            }
            else if (_copiedRows is not null) PasteRows(); else if (_copiedColumns is not null) PasteColumns();
        }
        private void ClearSelection() { foreach (var (r, c) in _selectedCells) if (r > 0 && r < _values.Count) _values[r][c] = ""; Render(); }
        private void SetWrap(bool value) { foreach (var cell in _selectedCells) _wrap[cell] = value; foreach (var r in TargetRows()) foreach (var c in TargetColumns()) _wrap[(r, c)] = value; Render(); }
        private void SetWrapForColumns(bool value)
        {
            IEnumerable<int> columns = _selectedColumns.Count > 0 ? _selectedColumns : (_selectedColumn >= 0 ? new[] { _selectedColumn } : Array.Empty<int>());
            foreach (var r in Enumerable.Range(1, Math.Max(0, _values.Count - 1)))
                foreach (var c in columns)
                    _wrap[(r, c)] = value;
            _status(value ? "Wrapped the selected columns." : "Removed wrapping from the selected columns.", false);
            Render();
        }
        private void SetCellColor(string? color) { foreach (var cell in _selectedCells) _cellColors[cell] = color; foreach (var r in TargetRows()) foreach (var c in TargetColumns()) _cellColors[(r, c)] = color; _status(color is null ? "Highlight cleared for the selected range." : "Highlight applied to the selected range.", false); Render(); }
        private void ChangeFont(double delta) { _fontSize = Math.Clamp(_fontSize + delta, 8, 24); Render(); }
        private void RefreshSelectionVisuals()
        {
            foreach (var ((row, column), box) in _cellBoxes)
                box.Background = _selectedCells.Contains((row, column)) || _selectedColumns.Contains(column) || _selectedRows.Contains(row)
                    ? DashboardStyle.Brush("#173559") : (_cellColors.TryGetValue((row, column), out var color) && color is not null ? DashboardStyle.Brush(color) : Brushes.Transparent);
            foreach (var (row, label) in _rowHeaders)
                label.Background = _selectedRows.Contains(row) ? DashboardStyle.Brush("#275B8F") : Brushes.Transparent;
        }
        private void BeginResize(int column)
        {
            if (!_selectedColumns.Contains(column)) { _selectedCells.Clear(); _selectedRows.Clear(); _selectedColumns.Clear(); _selectedColumns.Add(column); _selectedColumn = column; UpdateSelectionText(); RefreshSelectionVisuals(); }
            _resizeColumns = TargetColumns().Distinct().ToArray();
        }
        private void AccumulateResize(double delta) { foreach (var c in _resizeColumns) _widths[c] = Math.Clamp(_widths[c] + delta, 100, 700); }
        private void EndResize() { if (_resizeColumns.Length > 0) Render(); _resizeColumns = []; }
        private void ResizeSelectedColumns(double delta) { BeginResize(_selectedColumn >= 0 ? _selectedColumn : 0); AccumulateResize(delta); EndResize(); }
        private void AutoFitSelectedColumns() { foreach (var c in TargetColumns()) _widths[c] = Math.Clamp(Math.Max(120, Math.Max(_values.Max(row => row[c].Length * 7.5 + 24), Headers[c].Length * 7.5 + 24)), 100, 700); Render(); }
        private void SetSelectedColumnsWidth(double width) { foreach (var c in TargetColumns()) _widths[c] = Math.Clamp(width, 100, 700); Render(); }

        public void Validate() { if (_values.Count == 0 || _values[0].All(string.IsNullOrWhiteSpace)) throw new InvalidDataException($"Sheet '{_sheetName}' must retain a header row."); if (_values[0].Distinct(StringComparer.OrdinalIgnoreCase).Count() != _values[0].Count) throw new InvalidDataException($"Sheet '{_sheetName}' contains duplicate headers."); }
        public void WriteTo(IXLWorksheet sheet)
        {
            sheet.RangeUsed()?.Clear(XLClearOptions.All);
            for (var r = 0; r < _values.Count; r++) for (var c = 0; c < _values[r].Count; c++) { var cell = sheet.Cell(r + 1, c + 1); cell.Value = _values[r][c]; cell.Style.Alignment.WrapText = _wrap.TryGetValue((r, c), out var wrapped) && wrapped; cell.Style.Font.FontSize = _fontSize; if (_cellColors.TryGetValue((r, c), out var color) && color is not null) cell.Style.Fill.BackgroundColor = XLColor.FromHtml(color); }
            for (var c = 0; c < _widths.Count; c++)
                sheet.Column(c + 1).Width = Math.Clamp(_widths[c] / 7.5, 13.3, 93.3);
            sheet.Row(1).Style.Font.Bold = true; sheet.SheetView.FreezeRows(1);
        }
    }
}
