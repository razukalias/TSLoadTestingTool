using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LoadTestingTool.UI;

public sealed class EnvironmentManagerWindow : Window
{
    private readonly ListBox _items = new() { MinHeight = 220 };
    private readonly TextBox _name = new() { Watermark = "Environment name" };
    private readonly TextBlock _status = DashboardStyle.Text("Changes are saved to central appsettings.json.", 12, false, DashboardStyle.Muted);
    private readonly List<string> _environments;
    private readonly Action<IReadOnlyList<string>> _save;

    public EnvironmentManagerWindow(IEnumerable<string> environments, Action<IReadOnlyList<string>> save)
    {
        _environments = environments.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        _save = save;
        Title = "Global environments";
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Refresh();

        var add = DashboardStyle.Action("Add", Add, "primary");
        _name.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Add(); };
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _name, add } };
        add.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(add, 1);
        var remove = DashboardStyle.Action("Delete selected", DeleteSelected);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { remove, DashboardStyle.Action("Close", Close) } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(20) };
        root.Children.Add(DashboardStyle.Text("Global environments", 21, true));
        Grid.SetRow(addRow, 1); root.Children.Add(addRow);
        Grid.SetRow(_items, 2); root.Children.Add(_items);
        var footer = new StackPanel { Spacing = 8, Children = { _status, buttons } };
        Grid.SetRow(footer, 3); root.Children.Add(footer);
        Content = root;
    }

    private void Refresh() => _items.ItemsSource = _environments.ToList();

    private void Add()
    {
        var name = _name.Text?.Trim() ?? string.Empty;
        if (name.Length == 0) { _status.Text = "Enter an environment name."; _status.Foreground = DashboardStyle.Red; return; }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(',', StringComparison.Ordinal) || name.Contains('|', StringComparison.Ordinal))
        { _status.Text = "Use a simple name without path or selection separators."; _status.Foreground = DashboardStyle.Red; return; }
        if (_environments.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)))
        { _status.Text = $"Environment '{name}' already exists."; _status.Foreground = DashboardStyle.Red; return; }
        _environments.Add(name);
        _environments.Sort(StringComparer.OrdinalIgnoreCase);
        _name.Text = string.Empty;
        Refresh();
        Save();
    }

    private void DeleteSelected()
    {
        var name = _items.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(name)) { _status.Text = "Select an environment first."; _status.Foreground = DashboardStyle.Red; return; }
        _environments.RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        Refresh();
        Save();
    }

    private void Save()
    {
        try
        {
            _save(_environments);
            _status.Foreground = DashboardStyle.Green;
            _status.Text = "Saved global environments to central appsettings.json.";
        }
        catch (Exception ex)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Could not save environments: {ex.Message}";
        }
    }
}
