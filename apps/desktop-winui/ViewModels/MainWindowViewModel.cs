using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace CircuitAssistant.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "电路教学辅助系统 (Circuit Learning Assistant)";

    [ObservableProperty]
    private ElementTheme _currentTheme = ElementTheme.Default;

    [ObservableProperty]
    private string _selectedCategory = "AnalysisStudio";
}
