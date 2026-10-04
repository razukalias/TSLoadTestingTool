using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI;

public sealed class RunSelectionWindow : Window
{
    private readonly RunSelectionDraft _draft;
    private readonly StackPanel _caseItems = new() { Spacing = 10 };
    private readonly StackPanel _stepItems = new() { Spacing = 10 };
    private readonly StackPanel _environmentItems = new() { Spacing = 10 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = DashboardStyle.Red };
    private readonly Button _apply;
    private bool _rebuilding;

    public RunSelectionWindow(InstanceInfo item)
    {
        Title = $"Choose what to run — {item.Name}";
        Width = 1020; Height = 720; MinWidth = 860; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _draft = new RunSelectionDraft(item.TestcaseOptions, item.TestcaseSelection, item.StepSelectionJson, item.EnvironmentSelection, item.EnvironmentSelectionJson);
        _apply = DashboardStyle.Action("Apply selection", Apply, "primary"); _apply.IsDefault = true;
        var heading = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16), Children =
        {
            DashboardStyle.Text("Choose what to run", 22, true),
            DashboardStyle.Text(item.Name, 13, false, DashboardStyle.Muted),
            DashboardStyle.Text("Tick testcases, then their steps and environments. Only checked, enabled steps run in workbook order.", 12, false, DashboardStyle.Muted)
        } };
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*,225") };
        var cases = Panel("1  Testcases", _caseItems, () => SetCases(true), () => SetCases(false));
        var steps = Panel("2  Test steps", _stepItems, () => SetSteps(true), () => SetSteps(false));
        var environments = Panel("3  Environments", _environmentItems, () => { _draft.SelectAllEnvironments(); BuildEnvironments(); UpdateSummary(); },
            () => { _draft.ClearEnvironments(); BuildEnvironments(); UpdateSummary(); });
        cases.Margin = new Thickness(0, 0, 10, 0); steps.Margin = new Thickness(0, 0, 10, 0);
        columns.Children.Add(cases); Grid.SetColumn(steps, 1); columns.Children.Add(steps); Grid.SetColumn(environments, 2); columns.Children.Add(environments);
        var status = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12), Children = { _summary, _error,
            DashboardStyle.Text("Dependencies are not auto-selected. Include setup/dependency steps when your chosen step needs them.", 11, false, DashboardStyle.Muted) } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children =
        {
            DashboardStyle.Action("Cancel", () => Close(null)), _apply
        } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(20) };
        grid.Children.Add(heading); Grid.SetRow(columns, 1); grid.Children.Add(columns); Grid.SetRow(status, 2); grid.Children.Add(status);
        Grid.SetRow(buttons, 3); grid.Children.Add(buttons); Content = grid;
        BuildCases(); BuildSteps(); BuildEnvironments(); UpdateSummary();
    }
    private static Border Panel(string title, Control items, Action all, Action clear)
    {
        var header = new StackPanel { Spacing = 8, Children = { DashboardStyle.Text(title, 15, true),
            new StackPanel { Orientation = Orientation.Horizontal, Children = { DashboardStyle.Action("All", all), DashboardStyle.Action("Clear", clear) } } } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") }; grid.Children.Add(header);
        var scroll = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll); return DashboardStyle.Card(grid, new Thickness(12));
    }
    private static CheckBox Check(string text, bool value, bool enabled = true)
    {
        var label = DashboardStyle.Text(text, 12); label.TextWrapping = TextWrapping.Wrap;
        return new CheckBox { Content = label, IsChecked = value, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Stretch };
    }
    private void BuildCases()
    {
        _rebuilding = true; _caseItems.Children.Clear();
        foreach (var item in _draft.Catalog)
        {
            var enabled = item.Steps.Any(s => s.Enabled);
            var check = Check($"{item.Index}  {item.Name}" + (enabled ? "" : " (no enabled steps)"), enabled && _draft.Cases.Contains(item.Index), enabled);
            check.IsCheckedChanged += (_, _) =>
            {
                if (_rebuilding) return;
                _draft.SetCase(item.Index, check.IsChecked == true); BuildSteps(); BuildEnvironments(); UpdateSummary();
            };
            _caseItems.Children.Add(check);
        }
        _rebuilding = false;
    }
    private void BuildSteps()
    {
        _rebuilding = true; _stepItems.Children.Clear();
        foreach (var testcase in _draft.Catalog.Where(c => _draft.Cases.Contains(c.Index)))
        {
            _stepItems.Children.Add(DashboardStyle.Text($"{testcase.Index}  {testcase.Name}", 12, true, DashboardStyle.Muted));
            foreach (var step in testcase.Steps)
            {
                var check = Check($"{step.Name}  [{step.Type}]" + (step.Enabled ? "" : " — disabled in workbook"), step.Enabled && _draft.Steps[testcase.Index].Contains(step.Name), step.Enabled);
                ToolTip.SetTip(check, step.Environments.Count == 0 ? "Untagged: applies to selected environments" : "Environments: " + string.Join(", ", step.Environments));
                check.IsCheckedChanged += (_, _) =>
                {
                    if (_rebuilding) return;
                    _draft.SetStep(testcase.Index, step.Name, check.IsChecked == true); BuildEnvironments(); UpdateSummary();
                };
                _stepItems.Children.Add(check);
            }
        }
        if (_stepItems.Children.Count == 0) _stepItems.Children.Add(DashboardStyle.Text("Choose a testcase first.", color: DashboardStyle.Muted));
        _rebuilding = false;
    }
    private void BuildEnvironments()
    {
        _rebuilding = true; _environmentItems.Children.Clear();
        foreach (var environment in _draft.AvailableEnvironments)
        {
            var check = Check(string.IsNullOrEmpty(environment) ? "Default (untagged steps)" : environment, _draft.Environments.Contains(environment));
            check.IsCheckedChanged += (_, _) => { if (_rebuilding) return; _draft.SetEnvironment(environment, check.IsChecked == true); UpdateSummary(); };
            _environmentItems.Children.Add(check);
        }
        _rebuilding = false;
    }
    private void SetCases(bool value)
    {
        foreach (var c in _draft.Catalog) _draft.SetCase(c.Index, value && c.Steps.Any(s => s.Enabled));
        BuildCases(); BuildSteps(); BuildEnvironments(); UpdateSummary();
    }
    private void SetSteps(bool value)
    {
        foreach (var c in _draft.Catalog.Where(c => _draft.Cases.Contains(c.Index)))
            foreach (var s in c.Steps.Where(s => s.Enabled)) _draft.SetStep(c.Index, s.Name, value);
        BuildSteps(); BuildEnvironments(); UpdateSummary();
    }
    private void UpdateSummary()
    {
        var count = _draft.Catalog.Where(c => _draft.Cases.Contains(c.Index)).Sum(c => c.Steps.Count(s => s.Enabled && _draft.Steps[c.Index].Contains(s.Name)));
        var env = _draft.AvailableEnvironments.Where(e => _draft.Environments.Contains(e)).Select(e => string.IsNullOrEmpty(e) ? "Default" : e).ToList();
        var chosenEnvironments = _draft.AvailableEnvironments.Where(e => _draft.Environments.Contains(e)).ToList();
        var matching = _draft.Catalog.Where(c => _draft.Cases.Contains(c.Index))
            .SelectMany(c => chosenEnvironments.Select(e => c.Steps.Count(s => s.Enabled && _draft.Steps[c.Index].Contains(s.Name)
                && (s.Environments.Count == 0 || s.Environments.Contains(e, StringComparer.OrdinalIgnoreCase)))))
            .ToList();
        _summary.Text = $"Selected: {_draft.Cases.Count} testcase(s), {count} enabled step(s), environments: {(env.Count == 0 ? "none" : string.Join(", ", env))}.\nExecutable: {matching.Count(n => n > 0)} testcase/environment pair(s), {matching.Sum()} step execution(s) per data row / iteration.";
        _apply.IsEnabled = _draft.TryBuild(out _, out var error); _error.Text = error;
    }
    private void Apply() { if (_draft.TryBuild(out var result, out var error)) Close(result); else _error.Text = error; }
}
