using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LoadTestingTool.UI;

public sealed class UiConfig
{
    public string InstancesRoot { get; set; } = "../Instances";
    public string RunnerDll { get; set; } = "../Runner/LoadTestingTool.exe";
    public string InstancePrefix { get; set; } = "DataEngine_";
    public string DocumentationFile { get; set; } = "../APPLICATION_GUIDE.pdf";
    public string UiLogFile { get; set; } = "Logs/ui-internal.log";
    public string ExcelApplicationPath { get; set; } = "excel.exe";
    public string DocumentationApplicationPath { get; set; } = "";
    public string LogApplicationPath { get; set; } = "";
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
