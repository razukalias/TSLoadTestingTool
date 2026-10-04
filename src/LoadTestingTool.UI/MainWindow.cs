using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using ClosedXML.Excel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;

namespace LoadTestingTool.UI;

public sealed class InstanceInfo
{
    public bool Selected { get; set; }
    public string Name { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string WorkbookPath { get; init; } = string.Empty;
    public string Status { get; set; } = "Ready";
    public string LastEvent { get; set; } = "No results yet";
    public string TestcaseSelection { get; set; } = "0";
    public string ExecutionMode { get; set; } = "threaded";
    public bool RunScenariosInParallel { get; set; }
    public string EnvironmentSelection { get; set; } = "";
    public string DataIdSelection { get; set; } = "";
    public Dictionary<int, List<string>> DataIdOptions { get; } = [];
    public List<string> EnvironmentOptions { get; } = [];
    public Process? Process { get; set; }
    public long EventPosition { get; set; }
    public int EventLineIndex { get; set; }
    public Dictionary<string, int> EventLinePositions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string CurrentRunFolder { get; set; } = string.Empty;
    public string CurrentRunId { get; set; } = string.Empty;
    public List<AssertionInfo> Assertions { get; } = [];
    public string ResultsFolder => Path.Combine(FolderPath, "Results");
    public string HistoryFolder => Path.Combine(FolderPath, "History");
    public string TemplatesFolder => Path.Combine(FolderPath, "Templates");
    public string LogsFolder => Path.Combine(FolderPath, "Logs");
    public string InstanceId => Name;
}

public sealed class AssertionInfo
{
    public bool Selected { get; set; }
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string ResponsePath { get; init; } = string.Empty;
    public string AssertionVerb { get; init; } = string.Empty;
    public string ExpectedValue { get; init; } = string.Empty;
    public string ActualValue { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public int ExcelRowNumber { get; init; }
    public int ExpectedValueColumn { get; init; }
}

internal sealed record AssertionSnapshot(
    string Environment,
    string Testcase,
    string DataId,
    string StepName,
    string ResponsePath,
    string AssertionVerb,
    string ExpectedValue,
    string ActualValue,
    string Result,
    string FailureMessage);

internal sealed record AssertionComparisonKey(
    string Testcase,
    string DataId,
    string StepName,
    string ResponsePath,
    string AssertionVerb);

internal sealed class InstanceRow : Border
{
    private readonly InstanceInfo _item;
    private readonly CheckBox _check;
    private readonly TextBlock _status;
    private readonly Border _statusBadge;
    private readonly TextBlock _last;
    private readonly TextBox _testcases;
    private readonly ComboBox _executionMode;
    private readonly ComboBox _scenarioOrder;
    private readonly Button _dataIdsButton;
    private readonly WrapPanel _environmentChecks = new() { Orientation = Orientation.Horizontal, MinWidth = 180, MaxWidth = 320 };
    private readonly Action<InstanceInfo> _select;
    private readonly Action<string> _log;

    public InstanceRow(InstanceInfo item, Action<InstanceInfo> select, Action<string> log)
    {
        _item = item;
        _select = select;
        _log = log;
        Padding = new Thickness(8, 10);
        BorderBrush = new SolidColorBrush(Color.Parse("#EAECF0"));
        BorderThickness = new Thickness(0, 0, 0, 1);

        _check = new CheckBox { IsChecked = item.Selected, VerticalAlignment = VerticalAlignment.Center };
        _check.IsCheckedChanged += (_, _) => { item.Selected = _check.IsChecked == true; _log($"Instance selection changed. instance={item.Name}; selected={item.Selected}"); };
        var name = new TextBlock { Text = item.Name, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#172B4D")), TextTrimming = TextTrimming.CharacterEllipsis };
        var path = new TextBlock { Text = item.WorkbookPath, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#667085")), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
        var description = new TextBlock { Text = "Workbook-backed load test instance", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#98A2B3")), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        var namePanel = new StackPanel { Children = { name, description, path } };
        _testcases = new TextBox { Text = item.TestcaseSelection, Width = 92, Watermark = "0 or 1,3", VerticalContentAlignment = VerticalAlignment.Center };
        _testcases.LostFocus += (_, _) => { item.TestcaseSelection = string.IsNullOrWhiteSpace(_testcases.Text) ? "0" : _testcases.Text.Trim(); _log($"Testcase selection changed. instance={item.Name}; selection={item.TestcaseSelection}"); };
        _testcases.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { item.TestcaseSelection = string.IsNullOrWhiteSpace(_testcases.Text) ? "0" : _testcases.Text.Trim(); _log($"Testcase selection changed. instance={item.Name}; selection={item.TestcaseSelection}; source=enter"); e.Handled = true; } };
        _executionMode = new ComboBox { Width = 112, ItemsSource = new[] { "Threaded", "Sequential loop" }, SelectedIndex = item.ExecutionMode.Equals("loop", StringComparison.OrdinalIgnoreCase) ? 1 : 0, VerticalContentAlignment = VerticalAlignment.Center };
        _executionMode.SelectionChanged += (_, _) => { item.ExecutionMode = _executionMode.SelectedIndex == 1 ? "loop" : "threaded"; _log($"Execution mode changed. instance={item.Name}; mode={item.ExecutionMode}"); };
        _scenarioOrder = new ComboBox { Width = 140, ItemsSource = new[] { "Sequential testcases", "Parallel testcases" }, SelectedIndex = item.RunScenariosInParallel ? 1 : 0, VerticalContentAlignment = VerticalAlignment.Center };
        _scenarioOrder.SelectionChanged += (_, _) => { item.RunScenariosInParallel = _scenarioOrder.SelectedIndex == 1; _log($"Testcase order changed. instance={item.Name}; parallel={item.RunScenariosInParallel}"); };
        _dataIdsButton = new Button { Content = string.IsNullOrWhiteSpace(item.DataIdSelection) ? "Data IDs" : "Data IDs ✓", VerticalContentAlignment = VerticalAlignment.Center };
        _dataIdsButton.Click += async (_, _) => await ConfigureDataIdsAsync();
        namePanel.Children.Add(_dataIdsButton);
        _status = new TextBlock { Text = item.Status, FontWeight = FontWeight.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
        _last = new TextBlock { Text = item.LastEvent, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#667085")), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(30, 7, 0, 0) };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,2.4*,Auto,Auto,Auto,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        _statusBadge = new Border { Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(12), Child = _status, HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(_check); Grid.SetColumn(_check, 0);
        grid.Children.Add(namePanel); Grid.SetColumn(namePanel, 1);
        grid.Children.Add(_testcases); Grid.SetColumn(_testcases, 2);
        grid.Children.Add(_executionMode); Grid.SetColumn(_executionMode, 3);
        grid.Children.Add(_scenarioOrder); Grid.SetColumn(_scenarioOrder, 4);
        grid.Children.Add(_environmentChecks); Grid.SetColumn(_environmentChecks, 5);
        grid.Children.Add(_statusBadge); Grid.SetColumn(_statusBadge, 6);
        grid.Children.Add(_last); Grid.SetRow(_last, 1); Grid.SetColumn(_last, 1); Grid.SetColumnSpan(_last, 6);
        Child = grid;
        PointerPressed += (_, _) => _select(_item);
        Refresh();
    }

    public void Refresh()
    {
        _check.IsChecked = _item.Selected;
        if (_testcases.IsFocused is false) _testcases.Text = _item.TestcaseSelection;
        if (_executionMode.IsFocused is false) _executionMode.SelectedIndex = _item.ExecutionMode.Equals("loop", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (_scenarioOrder.IsFocused is false) _scenarioOrder.SelectedIndex = _item.RunScenariosInParallel ? 1 : 0;
        _status.Text = _item.Status;
        _last.Text = _item.LastEvent;
        _dataIdsButton.Content = string.IsNullOrWhiteSpace(_item.DataIdSelection) ? "Data IDs" : "Data IDs ✓";
        Background = _item.Selected ? new SolidColorBrush(Color.Parse("#EEF4FF")) : Brushes.White;
        _status.Foreground = _item.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.Parse("#067647"))
            : _item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.Parse("#B42318"))
                : _item.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) || _item.Status.Equals("Stopping", StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.Parse("#315BD8"))
                    : new SolidColorBrush(Color.Parse("#667085"));
        _statusBadge.Background = _item.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.Parse("#ECFDF3"))
            : _item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.Parse("#FEF3F2"))
                : _item.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) || _item.Status.Equals("Stopping", StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.Parse("#EEF4FF"))
                    : new SolidColorBrush(Color.Parse("#F2F4F7"));
    }

