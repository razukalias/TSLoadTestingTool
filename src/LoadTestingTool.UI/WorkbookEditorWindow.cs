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
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _save;

    public WorkbookEditorWindow(string workbookPath)
    {
        _path = workbookPath; Title = $"Edit DataEngine — {Path.GetFileName(workbookPath)}"; Width = 1320; Height = 820; MinWidth = 980; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var tabs = new TabControl();
        using var workbook = new XLWorkbook(workbookPath);
        foreach (var name in new[] { "config", "request", "response", "README" })
        {
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (sheet is null) continue;
            var draft = new SheetDraft(sheet); _sheets[name] = draft; tabs.Items.Add(new TabItem { Header = name, Content = draft.View });
        }
        _save = DashboardStyle.Action("Save workbook", Save, "primary");
        var help = DashboardStyle.Text("Edit cells directly. Use the Config sheet for testcase/step definitions, Request for data rows, Response for assertion cells such as {eq}200, and README for documentation. Saving creates a timestamped backup.", 11, false, DashboardStyle.Muted); help.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = DashboardStyle.Red;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { DashboardStyle.Action("Cancel", () => Close(null)), _save } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(18) };
        root.Children.Add(DashboardStyle.Text("Edit existing DataEngine instance", 21, true)); Grid.SetRow(help, 1); root.Children.Add(tabs); Grid.SetRow(tabs, 1); Grid.SetRow(_status, 2); root.Children.Add(_status); Grid.SetRow(buttons, 3); root.Children.Add(buttons); Content = root;
    }

    private void Save()
    {
        try
        {
            foreach (var draft in _sheets.Values) draft.Validate();
            using var workbook = new XLWorkbook(_path);
            foreach (var (name, draft) in _sheets) draft.WriteTo(workbook.Worksheet(name));
            var backup = $"{_path}.backup.{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"; File.Copy(_path, backup, overwrite: false); workbook.Save(); Close(backup);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private sealed class SheetDraft
    {
        private readonly List<List<TextBox>> _cells = [];
        private readonly StackPanel _rows = new() { Spacing = 2 };
        private int _selectedRow = -1;
        public Control View { get; }
        public SheetDraft(IXLWorksheet sheet)
        {
            var used = sheet.RangeUsed(); var lastRow = used?.RangeAddress.LastAddress.RowNumber ?? 1; var lastColumn = used?.RangeAddress.LastAddress.ColumnNumber ?? 1;
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { DashboardStyle.Text("Rows are editable; click a row before Delete row.", 11, false, DashboardStyle.Muted), DashboardStyle.Action("Add row", AddRow), DashboardStyle.Action("Delete row", DeleteRow) } };
            var body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new Border { Width = 48 }, new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled } } };
            for (var r = 1; r <= lastRow; r++) AddExistingRow(Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(r, c).GetString()).ToArray(), r == 1);
            View = new DockPanel { LastChildFill = true, Children = { header, body } }; DockPanel.SetDock(header, Dock.Top);
        }
        private void AddExistingRow(IReadOnlyList<string> values, bool isHeader)
        {
            var rowIndex = _cells.Count; var boxes = new List<TextBox>(); var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("190", values.Count))), Background = isHeader ? DashboardStyle.Brush("#173559") : Brushes.Transparent, Margin = new Thickness(0, 0, 0, 1) };
            for (var i = 0; i < values.Count; i++) { var box = new TextBox { Text = values[i], FontSize = 11, MinWidth = 180, Margin = new Thickness(1), IsReadOnly = isHeader }; if (isHeader) box.Foreground = DashboardStyle.Brush("#E3EDF9"); box.PointerPressed += (_, _) => Select(rowIndex, grid); Grid.SetColumn(box, i); grid.Children.Add(box); boxes.Add(box); }
            _cells.Add(boxes); _rows.Children.Add(grid);
        }
        private void AddRow() { var width = _cells.Count == 0 ? 1 : _cells[0].Count; AddExistingRow(Enumerable.Repeat("", width).ToArray(), false); }
        private void DeleteRow() { if (_selectedRow <= 0 || _selectedRow >= _cells.Count) return; _rows.Children.RemoveAt(_selectedRow); _cells.RemoveAt(_selectedRow); _selectedRow = -1; }
        private void Select(int row, Control control) { _selectedRow = row; }
        public void Validate()
        {
            if (_cells.Count == 0 || _cells[0].All(c => string.IsNullOrWhiteSpace(c.Text))) throw new InvalidDataException("Each edited sheet must retain a header row.");
        }
        public void WriteTo(IXLWorksheet sheet)
        {
            var existing = sheet.RangeUsed(); existing?.Clear(XLClearOptions.All);
            for (var r = 0; r < _cells.Count; r++) for (var c = 0; c < _cells[r].Count; c++) sheet.Cell(r + 1, c + 1).Value = _cells[r][c].Text ?? "";
            sheet.Row(1).Style.Font.Bold = true; sheet.SheetView.FreezeRows(1);
        }
    }
}
