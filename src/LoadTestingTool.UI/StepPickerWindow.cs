using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LoadTestingTool.UI;

internal sealed class StepPickerWindow : Window
{
    public StepPickerWindow(string instanceName, IReadOnlyList<TestcaseOption> catalog, Action<int, string> selected)
    {
        Title = $"Edit test step • {instanceName}";
        Width = 620; Height = 620; MinWidth = 520; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var search = new TextBox { Watermark = "Filter testcase or step…", Margin = new Thickness(0, 0, 0, 8) };
        var list = new ListBox { ItemsSource = BuildItems(catalog), MinHeight = 280 };
        var allItems = (list.ItemsSource as IEnumerable<StepChoice>)?.ToList() ?? [];
        search.TextChanged += (_, _) => list.ItemsSource = allItems.Where(x => x.Label.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
        var open = DashboardStyle.Action("Edit selected step", () =>
        {
            if (list.SelectedItem is not StepChoice choice) return;
            selected(choice.TestcaseIndex, choice.StepName); Close();
        }, "primary");
        var cancel = DashboardStyle.Action("Cancel", Close);
        var help = DashboardStyle.Action("Help", () => ContextHelpWindow.Show(this, "Edit step picker help", "Choose the testcase and step to edit. The wizard then loads the existing config row, including its StepConfig JSON. Changes are written back to the config sheet and a workbook backup is created."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { help, cancel, open } };
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new StackPanel { Spacing = 8, Children = { DashboardStyle.Text("Choose a step to edit", 18, true), DashboardStyle.Text("This opens the type-specific wizard outside the workbook editor.", 12, false, DashboardStyle.Muted), search, list } });
        Content = new Border { Padding = new Thickness(18), Background = DashboardStyle.Panel, Child = dock };
    }

    private static IReadOnlyList<StepChoice> BuildItems(IReadOnlyList<TestcaseOption> catalog) => catalog
        .SelectMany(c => c.Steps.Select(s => new StepChoice(c.Index, c.Name, s.Name, s.Type, s.Enabled)))
        .ToList();

    private sealed record StepChoice(int TestcaseIndex, string TestcaseName, string StepName, string Type, bool Enabled)
    {
        public string Label => $"TC {TestcaseIndex} • {TestcaseName}  /  {StepName}  [{Type}]" + (Enabled ? "" : "  (disabled)");
        public override string ToString() => Label;
    }
}