    private async Task ConfigureDataIdsAsync()
    {
        var owner = VisualRoot as Window;
        if (owner is null) { _log($"Data-ID dialog could not open. instance={_item.Name}; reason=no-owner-window"); return; }
        _log($"Data-ID dialog opened. instance={_item.Name}; currentSelection={_item.DataIdSelection}");
        var dialog = new DataIdSelectionWindow(_item.DataIdOptions, _item.DataIdSelection, _log);
        var selected = await dialog.ShowDialog<string?>(owner);
        if (selected is null) { _log($"Data-ID dialog cancelled. instance={_item.Name}"); return; }
        _item.DataIdSelection = selected;
        _dataIdsButton.Content = string.IsNullOrWhiteSpace(selected) ? "Configure data IDs..." : "Data IDs selected";
        _log($"Data-ID selection applied. instance={_item.Name}; selection={selected}");
    }

    public void SetEnvironmentOptions(IEnumerable<string> options)
    {
        _environmentChecks.Children.Clear();
        var selected = _item.EnvironmentSelection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var environment in options.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var check = new CheckBox { Content = environment, IsChecked = selected.Contains(environment, StringComparer.OrdinalIgnoreCase), Margin = new Thickness(3, 0) };
            check.IsCheckedChanged += (_, _) => { _item.EnvironmentSelection = string.Join(",", _environmentChecks.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => x.Content?.ToString())); _log($"Environment selection changed. instance={_item.Name}; environments={_item.EnvironmentSelection}"); };
            _environmentChecks.Children.Add(check);
        }
    }
}

