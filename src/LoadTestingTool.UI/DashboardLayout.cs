using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ClosedXML.Excel;
using System.Diagnostics;
using System.Text.Json;

namespace LoadTestingTool.UI;

internal static class DashboardStyle
{
    public const string Build = "Dashboard 2026.10.04.17";
    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
    public static IBrush Panel => Brush("#101E2E");
    public static IBrush Muted => Brush("#8EA4BC");
    public static IBrush Line => Brush("#23364B");
    public static IBrush Blue => Brush("#3291FF");
    public static IBrush Green => Brush("#39D39B");
    public static IBrush Red => Brush("#FF7588");
    public static TextBlock Text(string text, double size = 13, bool bold = false, IBrush? color = null) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = color ?? Brush("#E3EDF9"), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.None
    };
    public static Button Action(string label, Action action, string? style = null)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 8, 6), FontSize = 12 };
        if (style is not null) button.Classes.Add(style);
        button.Click += (_, _) => action();
        return button;
    }
    public static Border Card(Control child, Thickness? padding = null) => new()
    {
        Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        Padding = padding ?? new Thickness(14), Child = child
    };
    public static string Status(string value) => value.ToUpperInvariant() switch
    {
        "PASSED" => "Passed", "FAILED" => "Failed", "CANCELLED" => "Cancelled", _ => value
    };
    public static Border Badge(string value)
    {
        var status = Status(value);
        var (foreground, background) = status switch
        {
            "Passed" => ("#39D39B", "#143F35"),
            "Failed" or "Launch failed" or "Stop failed" => ("#FF7588", "#432333"),
            "Running" => ("#72B7FF", "#193B61"),
            "Stopping" or "Cancelled" or "Force stopped" => ("#F6C76B", "#443A25"),
            _ => ("#A6B9CE", "#24364B")
        };
        return new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 4),
            Background = Brush(background), HorizontalAlignment = HorizontalAlignment.Left,
            Child = Text(status, 11, true, Brush(foreground)) };
    }
}

// The table only selects and summarizes an instance. Editable settings live in Configuration.
internal sealed class InstanceRow : Border
{
    private readonly InstanceInfo _item;
    private readonly CheckBox _check;
    private readonly TextBlock _testcases = DashboardStyle.Text("");
    private readonly TextBlock _mode = DashboardStyle.Text("");
    private readonly TextBlock _order = DashboardStyle.Text("");
    private readonly TextBlock _environment = DashboardStyle.Text("");
    private readonly Border _badgeHost = new();
    public InstanceRow(InstanceInfo item, Action<InstanceInfo> select, Action<InstanceInfo> configure, Action<string> log)
    {
        _item = item;
        Padding = new Thickness(8, 12);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        BorderBrush = DashboardStyle.Line;
        BorderThickness = new Thickness(0, 0, 0, 1);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,62,80,82,90,92,110"), MinWidth = 740 };
        _check = new CheckBox { IsChecked = item.Selected, VerticalAlignment = VerticalAlignment.Center, MinWidth = 24 };
        _check.IsCheckedChanged += (_, _) => { item.Selected = _check.IsChecked == true; log($"Instance selection changed. instance={item.Name}; selected={item.Selected}"); select(item); Refresh(); };
        var name = DashboardStyle.Text(item.Name, 12, true);
        ToolTip.SetTip(name, item.WorkbookPath);
        var configureButton = DashboardStyle.Action("Configure", () => configure(item), "primary"); configureButton.Margin = new Thickness(0, 0, 4, 0); configureButton.Padding = new Thickness(8, 5);
        var cells = new Control[] { _check, name, _testcases, _mode, _order, _environment, _badgeHost, configureButton };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); cells[i].Margin = new Thickness(0, 0, 6, 0); grid.Children.Add(cells[i]); }
        Child = grid;
        PointerPressed += (_, _) => select(item);
        Refresh();
    }
    public void Refresh()
    {
        _check.IsChecked = _item.Selected;
        _testcases.Text = _item.TestcaseSelection == "0" ? "All" : _item.TestcaseSelection;
        _mode.Text = _item.ExecutionMode == "loop" ? "Loop" : "Threaded";
        _order.Text = _item.RunScenariosInParallel ? "Parallel" : "Sequential";
        _environment.Text = string.IsNullOrWhiteSpace(_item.EnvironmentSelection) ? "All" : _item.EnvironmentSelection;
        ToolTip.SetTip(_environment, string.Join(", ", _item.EnvironmentOptions));
        _badgeHost.Child = DashboardStyle.Badge(_item.Status);
        Background = _item.Selected ? DashboardStyle.Brush("#173559") : Brushes.Transparent;
    }
    public void SetEnvironmentOptions(IEnumerable<string> options) => Refresh();
}

