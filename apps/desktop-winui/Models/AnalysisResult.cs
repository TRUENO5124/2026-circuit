using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CircuitAssistant.Models;

public partial class QPointInfo : ObservableObject
{
    [ObservableProperty]
    private string _ibq = "0 μA";

    [ObservableProperty]
    private string _icq = "0 mA";

    [ObservableProperty]
    private string _vceq = "0 V";

    [ObservableProperty]
    private string _workingRegion = "放大区 (Active Region)";

    [ObservableProperty]
    private bool _isActiveRegion = true;
}

public partial class AcParametersInfo : ObservableObject
{
    [ObservableProperty]
    private string _voltageGain = "-100.0";

    [ObservableProperty]
    private string _inputResistance = "2.1 kΩ";

    [ObservableProperty]
    private string _outputResistance = "3.0 kΩ";

    [ObservableProperty]
    private string _lowerCutoffFreq = "20.5 Hz";

    [ObservableProperty]
    private string _upperCutoffFreq = "1.59 MHz";

    [ObservableProperty]
    private string _bandwidth = "1.59 MHz";
}

public partial class AnalysisResult : ObservableObject
{
    [ObservableProperty]
    private bool _isSuccess = true;

    [ObservableProperty]
    private string _circuitTitle = "共射极单管放大电路交流小信号与频响分析";

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private QPointInfo _qPoint = new();

    [ObservableProperty]
    private AcParametersInfo _acParameters = new();

    [ObservableProperty]
    private ObservableCollection<AnalysisStep> _steps = [];

    [ObservableProperty]
    private string _rawMnaLog = string.Empty;
}
