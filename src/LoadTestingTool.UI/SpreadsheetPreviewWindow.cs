using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using Spreadalonia;

namespace LoadTestingTool.UI;

/// <summary>
/// Experimental spreadsheet editor. Changes are saved to the source workbook after a backup is created.
/// </summary>
public sealed class SpreadsheetPreviewWindow : Window
{
    private sealed class SheetView
    {
        public sealed class CellStyle
        {
            public bool Bold { get; set; }
            public bool Italic { get; set; }
            public string? TextColor { get; set; }
            public string? Highlight { get; set; }
        }

        public required string Name { get; set; }
        public string? OriginalName { get; init; }
        public required Dictionary<(int Col, int Row), string> Cells { get; init; }
        public Spreadsheet Grid { get; set; } = null!;
        public Canvas HighlightOverlay { get; set; } = null!;
        public required TabItem Tab { get; init; }
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public bool StructureDirty { get; set; }
        public Dictionary<int, int> DisplayRows { get; set; } = [];
        public Dictionary<int, int> DisplayColumns { get; set; } = [];
        public Dictionary<(int Col, int Row), CellStyle> Styles { get; } = [];
    }

    private readonly string _sourcePath;
    private readonly TabControl _tabs = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Dictionary<string, SheetView> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedSheetNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _headerFilter = new() { Watermark = "Headers: header1 & header2…", MinWidth = 230 };
    private readonly TextBox _rowFilter = new() { Watermark = "Rows: 1,3,5…", MinWidth = 150 };
    private readonly TextBox _valueFilter = new() { Watermark = "Cell values: term1 & term2…", MinWidth = 230 };
    private readonly TextBlock _status = DashboardStyle.Text("Edit cells here and save the original workbook.", 12, false, DashboardStyle.Muted);
    private readonly ComboBox _textColor = new() { Width = 120, ItemsSource = new[] { "Text color", "White", "Yellow", "Cyan", "Red", "Green" }, SelectedIndex = 0 };
    private readonly ComboBox _highlightColor = new() { Width = 130, ItemsSource = new[] { "Highlight", "Blue", "Yellow", "Green", "Red" }, SelectedIndex = 0 };
    private readonly Popup _suggestionPopup = new() { PlacementMode = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = false };
    private readonly ListBox _suggestions = new() { MaxHeight = 240, MinWidth = 330 };
    private SheetView? _suggestionSheet;
    private Spreadsheet? _suggestionGrid;
    private bool _refreshing;

