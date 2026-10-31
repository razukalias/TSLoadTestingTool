using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LoadTestingTool.UI;

public sealed class UiConfig
{
    public string InstancesRoot { get; set; } = "../Instances";
    public string RunnerDll { get; set; } = "../Runner/LoadTestingTool.exe";
    public string SettingsFile { get; set; } = "appsettings.json";
    public string InstancePrefix { get; set; } = "DataEngine_";
    public string DocumentationFile { get; set; } = "../APPLICATION_GUIDE.pdf";
    public string UiLogFile { get; set; } = "Logs/ui-internal.log";
    public int Threads { get; set; } = 1;
    public int Iterations { get; set; } = 1;
    public int ThreadIntervalMs { get; set; }
    public int IterationIntervalMs { get; set; }
    public bool GenerateResults { get; set; } = true;
    public Dictionary<string, UiInstanceDefinition> Instances { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UiInstanceDefinition
{
    public string Folder { get; set; } = string.Empty;
    public string Workbook { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int? Threads { get; set; }
    public int? Iterations { get; set; }
    public int? ThreadIntervalMs { get; set; }
    public int? IterationIntervalMs { get; set; }
    public string? ExecutionMode { get; set; }
    public bool? RunScenariosInParallel { get; set; }
    public bool? GenerateResults { get; set; }
    public bool? InternalLoggingEnabled { get; set; }
    public bool? SaveRequestBodyToHistory { get; set; }
    public bool? SaveResponseBodyToHistory { get; set; }
    public bool? MaskSensitiveData { get; set; }
    public Dictionary<string, bool>? HistorySheets { get; set; }
}

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
