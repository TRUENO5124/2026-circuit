using CommunityToolkit.Mvvm.ComponentModel;

namespace CircuitAssistant.Models;

public partial class AnalysisStep : ObservableObject
{
    [ObservableProperty]
    private int _stepNumber;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _methodName = string.Empty;

    [ObservableProperty]
    private string _explanation = string.Empty;

    [ObservableProperty]
    private string _latexFormula = string.Empty;

    [ObservableProperty]
    private string _substitutionFormula = string.Empty;

    [ObservableProperty]
    private string _resultValue = string.Empty;

    [ObservableProperty]
    private string _mnaLog = string.Empty;

    [ObservableProperty]
    private bool _hasMnaLog;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private string _badgeText = string.Empty;
}
