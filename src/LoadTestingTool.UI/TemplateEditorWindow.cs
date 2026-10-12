using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LoadTestingTool.UI;

internal sealed class TemplateEditorWindow : Window
{
    private readonly string _templatesFolder;
    private readonly TextBox _path;
    private readonly ComboBox _format;
    private readonly TextBox _content;
    private readonly TextBlock _status = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly Action<string> _saved;

    public TemplateEditorWindow(string templatesFolder, string sourcePath, string stepType, Action<string> saved)
    {
        _templatesFolder = Path.GetFullPath(templatesFolder);
        Directory.CreateDirectory(_templatesFolder);
        _saved = saved;
        Title = "Template editor"; Width = 900; Height = 700; MinWidth = 640; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _path = new TextBox { Text = sourcePath, Watermark = "relative path inside Templates", MinWidth = 320 };
        _format = new ComboBox { ItemsSource = new[] { "JSON", "XML", "SQL", "GraphQL", "C# script", "Plain text" }, SelectedIndex = FormatIndex(sourcePath, stepType), Width = 150 };
        _content = new TextBox { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.NoWrap, FontFamily = new Avalonia.Media.FontFamily("Consolas"), MinHeight = 360 };
        ScrollViewer.SetHorizontalScrollBarVisibility(_content, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_content, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        LoadFile();
        var validate = DashboardStyle.Action("Validate", Validate, "primary");
        var save = DashboardStyle.Action("Save template", Save, "primary");
        var close = DashboardStyle.Action("Close", Close);
        var help = DashboardStyle.Action("Help", () => ContextHelpWindow.Show(this, "Template editor help", "Template files are stored under the instance Templates folder. Use relative paths such as requests/create-user.json or sql/find-user.sql.\n\nTemplates use underscore variables only, for example _userId_, _token_, or _requestId_. The editor does not suggest or generate <guid>, <randomnumber>, or <From_...> syntax.\n\nJSON and XML are syntax-checked. SQL and GraphQL receive basic required-content checks. C# script templates must contain content and require trusted scripts at runtime."));
        var meta = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,Auto") };
        meta.Children.Add(DashboardStyle.Text("File", 12, false, DashboardStyle.Muted)); Grid.SetColumn(_path, 1); meta.Children.Add(_path); Grid.SetColumn(_format, 2); meta.Children.Add(_format);
        Content = new Border { Padding = new Thickness(18), Background = DashboardStyle.Panel, Child = new DockPanel { LastChildFill = true, Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { help, validate, save, close } },
            new StackPanel { Spacing = 8, Children = { DashboardStyle.Text($"Edit {stepType} template", 19, true), DashboardStyle.Text("Only _underscore_ variables are supported in template content.", 12, false, DashboardStyle.Muted), meta, _content, _status } }
        } } };
    }

    private void LoadFile()
    {
        try
        {
            var path = ResolvePath(_path.Text ?? "");
            if (File.Exists(path)) _content.Text = File.ReadAllText(path);
            else _content.Text = DefaultContent(_format.SelectedItem?.ToString() ?? "Plain text");
        }
        catch (Exception ex) { _status.Text = ex.Message; _status.Foreground = DashboardStyle.Red; }
    }

    private void Validate()
    {
        try { ValidateContent(_content.Text ?? "", _format.SelectedItem?.ToString() ?? "Plain text"); _status.Text = "Template is valid."; _status.Foreground = DashboardStyle.Green; }
        catch (Exception ex) { _status.Text = ex.Message; _status.Foreground = DashboardStyle.Red; }
    }

    private void Save()
    {
        try
        {
            ValidateContent(_content.Text ?? "", _format.SelectedItem?.ToString() ?? "Plain text");
            var path = ResolvePath(_path.Text ?? "");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, _content.Text ?? "");
            var relative = Path.GetRelativePath(_templatesFolder, path).Replace(Path.DirectorySeparatorChar, '/');
            _saved(relative); _status.Text = $"Saved {relative}"; _status.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { _status.Text = ex.Message; _status.Foreground = DashboardStyle.Red; }
    }

    private string ResolvePath(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException("Template source path is required.");
        var path = Path.GetFullPath(Path.Combine(_templatesFolder, source.Replace('/', Path.DirectorySeparatorChar)));
        var root = _templatesFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Template path must remain inside the Templates folder.");
        return path;
    }

    private static void ValidateContent(string content, string format)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("Template content cannot be empty.");
        foreach (Match match in Regex.Matches(content, @"_([\p{L}\p{N}._-]+)_"))
            if (match.Groups[1].Value.Contains('<') || match.Groups[1].Value.Contains('>')) throw new InvalidDataException("Template variables must use _variable_ syntax.");
        if (format.Equals("JSON", StringComparison.OrdinalIgnoreCase)) using (JsonDocument.Parse(content)) { }
        else if (format.Equals("XML", StringComparison.OrdinalIgnoreCase)) XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        else if (format.Equals("SQL", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(content, @"\b(select|insert|update|delete|merge|exec|execute|declare|with)\b", RegexOptions.IgnoreCase)) throw new InvalidDataException("SQL template should contain a SQL command such as SELECT, INSERT, UPDATE, DELETE, EXEC, or WITH.");
        else if (format.Equals("GraphQL", StringComparison.OrdinalIgnoreCase) && !content.Contains('{')) throw new InvalidDataException("GraphQL template must contain a selection set enclosed by braces.");
    }

    private static int FormatIndex(string path, string stepType)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch { ".json" => 0, ".xml" => 1, ".sql" => 2, ".graphql" or ".gql" => 3, ".cs" or ".csx" => 4, _ when stepType.Equals("SQL", StringComparison.OrdinalIgnoreCase) => 2, _ when stepType.Equals("GraphQL", StringComparison.OrdinalIgnoreCase) => 3, _ when stepType.Equals("Script", StringComparison.OrdinalIgnoreCase) => 4, _ => 5 };
    }

    private static string DefaultContent(string format) => format switch
    {
        "JSON" => "{\n  \"id\": \"_id_\"\n}",
        "XML" => "<request><id>_id_</id></request>",
        "SQL" => "select * from Users where Id = _id_",
        "GraphQL" => "query Example { user(id: \"_id_\") { id } }",
        "C# script" => "return _data_;",
        _ => "_value_"
    };
}