public sealed partial class MainWindow
{
    private readonly StackPanel _overviewPanel = new() { Spacing = 8 };
    private readonly StackPanel _configurationPanel = new() { Spacing = 12 };
    private readonly StackPanel _artifactPanel = new() { Spacing = 10 };
    private readonly StackPanel _historyPanel = new() { Spacing = 10 };
    private readonly TextBox _historySearch = new() { Watermark = "Filter instance, testcase, step, Data ID, status…", Width = 330 };
    private readonly ComboBox _historyStatus = new() { ItemsSource = new[] { "All statuses", "Passed", "Failed", "Cancelled" }, SelectedIndex = 0, Width = 140 };
    private readonly StackPanel _historyResults = new() { Spacing = 5 };
    private readonly List<HistoryEntry> _historyEntries = [];
    private readonly HashSet<string> _historySelected = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _historyCompare = DashboardStyle.Text("Select two history files to compare.", 12, false, DashboardStyle.Muted);
    private bool _historyRefreshing;
    private readonly StackPanel _eventPanel = new() { Spacing = 3 };
    private readonly TextBlock _selectedTitle = DashboardStyle.Text("Selected instance", 16, true);
    private readonly TextBlock _pageTitle = DashboardStyle.Text("Instances", 18, true);
    private readonly TextBlock _listTitle = DashboardStyle.Text("Test instances", 15, true);
    private readonly TextBlock _footer = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly TextBox _search = new() { Watermark = "Search instances…", Width = 200, FontSize = 12 };
    private readonly Dictionary<string, Button> _navigation = [];
    private readonly Grid _workspace = new() { RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly TabControl _detailTabs = new();
    private Button _runButton = null!;
    private Button _chooseButton = null!;
    private Button _stopButton = null!;
    private Button _forceButton = null!;
    private string _page = "Instances";
    private Grid _body = null!;
    private Control _settingsPage = null!;
    private Control _historyPage = null!;
    private string _detailSignature = "";

    private void BuildLayout()
    {
        Background = DashboardStyle.Brush("#0A1421");
        Foreground = DashboardStyle.Brush("#E3EDF9");
        Width = 1440; Height = 900; MinWidth = 1280; MinHeight = 700;
        Title = $"Load Testing Tool • {DashboardStyle.Build}";
        var sidebar = new DockPanel { Background = DashboardStyle.Brush("#0D1B2C"), LastChildFill = true };
        var brand = new StackPanel { Margin = new Thickness(18, 24, 12, 24), Spacing = 7,
            Children = { DashboardStyle.Text("Load Testing Tool", 14, true), DashboardStyle.Text(DashboardStyle.Build, 10, false, DashboardStyle.Muted) } };
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var sideFooter = new StackPanel { Margin = new Thickness(18, 16), Spacing = 6,
            Children = { DashboardStyle.Text("Local runner", 12, true, DashboardStyle.Green), DashboardStyle.Text("Windows / Avalonia", 11, false, DashboardStyle.Muted) } };
        DockPanel.SetDock(sideFooter, Dock.Bottom); sidebar.Children.Add(sideFooter);
        var links = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0) };
        foreach (var page in new[] { "Instances", "Active runs", "History", "Settings" })
        {
            var button = DashboardStyle.Action(page, () => Navigate(page));
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0); button.Padding = new Thickness(14, 12); button.Classes.Add("nav");
            _navigation[page] = button; links.Children.Add(button);
        }
        sidebar.Children.Add(links);

        var heading = new StackPanel { Spacing = 5, Children = { _pageTitle, DashboardStyle.Text("Manage test instances and inspect execution results", 12, false, DashboardStyle.Muted) } };
        _chooseButton = DashboardStyle.Action("Choose what to run", () => _ = ChooseRunSelectionAsync(_selectedInstance ?? _instances.FirstOrDefault(x => x.Selected)));
        _runButton = DashboardStyle.Action("▶  Run selected", RunSelected, "primary");
        _stopButton = DashboardStyle.Action("Stop selected", () => StopSelected(false));
        _forceButton = DashboardStyle.Action("Force stop", () => StopSelected(true), "danger");
        var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _chooseButton, _runButton, _stopButton, _forceButton } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 16) };
        header.Children.Add(heading); Grid.SetColumn(commands, 1); header.Children.Add(commands);

        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"), Margin = new Thickness(0, 0, 0, 14) };
        var labels = new[] { "Ready", "Running", "Passed", "Failed", "Requests / sec" };
        var values = new[] { _readyValue, _runningValue, _passedValue, _failedValue, _rpsValue };
        var colors = new[] { "#A6B9CE", "#3291FF", "#39D39B", "#FF7588", "#7295FF" };
        for (var i = 0; i < values.Length; i++)
        {
            values[i].FontSize = 25; values[i].FontWeight = FontWeight.SemiBold; values[i].Foreground = DashboardStyle.Brush(colors[i]);
            var card = DashboardStyle.Card(new StackPanel { Spacing = 6, Children = { DashboardStyle.Text(labels[i], 12, false, DashboardStyle.Muted), values[i] } });
            card.Margin = new Thickness(0, 0, i == 4 ? 0 : 10, 0); Grid.SetColumn(card, i); stats.Children.Add(card);
        }
        _workspace.Children.Add(stats);
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12) };
        toolbar.Children.Add(_listTitle);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Children = { _search,
            DashboardStyle.Action("New instance", () => _ = CreateInstanceAsync(), "primary"), DashboardStyle.Action("Refresh", RefreshInstances), DashboardStyle.Action("All", () => { foreach (var item in _instances) item.Selected = true; RefreshRows(); }),
            DashboardStyle.Action("Clear", () => { foreach (var item in _instances) item.Selected = false; RefreshRows(); }) } };
        _search.Margin = new Thickness(0, 0, 8, 6); _search.TextChanged += (_, _) => FilterInstances();
        Grid.SetColumn(tools, 1); toolbar.Children.Add(tools);
        var columnHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,62,80,82,90,92,110"), Margin = new Thickness(8, 8), MinWidth = 740 };
        var columns = new[] { "", "Instance", "Cases", "Mode", "Order", "Env.", "Status", "Actions" };
        for (var i = 0; i < columns.Length; i++) { var text = DashboardStyle.Text(columns[i], 11, false, DashboardStyle.Muted); Grid.SetColumn(text, i); columnHeader.Children.Add(text); }
        _instanceList.Background = Brushes.Transparent; _instanceList.BorderThickness = new Thickness(0);
        _instanceList.ItemTemplate = new FuncDataTemplate<InstanceInfo>((item, _) => item is null ? new Border() : GetOrCreateRow(item));
        _instanceList.SelectionChanged += (_, _) => ShowDetails(_instanceList.SelectedItem as InstanceInfo);
        _instanceList.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Avalonia.Styling.Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Avalonia.Styling.Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) }
        });
        var table = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        table.Children.Add(toolbar); Grid.SetRow(columnHeader, 1); table.Children.Add(columnHeader); Grid.SetRow(_instanceList, 2); table.Children.Add(_instanceList);
        var tableCard = DashboardStyle.Card(table, new Thickness(0));
        var eventHeader = DashboardStyle.Text("Event log  /  selected instance", 13, true);
        var events = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        events.Children.Add(eventHeader);
        var eventScroll = new ScrollViewer { Content = _eventPanel, Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(eventScroll, 1); events.Children.Add(eventScroll);
        var eventCard = DashboardStyle.Card(events); eventCard.Margin = new Thickness(0, 12, 0, 0);
        var left = new Grid { RowDefinitions = new RowDefinitions("3*,2*") };
        left.Children.Add(tableCard); Grid.SetRow(eventCard, 1); left.Children.Add(eventCard);

        _failedOnly.IsCheckedChanged += (_, _) => RefreshAssertionList();
        _environmentFilter.Width = 175; _environmentFilter.Margin = new Thickness(0);
        _environmentFilter.SelectionChanged += (_, _) => RefreshAssertionList();
        _environmentFilter.ItemsSource = new[] { "All environments" }; _environmentFilter.SelectedIndex = 0;
        var assertionActions = new WrapPanel { Children = { DashboardStyle.Action("Select failed", () => { if (_selectedInstance is not null) foreach (var a in _selectedInstance.Assertions.Where(a => a.Result == "FAIL")) a.Selected = true; RefreshAssertionList(); }),
            DashboardStyle.Action("Clear", () => { if (_selectedInstance is not null) foreach (var a in _selectedInstance.Assertions) a.Selected = false; RefreshAssertionList(); }),
            DashboardStyle.Action("Apply actual values", ApplySelectedFixes), DashboardStyle.Action("Export comparison", () => _ = GenerateAssertionComparisonAsync()) } };
        var assertionPanel = new DockPanel { Margin = new Thickness(12) };
        var filters = new StackPanel { Spacing = 10, Children = { _failedOnly, _environmentFilter, assertionActions } };
        DockPanel.SetDock(filters, Dock.Top); assertionPanel.Children.Add(filters);
        assertionPanel.Children.Add(new ScrollViewer { Content = _assertionItems, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _detailTabs.ItemsSource = new[]
        {
            new TabItem { Header = "Overview", Content = Scroll(_overviewPanel) },
            new TabItem { Header = "Assertions", Content = assertionPanel },
            new TabItem { Header = "Config", Content = Scroll(_configurationPanel) },
            new TabItem { Header = "Artifacts", Content = Scroll(_artifactPanel) }
        };
        var selectedHeader = new StackPanel { Spacing = 6, Margin = new Thickness(14), Children = { _selectedTitle,
            DashboardStyle.Text("Select a row to inspect. Tick a box to run.", 11, false, DashboardStyle.Muted) } };
        var right = new DockPanel(); DockPanel.SetDock(selectedHeader, Dock.Top); right.Children.Add(selectedHeader); right.Children.Add(_detailTabs);
        var rightCard = DashboardStyle.Card(right, new Thickness(0)); rightCard.Margin = new Thickness(14, 0, 0, 0);
        _body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,375") };
        _body.Children.Add(left); Grid.SetColumn(rightCard, 1); _body.Children.Add(rightCard);
        Grid.SetRow(_body, 1); _workspace.Children.Add(_body);
        _workspace.Margin = new Thickness(18, 0, 18, 0);
        _settingsPage = CreateSettingsPage(); _historyPage = Scroll(_historyPanel);
        _details.TextWrapping = TextWrapping.Wrap; _details.Foreground = DashboardStyle.Muted; _details.FontSize = 11;
        var diagnostics = new ScrollViewer { Content = _details, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var bottom = new StackPanel { Margin = new Thickness(18, 10), Spacing = 4, Children = { _footer, diagnostics } };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        content.Children.Add(header); Grid.SetRow(_workspace, 1); content.Children.Add(_workspace); Grid.SetRow(bottom, 2); content.Children.Add(bottom);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") };
        root.Children.Add(sidebar); Grid.SetColumn(content, 1); root.Children.Add(content);
        Content = root;
        Navigate("Instances");
    }

    private static ScrollViewer Scroll(Control control) => new() { Content = control, Padding = new Thickness(14), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private void Navigate(string page)
    {
        _page = page; _pageTitle.Text = page;
        foreach (var (name, button) in _navigation) { if (name == page) button.Classes.Add("active"); else button.Classes.Remove("active"); }
        _workspace.Children.Remove(_body); _workspace.Children.Remove(_settingsPage); _workspace.Children.Remove(_historyPage);
        var view = page == "Settings" ? _settingsPage : page == "History" ? _historyPage : _body;
        Grid.SetRow(view, 1); _workspace.Children.Add(view);
        if (page == "History") RefreshHistoryPanel();
        FilterInstances();
    }
    private void FilterInstances()
    {
        var selected = _selectedInstance;
        var query = _search.Text?.Trim() ?? "";
        var visible = _instances.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(x => _page != "Active runs" || x.Process is { HasExited: false }).ToList();
        _instanceList.ItemsSource = visible;
        _listTitle.Text = $"{(_page == "Active runs" ? "Active runs" : "Test instances")} ({visible.Count})";
        _instanceList.SelectedItem = visible.Contains(selected!) ? selected : visible.FirstOrDefault();
    }
    private Control CreateSettingsPage()
    {
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(DashboardStyle.Text("Local runner settings", 20, true));
        panel.Children.Add(DashboardStyle.Text("Paths are resolved relative to the UI executable, not your current folder.", 12, false, DashboardStyle.Muted));
        foreach (var (label, value) in new[] { ("Instances root", _config.InstancesRoot), ("Runner", _config.RunnerDll), ("UI log", _config.UiLogFile), ("Documentation", _config.DocumentationFile) })
            panel.Children.Add(KeyValue(label, Path.GetFullPath(value)));
        panel.Children.Add(DashboardStyle.Text("Files and folders open with your system default applications. Set file associations in your OS settings.", 12, false, DashboardStyle.Muted));
        panel.Children.Add(_enableInternalLog);
        panel.Children.Add(DashboardStyle.Action("Open UI log", () => OpenFile(Path.GetFullPath(_config.UiLogFile), "UI log")));
        panel.Children.Add(DashboardStyle.Action("Open user manual", () => OpenFile(Path.GetFullPath(_config.DocumentationFile), "user manual")));
        panel.Children.Add(DashboardStyle.Text("Edit UI/appsettings.json to change configured paths, then restart the app.", 12, false, DashboardStyle.Muted));
        return Scroll(panel);
    }
    private static Control KeyValue(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("105,*") };
        grid.Children.Add(DashboardStyle.Text(label, 12, false, DashboardStyle.Muted));
        var text = DashboardStyle.Text(value, 12); text.TextWrapping = TextWrapping.Wrap; ToolTip.SetTip(text, value);
        Grid.SetColumn(text, 1); grid.Children.Add(text); return grid;
    }
    private void BuildConfiguration()
    {
        _configurationPanel.Children.Clear(); var item = _selectedInstance;
        if (item is null) { _configurationPanel.Children.Add(DashboardStyle.Text("No instance selected", color: DashboardStyle.Muted)); return; }
        _configurationPanel.Children.Add(DashboardStyle.Text("Run configuration", 16, true));
        if (!string.IsNullOrWhiteSpace(item.SelectionReadError))
        {
            var error = DashboardStyle.Text("Cannot read run choices: " + item.SelectionReadError, 12, false, DashboardStyle.Red);
            error.TextWrapping = TextWrapping.Wrap; _configurationPanel.Children.Add(error); return;
        }
        _configurationPanel.Children.Add(DashboardStyle.Text("Choose named testcases, steps and environments using the picker below.", 12, false, DashboardStyle.Muted));
        _configurationPanel.Children.Add(DashboardStyle.Action("Choose tests, steps, environments…", () => _ = ChooseRunSelectionAsync(), "primary"));
        var draft = new RunSelectionDraft(item.TestcaseOptions, item.TestcaseSelection, item.StepSelectionJson, item.EnvironmentSelection, item.EnvironmentSelectionJson);
        _configurationPanel.Children.Add(KeyValue("Testcases", string.Join(", ", item.TestcaseOptions.Where(c => draft.Cases.Contains(c.Index)).Select(c => $"{c.Index}: {c.Name}"))));
        _configurationPanel.Children.Add(KeyValue("Test steps", string.IsNullOrWhiteSpace(item.StepSelectionJson) ? "All enabled steps" : string.Join(", ", item.TestcaseOptions.Where(c => draft.Cases.Contains(c.Index)).SelectMany(c => c.Steps.Where(s => draft.Steps[c.Index].Contains(s.Name)).Select(s => $"{c.Index}: {s.Name}")))));
        _configurationPanel.Children.Add(KeyValue("Environments", string.IsNullOrWhiteSpace(item.EnvironmentSelection) ? "All available for selected steps" : item.EnvironmentSelection));
        var mode = new ComboBox { ItemsSource = new[] { "Threaded", "Sequential loop" }, SelectedIndex = item.ExecutionMode == "loop" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.SelectionChanged += (_, _) => item.ExecutionMode = mode.SelectedIndex == 1 ? "loop" : "threaded";
        _configurationPanel.Children.Add(Field("Execution mode", mode));
        var order = new ComboBox { ItemsSource = new[] { "Sequential testcases", "Parallel testcases" }, SelectedIndex = item.RunScenariosInParallel ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        order.SelectionChanged += (_, _) => item.RunScenariosInParallel = order.SelectedIndex == 1;
        _configurationPanel.Children.Add(Field("Testcase order", order));
        _configurationPanel.Children.Add(KeyValue("Data IDs", string.IsNullOrWhiteSpace(item.DataIdSelection) ? "All data rows" : item.DataIdSelection));
        _configurationPanel.Children.Add(DashboardStyle.Action("Edit DataEngine workbook", () => _ = EditWorkbookAsync(), "primary"));
        _configurationPanel.Children.Add(DashboardStyle.Action("Open DataEngine workbook", OpenSelectedDataEngine));
    }
    private async Task EditWorkbookAsync()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; return; }
        var editor = new WorkbookEditorWindow(_selectedInstance.WorkbookPath);
        editor.Closed += (_, _) => { if (!string.IsNullOrWhiteSpace(editor.SavedBackup)) { RefreshInstances(); _details.Text = $"Workbook saved. Backup created: {Path.GetFileName(editor.SavedBackup)}"; } };
        editor.Show(this);
        await Task.CompletedTask;
    }

    private void OpenInstanceConfiguration(InstanceInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.SelectionReadError)) { _details.Text = $"Cannot read run choices: {item.SelectionReadError}"; return; }
        var chooser = new RunSelectionWindow(item);
        chooser.Closed += (_, _) => { if (chooser.Result is not null) ApplyRunSelection(item, chooser.Result); };
        chooser.Show(this);
    }

    private void ApplyRunSelection(InstanceInfo item, RunSelectionResult result)
    {
        item.TestcaseSelection = result.Testcases; item.StepSelectionJson = result.StepsJson; item.EnvironmentSelection = result.Environments;
        item.EnvironmentSelectionJson = result.EnvironmentsJson; item.DataIdSelection = result.DataIds; item.ExecutionMode = result.ExecutionMode; item.RunScenariosInParallel = result.ScenariosParallel; item.HasAppliedSelection = true; item.Selected = true;
        try
        {
            var profile = Path.Combine(item.FolderPath, ".run-selection.json");
            var temporary = profile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, profile, overwrite: true);
            _details.Text = $"Selection saved for {item.Name}. Testcases: {result.Testcases}; Data IDs: {(string.IsNullOrWhiteSpace(result.DataIds) ? "all rows" : result.DataIds)}";
        }
        catch (Exception ex) { _details.Text = $"Selection applied, but could not save it: {ex.Message}"; UiLog($"Could not save selection profile for {item.Name}: {ex}"); }
        UiLog($"Run selection applied. instance={item.Name}; testcases={result.Testcases}; steps={result.StepsJson}; environments={result.Environments}; dataIds={result.DataIds}");
        BuildConfiguration(); RefreshRows(); RefreshDashboardDetails(true);
    }

    private async Task<bool> ChooseRunSelectionAsync(InstanceInfo? target = null)
    {
        var item = target ?? _selectedInstance;
        if (item is null) { _details.Text = "Select an instance row first."; return false; }
        if (!string.IsNullOrWhiteSpace(item.SelectionReadError)) { _details.Text = $"Cannot read run choices: {item.SelectionReadError}. Fix the workbook and click Refresh."; return false; }
        try
        {
            var result = await new RunSelectionWindow(item).ShowDialog<RunSelectionResult?>(this);
            if (result is null) return false;
            ApplyRunSelection(item, result); _details.Text = "Selection applied. Click Run selected to execute the checked choices."; return true;
        }
        catch (Exception ex) { _details.Text = $"Could not show run choices: {ex.Message}"; UiLog($"Run selection dialog failed: {ex}"); return false; }
    }

    private static Control Field(string label, Control control) => new StackPanel { Spacing = 6, Children = { DashboardStyle.Text(label, 12, false, DashboardStyle.Muted), control } };

    private void RefreshDashboardDetails(bool force = false)
    {
        var item = _selectedInstance;
        var signature = item is null ? "empty" : $"{item.Name}|{item.Status}|{item.Requests.Count}|{item.Assertions.Count}|{item.LastEvent}|{item.P95Ms}|{item.TestcaseSelection}|{item.EnvironmentSelection}|{item.ExecutionMode}|{item.RunScenariosInParallel}|{item.StepSelectionJson}";
        if (!force && signature == _detailSignature) return;
        _detailSignature = signature;
        _selectedTitle.Text = item?.Name ?? "Selected instance";
        ToolTip.SetTip(_selectedTitle, item?.Name);
        _overviewPanel.Children.Clear();
        if (item is null)
        {
            _overviewPanel.Children.Add(DashboardStyle.Text("Select an instance to inspect its run.", color: DashboardStyle.Muted));
            _eventPanel.Children.Clear(); _eventPanel.Children.Add(DashboardStyle.Text("No selected instance.", color: DashboardStyle.Muted));
            _artifactPanel.Children.Clear(); return;
        }
        _overviewPanel.Children.Add(DashboardStyle.Badge(item.Status));
        _overviewPanel.Children.Add(KeyValue("Environment", string.IsNullOrWhiteSpace(item.EnvironmentSelection) ? "All workbook environments" : item.EnvironmentSelection));
        _overviewPanel.Children.Add(KeyValue("Mode / order", $"{(item.ExecutionMode == "loop" ? "Loop" : "Threaded")} / {(item.RunScenariosInParallel ? "Parallel" : "Sequential")}"));
        _overviewPanel.Children.Add(KeyValue("Started", item.StartedAt?.ToString("HH:mm:ss") ?? "Not started"));
        _overviewPanel.Children.Add(KeyValue("Requests", (item.CompletedRequests ?? item.Requests.Count).ToString()));
        var average = item.AverageMs ?? (item.Requests.Count == 0 ? (double?)null : item.Requests.Average(r => r.DurationMs));
        _overviewPanel.Children.Add(KeyValue("Avg. latency", average is null ? "—" : $"{average:F1} ms"));
        _overviewPanel.Children.Add(KeyValue("p95 latency", item.P95Ms is null ? "Available on completion" : $"{item.P95Ms} ms"));
        _overviewPanel.Children.Add(KeyValue("Throughput", item.RequestsPerSecond is null ? "Available on completion" : $"{item.RequestsPerSecond:F2} requests / sec"));
        _overviewPanel.Children.Add(KeyValue("Assertions", $"{item.Assertions.Count(a => a.Result == "PASS")} passed / {item.Assertions.Count(a => a.Result == "FAIL")} failed"));
        _overviewPanel.Children.Add(new Border { Height = 1, Background = DashboardStyle.Line });
        _overviewPanel.Children.Add(DashboardStyle.Text("Run timeline  /  completed steps", 14, true));
        if (item.Requests.Count == 0) _overviewPanel.Children.Add(DashboardStyle.Text("No completed steps yet. Run this instance to see results.", 12, false, DashboardStyle.Muted));
        foreach (var request in item.Requests.TakeLast(20))
        {
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            top.Children.Add(DashboardStyle.Text($"{request.StepType}  ·  {request.StepName}", 12, true));
            var time = DashboardStyle.Text($"{request.DurationMs} ms", 11, false, DashboardStyle.Muted);
            Grid.SetColumn(time, 1); top.Children.Add(time);
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            bottom.Children.Add(DashboardStyle.Text($"{request.Testcase} / {request.DataId}", 10, false, DashboardStyle.Muted));
            var badge = DashboardStyle.Badge(request.Result == "PASS" ? "Passed" : "Failed");
            Grid.SetColumn(badge, 1); bottom.Children.Add(badge);
            var line = new StackPanel { Spacing = 4, Children = { top, bottom } };
            if (!string.IsNullOrWhiteSpace(request.FailureCategory) && request.FailureCategory != "None")
                line.Children.Add(DashboardStyle.Text($"{request.FailureCategory}: {request.Error}", 11, false, DashboardStyle.Red));
            _overviewPanel.Children.Add(DashboardStyle.Card(line, new Thickness(10)));
        }
        if (item.Requests.Count > 0)
        {
            _overviewPanel.Children.Add(DashboardStyle.Text("Latency trend  /  last 30 requests", 13, true));
            _overviewPanel.Children.Add(new LatencyChart(item.Requests.TakeLast(30).Select(r => (double)r.DurationMs).ToArray()) { Height = 90 });
            _overviewPanel.Children.Add(DashboardStyle.Text($"Range: 0–{item.Requests.TakeLast(30).Max(r => r.DurationMs)} ms / observed completion order", 10, false, DashboardStyle.Muted));
        }
        _eventPanel.Children.Clear();
        foreach (var line in item.EventMessages.TakeLast(100).Reverse())
        {
            var text = DashboardStyle.Text(line, 11, false, DashboardStyle.Muted); text.FontFamily = new FontFamily("monospace"); text.TextWrapping = TextWrapping.Wrap; _eventPanel.Children.Add(text);
        }
        if (item.EventMessages.Count == 0) _eventPanel.Children.Add(DashboardStyle.Text("No events yet. Runner events will appear here.", 12, false, DashboardStyle.Muted));
        RefreshAssertionList(); RefreshArtifacts();
    }
    private void RefreshArtifacts()
    {
        _artifactPanel.Children.Clear(); var item = _selectedInstance;
        if (item is null) return;
        _artifactPanel.Children.Add(DashboardStyle.Text("Workbooks and run artifacts", 16, true));
        _artifactPanel.Children.Add(DashboardStyle.Action("Open DataEngine", OpenSelectedDataEngine));
        _artifactPanel.Children.Add(DashboardStyle.Action("Open latest result workbook", OpenLatestResultWorkbook));
        _artifactPanel.Children.Add(DashboardStyle.Action("Open runner log", OpenLatestInternalLog));
        _artifactPanel.Children.Add(DashboardStyle.Action("Open Templates folder", OpenTemplatesFolder));
        _artifactPanel.Children.Add(DashboardStyle.Action("Open Results folder", () => { Directory.CreateDirectory(item.ResultsFolder); OpenFolder(item.ResultsFolder, "results folder"); }));
        _artifactPanel.Children.Add(DashboardStyle.Action("Open latest comparison", OpenLatestAssertionComparison));
        _artifactPanel.Children.Add(KeyValue("Workbook", item.WorkbookPath));
        _artifactPanel.Children.Add(KeyValue("Results", item.ResultsFolder));
        _artifactPanel.Children.Add(KeyValue("Current run", item.CurrentRunFolder));
    }
    private async void RefreshHistoryPanel()
    {
        if (_historyRefreshing) return;
        _historyRefreshing = true;
        try
        {
            var sources = _instances.Where(x => Directory.Exists(x.HistoryFolder)).Select(x => (Item: x, Paths: Directory.GetFiles(x.HistoryFolder, "Execution_History_*.xlsx", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).ToArray())).ToArray();
            var loaded = await Task.Run(() => sources.SelectMany(source => source.Paths.Select(path => (source.Item, Path: path))).Select(source => { try { return ReadHistoryEntry(source.Item, source.Path); } catch { return null; } }).Where(x => x is not null).Cast<HistoryEntry>().ToList());
            _historyEntries.Clear(); _historyEntries.AddRange(loaded); _historySelected.RemoveWhere(path => !File.Exists(path));
        }
        catch (Exception ex)
        {
            UiLog($"History refresh failed safely: {ex}");
            _historyEntries.Clear();
            _historyPanel.Children.Clear();
            _historyPanel.Children.Add(DashboardStyle.Text("History could not be loaded. The dashboard is still available; fix the file or permission problem and click Refresh.", color: DashboardStyle.Red));
            _historyPanel.Children.Add(DashboardStyle.Text(ex.Message, 11, false, DashboardStyle.Muted));
            return;
        }
        finally { _historyRefreshing = false; }
        _historyPanel.Children.Clear();
        _historyPanel.Children.Add(DashboardStyle.Text("History Explorer", 20, true));
        _historyPanel.Children.Add(DashboardStyle.Text("Browse complete runs, testcase rows, steps, Data IDs, assertions, request/response data, and compare two executions.", 12, false, DashboardStyle.Muted));
        var filterTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _historySearch, _historyStatus, DashboardStyle.Action("Refresh", RefreshHistoryPanel), DashboardStyle.Action("Clear comparison", () => { _historySelected.Clear(); RenderHistoryEntries(); }) } };
        _historySearch.TextChanged -= HistoryFilterChanged; _historySearch.TextChanged += HistoryFilterChanged;
        _historyStatus.SelectionChanged -= HistoryFilterChanged; _historyStatus.SelectionChanged += HistoryFilterChanged;
        _historyPanel.Children.Add(filterTools);
        var compare = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DashboardStyle.Action("Compare selected", CompareHistory), _historyCompare } };
        _historyPanel.Children.Add(DashboardStyle.Card(compare, new Thickness(10)));
        _historyPanel.Children.Add(new Border { Height = 1, Background = DashboardStyle.Line });
        _historyPanel.Children.Add(new ScrollViewer { Content = _historyResults, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        RenderHistoryEntries();
    }

    private void HistoryFilterChanged(object? sender, EventArgs e) => RenderHistoryEntries();

    private void RenderHistoryEntries()
    {
        _historyResults.Children.Clear();
        var query = _historySearch.Text?.Trim() ?? "";
        var status = _historyStatus.SelectedItem?.ToString() ?? "All statuses";
        var visible = _historyEntries.Where(x => status == "All statuses" || x.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(query) || x.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var entry in visible.Take(200))
        {
            var selected = _historySelected.Contains(entry.Path);
            var check = new CheckBox { IsChecked = selected, VerticalAlignment = VerticalAlignment.Top };
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) _historySelected.Add(entry.Path); else _historySelected.Remove(entry.Path); _historyCompare.Text = $"{_historySelected.Count} history file(s) selected; choose exactly two to compare."; };
            var title = DashboardStyle.Text($"{entry.Instance}  ·  {entry.Status}  ·  {entry.RunId}", 13, true); title.TextWrapping = TextWrapping.Wrap;
            var summary = DashboardStyle.Text($"{entry.Started:yyyy-MM-dd HH:mm:ss}  ·  {entry.Requests.Count} step rows  ·  {entry.Passed} passed  ·  {entry.Failed} failed  ·  p95 {entry.P95Ms} ms", 11, false, DashboardStyle.Muted); summary.TextWrapping = TextWrapping.Wrap;
            var open = DashboardStyle.Action("Open workbook", () => OpenExcelFile(entry.Path, "history workbook"));
            var details = new StackPanel { Spacing = 4, Children = { title, summary } };
            foreach (var row in entry.Requests.Take(8)) { var line = DashboardStyle.Text($"{row.Result,-5}  {row.Testcase} / {row.DataId} / {row.Step}  {row.DurationMs} ms  HTTP {row.HttpStatus}", 11, false, row.Result == "FAIL" ? DashboardStyle.Red : DashboardStyle.Muted); line.TextWrapping = TextWrapping.Wrap; details.Children.Add(line); }
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto"), Children = { check, details, open } }; Grid.SetColumn(details, 1); Grid.SetColumn(open, 2);
            _historyResults.Children.Add(DashboardStyle.Card(grid, new Thickness(10)));
        }
        if (visible.Count == 0) _historyResults.Children.Add(DashboardStyle.Text("No history matches the current filters.", color: DashboardStyle.Muted));
        else if (visible.Count > 200) _historyResults.Children.Add(DashboardStyle.Text($"Showing the first 200 of {visible.Count} matches. Use filters to narrow the list.", color: DashboardStyle.Muted));
        _historyCompare.Text = $"{_historySelected.Count} history file(s) selected; choose exactly two to compare.";
    }

    private void CompareHistory()
    {
        var selected = _historyEntries.Where(x => _historySelected.Contains(x.Path)).ToList();
        if (selected.Count != 2) { _historyCompare.Text = "Select exactly two history files first."; return; }
        var left = selected[0]; var right = selected[1];
        var byKey = left.Requests.Concat(right.Requests).GroupBy(x => $"{x.Testcase}|{x.DataId}|{x.Step}", StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key);
        var changed = 0; var requestLines = new List<string> { $"Comparison: {Path.GetFileName(left.Path)}  vs  {Path.GetFileName(right.Path)}" };
        var responseLines = new List<string>(); var configLines = new List<string>(); var assertionLines = new List<string>();
        var variableLines = CompareSheet(left.Path, right.Path, "Variables");
        var dataRowLines = CompareSheet(left.Path, right.Path, "DataEngineRequestRows");
        configLines.AddRange(CompareSheet(left.Path, right.Path, "DataEngineConfig"));
        foreach (var group in byKey)
        {
            var a = group.FirstOrDefault(x => left.Requests.Contains(x)); var b = group.FirstOrDefault(x => right.Requests.Contains(x));
            if (a is null || b is null || a.Result != b.Result || a.HttpStatus != b.HttpStatus || a.DurationMs != b.DurationMs || a.RequestBody != b.RequestBody || a.ResponseBody != b.ResponseBody || a.RequestHeaders != b.RequestHeaders || a.ResponseHeaders != b.ResponseHeaders || a.StepConfig != b.StepConfig)
            { changed++; requestLines.Add($"{group.Key}: {(a?.Result ?? "missing")} / {(b?.Result ?? "missing")}; HTTP {a?.HttpStatus.ToString() ?? "-"} / {b?.HttpStatus.ToString() ?? "-"}; {a?.DurationMs.ToString() ?? "-"} ms / {b?.DurationMs.ToString() ?? "-"} ms"); configLines.Add($"{group.Key}:\nA: {a?.StepConfig ?? "missing"}\nB: {b?.StepConfig ?? "missing"}"); responseLines.Add($"{group.Key}:\nA headers: {a?.ResponseHeaders ?? "missing"}\nB headers: {b?.ResponseHeaders ?? "missing"}\nA body: {a?.ResponseBody ?? "missing"}\nB body: {b?.ResponseBody ?? "missing"}"); }
        }
        var assertionKeys = left.Assertions.Concat(right.Assertions).GroupBy(x => $"{x.Testcase}|{x.DataId}|{x.Step}|{x.Path}", StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key);
        foreach (var group in assertionKeys)
        {
            var a = group.FirstOrDefault(x => left.Assertions.Contains(x)); var b = group.FirstOrDefault(x => right.Assertions.Contains(x));
            if (a is null || b is null || a.Result != b.Result || a.Verb != b.Verb || a.Expected != b.Expected || a.Actual != b.Actual) assertionLines.Add($"{group.Key}: {(a?.Result ?? "missing")} / {(b?.Result ?? "missing")}; verb {a?.Verb ?? "-"} / {b?.Verb ?? "-"}; expected {a?.Expected ?? "-"} / {b?.Expected ?? "-"}; actual {a?.Actual ?? "-"} / {b?.Actual ?? "-"}");
        }
        _historyCompare.Text = $"Comparison complete: {changed} changed row(s).";
        var dialog = new Window { Title = "History comparison", Width = 1100, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var search = new TextBox { Watermark = "Filter differences…", Margin = new Thickness(0, 0, 0, 8) };
        var tabs = new TabControl();
        void AddTab(string header, IEnumerable<string> values)
        {
            var source = values.ToList(); var host = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
            void Render() { host.Children.Clear(); var filtered = source.Where(x => x.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList(); foreach (var line in filtered) host.Children.Add(DiffVisual(line)); if (filtered.Count == 0) host.Children.Add(DashboardStyle.Text("No differences in this tab.", color: DashboardStyle.Muted)); }
            search.TextChanged += (_, _) => Render(); Render();
            tabs.Items.Add(new TabItem { Header = header, Content = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } });
        }
        AddTab("Request", requestLines); AddTab("Response", responseLines); AddTab("Assertions", assertionLines); AddTab("Variables", variableLines); AddTab("DataEngine rows", dataRowLines); AddTab("Config", configLines);
        var close = DashboardStyle.Action("Close", dialog.Close);
        var comparisonGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(18) };
        comparisonGrid.Children.Add(search); Grid.SetRow(tabs, 1); comparisonGrid.Children.Add(tabs); Grid.SetRow(close, 2); comparisonGrid.Children.Add(close); dialog.Content = comparisonGrid;
        dialog.Show(this);
    }

    private static Control DiffVisual(string line)
    {
        var panel = new StackPanel { Spacing = 2 };
        var heading = DashboardStyle.Text(line.Split('\n')[0], 11, true); heading.TextWrapping = TextWrapping.Wrap; panel.Children.Add(heading);
        foreach (var part in line.Split('\n').Skip(1))
        {
            var color = part.StartsWith("A:", StringComparison.OrdinalIgnoreCase) ? DashboardStyle.Red : part.StartsWith("B:", StringComparison.OrdinalIgnoreCase) ? DashboardStyle.Green : DashboardStyle.Muted;
            var value = DashboardStyle.Text(part, 11, false, color); value.TextWrapping = TextWrapping.Wrap; panel.Children.Add(value);
        }
        return DashboardStyle.Card(panel, new Thickness(7));
    }

    private static List<string> CompareSheet(string leftPath, string rightPath, string sheetName)
    {
        try
        {
            using var aBook = new XLWorkbook(leftPath); using var bBook = new XLWorkbook(rightPath);
            var a = aBook.Worksheets.FirstOrDefault(x => x.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase)); var b = bBook.Worksheets.FirstOrDefault(x => x.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
            if (a is null || b is null) return [];
            var aRows = a.RowsUsed().Select(r => string.Join(" | ", r.CellsUsed().Select(c => c.GetString()))).Skip(1).ToHashSet(StringComparer.Ordinal);
            var bRows = b.RowsUsed().Select(r => string.Join(" | ", r.CellsUsed().Select(c => c.GetString()))).Skip(1).ToHashSet(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var row in aRows.Except(bRows)) result.Add($"{sheetName} row changed\nA: {row}\nB: <removed or changed>");
            foreach (var row in bRows.Except(aRows)) result.Add($"{sheetName} row changed\nA: <removed or changed>\nB: {row}");
            return result;
        }
        catch { return []; }
    }

    private static HistoryEntry ReadHistoryEntry(InstanceInfo item, string path)
    {
        using var workbook = new XLWorkbook(path); var summary = workbook.Worksheet("RunSummary"); var metrics = workbook.Worksheet("Metrics"); var requestSheet = workbook.Worksheet("RequestHistory");
        var headers = requestSheet.FirstRowUsed()!.CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        string Cell(IXLRow row, string name) => headers.TryGetValue(name, out var c) ? row.Cell(c).GetString() : "";
        var requests = requestSheet.RowsUsed().Skip(1).Select(row => new HistoryRequest(Cell(row, "testcase"), Cell(row, "dataid"), Cell(row, "stepname"), Cell(row, "requestresult"), long.TryParse(Cell(row, "durationms"), out var d) ? d : 0, int.TryParse(Cell(row, "httpstatus"), out var s) ? s : 0, Cell(row, "requestbody"), Cell(row, "responsebody"), Cell(row, "requestheaders"), Cell(row, "responseheaders"), Cell(row, "stepconfig"))).ToList();
        var assertionSheet = workbook.Worksheet("AssertionHistory"); var ah = assertionSheet.FirstRowUsed()!.CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase); string ACell(IXLRow row, string name) => ah.TryGetValue(name, out var c) ? row.Cell(c).GetString() : "";
        var assertions = assertionSheet.RowsUsed().Skip(1).Select(row => new HistoryAssertion(ACell(row, "testcase"), ACell(row, "dataid"), ACell(row, "stepname"), ACell(row, "responsepath"), ACell(row, "assertionverb"), ACell(row, "expectedvalue"), ACell(row, "actualvalue"), ACell(row, "result"))).ToList();
        var result = summary.Cell(2, 16).GetString(); var runId = summary.Cell(2, 1).GetString(); var started = DateTime.TryParse(summary.Cell(2, 2).GetString(), out var date) ? date : File.GetLastWriteTime(path); var p95 = metrics.RowsUsed().FirstOrDefault(r => r.Cell(1).GetString().Equals("p95DurationMs", StringComparison.OrdinalIgnoreCase))?.Cell(2).GetString() ?? "0";
        return new HistoryEntry(path, item.Name, result, runId, started, requests, assertions, requests.Count(x => x.Result == "PASS"), requests.Count(x => x.Result == "FAIL"), p95, string.Join(" ", item.Name, result, runId, requests.Select(x => $"{x.Testcase} {x.DataId} {x.Step}")));
    }
    private static void RecordDashboardEvent(InstanceInfo item, JsonElement e)
    {
        string Str(string name) => e.TryGetProperty(name, out var value) ? value.ToString() : "";
        long Long(string name) => e.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
        double? Number(string name) => e.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
        var type = Str("eventType");
        if (type == "request-completed")
        {
            item.Requests.Add(new DashboardRequest(Str("stepName"), Str("stepType"), Str("testcase"), Str("dataId"), Str("result"), Long("durationMs"), Str("failureCategory"), Str("errorMessage")));
            item.EventMessages.Add($"{DateTime.Now:HH:mm:ss}  {Str("result"),-5}  {Str("stepType")} / {Str("stepName")} / {Long("durationMs")} ms / {Str("failureCategory")}");
        }
        else if (type == "run-completed")
        {
            item.CompletedRequests = (int)Long("totalRequests"); item.AverageMs = Number("averageDurationMs");
            item.P95Ms = Long("p95DurationMs"); item.RequestsPerSecond = Number("requestsPerSecond"); item.CompletedAt = DateTimeOffset.Now;
            item.EventMessages.Add($"{DateTime.Now:HH:mm:ss}  RUN {Str("result")} / {item.CompletedRequests} requests / p95 {item.P95Ms} ms");
        }
        if (item.EventMessages.Count > 200) item.EventMessages.RemoveRange(0, item.EventMessages.Count - 200);
    }
}

