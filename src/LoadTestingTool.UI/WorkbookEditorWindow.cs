using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClosedXML.Excel;

namespace LoadTestingTool.UI;

public sealed class WorkbookEditorWindow : Window
{
    private readonly string _path;
    private readonly Dictionary<string, SheetDraft> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel _sheetHost = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _save;
    private SheetDraft? _active;
    public string? SavedBackup { get; private set; }

    public WorkbookEditorWindow(string workbookPath)
    {
        _path = workbookPath;
        Title = $"DataEngine Editor — {Path.GetFileName(workbookPath)}";
        Width = 1500; Height = 900; MinWidth = 1100; MinHeight = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        using var workbook = new XLWorkbook(workbookPath);
        var sheetTabs = new TabControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        foreach (var name in new[] { "config", "request", "response", "README" })
        {
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (sheet is null) continue;
            var draft = new SheetDraft(sheet, name, SelectCell); _sheets[name] = draft;
            sheetTabs.Items.Add(new TabItem { Header = name, Content = draft.View });
        }
        sheetTabs.SelectionChanged += (_, _) => { if (sheetTabs.SelectedItem is TabItem tab && tab.Content is Control c) _active = _sheets.Values.FirstOrDefault(x => ReferenceEquals(x.View, c)); UpdateWizard(); };
        _active = _sheets.Values.FirstOrDefault();
        _save = DashboardStyle.Action("Save workbook", Save, "primary");
        var title = DashboardStyle.Text("DataEngine Editor", 21, true);
        var subtitle = DashboardStyle.Text("Guided grid editor — select a cell to see compatible headers, assertion verbs, variables, and functions.", 12, false, DashboardStyle.Muted);
        _status.Foreground = DashboardStyle.Red;
        var top = new StackPanel { Spacing = 4, Children = { title, subtitle } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { DashboardStyle.Action("Open raw workbook", () => SystemDefaultFileOpener.Open(_path)), DashboardStyle.Action("Cancel", () => Close(null)), _save } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(18) };
        root.Children.Add(top); Grid.SetRow(sheetTabs, 1); root.Children.Add(sheetTabs); Grid.SetRow(_status, 2); root.Children.Add(_status); Grid.SetRow(buttons, 3); root.Children.Add(buttons); Content = root;
        sheetTabs.SelectedIndex = 0;
    }

    private void SelectCell(SheetDraft sheet, int row, int column)
    {
        _active = sheet; sheet.SelectedRow = row; sheet.SelectedColumn = column; UpdateWizard();
    }

    private void UpdateWizard()
    {
        foreach (var draft in _sheets.Values) draft.UpdateWizard = UpdateWizard;
        _active?.RefreshWizard();
    }