    public SpreadsheetPreviewWindow(string workbookPath)
    {
        _sourcePath = workbookPath;
        Title = $"Spreadsheet editor — {Path.GetFileName(workbookPath)}";
        Width = 1320; Height = 900; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        try
        {
            using var workbook = new XLWorkbook(workbookPath);
            foreach (var worksheet in workbook.Worksheets)
            {
                var used = worksheet.RangeUsed();
                var cells = ReadCells(worksheet).ToDictionary(x => (x.Key.Item1, x.Key.Item2), x => x.Value);
                var tab = new TabItem { Header = worksheet.Name };
                var view = new SheetView { Name = worksheet.Name, OriginalName = worksheet.Name, Cells = cells, Tab = tab,
                    RowCount = Math.Max(1, used?.RangeAddress.LastAddress.RowNumber ?? 1),
                    ColumnCount = Math.Max(1, used?.RangeAddress.LastAddress.ColumnNumber ?? 1) };
                foreach (var cell in worksheet.CellsUsed())
                {
                    if (cell.Style.Fill.BackgroundColor.ColorType != XLColorType.Color) continue;
                    var color = cell.Style.Fill.BackgroundColor.Color;
                    view.Styles[(cell.Address.ColumnNumber - 1, cell.Address.RowNumber - 1)] = new SheetView.CellStyle
                    {
                        Highlight = $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                    };
                }
                _sheets[worksheet.Name] = view;
                _tabs.Items.Add(tab);
            }
            if (_tabs.ItemCount == 0) _status.Text = "The workbook contains no worksheets.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not load workbook: {ex.Message}";
            _status.Foreground = DashboardStyle.Red;
        }

        _headerFilter.TextChanged += (_, _) => RefreshFilteredViews();
        _rowFilter.TextChanged += (_, _) => RefreshFilteredViews();
        _valueFilter.TextChanged += (_, _) => RefreshFilteredViews();
        _suggestions.PointerReleased += (_, _) => { if (_suggestions.SelectedItem is string value) AcceptSuggestion(value); };
        _suggestionPopup.Child = _suggestions;
        _textColor.SelectionChanged += (_, _) => ApplyTextColor();
        _highlightColor.SelectionChanged += (_, _) => ApplyHighlight();
        AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control && e.Key == Key.S)
            {
                SaveWorkbook();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        var heading = new StackPanel { Spacing = 4, Children =
        {
            DashboardStyle.Text("Spreadsheet editor", 21, true),
            DashboardStyle.Text("Edits save to the original DataEngine workbook after creating a .bak.xlsx backup. Filters are visual only and do not delete workbook data.", 12, false, DashboardStyle.Muted)
        }};
        var filterBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 8), Children =
        {
            Labeled("Header filter", _headerFilter), Labeled("Row filter", _rowFilter), Labeled("Cell-value filter", _valueFilter),
            DashboardStyle.Action("Clear filters", ClearFilters)
        }};
        var formatBar = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN, ItemHeight = double.NaN, Margin = new Thickness(0, 0, 0, 8), Children =
        {
            DashboardStyle.Text("Format", 10, false, DashboardStyle.Muted),
            DashboardStyle.Action("Bold", () => ApplyTypeface(FontWeight.Bold)),
            DashboardStyle.Action("Italic", () => ApplyTypeface(FontWeight.Normal)),
            _textColor, _highlightColor,
            DashboardStyle.Action("Copy", () => ApplyFormatCommand("Copied", g => g.Copy())),
            DashboardStyle.Action("Cut", () => { var g = ActiveGrid(); if (g is not null) ApplyDataCommand(g, x => x.Cut()); }),
            DashboardStyle.Action("Paste", () => { var g = ActiveGrid(); if (g is not null) _ = PasteAndSync(g); }),
            DashboardStyle.Action("Undo", () => ApplyFormatCommand("Undo", g => g.Undo())),
            DashboardStyle.Action("Redo", () => ApplyFormatCommand("Redo", g => g.Redo())),
            DashboardStyle.Action("Insert row", () => ApplyStructuralCommand("Inserted row", true, 1)),
            DashboardStyle.Action("Delete row", () => ApplyStructuralCommand("Deleted row", true, -1)),
            DashboardStyle.Action("Insert column", () => ApplyStructuralCommand("Inserted column", false, 1)),
            DashboardStyle.Action("Delete column", () => ApplyStructuralCommand("Deleted column", false, -1)),
            DashboardStyle.Action("Auto-fit width", () => ApplyFormatCommand("Auto-fit columns", g => g.AutoFitWidth())),
            DashboardStyle.Action("Auto-fit height", () => ApplyFormatCommand("Auto-fit rows", g => g.AutoFitHeight())),
            DashboardStyle.Action("Reset format", ResetSelectedFormat),
            DashboardStyle.Text("Sheets", 10, false, DashboardStyle.Muted),
            DashboardStyle.Action("Add sheet", AddSheet),
            DashboardStyle.Action("Remove sheet", RemoveSheet),
            DashboardStyle.Action("Rename sheet", RenameSheet)
        }};
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children =
        {
            DashboardStyle.Action("Save workbook", SaveWorkbook, "primary"),
            DashboardStyle.Action("Open source workbook", () => SystemDefaultFileOpener.Open(_sourcePath)),
            DashboardStyle.Action("Close", Close)
        }};
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(18) };
        root.Children.Add(heading);
        Grid.SetRow(filterBar, 1); root.Children.Add(filterBar);
        Grid.SetRow(formatBar, 2); root.Children.Add(formatBar);
        Grid.SetRow(_tabs, 3); root.Children.Add(_tabs);
        Grid.SetRow(_status, 4); root.Children.Add(_status);
        Grid.SetRow(buttons, 5); root.Children.Add(buttons);
        Content = new Grid { Children = { root, _suggestionPopup } };
        _tabs.SelectionChanged += (_, _) => RefreshStatus();
        _tabs.SelectedIndex = 0;
        Dispatcher.UIThread.Post(() => RefreshFilteredViews(), DispatcherPriority.Loaded);
    }

    private static Control Labeled(string label, Control field) => new StackPanel { Spacing = 3, Children = { DashboardStyle.Text(label, 10, false, DashboardStyle.Muted), field } };

    private void ClearFilters()
    {
        _headerFilter.Text = ""; _rowFilter.Text = ""; _valueFilter.Text = "";
    }

    private static IEnumerable<KeyValuePair<(int, int), string>> ReadCells(IXLWorksheet worksheet)
    {
        var used = worksheet.RangeUsed();
        if (used is null) yield break;
        foreach (var cell in used.Cells())
            yield return new KeyValuePair<(int, int), string>((cell.Address.ColumnNumber - 1, cell.Address.RowNumber - 1), cell.GetString());
    }

    private void RefreshFilteredViews(bool syncExisting = true)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            foreach (var view in _sheets.Values)
            {
                if (syncExisting) SyncView(view);
                var rows = Enumerable.Range(0, Math.Max(1, view.RowCount)).ToArray();
                var columns = Enumerable.Range(0, Math.Max(1, view.ColumnCount)).ToArray();
                var selectedRows = ParseRows(_rowFilter.Text, rows);
                var headerTerms = Terms(_headerFilter.Text);
                var valueTerms = Terms(_valueFilter.Text);
                var selectedColumns = columns.Where(col => headerTerms.Length == 0 || headerTerms.All(term => Cell(view.Cells, col, 0).Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
                selectedRows = selectedRows.Where(row => row == 0 || valueTerms.Length == 0 || valueTerms.All(term => RowText(view.Cells, row).Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();

                view.DisplayRows = selectedRows.Select((row, index) => (row, index)).ToDictionary(x => x.row, x => x.index);
                view.DisplayColumns = selectedColumns.Select((col, index) => (col, index)).ToDictionary(x => x.col, x => x.index);
                var projected = new Dictionary<(int, int), string>();
                foreach (var row in selectedRows)
                    foreach (var col in selectedColumns)
                        if (view.Cells.TryGetValue((col, row), out var value) && !string.IsNullOrEmpty(value))
                            projected[(view.DisplayColumns[col], view.DisplayRows[row])] = value;
                ReplaceGrid(view, projected, syncExisting);
            }
            RefreshStatus();
        }
        finally { _refreshing = false; }
    }

    private static string[] Terms(string? text) => (text ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int[] ParseRows(string? text, int[] allRows)
    {
        var filter = (text ?? "").Trim();
        if (filter.Length == 0) return allRows;

        var requested = new HashSet<int>();
        foreach (var part in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length == 2 && int.TryParse(range[0], out var first) && int.TryParse(range[1], out var last))
            {
                var lower = Math.Max(1, Math.Min(first, last));
                var upper = Math.Min(allRows.Length, Math.Max(first, last));
                for (var number = lower; number <= upper; number++) requested.Add(number - 1);
            }
            else if (range.Length == 1 && int.TryParse(part, out var number) && number >= 1)
            {
                requested.Add(number - 1);
            }
        }

        // Row 0 is the workbook header and must stay visible for every row filter.
        return requested.Count == 0
            ? allRows
            : allRows.Where(row => row == 0 || requested.Contains(row)).ToArray();
    }

    private static string Cell(Dictionary<(int Col, int Row), string> cells, int col, int row) => cells.TryGetValue((col, row), out var value) ? value : "";
    private static string RowText(Dictionary<(int Col, int Row), string> cells, int row) => string.Join(" ", cells.Where(x => x.Key.Row == row).OrderBy(x => x.Key.Col).Select(x => x.Value));

    private void ReplaceGrid(SheetView view, Dictionary<(int, int), string> projected, bool syncExisting = true)
    {
        if (syncExisting && view.Grid is not null) SyncView(view);
        var grid = new Spreadsheet
        {
            Foreground = Brushes.Black,
            SpreadsheetBackground = new SolidColorBrush(Colors.White),
            HeaderForeground = Brushes.Black,
            HeaderBackground = Color.Parse("#E6E6E6"),
            SelectionAccent = new SolidColorBrush(Color.Parse("#9CC7F5"))
        };
        view.Grid = grid;
        var host = new Grid { ClipToBounds = true };
        var overlay = new Canvas { IsHitTestVisible = false };
        view.HighlightOverlay = overlay;
        host.Children.Add(grid);
        host.Children.Add(overlay);
        view.Tab.Content = new Border { Padding = new Thickness(6), Child = host };
        WireAutocomplete(grid, view);
        grid.PropertyChanged += (_, e) =>
        {
            if (e.Property == Spreadsheet.SelectionProperty)
                Dispatcher.UIThread.Post(() => RefreshHighlightOverlay(view), DispatcherPriority.Render);
        };
        ScheduleGridLoad(grid, projected);
        Dispatcher.UIThread.Post(() => RefreshHighlightOverlay(view), DispatcherPriority.Render);
    }

    private void WireAutocomplete(Spreadsheet grid, SheetView view)
    {
        grid.ContextMenu = BuildGridMenu(grid);
        grid.AddHandler(InputElement.TextInputEvent, (_, _) => Dispatcher.UIThread.Post(() => UpdateSuggestions(grid, view)), RoutingStrategies.Bubble);
        grid.AddHandler(InputElement.KeyDownEvent, (_, e) => HandleClipboardKey(grid, view, e), RoutingStrategies.Tunnel);
        grid.AddHandler(InputElement.KeyDownEvent, (_, e) => HandleAutocompleteKey(grid, view, e), RoutingStrategies.Tunnel);
        grid.PropertyChanged += (_, e) =>
        {
            if (e.Property == Spreadsheet.IsEditingProperty && grid.IsEditing)
            {
                Dispatcher.UIThread.Post(() => AttachEditingAutocomplete(grid, view), DispatcherPriority.Background);
                Dispatcher.UIThread.Post(() => AttachEditingAutocomplete(grid, view), DispatcherPriority.Render);
            }
        };
        grid.Loaded += (_, _) => Dispatcher.UIThread.Post(() => AttachEditingAutocomplete(grid, view), DispatcherPriority.Loaded);
    }

    private static void HandleClipboardKey(Spreadsheet grid, SheetView view, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != KeyModifiers.Control || grid.Selection.Count == 0) return;
        switch (e.Key)
        {
            case Key.C:
                grid.Copy();
                e.Handled = true;
                break;
            case Key.X:
                grid.Cut();
                SyncView(view);
                Dispatcher.UIThread.Post(() => SyncView(view), DispatcherPriority.Background);
                e.Handled = true;
                break;
            case Key.V:
                _ = PasteFromKeyboardAsync(grid, view);
                e.Handled = true;
                break;
        }
    }

    private static async Task PasteFromKeyboardAsync(Spreadsheet grid, SheetView view)
    {
        await grid.Paste(false);
        SyncView(view);
        Dispatcher.UIThread.Post(() => SyncView(view), DispatcherPriority.Background);
    }

    private void AttachEditingAutocomplete(Spreadsheet grid, SheetView view)
    {
        var editor = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.Name == "EditingBox");
        if (editor is null || editor.Tag is not null) return;
        editor.Tag = "autocomplete-attached";
        editor.Foreground = Brushes.Black;
        editor.Background = Brushes.White;
        editor.CaretBrush = Brushes.Black;
        editor.Opacity = 1;
        editor.TextChanged += (_, _) => Dispatcher.UIThread.Post(() => UpdateSuggestions(grid, view, liveText: editor.Text), DispatcherPriority.Input);
        editor.TextChanged += (_, _) => SyncEditingCell(grid, view, editor.Text ?? string.Empty);
    }

    private ContextMenu BuildGridMenu(Spreadsheet grid)
    {
        var menu = new ContextMenu();
        foreach (var item in new Control[]
        {
            Menu("Copy", () => grid.Copy()), Menu("Cut", () => ApplyDataCommand(grid, g => g.Cut())), Menu("Paste", () => _ = PasteAndSync(grid)),
            new Separator(),
            Menu("Bold", () => ApplyFormatCommand("Bold", g => { g.SetTypeface(new Typeface("Arial", FontStyle.Normal, FontWeight.Bold)); CaptureSelectionStyle(g, bold: true); })),
            Menu("Italic", () => ApplyFormatCommand("Italic", g => { g.SetTypeface(new Typeface("Arial", FontStyle.Italic, FontWeight.Normal)); CaptureSelectionStyle(g, italic: true); })),
            Menu("Text color: yellow", () => ApplyFormatCommand("Text color", g => { g.SetForeground(Brushes.Yellow); CaptureSelectionStyle(g, textColor: "#FFFF00"); })),
            Menu("Highlight selection", () => ApplyFormatCommand("Highlight", g => { g.SelectionAccent = new SolidColorBrush(Color.Parse("#3B6EA8")); CaptureSelectionStyle(g, highlight: "#3B6EA8"); })),
            Menu("Reset format", ResetSelectedFormat),
            new Separator(),
            Menu("Insert row", () => ApplyStructuralCommand("Inserted row", true, 1)), Menu("Delete row", () => ApplyStructuralCommand("Deleted row", true, -1)),
            Menu("Insert column", () => ApplyStructuralCommand("Inserted column", false, 1)), Menu("Delete column", () => ApplyStructuralCommand("Deleted column", false, -1)),
            Menu("Auto-fit width", () => grid.AutoFitWidth()), Menu("Auto-fit height", () => grid.AutoFitHeight()),
            Menu("Undo", () => grid.Undo()), Menu("Redo", () => grid.Redo())
        }) menu.Items.Add(item);
        return menu;
    }

    private static MenuItem Menu(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }

    private Spreadsheet? ActiveGrid() => _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex)?.Grid;

    private void ApplyFormatCommand(string message, Action<Spreadsheet> action)
    {
        var grid = ActiveGrid();
        if (grid is null) return;
        try
        {
            action(grid);
            var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
            if (view is not null) Dispatcher.UIThread.Post(() => RefreshHighlightOverlay(view), DispatcherPriority.Render);
            _status.Text = message + "."; _status.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { _status.Text = $"{message} failed: {ex.Message}"; _status.Foreground = DashboardStyle.Red; }
    }

    private void ApplyDataCommand(Spreadsheet grid, Action<Spreadsheet> action)
    {
        try
        {
            action(grid);
            var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
            if (view is not null)
            {
                SyncView(view);
                Dispatcher.UIThread.Post(() => SyncView(view), DispatcherPriority.Background);
            }
            _status.Foreground = DashboardStyle.Green;
            _status.Text = "Cell changes applied. Save workbook to write them to the original file.";
        }
        catch (Exception ex)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Cell operation failed: {ex.Message}";
        }
    }

    private async Task PasteAndSync(Spreadsheet grid)
    {
        try
        {
            await grid.Paste(false);
            var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
            if (view is not null)
            {
                SyncView(view);
                Dispatcher.UIThread.Post(() => SyncView(view), DispatcherPriority.Background);
            }
            _status.Foreground = DashboardStyle.Green;
            _status.Text = "Pasted cells applied. Save workbook to write them to the original file.";
        }
        catch (Exception ex)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Paste failed: {ex.Message}";
        }
    }

    private void ApplyStructuralCommand(string message, bool rows, int direction)
    {
        var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
        var grid = view?.Grid;
        if (view is null || grid is null || grid.Selection.Count == 0) return;
        try
        {
            SyncView(view);
            var range = grid.Selection[0];
            var start = rows ? OriginalRowAt(view, range.Top) : OriginalColumnAt(view, range.Left);
            var reportedCount = rows ? range.Height : range.Width;
            // Spreadalonia reports a whole row/column header selection as the
            // full Excel grid (over a million rows or thousands of columns).
            // Structural editing must operate on the selected workbook area,
            // never on the virtual grid dimensions.
            var workbookCount = rows ? view.RowCount : view.ColumnCount;
            var count = reportedCount is > 0 and <= 256
                ? Math.Min(reportedCount, Math.Max(1, workbookCount - start))
                : 1;
            if (rows)
            {
                ShiftRows(view, start, direction * count);
                view.RowCount = Math.Max(1, view.RowCount + direction * count);
            }
            else
            {
                ShiftColumns(view, start, direction * count);
                view.ColumnCount = Math.Max(1, view.ColumnCount + direction * count);
            }
            view.StructureDirty = true;
            RefreshFilteredViews(syncExisting: false);
            _status.Foreground = DashboardStyle.Green;
            _status.Text = $"{message} at {(rows ? $"row {start + 1}" : $"column {start + 1}")}. Changes will be written to the original workbook when saved.";
        }
        catch (Exception ex)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"{message} failed: {ex.Message}";
        }
    }

    private static void ShiftRows(SheetView view, int start, int delta)
    {
        var cells = view.Cells.ToArray();
        view.Cells.Clear();
        foreach (var cell in cells)
        {
            if (delta < 0 && cell.Key.Row >= start && cell.Key.Row < start - delta) continue;
            var row = cell.Key.Row >= start + (delta < 0 ? -delta : 0) ? cell.Key.Row + delta : cell.Key.Row;
            view.Cells[(cell.Key.Col, row)] = cell.Value;
        }
        ShiftStyles(view, start, delta, true);
    }

    private static void ShiftColumns(SheetView view, int start, int delta)
    {
        var cells = view.Cells.ToArray();
        view.Cells.Clear();
        foreach (var cell in cells)
        {
            if (delta < 0 && cell.Key.Col >= start && cell.Key.Col < start - delta) continue;
            var column = cell.Key.Col >= start + (delta < 0 ? -delta : 0) ? cell.Key.Col + delta : cell.Key.Col;
            view.Cells[(column, cell.Key.Row)] = cell.Value;
        }
        ShiftStyles(view, start, delta, false);
    }

    private static void ShiftStyles(SheetView view, int start, int delta, bool rows)
    {
        var styles = view.Styles.ToArray();
        view.Styles.Clear();
        foreach (var style in styles)
        {
            var coordinate = rows ? style.Key.Row : style.Key.Col;
            if (delta < 0 && coordinate >= start && coordinate < start - delta) continue;
            coordinate = coordinate >= start + (delta < 0 ? -delta : 0) ? coordinate + delta : coordinate;
            view.Styles[rows ? (style.Key.Col, coordinate) : (coordinate, style.Key.Row)] = style.Value;
        }
    }

    private static int OriginalRowAt(SheetView view, int displayRow)
    {
        foreach (var pair in view.DisplayRows)
            if (pair.Value == displayRow) return pair.Key;
        return Math.Clamp(displayRow, 0, Math.Max(0, view.RowCount - 1));
    }

    private static int OriginalColumnAt(SheetView view, int displayColumn)
    {
        foreach (var pair in view.DisplayColumns)
            if (pair.Value == displayColumn) return pair.Key;
        return Math.Clamp(displayColumn, 0, Math.Max(0, view.ColumnCount - 1));
    }

    private void ApplyTypeface(FontWeight weight)
    {
        ApplyFormatCommand(weight == FontWeight.Bold ? "Bold applied" : "Italic applied", grid =>
        {
            grid.SetTypeface(new Typeface("Arial", weight == FontWeight.Bold ? FontStyle.Normal : FontStyle.Italic, weight));
            CaptureSelectionStyle(grid, bold: weight == FontWeight.Bold, italic: weight != FontWeight.Bold);
        });
    }

    private void ApplyTextColor()
    {
        var color = _textColor.SelectedItem?.ToString() switch { "White" => "#FFFFFF", "Yellow" => "#FFFF00", "Cyan" => "#00FFFF", "Red" => "#FF0000", "Green" => "#32CD32", _ => null };
        if (color is not null)
            ApplyFormatCommand("Text color applied", grid => { grid.SetForeground(new SolidColorBrush(Color.Parse(color))); CaptureSelectionStyle(grid, textColor: color); });
        _textColor.SelectedIndex = 0;
    }

    private void ApplyHighlight()
    {
        var color = _highlightColor.SelectedItem?.ToString() switch { "Blue" => "#3B6EA8", "Yellow" => "#806B20", "Green" => "#276144", "Red" => "#74353F", _ => null };
        if (color is not null)
            ApplyFormatCommand("Selection highlight applied", grid => { grid.SelectionAccent = new SolidColorBrush(Color.Parse(color)); CaptureSelectionStyle(grid, highlight: color); });
        _highlightColor.SelectedIndex = 0;
    }

    private void CaptureSelectionStyle(Spreadsheet grid, bool? bold = null, bool? italic = null, string? textColor = null, string? highlight = null)
    {
        var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
        if (view is null || grid.Selection.Count == 0) return;
        var maxColumn = Math.Max(0, view.DisplayColumns.Count - 1);
        var maxRow = Math.Max(0, view.DisplayRows.Count - 1);
        foreach (var range in grid.Selection)
        {
            var left = Math.Clamp(range.Left, 0, maxColumn);
            var right = Math.Clamp(range.Right, 0, maxColumn);
            var top = Math.Clamp(range.Top, 0, maxRow);
            var bottom = Math.Clamp(range.Bottom, 0, maxRow);
            for (var row = top; row <= bottom; row++)
                for (var column = left; column <= right; column++)
                {
                    if (!view.DisplayRows.TryGetValue(row, out var originalRow) || !view.DisplayColumns.TryGetValue(column, out var originalColumn)) continue;
                    var key = (originalColumn, originalRow);
                    if (!view.Styles.TryGetValue(key, out var style)) view.Styles[key] = style = new SheetView.CellStyle();
                    if (bold.HasValue) style.Bold = bold.Value;
                    if (italic.HasValue) style.Italic = italic.Value;
                    if (textColor is not null) style.TextColor = textColor;
                    if (highlight is not null) style.Highlight = highlight;
                }
        }
    }

    private void ResetSelectedFormat()
    {
        var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
        var grid = ActiveGrid();
        if (view is null || grid is null) return;
        ApplyFormatCommand("Reset format", g => g.ResetFormat());
        var maxColumn = Math.Max(0, view.DisplayColumns.Count - 1);
        var maxRow = Math.Max(0, view.DisplayRows.Count - 1);
        foreach (var range in grid.Selection)
        {
            var left = Math.Clamp(range.Left, 0, maxColumn);
            var right = Math.Clamp(range.Right, 0, maxColumn);
            var top = Math.Clamp(range.Top, 0, maxRow);
            var bottom = Math.Clamp(range.Bottom, 0, maxRow);
            for (var row = top; row <= bottom; row++)
                for (var column = left; column <= right; column++)
                    if (view.DisplayRows.TryGetValue(row, out var originalRow) && view.DisplayColumns.TryGetValue(column, out var originalColumn))
                        view.Styles.Remove((originalColumn, originalRow));
        }
    }

    private void HandleAutocompleteKey(Spreadsheet grid, SheetView view, KeyEventArgs e)
    {
        if (_suggestionPopup.IsOpen && e.Key == Key.Up)
        {
            _suggestions.SelectedIndex = Math.Max(0, _suggestions.SelectedIndex - 1); e.Handled = true; return;
        }
        if (_suggestionPopup.IsOpen && e.Key == Key.Down)
        {
            _suggestions.SelectedIndex = Math.Min(_suggestions.ItemCount - 1, _suggestions.SelectedIndex + 1); e.Handled = true; return;
        }
        if (_suggestionPopup.IsOpen && (e.Key == Key.Enter || e.Key == Key.Tab) && _suggestions.SelectedItem is string selected)
        {
            AcceptSuggestion(selected); e.Handled = true; return;
        }
        if (e.Key == Key.Escape && _suggestionPopup.IsOpen) { _suggestionPopup.IsOpen = false; e.Handled = true; return; }
        if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            UpdateSuggestions(grid, view, true); e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            Dispatcher.UIThread.Post(() => UpdateSuggestions(grid, view), DispatcherPriority.Background);
        }
    }

    private void UpdateSuggestions(Spreadsheet grid, SheetView view, bool force = false, string? liveText = null)
    {
        AttachEditingAutocomplete(grid, view);
        var data = grid.Data;
        if (grid.Selection.Count == 0 || data is null) { _suggestionPopup.IsOpen = false; return; }
        var selection = grid.Selection[0];
        var editor = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.Name == "EditingBox" && x.IsVisible);
        var text = liveText ?? (grid.IsEditing && editor is not null ? editor.Text ?? "" : data.TryGetValue((selection.Left, selection.Top), out var value) ? value ?? "" : "");
        var caret = text.Length;
        var before = text[..caret];
        var lt = before.LastIndexOf('<'); var brace = before.LastIndexOf('{');
        var start = Math.Max(lt, brace);
        var token = start >= 0 ? before[start..] : "";
        var suggestions = new List<string>();
        if (brace > lt && !before[(brace + 1)..].Contains('}'))
            suggestions.AddRange(new[] { "eq", "ne", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "gt", "gte", "lt", "lte", "in", "size" }.Select(x => "{" + x + "}"));
        else if (lt >= 0 && !before[(lt + 1)..].Contains('>'))
        {
            var typed = before[(lt + 1)..];
            var from = typed.StartsWith("From_", StringComparison.OrdinalIgnoreCase);
            if (typed.Equals("from", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add("<From_");
            }
            else if (from)
            {
                var parts = typed[5..].Split('_', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) suggestions.AddRange(_sheets.Keys.Select(x => $"<From_{x}_"));
                else if (parts.Length == 1)
                {
                    var source = _sheets.Values.FirstOrDefault(x => x.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    if (source is not null) suggestions.AddRange(source.Cells.Where(x => x.Key.Row == 0).Select(x => $"<From_{source.Name}_{x.Value}_r>"));
                }
                else
                {
                    var source = _sheets.Values.FirstOrDefault(x => x.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    var prefix = string.Join('_', parts.Skip(1));
                    if (source is not null) suggestions.AddRange(source.Cells.Where(x => x.Key.Row == 0 && x.Value.Contains(prefix, StringComparison.OrdinalIgnoreCase)).Select(x => $"<From_{source.Name}_{x.Value}_r>"));
                }
            }
            else suggestions.AddRange(new[] { "<From_", "<datum>", "<currentdatum>", "<currenttime>", "<currentdatetime>", "<currenttimestamp>", "<randomnumber>", "<randomnumber:1-100>", "<guid>", "<guid:N>" });
        }
        else if (force) suggestions.AddRange(new[] { "<From_", "<datum>", "<currentdatetime>", "<randomnumber>", "<guid>", "{eq}", "{contains}", "{exists}" });
        suggestions = suggestions.Where(x => force || x.Contains(token, StringComparison.OrdinalIgnoreCase)).Distinct().Take(80).ToList();
        if (suggestions.Count == 0) { _suggestionPopup.IsOpen = false; return; }
        _suggestions.ItemsSource = suggestions; _suggestions.SelectedIndex = 0;
        _suggestionSheet = view; _suggestionGrid = grid; _suggestionPopup.PlacementTarget = editor is not null ? editor : grid; _suggestionPopup.IsOpen = true;
    }

    private void AcceptSuggestion(string suggestion)
    {
        if (_suggestionGrid is null || _suggestionSheet is null || _suggestionGrid.Selection.Count == 0) return;
        var selection = _suggestionGrid.Selection[0];
        var key = (selection.Left, selection.Top);
        var editor = _suggestionGrid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.Name == "EditingBox");
        var text = editor?.Text ?? (_suggestionGrid.Data.TryGetValue(key, out var current) ? current ?? "" : "");
        var lt = text.LastIndexOf('<'); var brace = text.LastIndexOf('{'); var start = Math.Max(lt, brace); if (start < 0) start = text.Length;
        var next = text[..start] + suggestion + text[Math.Min(text.Length, start + Math.Max(0, text.Length - start))..];
        if (editor is not null && _suggestionGrid.IsEditing)
        {
            editor.Text = next;
            editor.CaretIndex = next.Length;
            editor.Focus();
        }
        else _suggestionGrid.SetData(new[] { new KeyValuePair<(int, int), string>(key, next) });
        _suggestionPopup.IsOpen = false;
    }

    private static void ScheduleGridLoad(Spreadsheet grid, Dictionary<(int, int), string> projected)
    {
        void Load() => Dispatcher.UIThread.Post(() =>
        {
            if (grid.Data is not null) grid.SetData(projected);
        }, DispatcherPriority.Render);
        grid.AttachedToVisualTree += (_, _) => Load();
        grid.Loaded += (_, _) => Load();
    }

    private static void SyncEditingCell(Spreadsheet grid, SheetView view, string value)
    {
        if (grid.Selection.Count == 0) return;
        var selection = grid.Selection[0];
        if (!view.DisplayRows.TryGetValue(selection.Top, out var originalRow) ||
            !view.DisplayColumns.TryGetValue(selection.Left, out var originalColumn)) return;

        var key = (originalColumn, originalRow);
        if (string.IsNullOrEmpty(value)) view.Cells.Remove(key);
        else view.Cells[key] = value;
    }

    private static void RefreshHighlightOverlay(SheetView view)
    {
        if (view.Grid is null || view.HighlightOverlay is null) return;
        view.HighlightOverlay.Children.Clear();
        var table = view.Grid.GetVisualDescendants().OfType<Control>().FirstOrDefault(x => x.Name == "ContentTable");
        if (table is null) return;
        var origin = table.TranslatePoint(new Point(0, 0), view.HighlightOverlay);
        if (origin is null) return;
        var horizontalScroll = view.Grid.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(x => x.Name == "HorizontalScrollBar");
        var verticalScroll = view.Grid.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(x => x.Name == "VerticalScrollBar");
        var offset = new Vector(horizontalScroll?.Value ?? 0, verticalScroll?.Value ?? 0);
        var columnStarts = new Dictionary<int, double>();
        var rowStarts = new Dictionary<int, double>();
        double x = 0;
        for (var column = 0; column < view.ColumnCount; column++)
        {
            columnStarts[column] = x;
            x += view.Grid.GetCellSize(column, 0).width;
        }
        double y = 0;
        for (var row = 0; row < view.RowCount; row++)
        {
            rowStarts[row] = y;
            y += view.Grid.GetCellSize(0, row).height;
        }

        foreach (var item in view.Styles)
        {
            if (string.IsNullOrWhiteSpace(item.Value.Highlight) ||
                !view.DisplayColumns.TryGetValue(item.Key.Col, out var displayColumn) ||
                !view.DisplayRows.TryGetValue(item.Key.Row, out var displayRow) ||
                !columnStarts.TryGetValue(displayColumn, out var left) ||
                !rowStarts.TryGetValue(displayRow, out var top)) continue;
            var size = view.Grid.GetCellSize(displayColumn, displayRow);
            var color = Color.Parse(item.Value.Highlight);
            var fill = new SolidColorBrush(Color.FromArgb(72, color.R, color.G, color.B));
            var cell = new Border { Background = fill, Width = size.width, Height = size.height, IsHitTestVisible = false };
            Canvas.SetLeft(cell, origin.Value.X + left - offset.X);
            Canvas.SetTop(cell, origin.Value.Y + top - offset.Y);
            view.HighlightOverlay.Children.Add(cell);
        }
    }

    private static void SyncView(SheetView view)
    {
        if (view.Grid is null || view.DisplayRows.Count == 0 || view.DisplayColumns.Count == 0 || view.Grid.Data is null) return;
        var visibleCells = view.Grid.Data.ToDictionary(x => x.Key, x => x.Value);
        var editor = view.Grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.Name == "EditingBox" && x.IsVisible);
        if (view.Grid.IsEditing && editor is not null && view.Grid.Selection.Count > 0)
        {
            var selected = view.Grid.Selection[0];
            visibleCells[(selected.Left, selected.Top)] = editor.Text ?? string.Empty;
        }

        // Remove only currently visible cells. The previous row×column Cartesian
        // allocation could freeze the UI for large sheets during insert/delete.
        var visibleOriginalKeys = view.Cells.Keys
            .Where(key => view.DisplayRows.ContainsKey(key.Row) && view.DisplayColumns.ContainsKey(key.Col))
            .ToArray();
        foreach (var original in visibleOriginalKeys) view.Cells.Remove(original);
        var originalRows = view.DisplayRows.ToDictionary(x => x.Value, x => x.Key);
        var originalColumns = view.DisplayColumns.ToDictionary(x => x.Value, x => x.Key);
        foreach (var cell in visibleCells)
        {
            if (!originalRows.TryGetValue(cell.Key.Item2, out var row) || !originalColumns.TryGetValue(cell.Key.Item1, out var col)) continue;
            if (!string.IsNullOrEmpty(cell.Value)) view.Cells[(col, row)] = cell.Value;
        }
    }

    private void RefreshStatus()
    {
        var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
        if (view is null) return;
        _status.Text = $"Showing {view.DisplayRows.Count} rows × {view.DisplayColumns.Count} columns in {view.Name}. Use & for AND filters; row numbers are 1-based workbook rows.";
        _status.Foreground = DashboardStyle.Muted;
    }

    private SheetView? SelectedSheet() => _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);

    private void AddSheet()
    {
        var number = 1;
        string name;
        do name = $"Sheet{number++}"; while (_sheets.ContainsKey(name));

        var tab = new TabItem { Header = name };
        var view = new SheetView { Name = name, Cells = [], Tab = tab, RowCount = 1, ColumnCount = 1 };
        _sheets.Add(name, view);
        _tabs.Items.Add(tab);
        _tabs.SelectedIndex = _tabs.ItemCount - 1;
        RefreshFilteredViews(syncExisting: false);
        _status.Foreground = DashboardStyle.Green;
        _status.Text = $"Added sheet '{name}'. Press Ctrl+S or click Save workbook to write it.";
    }

    private void RemoveSheet()
    {
        var view = SelectedSheet();
        if (view is null) return;
        if (_sheets.Count <= 1)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = "A workbook must contain at least one worksheet.";
            return;
        }

        SyncView(view);
        _sheets.Remove(view.Name);
        if (view.OriginalName is not null) _removedSheetNames.Add(view.OriginalName);
        _tabs.Items.Remove(view.Tab);
        _tabs.SelectedIndex = Math.Clamp(_tabs.SelectedIndex, 0, _tabs.ItemCount - 1);
        RefreshFilteredViews(syncExisting: false);
        _status.Foreground = DashboardStyle.Green;
        _status.Text = $"Removed sheet '{view.Name}'. Press Ctrl+S or click Save workbook to write it.";
    }

    private void RenameSheet()
    {
        var view = SelectedSheet();
        if (view is null) return;
        var input = new TextBox { Text = view.Name, Width = 300 };
        var dialog = new Window { Title = "Rename sheet", Width = 440, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var error = DashboardStyle.Text("", 11, false, DashboardStyle.Red);
        var ok = DashboardStyle.Action("Rename", () =>
        {
            var name = input.Text?.Trim() ?? "";
            if (!IsValidSheetName(name)) { error.Text = "Use 1–31 characters and do not use : \\ / ? * [ ]."; return; }
            if (!name.Equals(view.Name, StringComparison.OrdinalIgnoreCase) && _sheets.ContainsKey(name)) { error.Text = $"A sheet named '{name}' already exists."; return; }
            var oldName = view.Name;
            _sheets.Remove(oldName);
            view.Name = name;
            view.Tab.Header = name;
            _sheets.Add(name, view);
            dialog.Close();
            RefreshFilteredViews(syncExisting: false);
            _status.Foreground = DashboardStyle.Green;
            _status.Text = $"Renamed sheet '{oldName}' to '{name}'. Press Ctrl+S or click Save workbook to write it.";
        }, "primary");
        var cancel = DashboardStyle.Action("Cancel", () => dialog.Close());
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 8, Children =
        {
            DashboardStyle.Text("New sheet name", 14, true), input, error,
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } }
        }};
        dialog.Show(this);
        input.Focus(); input.SelectAll();
    }

    private static bool IsValidSheetName(string name) => name.Length is >= 1 and <= 31 && name.IndexOfAny([':', '\\', '/', '?', '*', '[', ']']) < 0;

    private void SaveWorkbook()
    {
        var temp = _sourcePath + ".saving.xlsx";
        var backup = _sourcePath + ".bak.xlsx";
        var formattingSkipped = false;
        try
        {
            foreach (var view in _sheets.Values)
                if (!view.StructureDirty) SyncView(view);
            using var workbook = new XLWorkbook(_sourcePath);
            WriteWorkbookChanges(workbook, includeEditorStyles: true);
            workbook.SaveAs(temp, true);
        }
        catch (Exception ex) when (IsStylesXmlFailure(ex))
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                using var recoveryWorkbook = new XLWorkbook(_sourcePath);
                WriteWorkbookChanges(recoveryWorkbook, includeEditorStyles: true);
                // Older workbooks can contain legacy font/color ordering that
                // ClosedXML reads but rejects during strict OpenXML validation.
                // Validation-disabled output remains readable by Excel and keeps
                // the workbook contents and editor changes.
                recoveryWorkbook.SaveAs(temp, false);
                formattingSkipped = true;
            }
            catch (Exception recoveryException)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                _status.Foreground = DashboardStyle.Red;
                _status.Text = $"Could not save workbook after style recovery: {recoveryException.Message}";
                return;
            }
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Could not save workbook: {ex.Message}";
            return;
        }

        try
        {
            File.Copy(_sourcePath, backup, true);
            File.Move(temp, _sourcePath, true);
            _removedSheetNames.Clear();
            foreach (var view in _sheets.Values) view.StructureDirty = false;
            _status.Foreground = DashboardStyle.Green;
            _status.Text = formattingSkipped
                ? $"Workbook saved: {_sourcePath}. Backup: {backup}. Legacy styles were preserved using non-strict validation."
                : $"Workbook saved: {_sourcePath}. Backup: {backup}";
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Could not save workbook: {ex.Message}";
        }
    }

    private void WriteWorkbookChanges(XLWorkbook workbook, bool includeEditorStyles)
    {
        foreach (var removedName in _removedSheetNames.ToArray())
        {
            var removed = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(removedName, StringComparison.OrdinalIgnoreCase));
            removed?.Delete();
        }

        foreach (var view in _sheets.Values)
        {
            var sheet = view.OriginalName is not null
                ? workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(view.OriginalName, StringComparison.OrdinalIgnoreCase))
                : null;
            sheet ??= workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(view.Name, StringComparison.OrdinalIgnoreCase));
            sheet ??= workbook.Worksheets.Add(view.Name);
            if (!sheet.Name.Equals(view.Name, StringComparison.Ordinal)) sheet.Name = view.Name;

            sheet.RangeUsed()?.Clear(XLClearOptions.Contents);
            foreach (var cell in view.Cells)
            {
                if (cell.Key.Row >= view.RowCount || cell.Key.Col >= view.ColumnCount) continue;
                var target = sheet.Cell(cell.Key.Row + 1, cell.Key.Col + 1);
                target.Value = cell.Value;
                if (!includeEditorStyles || !view.Styles.TryGetValue(cell.Key, out var style)) continue;
                target.Style.Font.Bold = style.Bold;
                target.Style.Font.Italic = style.Italic;
                if (!string.IsNullOrWhiteSpace(style.TextColor)) target.Style.Font.FontColor = XLColor.FromHtml(style.TextColor);
                if (!string.IsNullOrWhiteSpace(style.Highlight)) target.Style.Fill.BackgroundColor = XLColor.FromHtml(style.Highlight);
            }
        }
    }

    private static bool IsStylesXmlFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current.Message.Contains("styles.xml", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("unexpected child element", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