public sealed record DashboardRequest(string StepName, string StepType, string Testcase, string DataId, string Result, long DurationMs, string FailureCategory, string Error);
internal sealed record HistoryRequest(string Testcase, string DataId, string Step, string Result, long DurationMs, int HttpStatus, string RequestBody, string ResponseBody, string RequestHeaders, string ResponseHeaders, string StepConfig);
internal sealed record HistoryAssertion(string Testcase, string DataId, string Step, string Path, string Verb, string Expected, string Actual, string Result);
internal sealed record HistoryEntry(string Path, string Instance, string Status, string RunId, DateTime Started, IReadOnlyList<HistoryRequest> Requests, IReadOnlyList<HistoryAssertion> Assertions, int Passed, int Failed, string P95Ms, string SearchText);
internal sealed class LatencyChart(double[] values) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width; var height = Bounds.Height;
        var pen = new Pen(DashboardStyle.Line, 1);
        for (var i = 0; i < 3; i++) context.DrawLine(pen, new Point(0, height * i / 2), new Point(width, height * i / 2));
        if (values.Length == 0) return;
        var max = Math.Max(1, values.Max());
        for (var i = 0; i < values.Length; i++)
        {
            var x = values.Length == 1 ? width / 2 : width * i / (values.Length - 1);
            var y = height - 5 - (height - 10) * values[i] / max;
            context.DrawEllipse(DashboardStyle.Blue, null, new Point(x, y), 2, 2);
            if (i > 0) context.DrawLine(new Pen(DashboardStyle.Blue, 2), new Point(width * (i - 1) / (values.Length - 1), height - 5 - (height - 10) * values[i - 1] / max), new Point(x, y));
        }
    }
}
