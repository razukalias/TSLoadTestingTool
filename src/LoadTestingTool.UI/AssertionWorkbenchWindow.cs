using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClosedXML.Excel;

namespace LoadTestingTool.UI;

public sealed class AssertionWorkbenchWindow : Window
{
    private readonly string _workbookPath;
    private readonly IReadOnlyList<AssertionInfo> _assertions;
    private readonly Action<string> _status;
    private readonly Action _refresh;
    private readonly Action<string> _log;
    private readonly TextBox _search = new() { Watermark = "Search testcase, step, header, expected, actual…" };
    private readonly CheckBox _failedOnly = new() { Content = "Failed only", IsChecked = true };
    private readonly ListBox _list = new();
    private readonly TextBox _header = new() { MinHeight = 36 };
    private readonly TextBox _expected = new() { MinHeight = 36 };
    private readonly TextBox _actual = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
    private readonly TextBox _failure = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 105 };
    private readonly TextBox _meaning = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72 };
    private readonly TextBlock _selectionTitle = new();
    private readonly TextBlock _message = new();
    private readonly Dictionary<AssertionInfo, Draft> _drafts = new();
    private AssertionInfo? _selected;

    private sealed record Draft(string Header, string Expected);

    public AssertionWorkbenchWindow(string workbookPath, IReadOnlyList<AssertionInfo> assertions, Action<string> status, Action refresh, Action<string> log)
    {
        _workbookPath = workbookPath; _assertions = assertions; _status = status; _refresh = refresh; _log = log;
        Title = "Assertion Workbench"; Width = 1320; Height = 780; MinWidth = 980; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        foreach (var assertion in assertions) _drafts[assertion] = new Draft(CompleteHeader(assertion), AssertionCell(assertion));
        _search.TextChanged += (_, _) => RenderList();
        _failedOnly.IsCheckedChanged += (_, _) => RenderList();
        _list.SelectionChanged += (_, _) => Select(_list.SelectedItem as AssertionInfo);
        _message.Foreground = DashboardStyle.Red; _message.TextWrapping = TextWrapping.Wrap;
        Content = BuildView(); RenderList();
    }

    private Control BuildView()
    {
        var title = DashboardStyle.Text("Assertion Workbench", 23, true);
        var subtitle = DashboardStyle.Text("Review and edit every assertion field in one place. Changes are backed up before saving.", 12, false, DashboardStyle.Muted);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _search, _failedOnly, DashboardStyle.Action("Help", ShowHelp), DashboardStyle.Action("Refresh", RenderList), DashboardStyle.Action("Save all changes", SaveAll, "primary") } };
        _search.Width = 430;
        var header = new StackPanel { Spacing = 4, Children = { title, subtitle, toolbar } };
        var left = new DockPanel { Margin = new Thickness(18, 0, 10, 18) };
        var listTitle = DashboardStyle.Text("Assertion fields", 15, true); listTitle.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(listTitle, Dock.Top); left.Children.Add(listTitle);
        _list.Background = Brushes.Transparent; _list.BorderThickness = new Thickness(0);
        _list.ItemTemplate = new FuncDataTemplate<AssertionInfo>((item, _) => AssertionListItem(item));
        left.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var detail = new StackPanel { Spacing = 10, Margin = new Thickness(10, 0, 18, 18) };
        detail.Children.Add(_selectionTitle); detail.Children.Add(DashboardStyle.Text("Response header", 12, false, DashboardStyle.Muted)); detail.Children.Add(_header);
        detail.Children.Add(DashboardStyle.Text("Assertion cell / expected value", 12, false, DashboardStyle.Muted)); detail.Children.Add(_expected);
        detail.Children.Add(DashboardStyle.Text("Actual response value", 12, false, DashboardStyle.Muted)); detail.Children.Add(_actual);
        detail.Children.Add(DashboardStyle.Text("Failure details · selectable and copyable", 12, false, DashboardStyle.Muted)); detail.Children.Add(_failure);
        detail.Children.Add(DashboardStyle.Text("Assertion type and meaning", 12, false, DashboardStyle.Muted)); detail.Children.Add(_meaning);
        detail.Children.Add(DashboardStyle.Text("Type { for assertion verbs or < for dynamic references. A cell-level verb overrides a header-level verb.", 11, false, DashboardStyle.Muted));
        detail.Children.Add(_message);
        _header.TextChanged += (_, _) => CaptureDraft(); _expected.TextChanged += (_, _) => CaptureDraft();
        var detailCard = DashboardStyle.Card(new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new Thickness(14));
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("1.15*,1.5*"), Margin = new Thickness(0, 18, 0, 0) };
        var listCard = DashboardStyle.Card(left, new Thickness(14)); body.Children.Add(listCard); Grid.SetColumn(detailCard, 1); body.Children.Add(detailCard);
        var close = DashboardStyle.Action("Close", Close);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
        root.Children.Add(header); Grid.SetRow(body, 1); root.Children.Add(body); Grid.SetRow(close, 2); close.HorizontalAlignment = HorizontalAlignment.Right; root.Children.Add(close);
        return root;
    }

    private Control AssertionListItem(AssertionInfo assertion)
    {
        if (assertion is null) return DashboardStyle.Text("No assertion selected.", 11, false, DashboardStyle.Muted);
        if (!_drafts.TryGetValue(assertion, out var draft))
        {
            draft = new Draft(CompleteHeader(assertion), AssertionCell(assertion));
            _drafts[assertion] = draft;
        }
        var result = DashboardStyle.Badge(assertion.Result == "PASS" ? "Passed" : "Failed");
        var title = DashboardStyle.Text($"{assertion.Testcase}  /  {assertion.StepName}", 12, true);
        var path = DashboardStyle.Text(draft.Header, 11, false, DashboardStyle.Muted); path.TextWrapping = TextWrapping.Wrap;
        var values = DashboardStyle.Text($"Expected: {draft.Expected}\nActual: {assertion.ActualValue}", 11); values.TextWrapping = TextWrapping.Wrap;
        var type = string.IsNullOrWhiteSpace(assertion.AssertionType) ? AssertionSemanticsUi.Type(assertion.AssertionVerb, assertion.ActualValue, assertion.FailureMessage) : assertion.AssertionType;
        var meaning = string.IsNullOrWhiteSpace(assertion.AssertionMeaning) ? AssertionSemanticsUi.Meaning(assertion.AssertionVerb, assertion.ActualValue, assertion.FailureMessage) : assertion.AssertionMeaning;
        var explanation = DashboardStyle.Text($"Type: {type}\n{meaning}", 11, false, DashboardStyle.Muted); explanation.TextWrapping = TextWrapping.Wrap;
        return new Border { Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 6), BorderBrush = DashboardStyle.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = new StackPanel { Spacing = 4, Children = { result, title, path, explanation, values } } };
    }

    private void RenderList()
    {
        CaptureDraft();
        var terms = (_search.Text ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = _assertions.Where(a => !_failedOnly.IsChecked.GetValueOrDefault() || a.Result == "FAIL")
            .Where(a => terms.All(term => SearchText(a).Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        _list.ItemsSource = values;
        if (_selected is not null && values.Contains(_selected)) _list.SelectedItem = _selected;
        else if (values.Count > 0) _list.SelectedIndex = 0;
        else ClearSelection();
        _message.Text = $"Showing {values.Count} of {_assertions.Count} assertion field(s).";
    }

    private string SearchText(AssertionInfo a)
    {
        var header = _drafts.TryGetValue(a, out var draft) ? draft.Header : CompleteHeader(a);
        var expected = _drafts.TryGetValue(a, out draft) ? draft.Expected : AssertionCell(a);
        return $"{a.Environment} {a.Testcase} {a.DataId} {a.StepName} {header} {expected} {a.ActualValue} {a.Result} {a.FailureMessage}";
    }

    private void Select(AssertionInfo? assertion)
    {
        CaptureDraft(); _selected = assertion;
        if (assertion is null) { ClearSelection(); return; }
        var draft = _drafts[assertion];
        _selectionTitle.Text = $"{assertion.Result}  ·  {assertion.Environment}  ·  {assertion.Testcase} / {assertion.StepName} / {assertion.DataId}";
        _selectionTitle.FontSize = 16; _selectionTitle.FontWeight = FontWeight.SemiBold; _selectionTitle.Foreground = assertion.Result == "FAIL" ? DashboardStyle.Red : DashboardStyle.Green;
        _header.Text = draft.Header; _expected.Text = draft.Expected; _actual.Text = assertion.ActualValue; _failure.Text = string.IsNullOrWhiteSpace(assertion.FailureMessage) ? "No failure message." : assertion.FailureMessage;
        var type = string.IsNullOrWhiteSpace(assertion.AssertionType) ? AssertionSemanticsUi.Type(assertion.AssertionVerb, assertion.ActualValue, assertion.FailureMessage) : assertion.AssertionType;
        var meaning = string.IsNullOrWhiteSpace(assertion.AssertionMeaning) ? AssertionSemanticsUi.Meaning(assertion.AssertionVerb, assertion.ActualValue, assertion.FailureMessage) : assertion.AssertionMeaning;
        _meaning.Text = $"Type: {type}\n{meaning}";
    }

    private void ClearSelection() { _selected = null; _selectionTitle.Text = "Select an assertion field"; _header.Text = ""; _expected.Text = ""; _actual.Text = ""; _failure.Text = ""; _meaning.Text = ""; }
    private void CaptureDraft() { if (_selected is not null && _drafts.ContainsKey(_selected)) _drafts[_selected] = new Draft(_header.Text ?? "", _expected.Text ?? ""); }

    private void SaveAll()
    {
        CaptureDraft();
        var changes = _assertions.Where(a => !string.Equals(CompleteHeader(a), _drafts[a].Header, StringComparison.Ordinal) || !string.Equals(AssertionCell(a), _drafts[a].Expected, StringComparison.Ordinal)).ToList();
        if (changes.Count == 0) { _message.Text = "No assertion changes to save."; return; }
        try
        {
            var backup = UniqueBackupPath(_workbookPath); File.Copy(_workbookPath, backup);
            using var workbook = new XLWorkbook(_workbookPath); var sheet = workbook.Worksheet("response");
            foreach (var assertion in changes)
            {
                var draft = _drafts[assertion];
                if (assertion.ExpectedValueColumn <= 0 || assertion.ExcelRowNumber <= 0) continue;
                sheet.Cell(1, assertion.ExpectedValueColumn).Value = draft.Header;
                sheet.Cell(assertion.ExcelRowNumber, assertion.ExpectedValueColumn).Value = draft.Expected;
                assertion.ResponseHeader = draft.Header; assertion.ResponsePath = HeaderPath(draft.Header, assertion.StepName); assertion.AssertionVerb = HeaderVerb(draft.Header, draft.Expected); assertion.ExpectedValue = HeaderExpected(draft.Expected);
            }
            workbook.Save();
            _status($"Saved {changes.Count} assertion change(s). Backup: {Path.GetFileName(backup)}"); _log($"Assertion Workbench saved. changes={changes.Count}; backup={backup}"); _message.Foreground = DashboardStyle.Green; _message.Text = $"Saved {changes.Count} change(s). Backup created: {Path.GetFileName(backup)}"; RenderList(); _refresh();
        }
        catch (Exception ex) { _message.Foreground = DashboardStyle.Red; _message.Text = $"Could not save assertion changes: {ex.Message}"; _log($"Assertion Workbench save failed: {ex}"); }
    }

    private void ShowHelp() => ContextHelpWindow.Show(this, "Assertion Workbench help", "The left side lists all assertion fields from the current run. Use the search box with & to require multiple terms, for example status&failed. Select a row and edit its complete response header or assertion cell on the right. Header-level verbs can be written as {contains}Step.field; a cell-level verb such as {eq}200 overrides it. Failure details are read-only and selectable for copying. Save all changes creates a workbook backup before updating the response sheet.");

    private static string CompleteHeader(AssertionInfo a) => string.IsNullOrWhiteSpace(a.ResponseHeader) ? $"{a.StepName}.{a.ResponsePath}" : a.ResponseHeader;
    private static string AssertionCell(AssertionInfo a) => a.ExpectedValue;
    private static string HeaderVerb(string header, string cell) { var value = cell.TrimStart(); if (value.StartsWith("{", StringComparison.Ordinal) && value.IndexOf('}') > 1) return value[1..value.IndexOf('}')].ToLowerInvariant(); value = header.TrimStart(); return value.StartsWith("{", StringComparison.Ordinal) && value.IndexOf('}') > 1 ? value[1..value.IndexOf('}')].ToLowerInvariant() : "eq"; }
    private static string HeaderExpected(string cell) { var value = cell.Trim(); return value.StartsWith("{", StringComparison.Ordinal) && value.IndexOf('}') > 1 ? value[(value.IndexOf('}') + 1)..] : value; }
    private static string HeaderPath(string header, string fallbackStep) { var value = header.Trim(); if (value.StartsWith("{", StringComparison.Ordinal) && value.IndexOf('}') > 1) value = value[(value.IndexOf('}') + 1)..].Trim(); return value.StartsWith(fallbackStep + ".", StringComparison.OrdinalIgnoreCase) ? value[(fallbackStep.Length + 1)..] : value; }
    private static string UniqueBackupPath(string path) { var candidate = $"{path}.backup.{DateTime.Now:yyyyMMdd_HHmmssfff}.xlsx"; var i = 1; while (File.Exists(candidate)) candidate = $"{path}.backup.{DateTime.Now:yyyyMMdd_HHmmssfff}_{i++}.xlsx"; return candidate; }
}