    private void Save()
    {
        try
        {
            foreach (var draft in _sheets.Values) draft.Validate();
            using var workbook = new XLWorkbook(_path);
            foreach (var (name, draft) in _sheets) draft.WriteTo(workbook.Worksheet(name));
            var backup = $"{_path}.backup.{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            File.Copy(_path, backup, overwrite: false); workbook.Save(); SavedBackup = backup; Close();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private sealed class SheetDraft
    {
        private readonly Action<SheetDraft, int, int> _select;
        private readonly string _sheetName;
        private readonly List<List<string>> _values = [];
        private readonly StackPanel _rows = new() { Spacing = 1 };
        private readonly TextBox _columnSearch = new() { Watermark = "Filter columns by header…", Width = 260 };
        private readonly TextBlock _selection = DashboardStyle.Text("Select a cell", 11, false, DashboardStyle.Muted);
        private readonly StackPanel _wizard = new() { Spacing = 9 };
        private readonly StackPanel _tokenList = new() { Spacing = 3 };
        private readonly ComboBox _assertionVerb = new() { ItemsSource = AssertionVerbs, SelectedIndex = 0 };
        private readonly ComboBox _headerVerb = new() { ItemsSource = AssertionVerbs, SelectedIndex = 0 };
        private readonly TextBox _expected = new();
        private readonly TextBox _cellValue = new();
        private readonly TextBlock _help = DashboardStyle.Text("Select a cell to see help.", 11, false, DashboardStyle.Muted);
        private readonly TextBox _tokenSearch = new() { Watermark = "Filter functions, headers, and From_ references…" };
        private readonly Dictionary<int, TextBox> _activeEditors = [];
        public Control View { get; }
        public Action? UpdateWizard { get; set; }
        public int SelectedRow { get; set; } = -1;
        public int SelectedColumn { get; set; } = -1;
        private string[] Headers => _values.Count == 0 ? [] : _values[0].ToArray();
        private static readonly string[] AssertionVerbs = { "eq", "ne", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "gt", "gte", "lt", "lte", "in", "size" };
        private static readonly string[] Functions = { "<guid>", "<guid:N>", "<randomnumber:1-100>", "<randomnumber_6>", "<currentdatum>", "<currentdatetime>", "<currenttimestamp>" };
        private static readonly string[] CommonHeaders = { "header_Accept", "header_Content-Type", "header_Authorization", "header_User-Agent", "header_Cookie", "header_Cache-Control", "header_Origin" };

        public SheetDraft(IXLWorksheet sheet, string sheetName, Action<SheetDraft, int, int> select)
        {
            _sheetName = sheetName; _select = select;
            var used = sheet.RangeUsed(); var lastRow = used?.RangeAddress.LastAddress.RowNumber ?? 1; var lastColumn = used?.RangeAddress.LastAddress.ColumnNumber ?? 1;
            for (var r = 1; r <= lastRow; r++) _values.Add(Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(r, c).GetString()).ToList());
            _columnSearch.TextChanged += (_, _) => Render();
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DashboardStyle.Action("Add row", AddRow), DashboardStyle.Action("Delete row", DeleteRow), DashboardStyle.Action("Add column", AddColumn), _columnSearch, _selection } };
            var gridScroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { toolbar, gridScroll } }; Grid.SetRow(gridScroll, 1);
            var center = new Border { BorderBrush = DashboardStyle.Line, BorderThickness = new Thickness(1), Child = table };
            BuildWizard();
            var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,390"), Children = { center, DashboardStyle.Card(new ScrollViewer { Content = _wizard, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }) } }; Grid.SetColumn(split.Children[1], 1);
            View = split; Render();
        }

        private void BuildWizard()
        {
            _wizard.Children.Add(DashboardStyle.Text("Cell wizard", 18, true));
            _wizard.Children.Add(DashboardStyle.Text("Select a cell to edit it with compatible choices.", 11, false, DashboardStyle.Muted));
            _wizard.Children.Add(DashboardStyle.Text("Cell value", 11, false, DashboardStyle.Muted));
            _cellValue.TextChanged += (_, _) => { if (SelectedRow >= 1 && SelectedColumn >= 0 && _cellValue.Tag is not null) _values[SelectedRow][SelectedColumn] = _cellValue.Text ?? ""; };
            _wizard.Children.Add(_cellValue);
            _wizard.Children.Add(DashboardStyle.Action("Apply cell value", () => { if (SelectedRow >= 1 && SelectedColumn >= 0) { _values[SelectedRow][SelectedColumn] = _cellValue.Text ?? ""; Render(); } }, "primary"));
            _wizard.Children.Add(DashboardStyle.Text("Assertion verb", 11, false, DashboardStyle.Muted));
            _assertionVerb.SelectionChanged += (_, _) => { if (SelectedRow >= 1 && SelectedColumn >= 0 && IsAssertionColumn()) ApplyAssertion(); };
            _wizard.Children.Add(_assertionVerb);
            _wizard.Children.Add(DashboardStyle.Text("Expected value", 11, false, DashboardStyle.Muted));
            _expected.TextChanged += (_, _) => { if (SelectedRow >= 1 && SelectedColumn >= 0 && IsAssertionColumn()) ApplyAssertion(); };
            _wizard.Children.Add(_expected);
            _wizard.Children.Add(DashboardStyle.Text("Header-level assertion rule", 11, false, DashboardStyle.Muted));
            _wizard.Children.Add(_headerVerb);
            _wizard.Children.Add(DashboardStyle.Action("Apply verb to response header", ApplyHeaderVerb));
            _wizard.Children.Add(DashboardStyle.Text("Cell value overrides the header rule when it starts with {verb}. Functions in the cell remain the expected value, for example {eq}<From_request_user_r> or {contains}<randomnumber:1-100>.", 10, false, DashboardStyle.Muted));
            _wizard.Children.Add(DashboardStyle.Text("Insert token / function", 11, false, DashboardStyle.Muted));
            _tokenSearch.TextChanged += (_, _) => RefreshWizard(); _wizard.Children.Add(_tokenSearch);
            _wizard.Children.Add(_tokenList);
            _wizard.Children.Add(DashboardStyle.Text("Available assertion verbs", 11, false, DashboardStyle.Muted));
            var verbSearch = new TextBox { Watermark = "Search assertion verbs…" }; var verbs = new ListBox { Height = 130, ItemsSource = AssertionVerbs };
            verbSearch.TextChanged += (_, _) => verbs.ItemsSource = AssertionVerbs.Where(x => x.Contains(verbSearch.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            verbs.SelectionChanged += (_, _) => { if (verbs.SelectedItem is string v) { _assertionVerb.SelectedItem = v; if (IsAssertionColumn()) ApplyAssertion(); } };
            _wizard.Children.Add(verbSearch); _wizard.Children.Add(verbs);
            var overrides = new WrapPanel { ItemWidth = 125, ItemHeight = 32 };
            foreach (var verb in AssertionVerbs) overrides.Children.Add(DashboardStyle.Action($"{{{verb}}} override", () => { _assertionVerb.SelectedItem = verb; if (IsAssertionColumn()) ApplyAssertion(); }));
            _wizard.Children.Add(DashboardStyle.Text("Cell-level override verbs", 11, false, DashboardStyle.Muted)); _wizard.Children.Add(overrides);
            _wizard.Children.Add(_help);
            RefreshWizard();
        }

        public void RefreshWizard()
        {
            if (SelectedRow < 0 || SelectedColumn < 0 || SelectedRow >= _values.Count || SelectedColumn >= _values[SelectedRow].Count) { _selection.Text = "Select a cell"; return; }
            var header = Headers[SelectedColumn]; var value = _values[SelectedRow][SelectedColumn]; _selection.Text = $"Row {SelectedRow + 1}, Column {SelectedColumn + 1} ({header})";
            _cellValue.Tag = header; _cellValue.Text = value; _expected.Text = ParseExpected(value); _assertionVerb.SelectedItem = ParseVerb(value); _headerVerb.SelectedItem = ParseHeaderVerb(header);
            _help.Text = GetHelp(header);
            _tokenList.Children.Clear();
            foreach (var token in CompatibleTokens(header).Where(x => string.IsNullOrWhiteSpace(_tokenSearch.Text) || x.Contains(_tokenSearch.Text!, StringComparison.OrdinalIgnoreCase))) _tokenList.Children.Add(DashboardStyle.Action(token, () => InsertToken(token)));
        }
        private IEnumerable<string> CompatibleTokens(string header)
        {
            var from = Headers.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => $"<From_{_sheetName}_{x}_r>");
            var variables = Headers.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => $"_{x}_");
            if (header.StartsWith("header_", StringComparison.OrdinalIgnoreCase)) return CommonHeaders.Where(x => !x.Equals(header, StringComparison.OrdinalIgnoreCase)).Concat(Functions).Concat(from).Concat(variables);
            if (header.Contains("assert", StringComparison.OrdinalIgnoreCase) || header.Contains("response", StringComparison.OrdinalIgnoreCase)) return Functions.Concat(from);
            return Functions.Concat(from).Concat(variables);
        }
        private string GetHelp(string header) => header.ToLowerInvariant() switch
        {
            "steptype" => "Step type values: HTTP, GraphQL, SQL, File, Script. HTTP requires templetsource; other types require stepconfig.",
            "stage" => "Execution stage: Setup, Test, or Teardown.",
            "stoponfailure" or "enabled" or "assert" or "ignoreempty" or "assertonlyresponse" => "Boolean value: true or false.",
            "environments" => "Comma-separated environment names. Blank applies to the default/untagged environment.",
            _ when header.StartsWith("header_", StringComparison.OrdinalIgnoreCase) => "HTTP header column. Select a common header or type a custom header value.",
            _ when header.Contains("assert", StringComparison.OrdinalIgnoreCase) => "Assertion format: {verb}expected, for example {eq}200 or {contains}completed.",
            _ => "Variables and functions can be inserted with the buttons above. Existing workbook syntax is preserved."
        };
        private bool IsAssertionColumn() => SelectedColumn >= 0 && (_sheetName.Equals("response", StringComparison.OrdinalIgnoreCase) || Headers[SelectedColumn].Contains("assert", StringComparison.OrdinalIgnoreCase));
        private void ApplyAssertion() { var verb = _assertionVerb.SelectedItem?.ToString() ?? "eq"; var expected = _expected.Text ?? ""; _cellValue.Text = verb is "exists" or "notexists" or "empty" or "notempty" ? $"{{{verb}}}" : $"{{{verb}}}{expected}"; _values[SelectedRow][SelectedColumn] = _cellValue.Text; }
        private void ApplyHeaderVerb()
        {
            if (!_sheetName.Equals("response", StringComparison.OrdinalIgnoreCase) || SelectedColumn < 0 || SelectedColumn >= Headers.Length) return;
            var header = Headers[SelectedColumn]; var existing = header.StartsWith("{", StringComparison.Ordinal) && header.IndexOf('}') > 1 ? header[(header.IndexOf('}') + 1)..] : header;
            _values[0][SelectedColumn] = $"{{{_headerVerb.SelectedItem?.ToString() ?? "eq"}}}{existing}"; Render();
        }
        private static string ParseVerb(string value) { var open = value.IndexOf('{'); var close = value.IndexOf('}'); return open == 0 && close > 1 && AssertionVerbs.Contains(value[1..close]) ? value[1..close] : "eq"; }
        private static string ParseHeaderVerb(string value) { var close = value.IndexOf('}'); return value.StartsWith("{", StringComparison.Ordinal) && close > 1 && AssertionVerbs.Contains(value[1..close]) ? value[1..close] : "eq"; }
        private static string ParseExpected(string value) { var close = value.IndexOf('}'); return value.StartsWith("{") && close > 1 ? value[(close + 1)..] : value; }
        private void InsertToken(string token) { _cellValue.Text = (_cellValue.Text ?? "") + token; if (SelectedRow >= 1 && SelectedColumn >= 0) _values[SelectedRow][SelectedColumn] = _cellValue.Text; }
        private void Render(bool refreshWizard = true)
        {
            _rows.Children.Clear(); _activeEditors.Clear(); var filter = _columnSearch.Text?.Trim() ?? ""; var visible = Enumerable.Range(0, Headers.Length).Where(i => string.IsNullOrWhiteSpace(filter) || Headers[i].Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (visible.Count == 0) { _rows.Children.Add(DashboardStyle.Text("No columns match the filter. Clear the filter to restore the grid.", 12, false, DashboardStyle.Muted)); if (refreshWizard) RefreshWizard(); return; }
            AddGridRow(0, visible, true);
            for (var r = 1; r < _values.Count; r++) AddGridRow(r, visible, false);
            if (refreshWizard) RefreshWizard();
        }
        private void AddGridRow(int row, IReadOnlyList<int> visible, bool header)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("48," + string.Join(',', visible.Select(_ => "190"))), Background = header ? DashboardStyle.Brush("#173559") : Brushes.Transparent };
            var number = DashboardStyle.Text(header ? "#" : row.ToString(), 11, header); Grid.SetColumn(number, 0); grid.Children.Add(number);
            for (var n = 0; n < visible.Count; n++)
            {
                var c = visible[n]; var box = new TextBox { Text = _values[row][c], FontSize = 11, MinWidth = 180, Margin = new Thickness(1), IsReadOnly = header };
                if (header) { box.Foreground = DashboardStyle.Brush("#E3EDF9"); ToolTip.SetTip(box, "Click a data cell below this header to open its wizard."); }
                else box.TextChanged += (_, _) => _values[row][c] = box.Text ?? "";
                box.PointerPressed += (_, _) => { if (!header) _select(this, row, c); }; Grid.SetColumn(box, n + 1); grid.Children.Add(box);
            }
            _rows.Children.Add(grid);
        }
        private void AddRow() { _values.Add(Enumerable.Repeat("", Headers.Length).ToList()); Render(); }
        private void AddColumn() { foreach (var row in _values) row.Add(""); Render(); }
        private void DeleteRow() { if (SelectedRow > 0 && SelectedRow < _values.Count) { _values.RemoveAt(SelectedRow); SelectedRow = -1; Render(); } }
        public void Validate() { if (_values.Count == 0 || _values[0].All(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Each edited sheet must retain a header row."); if (_values[0].Distinct(StringComparer.OrdinalIgnoreCase).Count() != _values[0].Count) throw new InvalidDataException("Duplicate column headers are not allowed."); }
        public void WriteTo(IXLWorksheet sheet) { sheet.RangeUsed()?.Clear(XLClearOptions.All); for (var r = 0; r < _values.Count; r++) for (var c = 0; c < _values[r].Count; c++) sheet.Cell(r + 1, c + 1).Value = _values[r][c]; sheet.Row(1).Style.Font.Bold = true; sheet.SheetView.FreezeRows(1); }
    }
}