public sealed class MainWindow : Window
{
    private readonly UiConfig _config;
    private readonly ObservableCollection<InstanceInfo> _instances = [];
    private readonly Dictionary<string, InstanceRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ListBox _instanceList = new();
    private readonly TextBlock _details = new();
    private readonly TextBlock _overviewDetails = new();
    private readonly StackPanel _assertionItems = new() { Orientation = Orientation.Vertical };
    private readonly CheckBox _failedOnly = new() { Content = "Show failed only", IsChecked = true };
    private readonly ComboBox _environmentFilter = new() { Width = 150, Margin = new Thickness(8, 6, 6, 0) };
    private readonly CheckBox _enableInternalLog = new() { Content = "Enable internal log", IsChecked = true, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _readyValue = new();
    private readonly TextBlock _runningValue = new();
    private readonly TextBlock _passedValue = new();
    private readonly TextBlock _failedValue = new();
    private readonly TextBlock _rpsValue = new();
    private InstanceInfo? _selectedInstance;
    private readonly DispatcherTimer _timer;

    public MainWindow()
    {
        Title = "LoadTestingTool Instance Manager";
        Width = 1250;
        Height = 700;
        _config = LoadConfig();
        UiLog("UI started.");
        BuildLayout();
        RefreshInstances();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => PollEvents();
        _timer.Start();
        UiLog($"UI initialized. instancesRoot={Path.GetFullPath(_config.InstancesRoot)}; uiLog={Path.GetFullPath(_config.UiLogFile)}; pollIntervalMs=500");
    }

    private void BuildLayout()
    {
        Background = new SolidColorBrush(Color.Parse("#F4F7FB"));

        var refresh = new Button { Content = "Refresh", Margin = new Thickness(0, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
        refresh.Click += (_, _) => { UiLog("UI action: refresh instances clicked."); RefreshInstances(); };
        var selectAll = new Button { Content = "Select all", Margin = new Thickness(0, 0, 8, 0) };
        selectAll.Click += (_, _) => { UiLog($"UI action: select all clicked. instances={_instances.Count}"); foreach (var item in _instances) item.Selected = true; RefreshRows(); };
        var clear = new Button { Content = "Clear selection", Margin = new Thickness(0, 0, 8, 0) };
        clear.Click += (_, _) => { UiLog($"UI action: clear selection clicked. instances={_instances.Count}"); foreach (var item in _instances) item.Selected = false; RefreshRows(); };
        var run = new Button { Content = "▶  Run selected", Background = new SolidColorBrush(Color.Parse("#315BD8")), Foreground = Brushes.White, Padding = new Thickness(14, 8), FontWeight = FontWeight.Bold };
        run.Click += (_, _) => { UiLog("UI action: run selected clicked."); RunSelected(); };
        var stop = new Button { Content = "■  Stop selected", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 8) };
        stop.Click += (_, _) => { UiLog("UI action: stop selected clicked."); StopSelected(false); };
        var forceStop = new Button { Content = "⚠  Force stop", Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush(Color.Parse("#FFF1F2")), Foreground = new SolidColorBrush(Color.Parse("#B42318")), Padding = new Thickness(12, 8) };
        forceStop.Click += (_, _) => { UiLog("UI action: force stop clicked."); StopSelected(true); };
        var manual = new Button { Content = "Open user manual", Margin = new Thickness(8, 0, 0, 0) };
        manual.Click += (_, _) => { UiLog("UI action: open user manual clicked."); OpenConfiguredFile(Path.GetFullPath(_config.DocumentationFile), "user manual", _config.DocumentationApplicationPath); };
        var log = new Button { Content = "Open internal.log", Margin = new Thickness(8, 0, 0, 0) };
        log.Click += (_, _) => { UiLog("UI action: open internal log clicked."); OpenLatestInternalLog(); };
        var dataEngine = new Button { Content = "Open DataEngine", Margin = new Thickness(8, 0, 0, 0) };
        dataEngine.Click += (_, _) => { UiLog("UI action: open DataEngine clicked."); OpenSelectedDataEngine(); };
        var resultExcel = new Button { Content = "Open result Excel", Margin = new Thickness(8, 0, 0, 0) };
        resultExcel.Click += (_, _) => { UiLog("UI action: open result Excel clicked."); OpenLatestResultWorkbook(); };
        var templates = new Button { Content = "Open Templates Folder", Margin = new Thickness(8, 0, 0, 0) };
        templates.Click += (_, _) => { UiLog("UI action: open Templates folder clicked."); OpenTemplatesFolder(); };
        var uiLog = new Button { Content = "Open UI log", Margin = new Thickness(8, 0, 0, 0) };
        uiLog.Click += (_, _) => { UiLog("UI action: open UI log clicked."); OpenConfiguredFile(Path.GetFullPath(_config.UiLogFile), "UI log", _config.LogApplicationPath); };
        var compareAssertions = new Button { Content = "Compare failed assertions", Margin = new Thickness(8, 0, 0, 0) };
        compareAssertions.Click += async (_, _) => { UiLog("UI action: compare failed assertions clicked."); await GenerateAssertionComparisonAsync(); };
        var openComparison = new Button { Content = "Open latest comparison", Margin = new Thickness(8, 0, 0, 0) };
        openComparison.Click += (_, _) => { UiLog("UI action: open latest comparison clicked."); OpenLatestAssertionComparison(); };
        var title = new TextBlock { Text = "Run management", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#172B4D")) };
        var subtitle = new TextBlock { Text = "Select instances, configure test cases and run load tests.", Foreground = new SolidColorBrush(Color.Parse("#667085")), Margin = new Thickness(0, 4, 0, 0) };
        var heading = new StackPanel { Children = { title, subtitle } };
        var search = new TextBox { Watermark = "Search instances…", Width = 210, Margin = new Thickness(16, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 18, 20, 12) };
        Grid.SetColumn(heading, 0);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Children = { search, run, stop, forceStop } };
        Grid.SetColumn(commands, 1);
        top.Children.Add(heading); top.Children.Add(commands);

        var stats = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 20, 14) };
        stats.Children.Add(CreateSummaryCard("READY", "Instances available", "#667085", _readyValue));
        stats.Children.Add(CreateSummaryCard("RUNNING", "Active now", "#315BD8", _runningValue));
        stats.Children.Add(CreateSummaryCard("PASSED", "Last completed", "#067647", _passedValue));
        stats.Children.Add(CreateSummaryCard("FAILED", "Needs attention", "#B42318", _failedValue));
        stats.Children.Add(CreateSummaryCard("REQUESTS / SEC", "Average last run", "#7A5AF8", _rpsValue));

        var instanceHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 12, 14, 8) };
        var instanceTitle = new TextBlock { Text = "Test instances", FontSize = 17, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#172B4D")) };
        Grid.SetColumn(instanceTitle, 0);
        var instanceActions = new StackPanel { Orientation = Orientation.Horizontal, Children = { refresh, selectAll, clear } };
        Grid.SetColumn(instanceActions, 1);
        instanceHeader.Children.Add(instanceTitle); instanceHeader.Children.Add(instanceActions);

        _instanceList.Margin = new Thickness(10, 0, 10, 10);
        _instanceList.ItemTemplate = new FuncDataTemplate<InstanceInfo>((item, _) => item is null ? new Border { Height = 1 } : GetOrCreateRow(item));
        _instanceList.SelectionChanged += (_, _) => { UiLog($"UI action: instance selection changed. instance={(_instanceList.SelectedItem as InstanceInfo)?.Name ?? "<none>"}"); ShowDetails(_instanceList.SelectedItem as InstanceInfo); };
        _details.Margin = new Thickness(14); _details.TextWrapping = TextWrapping.Wrap;
        _failedOnly.IsCheckedChanged += (_, _) => { UiLog($"Assertion filter changed. failedOnly={_failedOnly.IsChecked == true}"); RefreshAssertionList(); };
        _environmentFilter.SelectionChanged += (_, _) => { UiLog($"Assertion environment filter changed. environment={_environmentFilter.SelectedItem ?? "<none>"}"); RefreshAssertionList(); };
        _environmentFilter.ItemsSource = new[] { "All environments" };
        _environmentFilter.SelectedIndex = 0;
        var refreshAssertions = new Button { Content = "Refresh assertions", Margin = new Thickness(0, 6, 6, 0) };
        refreshAssertions.Click += (_, _) => { UiLog($"UI action: refresh assertions clicked. instance={_selectedInstance?.Name ?? "<none>"}; assertionsBefore={_selectedInstance?.Assertions.Count ?? 0}"); UpdateEnvironmentFilterOptions(); RefreshAssertionList(); };
        var selectFailed = new Button { Content = "Select failed", Margin = new Thickness(0, 6, 6, 0) };
        selectFailed.Click += (_, _) => { var count = _selectedInstance?.Assertions.Count(a => a.Result == "FAIL") ?? 0; UiLog($"UI action: select failed assertions clicked. instance={_selectedInstance?.Name ?? "<none>"}; failedCount={count}"); if (_selectedInstance is not null) foreach (var a in _selectedInstance.Assertions.Where(a => a.Result == "FAIL")) a.Selected = true; RefreshAssertionList(); };
        var clearAssertions = new Button { Content = "Clear selection", Margin = new Thickness(0, 6, 6, 0) };
        clearAssertions.Click += (_, _) => { UiLog($"UI action: clear assertion selection clicked. instance={_selectedInstance?.Name ?? "<none>"}"); if (_selectedInstance is not null) foreach (var a in _selectedInstance.Assertions) a.Selected = false; RefreshAssertionList(); };
        var applyFixes = new Button { Content = "Apply selected actual values", Margin = new Thickness(0, 6, 0, 0) };
        applyFixes.Click += (_, _) => { UiLog($"UI action: apply selected actual values clicked. instance={_selectedInstance?.Name ?? "<none>"}"); ApplySelectedFixes(); };
        var filterLabel = new TextBlock { Text = "Environment:", Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var assertionActions = new StackPanel { Orientation = Orientation.Horizontal, Children = { refreshAssertions, filterLabel, _environmentFilter, selectFailed, clearAssertions, applyFixes } };
        _assertionItems.MinWidth = 900;
        var assertionScroll = new ScrollViewer
        {
            Content = _assertionItems,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            MinHeight = 250,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        var assertionPanel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), Children = { _details, _failedOnly, assertionActions, assertionScroll } };
        Grid.SetRow(_details, 0); Grid.SetRow(_failedOnly, 1); Grid.SetRow(assertionActions, 2); Grid.SetRow(assertionScroll, 3);

        var overviewActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 14, 14), Children = { manual, log, dataEngine, resultExcel, templates, uiLog, compareAssertions, openComparison } };
        _overviewDetails.Margin = new Thickness(14); _overviewDetails.TextWrapping = TextWrapping.Wrap;
        var overview = new ScrollViewer { Content = new StackPanel { Children = { _overviewDetails, overviewActions } }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var tabs = new TabControl { Margin = new Thickness(0, 0, 14, 10), ItemsSource = new[]
        {
            new TabItem { Header = "Overview", Content = overview },
            new TabItem { Header = "Assertions", Content = assertionPanel },
            new TabItem { Header = "Logs", Content = new TextBlock { Text = "Use Open internal.log or Open UI log to inspect structured runner events.", Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } },
            new TabItem { Header = "Artifacts", Content = new TextBlock { Text = "Use Open result Excel, Open Templates Folder, or Open latest comparison for run artifacts.", Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } }
        } };

        var listCard = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#E4E7EC")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = new DockPanel { Children = { instanceHeader, _instanceList } } };
        DockPanel.SetDock(instanceHeader, Dock.Top);
        var detailsCard = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#E4E7EC")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = tabs };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), Margin = new Thickness(20, 0, 20, 12) };
        Grid.SetColumn(listCard, 0); Grid.SetColumn(detailsCard, 1); body.Children.Add(listCard); body.Children.Add(detailsCard);

        var navTitle = new TextBlock { Text = "▥  Load Testing Tool", FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Margin = new Thickness(18, 20, 12, 24) };
        var nav = new StackPanel { Background = new SolidColorBrush(Color.Parse("#102A43")), Width = 190, Children = { navTitle,
            CreateNavItem("▣  Instances", true), CreateNavItem("▶  Runs", false), CreateNavItem("◷  History", false), CreateNavItem("⚙  Settings", false),
            new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#274C77")), Margin = new Thickness(16, 22, 16, 14) },
            new TextBlock { Text = "●  Connected", Foreground = new SolidColorBrush(Color.Parse("#6EE7B7")), Margin = new Thickness(18, 0, 12, 4) },
            new TextBlock { Text = "Instance manager", Foreground = new SolidColorBrush(Color.Parse("#9FB3C8")), Margin = new Thickness(18, 0, 12, 12), FontSize = 12 } } };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        Grid.SetRow(top, 0); Grid.SetRow(stats, 1); Grid.SetRow(body, 2); Grid.SetRow(_enableInternalLog, 3);
        content.Children.Add(top); content.Children.Add(stats); content.Children.Add(body); _enableInternalLog.Margin = new Thickness(22, 0, 0, 12); content.Children.Add(_enableInternalLog);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*") };
        Grid.SetColumn(nav, 0); Grid.SetColumn(content, 1); root.Children.Add(nav); root.Children.Add(content);
        Content = root;
    }

    private static Border CreateSummaryCard(string value, string label, string color, TextBlock valueText)
    {
        valueText.Text = value;
        valueText.FontSize = 18;
        valueText.FontWeight = FontWeight.Bold;
        valueText.Foreground = new SolidColorBrush(Color.Parse(color));
        return new Border
        {
            Width = 145, Height = 76, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(12),
            Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#E4E7EC")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Child = new StackPanel { Children = { valueText, new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#667085")), Margin = new Thickness(0, 5, 0, 0) } } }
        };
    }

    private static Border CreateNavItem(string text, bool selected)
    {
        return new Border
        {
            Background = selected ? new SolidColorBrush(Color.Parse("#1D4ED8")) : Brushes.Transparent,
            CornerRadius = new CornerRadius(5), Margin = new Thickness(8, 2), Padding = new Thickness(10, 10),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontWeight = selected ? FontWeight.Bold : FontWeight.Normal }
        };
    }

    private InstanceRow GetOrCreateRow(InstanceInfo item)
    {
        if (_rows.TryGetValue(item.FolderPath, out var existing)) return existing;
        var row = new InstanceRow(item, selected => { UiLog($"UI action: instance row clicked. instance={selected.Name}"); _instanceList.SelectedItem = selected; ShowDetails(selected); }, UiLog);
        row.SetEnvironmentOptions(item.EnvironmentOptions);
        _rows[item.FolderPath] = row;
        return row;
    }

    private Control CreateAssertionRow(AssertionInfo assertion)
    {
        var check = new CheckBox { IsChecked = assertion.Selected, VerticalAlignment = VerticalAlignment.Top };
        check.IsCheckedChanged += (_, _) => assertion.Selected = check.IsChecked == true;
        var result = new TextBlock { Text = assertion.Result, Width = 48, Foreground = assertion.Result == "PASS" ? Brushes.DarkGreen : Brushes.DarkRed, FontWeight = FontWeight.Bold };
        var text = new TextBlock { Text = $"[{assertion.Environment}] {assertion.Testcase} / DataId {assertion.DataId} / Step {assertion.StepName} / {assertion.ResponsePath} {assertion.AssertionVerb}\nExpected: {assertion.ExpectedValue}\nActual: {assertion.ActualValue}", TextWrapping = TextWrapping.NoWrap, MinWidth = 820 };
        return new Border { Padding = new Thickness(4), Margin = new Thickness(0, 2), Background = assertion.Result == "PASS" ? Brushes.Honeydew : Brushes.MistyRose, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { check, result, text } } };
    }

    private void RefreshAssertionList()
    {
        var selectedEnvironment = _environmentFilter.SelectedItem?.ToString();
        var values = _selectedInstance?.Assertions
            .Where(a => string.IsNullOrWhiteSpace(selectedEnvironment) || selectedEnvironment == "All environments" || a.Environment.Equals(selectedEnvironment, StringComparison.OrdinalIgnoreCase))
            .Where(a => !_failedOnly.IsChecked.GetValueOrDefault() || a.Result == "FAIL")
            .ToList() ?? [];
        _assertionItems.Children.Clear();
        foreach (var assertion in values) _assertionItems.Children.Add(CreateAssertionRow(assertion));
        UiLog($"Assertion list refreshed. instance={_selectedInstance?.Name ?? "<none>"}; environment={selectedEnvironment ?? "<none>"}; failedOnly={_failedOnly.IsChecked == true}; visible={values.Count}; total={_selectedInstance?.Assertions.Count ?? 0}");
    }

    private void UpdateEnvironmentFilterOptions()
    {
        var current = _environmentFilter.SelectedItem?.ToString() ?? "All environments";
        var environments = (_selectedInstance?.EnvironmentOptions ?? [])
            .Concat(_selectedInstance?.Assertions.Select(a => a.Environment) ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var options = new[] { "All environments" }.Concat(environments).ToList();
        var existing = (_environmentFilter.ItemsSource as IEnumerable<string>)?.ToList() ?? [];
        if (options.SequenceEqual(existing, StringComparer.OrdinalIgnoreCase)
            && options.Any(option => option.Equals(current, StringComparison.OrdinalIgnoreCase))) return;
        _environmentFilter.ItemsSource = options;
        _environmentFilter.SelectedItem = options.FirstOrDefault(option => option.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? "All environments";
        UiLog($"Assertion environment options updated. instance={_selectedInstance?.Name ?? "<none>"}; options={string.Join(",", options)}; selected={_environmentFilter.SelectedItem}");
    }

    private void ApplySelectedFixes()
    {
        if (_selectedInstance is null) { UiLog("Apply selected values ignored. reason=no-selected-instance"); return; }
        var selected = _selectedInstance.Assertions.Where(a => a.Selected && a.Result == "FAIL").ToList();
        UiLog($"Apply selected values started. instance={_selectedInstance.Name}; selectedFailed={selected.Count}");
        if (selected.Count == 0) { _details.Text = "Select one or more failed assertions first."; UiLog($"Apply selected values ignored. instance={_selectedInstance.Name}; reason=no-failed-assertions-selected"); return; }
        try
        {
            var backup = $"{_selectedInstance.WorkbookPath}.backup.{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            File.Copy(_selectedInstance.WorkbookPath, backup, overwrite: false);
            using var workbook = new XLWorkbook(_selectedInstance.WorkbookPath);
            var sheet = workbook.Worksheet("response");
            var skippedMissing = 0;
            var skippedVariables = 0;
            var updated = 0;
            var addedHeaders = 0;
            foreach (var assertion in selected)
            {
                if (assertion.ActualValue == "<missing>") { skippedMissing++; continue; }
                if (assertion.ExcelRowNumber <= 0) continue;
                var column = assertion.ExpectedValueColumn;
                var added = false;
                if (IsUnexpectedAssertion(assertion) || column <= 0)
                {
                    var header = $"{assertion.StepName}.{assertion.ResponsePath}";
                    column = FindResponseHeaderColumn(sheet, header);
                    if (column <= 0)
                    {
                        column = (sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0) + 1;
                        var headerCell = sheet.Cell(1, column);
                        headerCell.Value = header;
                        headerCell.Style.Font.Bold = true;
                        headerCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                        headerCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        added = true;
                        addedHeaders++;
                    }
                }
                var currentCell = sheet.Cell(assertion.ExcelRowNumber, column);
                if (ContainsWorkbookVariable(currentCell.GetString()))
                {
                    skippedVariables++;
                    continue;
                }
                currentCell.Value = assertion.ActualValue;
                if (added)
                {
                    currentCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                }
                updated++;
            }
            workbook.Save();
            _details.Text = $"Updated {updated} assertion value(s); added {addedHeaders} response header(s). Skipped {skippedMissing} missing response value(s) and {skippedVariables} variable cell(s); dynamic values were not overwritten. New headers are highlighted green. Backup created: {Path.GetFileName(backup)}. Run the instance again to validate.";
            UiLog($"Apply selected values completed. instance={_selectedInstance.Name}; updated={updated}; addedHeaders={addedHeaders}; skippedMissing={skippedMissing}; skippedVariables={skippedVariables}; backup={backup}");
        }
        catch (Exception ex) { _details.Text = $"Workbook update failed: {ex.Message}"; UiLog($"Apply selected values failed. instance={_selectedInstance.Name}; error={ex}"); }
    }

    private static bool IsUnexpectedAssertion(AssertionInfo assertion) => assertion.AssertionVerb.Equals("contract", StringComparison.OrdinalIgnoreCase) || assertion.ExpectedValue.Equals("<response-sheet-path>", StringComparison.OrdinalIgnoreCase);

    private static int FindResponseHeaderColumn(IXLWorksheet sheet, string header)
    {
        var used = sheet.Row(1).CellsUsed();
        return used.FirstOrDefault(cell => cell.GetString().Trim().Equals(header, StringComparison.OrdinalIgnoreCase))?.Address.ColumnNumber ?? 0;
    }

    private static bool ContainsWorkbookVariable(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, @"<[^<>]+>");

    private void RefreshInstances()
    {
        var root = Path.GetFullPath(_config.InstancesRoot);
        UiLog($"Instance refresh started. root={root}; prefix={_config.InstancePrefix}");
        Directory.CreateDirectory(root);
        var previous = _instances.ToDictionary(x => x.FolderPath, StringComparer.OrdinalIgnoreCase);
        _instances.Clear();
        foreach (var folder in Directory.GetDirectories(root, $"{_config.InstancePrefix}*", SearchOption.TopDirectoryOnly).OrderBy(x => x))
        {
            var workbook = Directory.GetFiles(folder, "*.xlsx", SearchOption.TopDirectoryOnly)
                .Where(x => !Path.GetFileName(x).StartsWith("~$", StringComparison.Ordinal))
                .OrderBy(x => x).FirstOrDefault();
            if (workbook is null) continue;
            var item = previous.GetValueOrDefault(folder) ?? new InstanceInfo { Name = Path.GetFileName(folder), FolderPath = folder, WorkbookPath = workbook };
            item.EnvironmentOptions.Clear();
            item.DataIdOptions.Clear();
            try
            {
                using var book = new XLWorkbook(workbook);
                var requestSheet = book.Worksheets.FirstOrDefault(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase));
                var requestHeader = requestSheet?.FirstRowUsed()?.CellsUsed().ToDictionary(x => x.GetString().Trim(), x => x.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
                if (requestSheet is not null && requestHeader.TryGetValue("testcaseindex", out var testcaseColumn) && requestHeader.TryGetValue("dataid", out var dataIdColumn))
                    foreach (var row in requestSheet.RowsUsed().Skip(1))
                        if (int.TryParse(row.Cell(testcaseColumn).GetString().Trim(), out var testcaseIndex))
                        {
                            var dataId = row.Cell(dataIdColumn).GetString().Trim();
                            if (!string.IsNullOrWhiteSpace(dataId))
                            {
                                item.DataIdOptions.TryAdd(testcaseIndex, []);
                                item.DataIdOptions[testcaseIndex].Add(dataId);
                            }
                        }
                var sheet = book.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase));
                var header = sheet?.FirstRowUsed()?.CellsUsed().FirstOrDefault(x =>
                    new[] { "environments", "environment", "enviromests", "enviromments" }
                        .Contains(x.GetString().Trim(), StringComparer.OrdinalIgnoreCase)
                    || x.GetString().Trim().Contains("env", StringComparison.OrdinalIgnoreCase));
                if (sheet is not null && header is not null)
                    foreach (var cell in sheet.Column(header.Address.ColumnNumber).CellsUsed().Skip(1))
                        foreach (var environment in cell.GetString().Split(',', '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (!item.EnvironmentOptions.Contains(environment, StringComparer.OrdinalIgnoreCase)) item.EnvironmentOptions.Add(environment);
            }
            catch (Exception ex) { UiLog($"Instance refresh could not read workbook '{workbook}': {ex}"); }
            _instances.Add(item);
            if (_rows.TryGetValue(folder, out var existingRow)) existingRow.SetEnvironmentOptions(item.EnvironmentOptions);
        }
        _instanceList.ItemsSource = _instances;
        _details.Text = $"Detected {_instances.Count} instance(s) under {root}. Select an instance to see its log. Set Testcases in each row, then select instances and press Run selected.";
        UiLog($"Instance refresh completed. root={root}; instances={_instances.Count}; elapsedNotMeasured=true");
    }

    private void RefreshRows()
    {
        foreach (var item in _instances)
            if (_rows.TryGetValue(item.FolderPath, out var row)) row.Refresh();
        _readyValue.Text = _instances.Count(item => item.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase)).ToString();
        _runningValue.Text = _instances.Count(item => item.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) || item.Status.Equals("Stopping", StringComparison.OrdinalIgnoreCase)).ToString();
        _passedValue.Text = _instances.Count(item => item.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase)).ToString();
        _failedValue.Text = _instances.Count(item => item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) || item.Status.Equals("Launch failed", StringComparison.OrdinalIgnoreCase)).ToString();
        _rpsValue.Text = "—";
    }

    private void RunSelected()
    {
        var selected = _instances.Where(x => x.Selected).ToList();
        UiLog($"Run selected started. selected={selected.Count}; runnable={selected.Count(x => x.Process is null || x.Process.HasExited)}");
        foreach (var item in selected.Where(x => x.Process is null || x.Process.HasExited))
        {
            try
            {
                Directory.CreateDirectory(item.ResultsFolder);
                var runner = Path.GetFullPath(_config.RunnerDll);
                var selection = string.IsNullOrWhiteSpace(item.TestcaseSelection) ? "0" : item.TestcaseSelection;
                item.CurrentRunId = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..25];
                item.CurrentRunFolder = item.ResultsFolder;
                var environments = item.EnvironmentSelection;
                var environmentArg = string.IsNullOrWhiteSpace(environments) ? "" : $" --environments \"{environments}\"";
                var dataIdArg = string.IsNullOrWhiteSpace(item.DataIdSelection) ? "" : $" --dataids \"{item.DataIdSelection}\"";
                var uiLaunchId = $"ui-{Guid.NewGuid():N}";
                var uiLaunchAt = DateTimeOffset.Now;
                var runnerArgs = $"--excel \"{item.WorkbookPath}\" --templates \"{item.TemplatesFolder}\" --history \"{item.HistoryFolder}\" --logs \"{item.LogsFolder}\" --results \"{item.ResultsFolder}\" --instance-id \"{item.InstanceId}\" --run-id \"{item.CurrentRunId}\" --ui-launch-id \"{uiLaunchId}\" --ui-launch-at \"{uiLaunchAt:O}\" --internal-log \"{_enableInternalLog.IsChecked == true}\" --scenarios-parallel \"{item.RunScenariosInParallel}\" --execution-mode \"{item.ExecutionMode}\" --testcases \"{selection}\"{environmentArg}{dataIdArg}";
                var runnerIsDll = runner.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                UiLog($"Runner launch requested. instance={item.Name}; runId={item.CurrentRunId}; uiLaunchId={uiLaunchId}; uiLaunchAt={uiLaunchAt:O}; runner={runner}; args={runnerArgs}");
                item.Process = Process.Start(new ProcessStartInfo(runnerIsDll ? "dotnet" : runner, runnerIsDll ? $"\"{runner}\" {runnerArgs}" : runnerArgs) { WorkingDirectory = item.FolderPath, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = false, RedirectStandardError = false });
                UiLog($"Runner launch returned. instance={item.Name}; runId={item.CurrentRunId}; uiLaunchId={uiLaunchId}; processId={item.Process?.Id}; returnedAt={DateTimeOffset.Now:O}");
                item.Status = "Running"; item.LastEvent = $"Started with testcases {selection}"; item.EventPosition = 0; item.EventLineIndex = 0; item.EventLinePositions.Clear(); item.Assertions.Clear();
                UiLog($"Run state initialized. instance={item.Name}; runId={item.CurrentRunId}; testcases={selection}; mode={item.ExecutionMode}; environments={environments}; dataIds={item.DataIdSelection}; internalLog={_enableInternalLog.IsChecked == true}");
            }
            catch (Exception ex)
            {
                UiLog($"Runner launch failed for '{item.Name}': {ex}");
                item.Status = "Launch failed";
                item.LastEvent = ex.Message;
            }
        }
        foreach (var item in selected.Where(x => x.Process is { HasExited: false })) UiLog($"Run skipped for already-running instance. instance={item.Name}; processId={item.Process?.Id}");
        RefreshRows();
        UiLog("Run selected completed.");
    }

    private void StopSelected(bool force)
    {
        foreach (var item in _instances.Where(x => x.Selected && x.Process is { HasExited: false }))
        {
            try
            {
                if (force)
                {
                    item.Process!.Kill(entireProcessTree: true);
                    item.Status = "Force stopped";
                    item.LastEvent = "Process terminated by user";
                }
                else
                {
                    Directory.CreateDirectory(item.ResultsFolder);
                    var stopFile = Path.Combine(item.ResultsFolder, $"run_{item.CurrentRunId}.stop");
                    File.WriteAllText(stopFile, "stop requested by UI");
                    item.Status = "Stopping";
                    item.LastEvent = "Graceful stop requested";
                }
                UiLog($"Runner stop requested. instance={item.Name}; runId={item.CurrentRunId}; force={force}; processId={item.Process?.Id}");
            }
            catch (Exception ex)
            {
                item.Status = "Stop failed";
                item.LastEvent = ex.Message;
                UiLog($"Runner stop failed. instance={item.Name}; runId={item.CurrentRunId}; force={force}; error={ex}");
            }
        }
        RefreshRows();
    }

    private void PollEvents()
    {
        var pollStarted = Stopwatch.GetTimestamp();
        var filesSeen = 0;
        var eventsApplied = 0;
        foreach (var item in _instances)
        {
            try
            {
                var eventPattern = string.IsNullOrWhiteSpace(item.CurrentRunId) ? "events_*.ndjson" : $"events_{item.CurrentRunId}*.ndjson";
                var files = !string.IsNullOrEmpty(item.CurrentRunFolder) && Directory.Exists(item.CurrentRunFolder) ? Directory.GetFiles(item.CurrentRunFolder, eventPattern, SearchOption.AllDirectories) : [];
                filesSeen += files.Length;
                foreach (var file in files)
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    var lines = text.Split('\n');
                    var completeLineCount = text.EndsWith('\n') ? lines.Length : Math.Max(0, lines.Length - 1);
                    var eventKey = Path.GetFileName(file);
                    var lineIndex = item.EventLinePositions.GetValueOrDefault(eventKey);
                    while (lineIndex < completeLineCount)
                    {
                        var line = lines[lineIndex++].TrimEnd('\r');
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try { ApplyEvent(item, JsonDocument.Parse(line).RootElement); eventsApplied++; } catch (Exception ex) { UiLog($"Invalid event received for '{item.Name}': {ex}"); item.LastEvent = "Invalid event received"; }
                    }
                    item.EventLinePositions[eventKey] = lineIndex;
                }
            }
            catch (IOException) { /* The runner may be rotating or opening the event file; retry on the next tick. */ }
            catch (UnauthorizedAccessException) { /* The file may be temporarily locked; retry on the next tick. */ }
            if (item.Process is { HasExited: true } && item.Status == "Running")
            {
                item.CurrentRunFolder = FindRunFolder(item, item.CurrentRunId) ?? item.CurrentRunFolder;
                item.Status = item.Process.ExitCode == 0 ? "Passed" : "Failed";
                UiLog($"Runner process exited. instance={item.Name}; runId={item.CurrentRunId}; processId={item.Process.Id}; exitCode={item.Process.ExitCode}; observedAt={DateTimeOffset.Now:O}");
            }
        }
        var pollElapsedMs = Stopwatch.GetElapsedTime(pollStarted).TotalMilliseconds;
        if (eventsApplied > 0 || pollElapsedMs >= 100)
            UiLog($"UI poll completed. filesSeen={filesSeen}; eventsApplied={eventsApplied}; elapsedMs={pollElapsedMs:F1}; observedAt={DateTimeOffset.Now:O}");
        RefreshRows();
        if (_instanceList.SelectedItem is InstanceInfo selected)
        {
            _selectedInstance = selected;
            UpdateDetailsText();
        }
    }

    private void ApplyEvent(InstanceInfo item, JsonElement e)
    {
        var type = e.GetProperty("eventType").GetString();
        if (type == "request-completed")
        {
            var error = e.TryGetProperty("errorMessage", out var errorProperty) ? errorProperty.GetString() : "";
            var stepType = e.TryGetProperty("stepType", out var stepTypeProperty) ? stepTypeProperty.GetString() : "";
            var template = e.TryGetProperty("template", out var templateProperty) ? templateProperty.GetString() : "";
            var targetUrl = e.TryGetProperty("targetUrl", out var urlProperty) ? urlProperty.GetString() : "";
            var status = e.TryGetProperty("httpStatus", out var statusProperty) ? statusProperty.GetInt32().ToString() : "";
            var failureCategory = e.TryGetProperty("failureCategory", out var categoryProperty) ? categoryProperty.GetString() ?? "" : "";
            var runtimeReference = e.TryGetProperty("runtimeReference", out var referenceProperty) ? referenceProperty.GetString() ?? "" : "";
            var failurePrefix = failureCategory.Equals("RuntimeReference", StringComparison.OrdinalIgnoreCase) ? $"RUNTIME REFERENCE FAILED: {runtimeReference} | " : "";
            item.LastEvent = $"{failurePrefix}DataId {e.GetProperty("dataId").GetString()} / Step {e.GetProperty("stepName").GetString()} [{stepType}] {e.GetProperty("result").GetString()} | HTTP {status} | assertions passed: {e.GetProperty("assertionsPassed").GetInt32()}, failed: {e.GetProperty("assertionsFailed").GetInt32()} | error: {error} | template: {template} | url: {targetUrl}";
            UiLog($"Runner event applied. type=request-completed; instance={item.Name}; runId={item.CurrentRunId}; dataId={e.GetProperty("dataId").GetString()}; step={e.GetProperty("stepName").GetString()}; result={e.GetProperty("result").GetString()}; status={status}; failureCategory={failureCategory}; runtimeReference={runtimeReference}; assertionsPassed={e.GetProperty("assertionsPassed").GetInt32()}; assertionsFailed={e.GetProperty("assertionsFailed").GetInt32()}; error={error}");
        }
        if (type == "assertion-completed")
        {
            var assertion = new AssertionInfo
            {
                TestcaseIndex = e.GetProperty("testcaseIndex").GetInt32(), Testcase = e.GetProperty("testcase").GetString() ?? "", DataId = e.GetProperty("dataId").GetString() ?? "", StepName = e.GetProperty("stepName").GetString() ?? "",
                ResponsePath = e.GetProperty("responsePath").GetString() ?? "", AssertionVerb = e.GetProperty("assertionVerb").GetString() ?? "", ExpectedValue = e.GetProperty("expectedValue").GetString() ?? "", ActualValue = e.GetProperty("actualValue").GetString() ?? "", Result = e.GetProperty("result").GetString() ?? "", FailureMessage = e.GetProperty("failureMessage").GetString() ?? "",
                Environment = e.TryGetProperty("environment", out var environment) ? environment.GetString() ?? "" : "",
                ExcelRowNumber = e.TryGetProperty("excelRowNumber", out var row) ? row.GetInt32() : 0, ExpectedValueColumn = e.TryGetProperty("expectedValueColumn", out var col) ? col.GetInt32() : 0
            };
            item.Assertions.Add(assertion);
            if (assertion.Result == "FAIL") item.LastEvent = $"FAILED: {assertion.ResponsePath} expected {assertion.ExpectedValue} actual {assertion.ActualValue}";
            UiLog($"Runner event applied. type=assertion-completed; instance={item.Name}; runId={item.CurrentRunId}; dataId={assertion.DataId}; step={assertion.StepName}; path={assertion.ResponsePath}; result={assertion.Result}; environment={assertion.Environment}");
        }
        if (type == "run-completed")
        {
            item.Status = e.GetProperty("result").GetString() ?? "Completed";
            var p95 = e.TryGetProperty("p95DurationMs", out var p95Property) ? p95Property.GetInt64() : 0;
            var rps = e.TryGetProperty("requestsPerSecond", out var rpsProperty) ? rpsProperty.GetDouble() : 0;
            if (e.TryGetProperty("historyFile", out var history) && !string.IsNullOrWhiteSpace(history.GetString()))
            {
                var historyPath = history.GetString()!;
                item.CurrentRunFolder = Path.GetDirectoryName(Path.GetFullPath(historyPath)) ?? item.CurrentRunFolder;
                item.LastEvent = $"Run completed: {item.Status}; p95: {p95} ms; RPS: {rps:F2}; history: {historyPath}";
            }
            var completedHistory = e.TryGetProperty("historyFile", out var historyEvent) ? historyEvent.GetString() ?? "" : "";
            UiLog($"Runner event applied. type=run-completed; instance={item.Name}; runId={item.CurrentRunId}; result={item.Status}; history={completedHistory}");
        }
    }

    private void ShowDetails(InstanceInfo? item)
    {
        _selectedInstance = item;
        UiLog($"Details selection changed. instance={item?.Name ?? "<none>"}; assertions={item?.Assertions.Count ?? 0}");
        UpdateEnvironmentFilterOptions();
        UpdateDetailsText();
        RefreshAssertionList();
    }

    private void UpdateDetailsText()
    {
        var item = _selectedInstance;
        var text = item is null ? "" : $"Instance: {item.Name}\nWorkbook: {item.WorkbookPath}\nStatus: {item.Status}\nTestcases: {item.TestcaseSelection}\nLatest: {item.LastEvent}\nAssertions: {item.Assertions.Count(a => a.Result == "PASS")} passed / {item.Assertions.Count(a => a.Result == "FAIL")} failed\nResults: {item.ResultsFolder}\nCurrent run: {item.CurrentRunFolder}\nLatest internal.log: {FindLatestInternalLog(item) ?? "not created yet"}";
        _details.Text = text;
        _overviewDetails.Text = text;
    }

    private void OpenLatestInternalLog()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; UiLog("Open internal.log ignored. reason=no-selected-instance"); return; }
        var path = FindLatestInternalLog(_selectedInstance);
        if (path is null) { _details.Text = "No internal.log exists for this instance yet. Run it with internal logging enabled."; UiLog($"Open internal.log ignored. instance={_selectedInstance.Name}; reason=file-not-found"); return; }
        UiLog($"Opening latest internal.log. instance={_selectedInstance.Name}; path={path}");
        OpenConfiguredFile(path, "internal.log", _config.LogApplicationPath);
    }

    private void OpenSelectedDataEngine()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; UiLog("Open DataEngine ignored. reason=no-selected-instance"); return; }
        UiLog($"Opening DataEngine workbook. instance={_selectedInstance.Name}; path={_selectedInstance.WorkbookPath}");
        OpenExcelFile(_selectedInstance.WorkbookPath, "DataEngine workbook");
    }

    private void OpenLatestResultWorkbook()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; UiLog("Open result workbook ignored. reason=no-selected-instance"); return; }
        var folder = _selectedInstance.CurrentRunFolder;
        if (Directory.Exists(folder) && Path.GetFileName(folder).Equals("log", StringComparison.OrdinalIgnoreCase))
            folder = Directory.GetParent(folder)?.FullName ?? folder;
        var path = !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.xlsx", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (path is null) { _details.Text = "No result Excel workbook exists for the current run yet."; UiLog($"Open result workbook ignored. instance={_selectedInstance.Name}; reason=file-not-found"); return; }
        UiLog($"Opening latest result workbook. instance={_selectedInstance.Name}; path={path}");
        OpenExcelFile(path, "result workbook");
    }

    private async Task GenerateAssertionComparisonAsync()
    {
        if (_selectedInstance is null)
        {
            _details.Text = "Select an instance first.";
            UiLog("Assertion comparison ignored. reason=no-selected-instance");
            return;
        }

        var instance = _selectedInstance;
        var snapshot = instance.Assertions.Select(a => new AssertionSnapshot(
            a.Environment, a.Testcase, a.DataId, a.StepName, a.ResponsePath,
            a.AssertionVerb, a.ExpectedValue, a.ActualValue, a.Result, a.FailureMessage)).ToList();
        if (snapshot.Count == 0)
        {
            _details.Text = "No assertions are available for comparison yet.";
            UiLog($"Assertion comparison ignored. instance={instance.Name}; reason=no-assertions");
            return;
        }

        var outputFolder = Path.Combine(instance.ResultsFolder, "failed");
        var outputPath = Path.Combine(outputFolder, $"AssertionComparison_{instance.CurrentRunId}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        _details.Text = $"Generating assertion comparison...\nAssertions captured: {snapshot.Count}";
        UiLog($"Assertion comparison started. instance={instance.Name}; runId={instance.CurrentRunId}; assertions={snapshot.Count}; output={outputPath}");
        try
        {
            await Task.Run(() => CreateAssertionComparisonWorkbook(snapshot, outputPath));
            _details.Text = $"Assertion comparison created:\n{outputPath}";
            UiLog($"Assertion comparison completed. instance={instance.Name}; runId={instance.CurrentRunId}; output={outputPath}");
        }
        catch (Exception ex)
        {
            _details.Text = $"Could not create assertion comparison:\n{ex.Message}";
            UiLog($"Assertion comparison failed. instance={instance.Name}; runId={instance.CurrentRunId}; error={ex}");
        }
    }

    private void OpenLatestAssertionComparison()
    {
        if (_selectedInstance is null)
        {
            _details.Text = "Select an instance first.";
            UiLog("Open assertion comparison ignored. reason=no-selected-instance");
            return;
        }

        var folder = Path.Combine(_selectedInstance.ResultsFolder, "failed");
        var path = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "AssertionComparison_*.xlsx", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (path is null)
        {
            _details.Text = "No assertion comparison workbook exists yet.";
            UiLog($"Open assertion comparison ignored. instance={_selectedInstance.Name}; reason=file-not-found");
            return;
        }
        UiLog($"Opening latest assertion comparison. instance={_selectedInstance.Name}; path={path}");
        OpenExcelFile(path, "assertion comparison workbook");
    }

    private static void CreateAssertionComparisonWorkbook(IReadOnlyList<AssertionSnapshot> assertions, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var environments = assertions.Select(a => string.IsNullOrWhiteSpace(a.Environment) ? "(unknown)" : a.Environment)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        var grouped = assertions
            .GroupBy(a => new AssertionComparisonKey(a.Testcase, a.DataId, a.StepName, a.ResponsePath, a.AssertionVerb))
            .OrderBy(group => group.Key.Testcase, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.DataId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.StepName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.ResponsePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rows = grouped.Select(group => new ComparisonRow(group.Key, environments.ToDictionary(
            environment => environment,
                environment => group.Where(a => (string.IsNullOrWhiteSpace(a.Environment) ? "(unknown)" : a.Environment).Equals(environment, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Result == "FAIL" ? 0 : 1).FirstOrDefault(), StringComparer.OrdinalIgnoreCase))).ToList();
        var failedRows = rows.Where(row => row.Values.Values.Any(value => value?.Result == "FAIL")).ToList();
        var differentRows = rows.Where(row => row.Values.Values.Select(value => value?.Result ?? "MISSING").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
            || row.Values.Values.Select(value => value?.ActualValue ?? "MISSING").Distinct(StringComparer.Ordinal).Count() > 1).ToList();

        using var workbook = new XLWorkbook();
        WriteComparisonSheet(workbook.Worksheets.Add("Comparison"), rows, environments);
        WriteComparisonSheet(workbook.Worksheets.Add("FailedOnly"), failedRows, environments);
        WriteComparisonSheet(workbook.Worksheets.Add("Differences"), differentRows, environments);

        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = "Environment";
        summary.Cell(1, 2).Value = "Assertions";
        summary.Cell(1, 3).Value = "Passed";
        summary.Cell(1, 4).Value = "Failed";
        summary.Cell(1, 5).Value = "Missing";
        summary.Cell(1, 6).Value = "Pass rate";
        for (var i = 0; i < environments.Count; i++)
        {
            var environment = environments[i];
            var values = assertions.Where(a => (string.IsNullOrWhiteSpace(a.Environment) ? "(unknown)" : a.Environment).Equals(environment, StringComparison.OrdinalIgnoreCase)).ToList();
            var row = i + 2;
            var passed = values.Count(a => a.Result == "PASS");
            var failed = values.Count(a => a.Result == "FAIL");
            var missing = values.Count(a => a.ActualValue == "<missing>");
            summary.Cell(row, 1).Value = environment;
            summary.Cell(row, 2).Value = values.Count;
            summary.Cell(row, 3).Value = passed;
            summary.Cell(row, 4).Value = failed;
            summary.Cell(row, 5).Value = missing;
            summary.Cell(row, 6).Value = values.Count == 0 ? 0 : (double)passed / values.Count;
            summary.Cell(row, 6).Style.NumberFormat.Format = "0.00%";
        }
        StyleHeader(summary.Row(1));
        summary.Columns().AdjustToContents();
        workbook.SaveAs(outputPath);
    }

    private sealed record ComparisonRow(AssertionComparisonKey Key, Dictionary<string, AssertionSnapshot?> Values);

    private static void WriteComparisonSheet(IXLWorksheet sheet, IReadOnlyList<ComparisonRow> rows, IReadOnlyList<string> environments)
    {
        var headers = new List<string> { "Testcase", "Data ID", "Step", "Response path", "Operator" };
        foreach (var environment in environments)
        {
            headers.Add($"{environment} result");
            headers.Add($"{environment} expected");
            headers.Add($"{environment} actual");
            headers.Add($"{environment} failure");
        }
        headers.Add("Different between environments");
        for (var i = 0; i < headers.Count; i++) sheet.Cell(1, i + 1).Value = headers[i];
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var excelRow = rowIndex + 2;
            sheet.Cell(excelRow, 1).Value = row.Key.Testcase;
            sheet.Cell(excelRow, 2).Value = row.Key.DataId;
            sheet.Cell(excelRow, 3).Value = row.Key.StepName;
            sheet.Cell(excelRow, 4).Value = row.Key.ResponsePath;
            sheet.Cell(excelRow, 5).Value = row.Key.AssertionVerb;
            var column = 6;
            foreach (var environment in environments)
            {
                row.Values.TryGetValue(environment, out var value);
                sheet.Cell(excelRow, column++).Value = value?.Result ?? "MISSING";
                sheet.Cell(excelRow, column++).Value = value?.ExpectedValue ?? "";
                sheet.Cell(excelRow, column++).Value = value?.ActualValue ?? "MISSING";
                sheet.Cell(excelRow, column++).Value = value?.FailureMessage ?? "";
            }
            var resultSet = row.Values.Values.Select(value => value?.Result ?? "MISSING").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var actualSet = row.Values.Values.Select(value => value?.ActualValue ?? "MISSING").Distinct(StringComparer.Ordinal).ToList();
            sheet.Cell(excelRow, column).Value = resultSet.Count > 1 || actualSet.Count > 1 ? "YES" : "NO";
        }
        StyleHeader(sheet.Row(1));
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        foreach (var column in sheet.Columns())
            if (column.Width > 60) column.Width = 60;
    }

    private static void StyleHeader(IXLRow row)
    {
        row.Style.Font.Bold = true;
        row.Style.Fill.BackgroundColor = XLColor.LightBlue;
    }

    private void OpenTemplatesFolder()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; UiLog("Open Templates folder ignored. reason=no-selected-instance"); return; }
        UiLog($"Opening Templates folder. instance={_selectedInstance.Name}; path={_selectedInstance.TemplatesFolder}");
        OpenFolder(_selectedInstance.TemplatesFolder, "Templates folder");
    }

    private void OpenExcelFile(string path, string description)
    {
        if (!File.Exists(path)) { UiLog($"Cannot open {description}; file not found: {path}"); _details.Text = $"File not found: {path}"; return; }
        try
        {
            var application = string.IsNullOrWhiteSpace(_config.ExcelApplicationPath) ? "excel.exe" : _config.ExcelApplicationPath;
            var start = new ProcessStartInfo { FileName = application, UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start);
            UiLog($"Opened {description} with '{application}': {path}");
        }
        catch (Exception ex) { UiLog($"Could not open {description} with Excel '{_config.ExcelApplicationPath}': {ex}"); _details.Text = $"Could not open Excel: {ex.Message}"; }
    }

    private void OpenFile(string path, string description)
    {
        if (!File.Exists(path)) { UiLog($"Cannot open {description}; file not found: {path}"); _details.Text = $"File not found: {path}"; return; }
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); UiLog($"Opened {description}: {path}"); }
        catch (Exception ex) { UiLog($"Could not open {description} '{path}': {ex}"); _details.Text = ex.Message; }
    }
    private void OpenConfiguredFile(string path, string description, string? application)
    {
        if (!File.Exists(path)) { UiLog($"Cannot open {description}; file not found: {path}"); _details.Text = $"File not found: {path}"; return; }
        try
        {
            if (string.IsNullOrWhiteSpace(application))
            {
                OpenFile(path, description);
                return;
            }
            var start = new ProcessStartInfo { FileName = application, UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start);
            UiLog($"Opened {description} with '{application}': {path}");
        }
        catch (Exception ex) { UiLog($"Could not open {description} with '{application}': {ex}"); _details.Text = $"Could not open {description}: {ex.Message}"; }
    }

    private void OpenFolder(string path, string description)
    {
        if (!Directory.Exists(path)) { UiLog($"Cannot open {description}; folder not found: {path}"); _details.Text = $"Folder not found: {path}"; return; }
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); UiLog($"Opened {description}: {path}"); }
        catch (Exception ex) { UiLog($"Could not open {description} '{path}': {ex}"); _details.Text = ex.Message; }
    }

    private void UiLog(string message)
    {
        try
        {
            var path = Path.GetFullPath(_config.UiLogFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [UI] {message}{Environment.NewLine}");
        }
        catch { /* UI logging must never prevent the instance manager from operating. */ }
    }

    private static string? FindLatestInternalLog(InstanceInfo item)
    {
        if (!Directory.Exists(item.ResultsFolder)) return null;
        if (!string.IsNullOrWhiteSpace(item.CurrentRunFolder) && Directory.Exists(item.CurrentRunFolder))
        {
            var current = Directory.GetFiles(item.CurrentRunFolder, "internal.log", SearchOption.AllDirectories).FirstOrDefault();
            if (current is not null) return current;
        }
        return Directory.GetFiles(item.ResultsFolder, "internal.log", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".runner" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !path.Contains(Path.DirectorySeparatorChar + ".running" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("runner_startup_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? FindRunFolder(InstanceInfo item, string runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || !Directory.Exists(item.ResultsFolder)) return null;
        var eventFile = Directory.GetFiles(item.ResultsFolder, $"events_{runId}*.ndjson", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains(Path.DirectorySeparatorChar + "pass" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                 || path.Contains(Path.DirectorySeparatorChar + "failed" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (eventFile is null) return null;
        var eventDirectory = Directory.GetParent(eventFile);
        if (eventDirectory is null) return null;
        // Events are stored in <run-folder>/log; the Excel history workbook is in <run-folder>.
        return eventDirectory.Name.Equals("log", StringComparison.OrdinalIgnoreCase)
            ? eventDirectory.Parent?.FullName
            : eventDirectory.FullName;
    }

    private static UiConfig LoadConfig()
    {
        var root = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: true).Build();
        var config = new UiConfig(); root.Bind(config); return config;
    }
}
