using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.Json;

namespace LoadTestingTool.UI;

internal sealed class TestStepWizardWindow : Window
{
    private readonly Action<WizardStepDraft> _saveStep;
    private readonly TextBox _testcaseIndex = Field("1");
    private readonly TextBox _testcase = Field("ExampleTest");
    private readonly TextBox _stepName = Field("NewStep");
    private readonly ComboBox _stepType = new() { Width = 230, ItemsSource = new[] { "HTTP", "GraphQL", "File", "Script", "SQL" }, SelectedIndex = 0 };
    private readonly ComboBox _stage = new() { Width = 180, ItemsSource = new[] { "Setup", "Test", "Teardown" }, SelectedIndex = 1 };
    private readonly CheckBox _enabled = new() { Content = "Enabled", IsChecked = true };
    private readonly TextBox _timeout = Field("30");
    private readonly TextBox _targetUrl = Field("https://example.test/api");
    private readonly TextBox _verb = Field("GET");
    private readonly TextBox _template = Field("request.json");
    private readonly TextBox _contentType = Field("application/json");
    private readonly TextBox _expectedStatus = Field("200");
    private readonly TextBox _environments = Field("local");
    private readonly TextBox _graphqlQuery = Field("query Example { field }");
    private readonly TextBox _graphqlOperation = Field("Example");
    private readonly TextBox _graphqlVariables = Field("{}");
    private readonly TextBox _fileAction = Field("ReadBinary");
    private readonly TextBox _filePath = Field("Files/image.png");
    private readonly TextBox _fileOutput = Field("fileData");
    private readonly TextBox _fileMode = Field("Reference");
    private readonly TextBox _scriptSource = Field("Scripts/setup.csx");
    private readonly TextBox _sqlConnection = Field("Default");
    private readonly TextBox _sqlQuery = Field("select 1");
    private readonly TextBox _stepConfig = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, FontFamily = new Avalonia.Media.FontFamily("Consolas") };
    private readonly TextBlock _help = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly TextBlock _error = DashboardStyle.Text("", 11, false, DashboardStyle.Red);
    private readonly TextBlock _preview = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly StackPanel _httpFields = new() { Spacing = 6 };
    private readonly StackPanel _typeFields = new() { Spacing = 6 };
    private readonly int? _editRow;

    public TestStepWizardWindow(Action<WizardStepDraft> saveStep, WizardStepDraft? existing = null)
    {
        _saveStep = saveStep; _editRow = existing?.Row;
        Title = existing is null ? "Add test step" : "Edit test step";
        Width = 760; Height = 820; MinWidth = 620; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (existing is not null) Load(existing);
        _stepType.SelectionChanged += (_, _) => UpdateTypeFields();
        _stepConfig.TextChanged += (_, _) => UpdatePreview();
        var common = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        AddRow(common, 0, "Testcase index", _testcaseIndex); AddRow(common, 1, "Testcase", _testcase); AddRow(common, 2, "Step name", _stepName); AddRow(common, 3, "Step type", _stepType); AddRow(common, 4, "Stage", _stage); AddRow(common, 5, "Timeout (seconds)", _timeout); AddRow(common, 6, "Environments", _environments); AddRow(common, 7, "Enabled", _enabled); AddRow(common, 8, "Target URL", _targetUrl); AddRow(common, 9, "HTTP verb", _verb); AddRow(common, 10, "Template source", _template); AddRow(common, 11, "Content type", _contentType);
        _httpFields.Children.Add(Labeled("Expected status", _expectedStatus));
        var configPanel = new StackPanel { Spacing = 5, Children = { DashboardStyle.Text("StepConfig", 13, true), _stepConfig, _help, DashboardStyle.Text("Generated preview", 13, true), _preview } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { DashboardStyle.Action("Help", ShowHelp), DashboardStyle.Action("Cancel", Close), DashboardStyle.Action(existing is null ? "Add step" : "Save step", Save, "primary") } };
        var body = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children = { DashboardStyle.Text(existing is null ? "Add test step" : "Edit test step", 21, true), DashboardStyle.Text("This writes the same config-sheet columns and StepConfig JSON used by the runner.", 12, false, DashboardStyle.Muted), common, _httpFields, _typeFields, configPanel, _error, buttons } };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        UpdateTypeFields(); UpdatePreview();
    }

    private static TextBox Field(string value) => new() { Text = value, MinWidth = 240 };
    private static Control Labeled(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
        var labelControl = DashboardStyle.Text(label, 12, false, DashboardStyle.Muted);
        grid.Children.Add(labelControl); grid.Children.Add(control); Grid.SetColumn(control, 1); return grid;
    }
    private static void AddRow(Grid grid, int row, string label, Control control) { grid.Children.Add(DashboardStyle.Text(label, 12, false, DashboardStyle.Muted)); Grid.SetRow(grid.Children[^1], row); grid.Children.Add(control); Grid.SetRow(control, row); Grid.SetColumn(control, 1); }
    private void Load(WizardStepDraft existing)
    {
        Set(_testcaseIndex, existing.Get("testcaseindex")); Set(_testcase, existing.Get("testcase")); Set(_stepName, existing.Get("stepname")); Set(_timeout, existing.Get("timeout", "30")); Set(_environments, existing.Get("environments")); Set(_targetUrl, existing.Get("targeturl")); Set(_verb, existing.Get("verb", "POST")); Set(_template, existing.Get("templetsource")); Set(_contentType, existing.Get("contenttype")); Set(_expectedStatus, existing.Get("expectedstatus")); _enabled.IsChecked = !existing.Get("enabled", "true").Equals("false", StringComparison.OrdinalIgnoreCase); _stage.SelectedItem = existing.Get("stage", "Test"); _stepType.SelectedItem = existing.Get("steptype", "HTTP"); _stepConfig.Text = existing.Get("stepconfig", "{}");
    }
    private static void Set(TextBox box, string value) => box.Text = value;
    private void UpdateTypeFields()
    {
        var type = _stepType.SelectedItem?.ToString() ?? "HTTP"; _httpFields.IsVisible = type is "HTTP" or "GraphQL";
        _typeFields.Children.Clear();
        _help.Text = type switch
        {
            "HTTP" => "HTTP uses Target URL, Verb, Template source, headers, and assertions from the response sheet.",
            "GraphQL" => "GraphQL uses Target URL and StepConfig such as Source, Query, Variables, and OperationName.",
            "File" => "File StepConfig examples: {\"Action\":\"ReadBinary\",\"Path\":\"Files/image.png\",\"OutputVariable\":\"imageFile\",\"OutputMode\":\"Reference\"}.",
            "Script" => "Script StepConfig should identify the script source and inputs. Trusted scripts must be enabled in the instance.",
            "SQL" => "SQL StepConfig should identify the connection profile, command type, query, and parameters.", _ => ""
        };
        if (type == "File") _typeFields.Children.Add(Labeled("File action", new TextBlock { Text = "Read / Json / Write / Append / ReadBinary / WriteBinary / AppendBinary", TextWrapping = TextWrapping.Wrap }));
        if (type == "Script") _typeFields.Children.Add(Labeled("Security", new TextBlock { Text = "Trusted scripts are disabled by default and require explicit instance configuration.", TextWrapping = TextWrapping.Wrap }));
        if (type == "GraphQL") { _typeFields.Children.Add(Labeled("Query", _graphqlQuery)); _typeFields.Children.Add(Labeled("Operation name", _graphqlOperation)); _typeFields.Children.Add(Labeled("Variables JSON", _graphqlVariables)); _typeFields.Children.Add(DashboardStyle.Action("Generate GraphQL StepConfig", GenerateStepConfig)); }
        if (type == "File") { _typeFields.Children.Add(Labeled("Action", _fileAction)); _typeFields.Children.Add(Labeled("Path", _filePath)); _typeFields.Children.Add(Labeled("Output variable", _fileOutput)); _typeFields.Children.Add(Labeled("Output mode", _fileMode)); _typeFields.Children.Add(DashboardStyle.Action("Generate File StepConfig", GenerateStepConfig)); }
        if (type == "Script") { _typeFields.Children.Add(Labeled("Script source", _scriptSource)); _typeFields.Children.Add(DashboardStyle.Action("Generate Script StepConfig", GenerateStepConfig)); }
        if (type == "SQL") { _typeFields.Children.Add(Labeled("Connection", _sqlConnection)); _typeFields.Children.Add(Labeled("Query", _sqlQuery)); _typeFields.Children.Add(DashboardStyle.Action("Generate SQL StepConfig", GenerateStepConfig)); }
    }
    private void GenerateStepConfig()
    {
        var type = _stepType.SelectedItem?.ToString() ?? "HTTP";
        object value = type switch
        {
            "GraphQL" => new { Query = _graphqlQuery.Text ?? "", OperationName = _graphqlOperation.Text ?? "", Variables = ParseObject(_graphqlVariables.Text) },
            "File" => new { Action = _fileAction.Text ?? "ReadBinary", Path = _filePath.Text ?? "", OutputVariable = _fileOutput.Text ?? "", OutputMode = _fileMode.Text ?? "Reference" },
            "Script" => new { Source = _scriptSource.Text ?? "" },
            "SQL" => new { Connection = _sqlConnection.Text ?? "", Query = _sqlQuery.Text ?? "" },
            _ => new { }
        };
        _stepConfig.Text = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    }
    private static object ParseObject(string? text) { try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text!).RootElement; } catch { return new Dictionary<string, string>(); } }
    private void UpdatePreview()
    {
        try { _preview.Text = JsonSerializer.Serialize(JsonDocument.Parse(string.IsNullOrWhiteSpace(_stepConfig.Text) ? "{}" : _stepConfig.Text).RootElement, new JsonSerializerOptions { WriteIndented = true }); }
        catch { _preview.Text = "Invalid JSON in StepConfig"; }
    }
    private void ShowHelp()
    {
        ContextHelpWindow.Show(this, "Test-step wizard help", "Testcase index identifies the testcase row in the workbook. Testcase and Step name are the stable names used by request/response headers.\n\nStep type selects the executor. Stage controls Setup, Test, or Teardown order. Timeout is per step in seconds; zero means no step timeout. Environments is a comma-separated filter such as local,staging.\n\nHTTP and GraphQL use Target URL, verb, template source, content type, and expected status. File uses StepConfig for Read, Write, Append, ReadBinary, WriteBinary, or AppendBinary. Script requires trusted scripts to be enabled. SQL uses StepConfig for the connection, command, and parameters.\n\nStepConfig must be valid JSON. Example HTTP: {\"Headers\":{\"Authorization\":\"Bearer _token_\"}}. Example binary file reference: {\"Action\":\"ReadBinary\",\"Path\":\"Files/image.png\",\"OutputVariable\":\"imageFile\",\"OutputMode\":\"Reference\"}. Use Advanced JSON when you need an executor option that is not shown as a form field.");
    }

    private void Save()
    {
        _error.Text = ""; var type = _stepType.SelectedItem?.ToString() ?? "HTTP";
        if (!int.TryParse(_testcaseIndex.Text, out var index) || index < 1) { _error.Text = "Testcase index must be a positive number."; return; }
        if (string.IsNullOrWhiteSpace(_testcase.Text) || string.IsNullOrWhiteSpace(_stepName.Text)) { _error.Text = "Testcase and step name are required."; return; }
        if (!int.TryParse(_timeout.Text, out var timeout) || timeout < 0) { _error.Text = "Timeout must be zero or a positive number."; return; }
        if (type is "HTTP" or "GraphQL" && string.IsNullOrWhiteSpace(_targetUrl.Text)) { _error.Text = "Target URL is required for HTTP and GraphQL steps."; return; }
        try { using var _ = JsonDocument.Parse(string.IsNullOrWhiteSpace(_stepConfig.Text) ? "{}" : _stepConfig.Text); } catch { _error.Text = "StepConfig must contain valid JSON."; return; }
        var row = new WizardStepDraft(_editRow, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["testcaseindex"] = index.ToString(), ["testcase"] = _testcase.Text?.Trim() ?? "", ["stepname"] = _stepName.Text?.Trim() ?? "", ["steptype"] = type, ["stage"] = _stage.SelectedItem?.ToString() ?? "Test", ["enabled"] = (_enabled.IsChecked == true).ToString().ToLowerInvariant(), ["timeout"] = timeout.ToString(), ["environments"] = _environments.Text?.Trim() ?? "", ["targeturl"] = _targetUrl.Text?.Trim() ?? "", ["verb"] = (_verb.Text?.Trim() ?? "POST").ToUpperInvariant(), ["templetsource"] = _template.Text?.Trim() ?? "", ["contenttype"] = _contentType.Text?.Trim() ?? "", ["expectedstatus"] = _expectedStatus.Text?.Trim() ?? "", ["stepconfig"] = string.IsNullOrWhiteSpace(_stepConfig.Text) ? "{}" : _stepConfig.Text!.Trim(), ["stoponfailure"] = "true", ["assert"] = "true", ["ignoreempty"] = "false", ["assertonlyresponse"] = "false", ["threads"] = "1", ["iterations"] = "1", ["threadintervalms"] = "0", ["iterationintervalms"] = "0", ["wait"] = "0" });
        _saveStep(row); Close();
    }
}

internal sealed class WizardStepDraft
{
    public int? Row { get; }
    public IReadOnlyDictionary<string, string> Values { get; }
    public WizardStepDraft(int? row, IReadOnlyDictionary<string, string> values) { Row = row; Values = values; }
    public string Get(string name, string fallback = "") => Values.TryGetValue(name, out var value) ? value : fallback;
}
