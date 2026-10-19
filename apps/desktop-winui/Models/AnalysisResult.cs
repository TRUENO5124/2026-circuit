using System.Collections.ObjectModel;
using CircuitAssistant.Engine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CircuitAssistant.Models;

public partial class QPointInfo : ObservableObject
{
    [ObservableProperty]
    private string _ibq = "—";

    [ObservableProperty]
    private string _icq = "—";

    [ObservableProperty]
    private string _vceq = "—";

    [ObservableProperty]
    private string _workingRegion = "尚未分析";

    [ObservableProperty]
    private bool _isActiveRegion;

    // ---------------------------------------------------------------------
    // Backend-only exact values (戴维南精确解). The UI deliberately shows the
    // textbook estimation result (教材估算法 + 结果最多两位小数); these fields
    // keep the exact solution available for diagnostics/tests/report internals
    // and are not bound in the UI.
    // ---------------------------------------------------------------------

    [ObservableProperty]
    private string _exactIbq = string.Empty;

    [ObservableProperty]
    private string _exactIcq = string.Empty;

    [ObservableProperty]
    private string _exactVceq = string.Empty;
}

/// <summary>
/// One group of derivation steps sharing a teaching phase (DC / AC equivalent
/// circuit). Groups without a title are rendered as a plain step list.
/// </summary>
public partial class AnalysisStepGroup : ObservableObject
{
    [ObservableProperty]
    private SolutionPhase _phase = SolutionPhase.None;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private bool _hasHeader;

    public ObservableCollection<AnalysisStep> Steps { get; } = [];
}

public partial class AcParametersInfo : ObservableObject
{
    [ObservableProperty]
    private string _voltageGain = "—";

    [ObservableProperty]
    private string _inputResistance = "—";

    [ObservableProperty]
    private string _outputResistance = "—";

    [ObservableProperty]
    private string _lowerCutoffFreq = "—";

    [ObservableProperty]
    private string _upperCutoffFreq = "—";

    [ObservableProperty]
    private string _bandwidth = "—";

    // ---------------------------------------------------------------------
    // Optional signal-source internal resistance (信号源内阻 Rs). Amplifier
    // topologies that identify a series source resistance before the input
    // coupling capacitor also report the source-to-output voltage gain
    // Aus = Av · Ri/(Rs+Ri). Both are only shown when HasSourceResistance.
    // ---------------------------------------------------------------------

    [ObservableProperty]
    private bool _hasSourceResistance;

    [ObservableProperty]
    private string _sourceResistance = "—";

    [ObservableProperty]
    private string _sourceVoltageGain = "—";
}

public partial class AnalysisResult : ObservableObject
{
    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private string _circuitTitle = "尚未分析";

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private QPointInfo _qPoint = new();

    [ObservableProperty]
    private AcParametersInfo _acParameters = new();

    [ObservableProperty]
    private ObservableCollection<AnalysisStep> _steps = [];

    /// <summary>
    /// Derivation steps grouped by teaching phase (DC/AC equivalent circuit), used
    /// by the "解题推导" tab. For solutions without phases this contains a single
    /// header-less group mirroring <see cref="Steps"/>.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AnalysisStepGroup> _stepGroups = [];

    [ObservableProperty]
    private string _rawMnaLog = string.Empty;

    /// <summary>
    /// Structured result of the general linear-network analyzer (MNA 2.0).
    /// Null for dedicated teaching strategies that do not use the MNA core.
    /// </summary>
    [ObservableProperty]
    private LinearNetworkInfo? _linearNetwork;

    /// <summary>
    /// AC phasor/frequency-response summary, mapped from
    /// <see cref="LinearNetworkInfo.FrequencyResponse"/> for direct binding.
    /// </summary>
    [ObservableProperty]
    private FrequencyResponseInfo? _frequencyResponse;

    /// <summary>
    /// Arbitrary-port Thévenin equivalents (resistance + open-circuit voltage).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PortEquivalentInfo> _portEquivalents = [];

    /// <summary>
    /// Computed AC sweep points (frequency, gain in dB, phase in degrees).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AcSweepPointInfo> _acSweepPoints = [];

    /// <summary>
    /// Solved DC node potentials of the linear network.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<NodePotentialInfo> _nodePotentials = [];
}
