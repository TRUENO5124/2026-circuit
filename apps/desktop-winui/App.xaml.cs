using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CircuitAssistant;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public static void LogTrace(string msg)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "startup.log");
            System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        LogTrace("App.ctor: Starting");

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            LogTrace($"[ProcessExit] ExitCode: {System.Environment.ExitCode}");
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogTrace($"[AppDomain Unhandled] {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogTrace($"[UnobservedTaskException] {e.Exception}");
        };

        UnhandledException += (sender, e) =>
        {
            LogTrace($"[WinUI UnhandledException] Message: {e.Message}, Exception: {e.Exception}");
            e.Handled = false;
        };

        LogTrace("App.ctor: Calling InitializeComponent");
        InitializeComponent();
        LogTrace("App.ctor: InitializeComponent complete");
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            LogTrace("OnLaunched: Starting");
            Window = new MainWindow();
            CircuitAssistant.Services.ProjectManagerService.WindowHandleProvider = () => WindowHandle;
            LogTrace($"OnLaunched: MainWindow created, HWND={WindowHandle}");
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            LogTrace("OnLaunched: DispatcherQueue obtained");
            Window.Activate();
            Window.AppWindow.Show();
            LogTrace($"OnLaunched: Window activated, Visible={Window.AppWindow.IsVisible}, Size={Window.AppWindow.Size.Width}x{Window.AppWindow.Size.Height}");
        }
        catch (System.Exception ex)
        {
            LogTrace($"[OnLaunched Exception] {ex}");
            throw;
        }
    }
}
