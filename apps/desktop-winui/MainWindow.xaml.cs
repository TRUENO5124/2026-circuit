using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CircuitAssistant.Views;

namespace CircuitAssistant;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        App.LogTrace("MainWindow.ctor: Starting");
        InitializeComponent();
        App.LogTrace("MainWindow.ctor: InitializeComponent complete");

        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            App.LogTrace("MainWindow.ctor: SetTitleBar complete");
        }
        catch (System.Exception ex)
        {
            App.LogTrace($"MainWindow.ctor: SetTitleBar failed: {ex}");
        }

        try
        {
            AppWindow.SetIcon("Assets/AppIcon.ico");
            App.LogTrace("MainWindow.ctor: SetIcon complete");
        }
        catch (System.Exception ex)
        {
            App.LogTrace($"MainWindow.ctor: SetIcon failed: {ex}");
        }

        Closed += (s, e) =>
        {
            App.LogTrace("MainWindow: Closed event fired");
        };

        // Navigate directly to Analysis Studio EDA Workspace
        App.LogTrace("MainWindow.ctor: Navigating to AnalysisStudioPage");
        ContentFrame.Navigate(typeof(AnalysisStudioPage));
        App.LogTrace("MainWindow.ctor: Navigation complete");
    }
}
