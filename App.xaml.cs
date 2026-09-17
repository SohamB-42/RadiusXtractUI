using Microsoft.UI.Xaml;

namespace RadiusXtractUI;

public partial class App : Application
{
    private Window? _window;
    public Window? MainWindow => _window;

    public App()
    {
        this.InitializeComponent();
        
        // Log all unhandled exceptions to a file for debugging
        this.UnhandledException += (s, e) =>
        {
            try
            {
                System.IO.File.WriteAllText("crash_log.txt", $"Exception: {e.Exception.Message}\nStack Trace:\n{e.Exception.StackTrace}");
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("crash_log_startup.txt", $"Exception: {ex.Message}\nStack Trace:\n{ex.StackTrace}");
            throw;
        }
    }
}
