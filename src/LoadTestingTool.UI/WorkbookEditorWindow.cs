using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
        var sheetHeaders = workbook.Worksheets.ToDictionary(x => x.Name, x => x.FirstRowUsed()?.CellsUsed().Select(c => c.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? [], StringComparer.OrdinalIgnoreCase);
        var tabs = new TabControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        foreach (var name in new[] { "config", "request", "response", "README" })
        {
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (sheet is null) continue;
            var draft = new SheetDraft(sheet, name, sheetHeaders, SetStatus);
            _sheets[name] = draft; tabs.Items.Add(new TabItem { Header = name, Content = draft.View });
        }
        _save = DashboardStyle.Action("Save workbook", Save, "primary");
        var title = DashboardStyle.Text("DataEngine Editor", 21, true);
        var subtitle = DashboardStyle.Text("Excel-like editor. Headers are editable, columns can be inserted/deleted/resized, and suggestions appear below the active cell. Use Ctrl+Space for autocomplete.", 12, false, DashboardStyle.Muted);
        _status.Foreground = DashboardStyle.Red;
        var top = new StackPanel { Spacing = 4, Children = { title, subtitle } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { DashboardStyle.Action("Open raw workbook", () => SystemDefaultFileOpener.Open(_path)), DashboardStyle.Action("Cancel", () => Close(null)), _save } };
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
        private readonly TextBlock _position = DashboardStyle.Text("Select a cell", 11, false, DashboardStyle.Muted);
        private readonly Popup _suggestionPopup = new() { PlacementMode = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = false };
        private readonly ListBox _suggestions = new() { MaxHeight = 220, MinWidth = 260 };
        private int _selectedRow = -1, _selectedColumn = -1;
        private bool _updatingSuggestions;
        private TextBox? _activeBox;
        private IReadOnlyList<int> _visibleColumns = [];
        public Control View { get; }

        public SheetDraft(IXLWorksheet sheet, string name, IReadOnlyDictionary<string, string[]> headers, Action<string, bool> status)
        {
            _sheetName = name; _allHeaders = new Dictionary<string, string[]>(headers, StringComparer.OrdinalIgnoreCase); _status = status;
            var used = sheet.RangeUsed(); var lastRow = used?.RangeAddress.LastAddress.RowNumber ?? 1; var lastColumn = used?.RangeAddress.LastAddress.ColumnNumber ?? 1;
            for (var r = 1; r <= lastRow; r++) _values.Add(Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(r, c).GetString()).ToList());
            if (_values.Count == 0) _values.Add(Enumerable.Repeat("", lastColumn).ToList());
            _widths.AddRange(Enumerable.Repeat(190d, lastColumn));
            _columnFilter.TextChanged += (_, _) => Render();
            _suggestions.SelectionChanged += (_, _) => { if (!_updatingSuggestions && _suggestionPopup.IsOpen && _suggestions.SelectedItem is string value) AcceptSuggestion(value); };
            _suggestionPopup.Child = _suggestions;
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DashboardStyle.Action("Add row", AddRow), DashboardStyle.Action("Delete row", DeleteRow), DashboardStyle.Action("Insert column before", () => InsertColumn(false)), DashboardStyle.Action("Insert column after", () => InsertColumn(true)), DashboardStyle.Action("Delete column", DeleteColumn), DashboardStyle.Action("Widen column", () => ResizeColumn(30)), DashboardStyle.Action("Narrow column", () => ResizeColumn(-30)), _columnFilter, _position } };
            var gridScroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { toolbar, gridScroll } }; Grid.SetRow(gridScroll, 1);
            var editorHost = new Grid { Children = { table, _suggestionPopup } };
            View = new Border { BorderBrush = DashboardStyle.Line, BorderThickness = new Thickness(1), Child = editorHost };
            Render();
        }

        private string[] Headers => _values.Count == 0 ? [] : _values[0].ToArray();
        private void Render()
        {
            _rows.Children.Clear(); var filter = _columnFilter.Text?.Trim() ?? "";
            _visibleColumns = Enumerable.Range(0, Headers.Length).Where(i => string.IsNullOrWhiteSpace(filter) || Headers[i].Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (_visibleColumns.Count == 0) { _rows.Children.Add(DashboardStyle.Text("No columns match the filter. Clear the filter to restore the grid.", color: DashboardStyle.Muted)); return; }
            AddRowView(0, true); for (var r = 1; r < _values.Count; r++) AddRowView(r, false);
        }

        private void AddRowView(int row, bool header)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("52," + string.Join(',', _visibleColumns.Select(i => $"{Math.Clamp(_widths[i], 100, 700)}"))), Background = header ? DashboardStyle.Brush("#173559") : Brushes.Transparent };
            var number = DashboardStyle.Text(header ? "#" : row.ToString(), 11, header); Grid.SetColumn(number, 0); grid.Children.Add(number);
            for (var n = 0; n < _visibleColumns.Count; n++)
            {
                var column = _visibleColumns[n]; var box = new TextBox { Text = _values[row][column], FontSize = 11, MinWidth = 80, Margin = new Thickness(1), AcceptsReturn = !header, TextWrapping = TextWrapping.NoWrap };
                box.CaretIndex = box.Text?.Length ?? 0; box.TextChanged += (_, _) => { _values[row][column] = box.Text ?? ""; if (!header && ReferenceEquals(box, _activeBox)) UpdateSuggestions(box); };
                box.GotFocus += (_, _) => SelectCell(row, column, box);
                box.PointerPressed += (_, _) => SelectCell(row, column, box);
                box.KeyDown += (_, e) => { if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) != 0) { UpdateSuggestions(box, true); e.Handled = true; } else if (e.Key == Key.Escape) CloseSuggestions(); else if (e.Key is Key.Enter or Key.Tab && _suggestionPopup.IsOpen && _suggestions.SelectedItem is string) { AcceptSuggestion(_suggestions.SelectedItem.ToString()!); e.Handled = true; } };
                if (header) { box.Foreground = DashboardStyle.Brush("#E3EDF9"); ToolTip.SetTip(box, "Header is editable. Use insert/delete column buttons to change the schema."); }
                Grid.SetColumn(box, n + 1); grid.Children.Add(box);
            }
            _rows.Children.Add(grid);
        }

        private void SelectCell(int row, int column, TextBox box) { _selectedRow = row; _selectedColumn = column; _activeBox = box; _position.Text = $"Sheet: {_sheetName} | Row: {row + 1} | Column: {column + 1} | {Headers.ElementAtOrDefault(column)}"; if (!box.IsReadOnly) UpdateSuggestions(box); }
        private void UpdateSuggestions(TextBox box, bool force = false)
        {
            if (_selectedRow < 1) { CloseSuggestions(); return; }
            var value = box.Text ?? ""; var token = value[(value.LastIndexOfAny(['<', '{']) + 1)..]; var suggestions = new List<string>();
            if (value.Contains('{') && value.LastIndexOf('{') > value.LastIndexOf('}')) suggestions.AddRange(AssertionVerbs.Select(x => "{" + x + "}"));
            else if (value.Contains("<From_", StringComparison.OrdinalIgnoreCase))
            {
                var from = value[(value.LastIndexOf("<From_", StringComparison.OrdinalIgnoreCase) + 6)..]; var parts = from.Split('_'); var sheet = parts.Length > 0 ? parts[0] : ""; var prefix = parts.Length > 1 ? string.Join('_', parts.Skip(1)) : "";
                if (parts.Length <= 1) suggestions.AddRange(_allHeaders.Keys.Select(x => $"<From_{x}_"));
                else if (_allHeaders.TryGetValue(sheet, out var headers)) suggestions.AddRange(headers.Where(x => x.Contains(prefix, StringComparison.OrdinalIgnoreCase)).Select(x => $"<From_{sheet}_{x}_r>"));
            }
            else if (value.Contains('<') || force) suggestions.AddRange(Functions);
            suggestions = suggestions.Where(x => force || x.Contains(token, StringComparison.OrdinalIgnoreCase)).Distinct().Take(80).ToList();
            if (suggestions.Count == 0) { CloseSuggestions(); return; }
            _updatingSuggestions = true; _suggestions.ItemsSource = suggestions; _suggestions.SelectedIndex = 0; _updatingSuggestions = false;
            _suggestionPopup.PlacementTarget = box; _suggestionPopup.IsOpen = true;
        }
        private void AcceptSuggestion(string suggestion)
        {
            if (_activeBox is null) return; var value = _activeBox.Text ?? ""; var start = Math.Max(value.LastIndexOf('<'), value.LastIndexOf('{')); _activeBox.Text = start >= 0 ? value[..start] + suggestion : value + suggestion; _activeBox.CaretIndex = _activeBox.Text.Length; _activeBox.Focus(); CloseSuggestions();
        }
        private void CloseSuggestions() { _suggestionPopup.IsOpen = false; }
        private void AddRow() { _values.Add(Enumerable.Repeat("", Headers.Length).ToList()); Render(); }
        private void DeleteRow() { if (_selectedRow > 0 && _selectedRow < _values.Count) { _values.RemoveAt(_selectedRow); _selectedRow = -1; Render(); } }
        private void InsertColumn(bool after) { var index = _selectedColumn < 0 ? Headers.Length : Math.Clamp(_selectedColumn + (after ? 1 : 0), 0, Headers.Length); foreach (var row in _values) row.Insert(index, ""); _widths.Insert(index, 190); Render(); }
        private void DeleteColumn() { if (_selectedColumn < 0 || Headers.Length <= 1) return; foreach (var row in _values) row.RemoveAt(_selectedColumn); _widths.RemoveAt(_selectedColumn); _selectedColumn = -1; Render(); }
        private void ResizeColumn(double delta) { if (_selectedColumn < 0 || _selectedColumn >= _widths.Count) return; _widths[_selectedColumn] = Math.Clamp(_widths[_selectedColumn] + delta, 100, 700); Render(); }
        public void Validate() { if (_values.Count == 0 || _values[0].All(string.IsNullOrWhiteSpace)) throw new InvalidDataException($"Sheet '{_sheetName}' must retain a header row."); if (_values[0].Distinct(StringComparer.OrdinalIgnoreCase).Count() != _values[0].Count) throw new InvalidDataException($"Sheet '{_sheetName}' contains duplicate headers."); }
        public void WriteTo(IXLWorksheet sheet) { sheet.RangeUsed()?.Clear(XLClearOptions.All); for (var r = 0; r < _values.Count; r++) for (var c = 0; c < _values[r].Count; c++) sheet.Cell(r + 1, c + 1).Value = _values[r][c]; sheet.Row(1).Style.Font.Bold = true; sheet.SheetView.FreezeRows(1); }
    }
}
