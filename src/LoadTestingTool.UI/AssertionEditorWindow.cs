using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LoadTestingTool.UI;

public sealed record AssertionEditResult(string ResponsePath, string AssertionVerb, string ExpectedValue, string ExtractVariable);

public sealed class AssertionEditorWindow : Window
{
    private readonly TextBox _path = new();
    private readonly ComboBox _verb = new();
    private readonly TextBox _expected = new();
    private readonly TextBox _extract = new();
    private readonly TextBlock _actual = new();
    private readonly TextBlock _error = new();
    private readonly Button _save;
    public AssertionEditResult? Result { get; private set; }
    private static readonly string[] Verbs = { "eq", "ne", "lt", "lte", "gt", "gte", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "in", "size" };

    public AssertionEditorWindow(AssertionInfo assertion)
    {
        Title = "Edit assertion"; Width = 760; Height = 660; MinWidth = 650; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _path.Text = assertion.ResponsePath; _verb.ItemsSource = Verbs; _verb.SelectedItem = Verbs.Contains(assertion.AssertionVerb, StringComparer.OrdinalIgnoreCase) ? assertion.AssertionVerb.ToLowerInvariant() : "eq";
        _expected.Text = assertion.ExpectedValue; _extract.Text = assertion.ExtractVariable;
        _actual.Text = $"Actual value: {assertion.ActualValue}   |   Result: {DashboardStyle.Status(assertion.Result)}   |   {assertion.Environment} / {assertion.Testcase} / {assertion.StepName} / {assertion.DataId}";
        _actual.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _error.Foreground = DashboardStyle.Red; _error.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _save = DashboardStyle.Action("Save correction", Save, "primary");
        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(DashboardStyle.Text("Edit assertion", 22, true));
        form.Children.Add(DashboardStyle.Text("This updates the response-sheet cell and creates a timestamped workbook backup. Run the instance again to validate the correction.", 12, false, DashboardStyle.Muted));
        form.Children.Add(DashboardStyle.Card(new StackPanel { Spacing = 8, Children = { DashboardStyle.Text($"{assertion.Result} assertion", 13, true), _actual } }));
        form.Children.Add(Field("Response path / header", _path));
        form.Children.Add(Field("Assertion verb", _verb));
        form.Children.Add(Field("Expected value", _expected));
        form.Children.Add(Field("Extract variable (optional)", _extract));
        form.Children.Add(DashboardStyle.Text("Examples: eq + 200, contains + completed, regex + ^ORD-, exists with an empty expected value.", 11, false, DashboardStyle.Muted));
        form.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { DashboardStyle.Action("Cancel", Close), _save } };
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(22) };
        root.Children.Add(new ScrollViewer { Content = form }); Grid.SetRow(buttons, 1); root.Children.Add(buttons); Content = root;
        _verb.SelectionChanged += (_, _) => Validate(); _path.TextChanged += (_, _) => Validate(); _expected.TextChanged += (_, _) => Validate(); Validate();
    }
    private static Control Field(string label, Control control) => new StackPanel { Spacing = 5, Children = { DashboardStyle.Text(label, 12, false, DashboardStyle.Muted), control } };
    private void Validate()
    {
        var verb = _verb.SelectedItem?.ToString() ?? "";
        var path = _path.Text?.Trim() ?? "";
        var needsExpected = verb is not ("exists" or "notexists" or "empty" or "notempty");
        _error.Text = string.IsNullOrWhiteSpace(path) ? "Response path is required." : needsExpected && string.IsNullOrWhiteSpace(_expected.Text) ? $"Expected value is required for {verb}." : "";
        _save.IsEnabled = string.IsNullOrWhiteSpace(_error.Text);
    }
    private void Save()
    {
        if (!_save.IsEnabled) return;
        Result = new AssertionEditResult(_path.Text!.Trim(), _verb.SelectedItem!.ToString()!, _expected.Text ?? "", _extract.Text?.Trim() ?? ""); Close();
    }
}
