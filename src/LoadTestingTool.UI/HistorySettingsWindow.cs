using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;

namespace LoadTestingTool.UI;

public sealed class HistorySettingsWindow : Window
{
    private readonly string _instanceFolder;
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _status = DashboardStyle.Text("Changes are saved to the instance appsettings.json.", 12, false, DashboardStyle.Muted);

    public HistorySettingsWindow(string instanceFolder)
    {
        _instanceFolder = instanceFolder;
        Title = "History output settings";
        Width = 620;
        Height = 700;
        MinWidth = 500;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                DashboardStyle.Text("History output sheets", 21, true),
                DashboardStyle.Text("Choose which worksheets the runner writes into each execution history workbook. Disabled sheets are not created.", 12, false, DashboardStyle.Muted)
            }
        };

        var sheetPanel = new StackPanel { Spacing = 4 };
        foreach (var name in UiHistorySheetCatalog.Names)
        {
            var check = new CheckBox
            {
                Content = DashboardStyle.Text(name, 13),
                IsChecked = ReadSetting(name),
                Margin = new Thickness(4, 2)
            };
            _checks[name] = check;
            sheetPanel.Children.Add(check);
        }

        var presetRow = new WrapPanel { Children =
        {
            Preset("All sheets", () => SetAll(true)), Preset("Clear all", () => SetAll(false)), Preset("Minimal", ApplyMinimal), Preset("Debug", ApplyDebug), Preset("Full", () => SetAll(true))
        }};
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children =
        {
            DashboardStyle.Action("Cancel", Close),
            DashboardStyle.Action("Save settings", Save, "primary")
        }};

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(20) };
        root.Children.Add(heading);
        Grid.SetRow(presetRow, 1); root.Children.Add(presetRow);
        var scroll = new ScrollViewer { Content = sheetPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12) };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 8, Children = { _status, buttons } };
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        Content = root;
    }

    private bool ReadSetting(string name)
    {
        try
        {
            var path = Path.Combine(_instanceFolder, "appsettings.json");
            if (!File.Exists(path)) return true;
            var node = JsonNode.Parse(File.ReadAllText(path));
            var value = node?["HistorySheets"]?[name];
            return value is null || value.GetValue<bool>();
        }
        catch { return true; }
    }

    private void SetAll(bool value)
    {
        foreach (var check in _checks.Values) check.IsChecked = value;
    }

    private static Button Preset(string label, Action action)
    {
        var button = DashboardStyle.Action(label, action);
        button.Margin = new Thickness(0, 0, 6, 6);
        return button;
    }

    private void ApplyMinimal()
    {
        SetAll(false);
        foreach (var name in new[] { "RunSummary", "Metrics", "TestcaseSummary" }) _checks[name].IsChecked = true;
    }

    private void ApplyDebug()
    {
        SetAll(true);
        _checks["RequestHistory"].IsChecked = false;
        _checks["Variables"].IsChecked = false;
        _checks["CorrelationHistory"].IsChecked = false;
    }

    private void Save()
    {
        try
        {
            if (_checks.Values.All(check => check.IsChecked != true))
            {
                _status.Foreground = DashboardStyle.Red;
                _status.Text = "Select at least one history sheet before saving.";
                return;
            }
            Directory.CreateDirectory(_instanceFolder);
            var path = Path.Combine(_instanceFolder, "appsettings.json");
            var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
            root ??= new JsonObject();
            var sheets = new JsonObject();
            foreach (var name in UiHistorySheetCatalog.Names) sheets[name] = _checks[name].IsChecked == true;
            root["HistorySheets"] = sheets;
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, root.ToJsonString(options) + Environment.NewLine);
            _status.Foreground = DashboardStyle.Green;
            _status.Text = $"Saved history sheet settings to {path}";
        }
        catch (Exception ex)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Could not save settings: {ex.Message}";
        }
    }
}
