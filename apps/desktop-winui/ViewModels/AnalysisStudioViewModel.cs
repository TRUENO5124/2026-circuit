using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CircuitAssistant.Models;
using CircuitAssistant.Services;
using CircuitAssistant.Engine;

namespace CircuitAssistant.ViewModels;

public partial class AnalysisStudioViewModel : ObservableObject
{
    private bool _isRestoringProject;
    private bool _suppressParamSync;
    private bool _isProjectTransition;
    private bool _isSavingProject;
    private long _projectRevision;
    private DateTime _projectCreatedAt = DateTime.Now;
    private string _projectDescription = "";
    private readonly HashSet<CircuitElement> _observedElements = new();
    private readonly HashSet<CircuitWire> _observedWires = new();
    public Func<Task<bool>>? ConfirmDiscardChangesAsync { get; set; }

    public AnalysisStudioViewModel()
    {
        App.LogTrace("AnalysisStudioViewModel.ctor: Starting");
        Elements.CollectionChanged += ProjectCollectionChanged;
        Wires.CollectionChanged += ProjectCollectionChanged;
        InitializeSymbols();
        StatusMessage = "请新建工程、加载教学模板或打开已保存的工程。";
        App.LogTrace("AnalysisStudioViewModel.ctor: Initialized default project state");
    }

    // Available unit options
    public UnitOption[] ResistanceUnits => EngineeringUnits.ResistanceUnits;
    public UnitOption[] CapacitanceUnits => EngineeringUnits.CapacitanceUnits;
    public UnitOption[] VoltageUnits => EngineeringUnits.VoltageUnits;
    public UnitOption[] FrequencyUnits => EngineeringUnits.FrequencyUnits;

    // Parameter: Vcc
    [ObservableProperty]
    private double _vccNumber = 12.0;

    [ObservableProperty]
    private UnitOption _selectedVccUnit = EngineeringUnits.VoltageUnits[1];

    // Parameter: Rb1
    [ObservableProperty]
    private double _rb1Number = 51.0;

    [ObservableProperty]
    private UnitOption _selectedRb1Unit = EngineeringUnits.ResistanceUnits[1];

    // Parameter: Rb2
    [ObservableProperty]
    private double _rb2Number = 15.0;

    [ObservableProperty]
    private UnitOption _selectedRb2Unit = EngineeringUnits.ResistanceUnits[1];

    // Parameter: Rc
    [ObservableProperty]
    private double _rcNumber = 3.0;

    [ObservableProperty]
    private UnitOption _selectedRcUnit = EngineeringUnits.ResistanceUnits[1];

    // Parameter: Re
    [ObservableProperty]
    private double _reNumber = 1.0;

    [ObservableProperty]
    private UnitOption _selectedReUnit = EngineeringUnits.ResistanceUnits[1];

    // Parameter: Rl
    [ObservableProperty]
    private double _rlNumber = 10.0;

    [ObservableProperty]
    private UnitOption _selectedRlUnit = EngineeringUnits.ResistanceUnits[1];

    // Parameter: C1
    [ObservableProperty]
    private double _c1Number = 10.0;

    [ObservableProperty]
    private UnitOption _selectedC1Unit = EngineeringUnits.CapacitanceUnits[2];

    // Parameter: C2
    [ObservableProperty]
    private double _c2Number = 10.0;

    [ObservableProperty]
    private UnitOption _selectedC2Unit = EngineeringUnits.CapacitanceUnits[2];

    // Parameter: Ce
    [ObservableProperty]
    private double _ceNumber = 47.0;

    [ObservableProperty]
    private UnitOption _selectedCeUnit = EngineeringUnits.CapacitanceUnits[2];

    // Parameter: Beta (hFE)
    [ObservableProperty]
    private double _betaNumber = 100.0;

    // Parameter: BJT base spreading resistance r_bb' in Ω (0 = teaching default:
    // 200 Ω for common emitter / common collector, 50 Ω for common base).
    [ObservableProperty]
    private double _rbbNumber;

    // Parameter: BJT directly-given small-signal input resistance r_be = h_ie in Ω
    // (0 = compute from the Q point with r_bb' + (1+β)·U_T/I_EQ).
    [ObservableProperty]
    private double _rbeNumber;

    // Parameter: Frequency
    [ObservableProperty]
    private double _freqNumber = 1.0;

    [ObservableProperty]
    private UnitOption _selectedFreqUnit = EngineeringUnits.FrequencyUnits[1];

    partial void OnVccNumberChanged(double value) => SyncParamToElement("VCC", value, SelectedVccUnit?.Symbol);
    partial void OnSelectedVccUnitChanged(UnitOption value) => SyncParamToElement("VCC", VccNumber, value.Symbol);

    partial void OnRb1NumberChanged(double value) => SyncParamToElement("Rb1", value, SelectedRb1Unit?.Symbol);
    partial void OnSelectedRb1UnitChanged(UnitOption value) => SyncParamToElement("Rb1", Rb1Number, value.Symbol);

    partial void OnRb2NumberChanged(double value) => SyncParamToElement("Rb2", value, SelectedRb2Unit?.Symbol);
    partial void OnSelectedRb2UnitChanged(UnitOption value) => SyncParamToElement("Rb2", Rb2Number, value.Symbol);

    partial void OnRcNumberChanged(double value) => SyncParamToElement("Rc", value, SelectedRcUnit?.Symbol);
    partial void OnSelectedRcUnitChanged(UnitOption value) => SyncParamToElement("Rc", RcNumber, value.Symbol);

    partial void OnReNumberChanged(double value) => SyncParamToElement("Re", value, SelectedReUnit?.Symbol);
    partial void OnSelectedReUnitChanged(UnitOption value) => SyncParamToElement("Re", ReNumber, value.Symbol);

    partial void OnRlNumberChanged(double value) => SyncParamToElement("RL", value, SelectedRlUnit?.Symbol);
    partial void OnSelectedRlUnitChanged(UnitOption value) => SyncParamToElement("RL", RlNumber, value.Symbol);

    partial void OnC1NumberChanged(double value) => SyncParamToElement("C1", value, SelectedC1Unit?.Symbol);
    partial void OnSelectedC1UnitChanged(UnitOption value) => SyncParamToElement("C1", C1Number, value.Symbol);

    partial void OnC2NumberChanged(double value) => SyncParamToElement("C2", value, SelectedC2Unit?.Symbol);
    partial void OnSelectedC2UnitChanged(UnitOption value) => SyncParamToElement("C2", C2Number, value.Symbol);

    partial void OnCeNumberChanged(double value) => SyncParamToElement("Ce", value, SelectedCeUnit?.Symbol);
    partial void OnSelectedCeUnitChanged(UnitOption value) => SyncParamToElement("Ce", CeNumber, value.Symbol);

    partial void OnBetaNumberChanged(double value) => SyncParamToElement("Q1", value, "β");

    partial void OnRbbNumberChanged(double value)
    {
        // Clearing the box yields NaN; treat it as "auto" (0) instead of an invalid
        // project value.
        if (!double.IsFinite(value) || value < 0) { RbbNumber = 0; return; }
        SyncBjtSmallSignalToElements(rbb: value, rbe: null);
    }

    partial void OnRbeNumberChanged(double value)
    {
        if (!double.IsFinite(value) || value < 0) { RbeNumber = 0; return; }
        SyncBjtSmallSignalToElements(rbb: null, rbe: value);
    }

    partial void OnFreqNumberChanged(double value) => SyncFrequencyToSource();
    partial void OnSelectedFreqUnitChanged(UnitOption value) => SyncFrequencyToSource();

    private void SyncFrequencyToSource()
    {
        if (_suppressParamSync) return;
        var source = SelectedElement?.IsAcSource == true ? SelectedElement : Elements.FirstOrDefault(e => e.IsAcSource);
        if (source != null) source.FrequencyHz = FreqNumber * SelectedFreqUnit.Multiplier;
    }

    // State
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterStatusLabel))]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _showMnaLogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterStatusLabel))]
    private AnalysisResult _result = new();

    public ObservableCollection<AnalysisStep> SolutionSteps => Result.Steps;

    partial void OnResultChanged(AnalysisResult value)
    {
        AnalysisErrorMessage = string.Empty;
        OnPropertyChanged(nameof(SolutionSteps));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterStatusMessage))]
    private string _statusMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnalysisError))]
    [NotifyPropertyChangedFor(nameof(FooterStatusMessage))]
    [NotifyPropertyChangedFor(nameof(FooterStatusLabel))]
    private string _analysisErrorMessage = string.Empty;

    public bool HasAnalysisError => !string.IsNullOrEmpty(AnalysisErrorMessage);
    public string FooterStatusMessage => HasAnalysisError ? AnalysisErrorMessage : StatusMessage;
    public string FooterStatusLabel => HasAnalysisError ? "ERROR" : IsAnalyzing ? "分析中" : Result.IsSuccess ? "完成" : "提示";

    // Project State
    [ObservableProperty]
    private bool _hasActiveProject;

    [ObservableProperty]
    private bool _isTeachingTemplate = true;

    [ObservableProperty]
    private string _projectName = "未命名电路工程";

    [ObservableProperty]
    private string _projectFileName = "CommonEmitter_Amp.sch";

    // Workspace & Layout
    [ObservableProperty]
    private string _cursorCoordinateText = "X: 0 | Y: 0";

    [ObservableProperty]
    private int _selectedRightTabIndex;

    [ObservableProperty]
    private bool _isBottomDrawerOpen;

    [ObservableProperty]
    private string _symbolSearchQuery = "";

    [ObservableProperty]
    private string _selectedCategory = "全部";

    [ObservableProperty]
    private CircuitSymbolItem? _selectedSymbol;

    [ObservableProperty]
    private CircuitElement? _selectedElement;

    [ObservableProperty]
    private CircuitWire? _selectedWire;

    public UnitOption[] SelectedElementAvailableUnits => SelectedElement?.AvailableUnits ?? [];

    partial void OnSelectedElementChanged(CircuitElement? value)
    {
        if (value != null)
        {
            value.PropertyChanged += SelectedElement_PropertyChanged;
            SelectedWire = null;
        }
        OnPropertyChanged(nameof(SelectedElementAvailableUnits));
    }

    partial void OnSelectedElementChanging(CircuitElement? value)
    {
        if (SelectedElement != null) SelectedElement.PropertyChanged -= SelectedElement_PropertyChanged;
    }

    partial void OnSelectedWireChanged(CircuitWire? value)
    {
        if (value != null)
        {
            SelectedElement = null;
        }
    }

    [ObservableProperty]
    private string? _currentProjectFilePath;

    [ObservableProperty]
    private bool _isProjectModified;

    // Interactive Canvas Elements & Wires
    public ObservableCollection<CircuitElement> Elements { get; } = new();
    public ObservableCollection<CircuitWire> Wires { get; } = new();

    private void ProjectCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Reset notifications (Clear) have no OldItems; detach using tracked sets.
        foreach (var item in _observedElements.Where(item => !Elements.Contains(item)).ToList())
        {
            item.PropertyChanged -= ProjectItemChanged;
            _observedElements.Remove(item);
        }
        foreach (var item in Elements.Where(item => !_observedElements.Contains(item)))
        {
            item.PropertyChanged += ProjectItemChanged;
            _observedElements.Add(item);
        }
        foreach (var item in _observedWires.Where(item => !Wires.Contains(item)).ToList())
        {
            item.PropertyChanged -= ProjectItemChanged;
            _observedWires.Remove(item);
        }
        foreach (var item in Wires.Where(item => !_observedWires.Contains(item)))
        {
            item.PropertyChanged += ProjectItemChanged;
            _observedWires.Add(item);
        }
        MarkProjectChanged();
    }

    private void ProjectItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Selection, display projections and extracted NetIds are not user edits.
        if (e.PropertyName is "IsSelected" or "NetId" or "ConnectedNetId" or "FormattedValueDisplay" or
            "PositionDisplay" or "RotationDisplay" or "AvailableUnits" or "IsAcSource") return;
        MarkProjectChanged();
    }

    private void MarkProjectChanged()
    {
        if (_isRestoringProject || !HasActiveProject) return;
        _projectRevision++;
        IsProjectModified = true;
        // A report may only be generated from the same state that produced the
        // displayed result; editing the circuit invalidates the cached solution.
        _lastSolution = null;
        _lastNetlist = null;
        if (Result.IsSuccess || Result.Steps.Count > 0 || HasAnalysisError)
        {
            Result = new AnalysisResult { SummaryText = "电路已修改，请重新求解。" };
            StatusMessage = Result.SummaryText;
        }
    }

    public async Task<bool> ConfirmProjectTransitionAsync()
    {
        if (_isSavingProject || IsAnalyzing) { StatusMessage = "请等待当前保存或求解完成。"; return false; }
        if (!HasActiveProject || !IsProjectModified) return true;
        if (ConfirmDiscardChangesAsync == null) { StatusMessage = "工程尚未保存，请先保存后再切换。"; return false; }
        return await ConfirmDiscardChangesAsync();
    }

    private async Task RequestCreateProjectAsync(string template)
    {
        if (_isProjectTransition) return;
        _isProjectTransition = true;
        try { if (await ConfirmProjectTransitionAsync()) CreateNewProject(template); }
        finally { _isProjectTransition = false; }
    }

    private readonly List<(List<CircuitElement> Elements, List<CircuitWire> Wires)> _undoStack = new();
    private readonly List<(List<CircuitElement> Elements, List<CircuitWire> Wires)> _redoStack = new();
    private bool _isPerformingUndoRedo = false;

    public void PushUndo()
    {
        if (_isPerformingUndoRedo) return;
        var elementsCopy = Elements.Select(e => e.Clone()).ToList();
        var wiresCopy = Wires.Select(w => w.Clone()).ToList();
        _undoStack.Add((elementsCopy, wiresCopy));
        _redoStack.Clear();
        if (_undoStack.Count > 50)
        {
            _undoStack.RemoveAt(0);
        }
    }

    [RelayCommand]
    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        _isPerformingUndoRedo = true;
        try
        {
            var currentElements = Elements.Select(e => e.Clone()).ToList();
            var currentWires = Wires.Select(w => w.Clone()).ToList();
            _redoStack.Add((currentElements, currentWires));

            var lastIdx = _undoStack.Count - 1;
            var (prevElements, prevWires) = _undoStack[lastIdx];
            _undoStack.RemoveAt(lastIdx);

            Elements.Clear();
            foreach (var el in prevElements) Elements.Add(el);
            Wires.Clear();
            foreach (var w in prevWires) Wires.Add(w);
            SelectedElement = null;
            SelectedWire = null;
            foreach (var el in Elements) SyncElementToModelProperty(el);
            StatusMessage = "已撤销上一步操作 (Ctrl+Z)";
        }
        finally
        {
            _isPerformingUndoRedo = false;
        }
    }

    [RelayCommand]
    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        _isPerformingUndoRedo = true;
        try
        {
            var currentElements = Elements.Select(e => e.Clone()).ToList();
            var currentWires = Wires.Select(w => w.Clone()).ToList();
            _undoStack.Add((currentElements, currentWires));

            var lastIdx = _redoStack.Count - 1;
            var (nextElements, nextWires) = _redoStack[lastIdx];
            _redoStack.RemoveAt(lastIdx);

            Elements.Clear();
            foreach (var el in nextElements) Elements.Add(el);
            Wires.Clear();
            foreach (var w in nextWires) Wires.Add(w);
            SelectedElement = null;
            SelectedWire = null;
            foreach (var el in Elements) SyncElementToModelProperty(el);
            StatusMessage = "已重做操作 (Ctrl+Y)";
        }
        finally
        {
            _isPerformingUndoRedo = false;
        }
    }

    public ObservableCollection<CircuitSymbolItem> AllSymbols { get; } = new();
    public ObservableCollection<CircuitSymbolItem> FilteredSymbols { get; } = new();

    public string[] SymbolCategories => new[] { "全部", "无源器件", "激励电源", "半导体管", "双极型三极管", "增强型场效应管", "耗尽型场效应管", "结型场效应管", "受控源", "参考地" };

    private void InitializeSymbols()
    {
        AllSymbols.Clear();

        // 1. Reference / Ground & Power (参考地与电源)
        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "reference.ground",
            Name = "参考地 (GND)",
            Category = "参考地",
            Subtitle = "0V",
            ValueText = "0 V",
            Glyph = "\uE8D7",
            Description = "基准零电位参考节点，统一电路地电位基准",
            Standard = "GB / IEC",
            DefaultNumber = 0,
            DefaultUnit = "V",
            ElementType = ElementType.Ground
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "reference.vcc_port",
            Name = "电源端口 (+VCC)",
            Category = "参考地",
            Subtitle = "+12 V",
            ValueText = "12 V",
            Glyph = "\uE781",
            Description = "正电源基准端口 (+VCC)，直接向电路网络注入 +12V 直流工作电位",
            Standard = "GB / IEC",
            DefaultNumber = 12,
            DefaultUnit = "V",
            ElementType = ElementType.VccPort
        });

        // 2. Passive Components (无源器件)
        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "passive.resistor",
            Name = "电阻器 (Resistor)",
            Category = "无源器件",
            Subtitle = "1 kΩ",
            ValueText = "1 kΩ",
            Glyph = "\uE8B9",
            Description = "欧姆定律无源线性电阻，分压偏置与交流负载",
            Standard = "IEC / ANSI",
            DefaultNumber = 1,
            DefaultUnit = "kΩ",
            ElementType = ElementType.Resistor
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "passive.capacitor",
            Name = "电容器 (Capacitor)",
            Category = "无源器件",
            Subtitle = "10 μF",
            ValueText = "10 μF",
            Glyph = "\uE756",
            Description = "隔直通交阻容耦合元件，滤波与交流旁路",
            Standard = "IEC",
            DefaultNumber = 10,
            DefaultUnit = "μF",
            ElementType = ElementType.Capacitor
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "passive.capacitor_polarized",
            Name = "电解电容 (Polarized)",
            Category = "无源器件",
            Subtitle = "47 μF (有极性)",
            ValueText = "47 μF",
            Glyph = "\uE756",
            Description = "大容量极性电解电容，用于电源平滑滤波、低频交流旁路与隔直耦合，需严格注意正负极性",
            Standard = "IEC / IEEE",
            DefaultNumber = 47,
            DefaultUnit = "μF",
            ElementType = ElementType.CapacitorPolarized
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "passive.inductor",
            Name = "电感器 (Inductor)",
            Category = "无源器件",
            Subtitle = "10 mH",
            ValueText = "10 mH",
            Glyph = "\uE74E",
            Description = "通直流阻交流电感元件，高频滤波与储能",
            Standard = "IEC / ANSI",
            DefaultNumber = 10,
            DefaultUnit = "mH",
            ElementType = ElementType.Inductor
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "passive.transformer",
            Name = "理想变压器",
            Category = "无源器件",
            Subtitle = "1:1",
            ValueText = "1:1",
            Glyph = "\uE8D7",
            Description = "双端口阻抗变换器，实现电压电流隔离与变换",
            Standard = "IEC",
            DefaultNumber = 1,
            DefaultUnit = "比",
            ElementType = ElementType.Transformer
        });

        // 3. Independent Sources (激励电源)
        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "source.vcc",
            Name = "直流电源 (VCC)",
            Category = "激励电源",
            Subtitle = "12 V",
            ValueText = "12 V",
            Glyph = "\uE781",
            Description = "恒定偏置直流供电电源 (VCC)，提供电路正向工作偏压",
            Standard = "GB / IEC",
            DefaultNumber = 12,
            DefaultUnit = "V",
            ElementType = ElementType.DcVoltageSource
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "source.v_dc",
            Name = "直流电压源 (VDC)",
            Category = "激励电源",
            Subtitle = "12 V",
            ValueText = "12 V",
            Glyph = "\uE781",
            Description = "恒定偏置直流电压源，为放大电路提供稳定静态偏置",
            Standard = "IEC",
            DefaultNumber = 12,
            DefaultUnit = "V",
            ElementType = ElementType.DcVoltageSource
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "source.i_dc",
            Name = "直流电流源 (IDC)",
            Category = "激励电源",
            Subtitle = "2 mA",
            ValueText = "2 mA",
            Glyph = "\uE781",
            Description = "恒定偏置直流电流源，提供基准偏置电流",
            Standard = "IEC",
            DefaultNumber = 2,
            DefaultUnit = "mA",
            ElementType = ElementType.DcCurrentSource
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "source.v_ac_small_signal",
            Name = "交流小信号源 (vs)",
            Category = "激励电源",
            Subtitle = "10 mV",
            ValueText = "10 mV",
            Glyph = "\uE944",
            Description = "正弦交流微变等效激励，用于动态放大指标推导",
            Standard = "IEC",
            DefaultNumber = 10,
            DefaultUnit = "mV",
            ElementType = ElementType.AcVoltageSource
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "source.i_ac_small_signal",
            Name = "交流电流源",
            Category = "激励电源",
            Subtitle = "1 mA",
            ValueText = "1 mA",
            Glyph = "\uE944",
            Description = "交流小信号电流激励，正弦微变电流源",
            Standard = "IEC",
            DefaultNumber = 1,
            DefaultUnit = "mA",
            ElementType = ElementType.AcCurrentSource
        });

        // 4. Semiconductors (半导体管)
        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "diode.standard",
            Name = "二极管 (Diode)",
            Category = "半导体管",
            Subtitle = "硅管 VF≈0.7 V",
            ValueText = "VF = 0.7 V",
            Glyph = "\uE8EF",
            Description = "单向导电二极管；默认硅管 VF≈0.7 V，可在参数面板切换锗管（≈0.3 V）",
            Standard = "IEC",
            DefaultNumber = 0.7,
            DefaultUnit = "V",
            DefaultMaterial = SemiconductorMaterial.Silicon,
            ElementType = ElementType.Diode
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "bjt.npn",
            Name = "NPN 双极型三极管",
            Category = "双极型三极管",
            Subtitle = "β = 100",
            ValueText = "β = 100",
            Glyph = "\uE943",
            Description = "NPN 型双极型三极管，默认集电极在上、发射极在下，发射极箭头向外；硅管 VBE≈0.7 V，可切换锗管（≈0.3 V）",
            Standard = "GB / IEC",
            DefaultNumber = 100,
            DefaultUnit = "β",
            DefaultMaterial = SemiconductorMaterial.Silicon,
            ElementType = ElementType.BjtNpn
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "bjt.pnp",
            Name = "PNP 双极型三极管",
            Category = "双极型三极管",
            Subtitle = "β = 100",
            ValueText = "β = 100",
            Glyph = "\uE943",
            Description = "PNP 型双极型三极管，默认发射极在上、集电极在下，发射极箭头向内；硅管 VEB≈0.7 V，可切换锗管（≈0.3 V）",
            Standard = "GB / IEC",
            DefaultNumber = 100,
            DefaultUnit = "β",
            DefaultMaterial = SemiconductorMaterial.Silicon,
            ElementType = ElementType.BjtPnp
        });

        // 5. Enhancement MOSFETs (增强型场效应管)
        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "fet.mosfet_n_enh",
            Name = "N沟道 增强型MOS管",
            Category = "增强型场效应管",
            Subtitle = "Vth = 1.5 V",
            ValueText = "Vth = 1.5 V",
            Glyph = "\uE8EF",
            Description = "N沟道绝缘栅增强型场效应管 (NMOS Enh)，正栅压导通，高输入阻抗",
            Standard = "ANSI / IEC",
            DefaultNumber = 1.5,
            DefaultUnit = "V",
            ElementType = ElementType.MosfetNEnh
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "fet.mosfet_p_enh",
            Name = "P沟道 增强型MOS管",
            Category = "增强型场效应管",
            Subtitle = "Vth = -1.5 V",
            ValueText = "Vth = -1.5 V",
            Glyph = "\uE8EF",
            Description = "P沟道绝缘栅增强型场效应管 (PMOS Enh)，负栅压导通，互补对称CMOS",
            Standard = "ANSI / IEC",
            DefaultNumber = -1.5,
            DefaultUnit = "V",
            ElementType = ElementType.MosfetPEnh
        });

        // 6. Depletion MOSFETs (耗尽型场效应管)
        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "fet.mosfet_n_dep",
            Name = "N沟道 耗尽型MOS管",
            Category = "耗尽型场效应管",
            Subtitle = "VGS(off) = -2.0 V",
            ValueText = "VGS(off) = -2.0 V",
            Glyph = "\uE8EF",
            Description = "N沟道绝缘栅耗尽型场效应管 (NMOS Dep)，零栅压导通，参数为夹断电压 VGS(off)",
            Standard = "ANSI / IEC",
            DefaultNumber = -2.0,
            DefaultUnit = "V",
            ElementType = ElementType.MosfetNDep
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "fet.mosfet_p_dep",
            Name = "P沟道 耗尽型MOS管",
            Category = "耗尽型场效应管",
            Subtitle = "VGS(off) = 2.0 V",
            ValueText = "VGS(off) = 2.0 V",
            Glyph = "\uE8EF",
            Description = "P沟道绝缘栅耗尽型场效应管 (PMOS Dep)，零栅压导通，参数为夹断电压 VGS(off)",
            Standard = "ANSI / IEC",
            DefaultNumber = 2.0,
            DefaultUnit = "V",
            ElementType = ElementType.MosfetPDep
        });

        // 7. JFETs (结型场效应管)
        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "fet.jfet_n",
            Name = "N沟道 结型场效应管",
            Category = "结型场效应管",
            Subtitle = "VGS(off) = -2.5 V",
            ValueText = "VGS(off) = -2.5 V",
            Glyph = "\uE8EF",
            Description = "N沟道结型场效应管 (N-JFET)，栅极箭头指向沟道，参数为夹断电压 VGS(off)",
            Standard = "ANSI / IEC",
            DefaultNumber = -2.5,
            DefaultUnit = "V",
            ElementType = ElementType.JfetN
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "fet.jfet_p",
            Name = "P沟道 结型场效应管",
            Category = "结型场效应管",
            Subtitle = "VGS(off) = 2.5 V",
            ValueText = "VGS(off) = 2.5 V",
            Glyph = "\uE8EF",
            Description = "P沟道结型场效应管 (P-JFET)，栅极箭头背离沟道，参数为夹断电压 VGS(off)",
            Standard = "ANSI / IEC",
            DefaultNumber = 2.5,
            DefaultUnit = "V",
            ElementType = ElementType.JfetP
        });

        // 8. Controlled Sources (受控源)
        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "controlled.vcvs",
            Name = "压控电压源 (VCVS)",
            Category = "受控源",
            Subtitle = "μ = 10",
            ValueText = "μ = 10",
            Glyph = "\uE8D7",
            Description = "电压控制的电压源，理想运算放大器基本微变模型",
            Standard = "IEEE",
            DefaultNumber = 10,
            DefaultUnit = "倍",
            ElementType = ElementType.Vcvs
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "controlled.vccs",
            Name = "压控电流源 (VCCS)",
            Category = "受控源",
            Subtitle = "gm = 5 mS",
            ValueText = "gm = 5 mS",
            Glyph = "\uE8D7",
            Description = "跨导受控电流源，场效应管与晶体管微变模型",
            Standard = "IEEE",
            DefaultNumber = 5,
            DefaultUnit = "mS",
            ElementType = ElementType.Vccs
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "controlled.ccvs",
            Name = "流控电压源 (CCVS)",
            Category = "受控源",
            Subtitle = "rm = 100 Ω",
            ValueText = "rm = 100 Ω",
            Glyph = "\uE8D7",
            Description = "互阻受控电压源，电流传感与转换微变模型",
            Standard = "IEEE",
            DefaultNumber = 100,
            DefaultUnit = "Ω",
            ElementType = ElementType.Ccvs
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "controlled.cccs",
            Name = "流控电流源 (CCCS)",
            Category = "受控源",
            Subtitle = "β = 50",
            ValueText = "β = 50",
            Glyph = "\uE8D7",
            Description = "电流受控电流源，双极型晶体管简化微变模型",
            Standard = "IEEE",
            DefaultNumber = 50,
            DefaultUnit = "倍",
            ElementType = ElementType.Cccs
        });

        FilterSymbols();
    }

    public void FilterSymbols()
    {
        FilteredSymbols.Clear();
        foreach (var sym in AllSymbols)
        {
            bool matchCategory = SelectedCategory == "全部" || sym.Category == SelectedCategory;
            bool matchQuery = string.IsNullOrWhiteSpace(SymbolSearchQuery) ||
                              sym.Name.Contains(SymbolSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                              sym.Subtitle.Contains(SymbolSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                              sym.Id.Contains(SymbolSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                              sym.Description.Contains(SymbolSearchQuery, StringComparison.OrdinalIgnoreCase);

            if (matchCategory && matchQuery)
            {
                FilteredSymbols.Add(sym);
            }
        }
    }

    partial void OnSymbolSearchQueryChanged(string value) => FilterSymbols();
    partial void OnSelectedCategoryChanged(string value) => FilterSymbols();

    public void InitializeDefaultCircuitElements()
    {
        Elements.Clear();
        Wires.Clear();

        var template = CircuitTemplateFactory.CreateCommonEmitterTemplate(
            VccNumber, Rb1Number, Rb2Number, RcNumber, ReNumber, RlNumber,
            C1Number, C2Number, CeNumber, BetaNumber, 10.0);

        foreach (var el in template.Elements)
        {
            Elements.Add(el);
        }

        foreach (var w in template.Wires)
        {
            Wires.Add(w);
        }
    }

    private void CreateNewProject(string? templateType = null)
    {
        _isRestoringProject = true;
        try
        {
            CurrentProjectFilePath = null;
            _projectCreatedAt = DateTime.Now;
            _projectDescription = "";
            SelectedSymbol = null;
            SelectedWire = null;
            _undoStack.Clear();
            _redoStack.Clear();
            _lastSolution = null;
            _lastNetlist = null;
            Result = new AnalysisResult();
            HasActiveProject = true;
            SelectedElement = null;
            Elements.Clear();
            Wires.Clear();

            if (templateType == "blank")
            {
                IsTeachingTemplate = false;
                var template = CircuitTemplateFactory.CreateBlankTemplate();
                ProjectName = template.ProjectName;
                ProjectFileName = template.ProjectFileName;
                StatusMessage = "空白电路工程已就绪 · 可从左侧元件库点击或拖拽添加元件至画布";
            }
            else if (templateType == "rc_transient")
            {
                IsTeachingTemplate = false;
                var template = CircuitTemplateFactory.CreateRcTransientTemplate();
                ProjectName = template.ProjectName;
                ProjectFileName = template.ProjectFileName;
                foreach (var el in template.Elements) Elements.Add(el);
                foreach (var w in template.Wires) Wires.Add(w);
                StatusMessage = $"工程已载入: {ProjectName}";
            }
            else if (templateType == "ac_coupled")
            {
                IsTeachingTemplate = true;
                var template = CircuitTemplateFactory.CreateRcCoupledTemplate(
                    VccNumber, 300.0, RcNumber, RlNumber, C1Number, C2Number, BetaNumber, 10.0);
                ProjectName = template.ProjectName;
                ProjectFileName = template.ProjectFileName;
                foreach (var el in template.Elements) Elements.Add(el);
                foreach (var w in template.Wires) Wires.Add(w);
                StatusMessage = $"工程已载入: {ProjectName}";
            }
            else if (templateType == "common_base")
            {
                IsTeachingTemplate = true;
                var template = CircuitTemplateFactory.CreateCommonBaseTemplate(
                    VccNumber, Rb1Number, Rb2Number, RcNumber, ReNumber, RlNumber, C1Number, C2Number, CeNumber, BetaNumber, 10.0);
                ProjectName = template.ProjectName;
                ProjectFileName = template.ProjectFileName;
                foreach (var el in template.Elements) Elements.Add(el);
                foreach (var w in template.Wires) Wires.Add(w);
                StatusMessage = $"工程已载入: {ProjectName}";
            }
            else // "common_emitter", "default", null, or anything else
            {
                IsTeachingTemplate = true;
                ProjectName = "分压式偏置共射放大电路";
                ProjectFileName = "CommonEmitter_Amp.sch";
                SetDefaultParameters();
                InitializeDefaultCircuitElements();
                StatusMessage = $"工程已载入: {ProjectName}";
            }
            RefreshBjtMacrosFromElements();
            foreach (var source in Elements.Where(e => e.IsAcSource))
                source.FrequencyHz = FreqNumber * SelectedFreqUnit.Multiplier;
            _projectRevision++;
            IsProjectModified = true;
        }
        finally { _isRestoringProject = false; }
    }

    [RelayCommand]
    public Task CreateBlankProject() => RequestCreateProjectAsync("blank");

    [RelayCommand]
    public Task CreateDefaultProject() => RequestCreateProjectAsync("common_emitter");

    [RelayCommand]
    public Task CreateAcCoupledProject() => RequestCreateProjectAsync("ac_coupled");

    [RelayCommand]
    public Task CreateCommonBaseProject() => RequestCreateProjectAsync("common_base");

    [RelayCommand]
    public Task CreateRcTransientProject() => RequestCreateProjectAsync("rc_transient");

    [RelayCommand]
    public Task SaveProjectAsync() => SaveProjectCoreAsync(saveAs: false);

    [RelayCommand]
    public Task SaveProjectAsAsync() => SaveProjectCoreAsync(saveAs: true);

    private async Task SaveProjectCoreAsync(bool saveAs)
    {
        if (!HasActiveProject || _isSavingProject || IsAnalyzing) return;
        _isSavingProject = true;
        try
        {
            string? path = saveAs ? null : CurrentProjectFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var file = await ProjectManagerService.PickSaveProjectFileAsync(
                    System.IO.Path.GetFileNameWithoutExtension(ProjectFileName) + ".circuit");
                if (file == null) return;
                path = file.Path;
            }
            long revision = _projectRevision;
            var data = CollectProjectData();
            data.ProjectFileName = System.IO.Path.GetFileName(path);
            // 保存时保留工程标题（ProjectName），只有未命名工程才回退到文件名。
            // 旧行为每次保存都把 ProjectName 覆盖为文件名，导致教材示例工程（B07/D06 等）
            // 被改名、生成器 --verify 的工程名校验失败，也丢失了教学工程标题。
            if (string.IsNullOrWhiteSpace(data.ProjectName) ||
                string.Equals(data.ProjectName, "未命名电路工程", StringComparison.Ordinal))
            {
                data.ProjectName = System.IO.Path.GetFileNameWithoutExtension(path);
            }
            await ProjectManagerService.SaveProjectToFileAsync(path, data);
            CurrentProjectFilePath = path;
            ProjectFileName = data.ProjectFileName;
            ProjectName = data.ProjectName;
            IsProjectModified = _projectRevision != revision;
            StatusMessage = IsProjectModified ? "工程快照已保存；保存期间发生的修改尚未保存。" : $"工程已保存至: {ProjectFileName}";
        }
        catch (Exception ex) { StatusMessage = $"保存工程失败: {ex.Message}"; }
        finally { _isSavingProject = false; }
    }

    [RelayCommand]
    public async Task OpenProjectAsync()
    {
        if (_isProjectTransition) return;
        _isProjectTransition = true;
        try
        {
            if (!await ConfirmProjectTransitionAsync()) return;
            long revision = _projectRevision;
            var file = await ProjectManagerService.PickOpenProjectFileAsync();
            if (file == null) return;
            // The same "open" entry point also accepts Web/Python CircuitGraph
            // JSON (e.g. the prototype benchmark seeds). Native project JSON keeps
            // using the strict DTO loader; a Web graph is converted into a native
            // in-memory project and stays "modified/unsaved" so it cannot silently
            // overwrite the imported JSON on Ctrl+S.
            var rawJson = await System.IO.File.ReadAllTextAsync(file.Path);
            bool importedWebGraph = WebCircuitGraphImporter.LooksLikeWebCircuitGraph(rawJson);
            var data = importedWebGraph
                ? WebCircuitGraphImporter.Import(rawJson, System.IO.Path.GetFileNameWithoutExtension(file.Name))
                : await ProjectManagerService.LoadProjectFromFileAsync(file.Path);
            var (elements, wires, parameters) = data.ToModel();
            // Opening performs all validation before replacing the current document.
            if (_projectRevision != revision && !await ConfirmProjectTransitionAsync()) return;
            _isRestoringProject = true;
            try
            {
                SelectedElement = null;
                SelectedWire = null;
                SelectedSymbol = null;
                Elements.Clear();
                Wires.Clear();
                foreach (var el in elements) Elements.Add(el);
                foreach (var wire in wires) Wires.Add(wire);
                ApplyParameters(parameters, data.ParameterUnits);
                ProjectName = data.ProjectName;
                ProjectFileName = importedWebGraph
                    ? System.IO.Path.GetFileNameWithoutExtension(file.Name) + ".circuit"
                    : file.Name;
                CurrentProjectFilePath = importedWebGraph ? null : file.Path;
                _projectCreatedAt = data.CreatedAt;
                _projectDescription = data.Description;
                IsTeachingTemplate = data.IsTeachingTemplate;
                HasActiveProject = true;
                _lastSolution = null;
                _lastNetlist = null;
                Result = new AnalysisResult();
                _undoStack.Clear();
                _redoStack.Clear();
                _projectRevision++;
                IsProjectModified = importedWebGraph;
            }
            finally { _isRestoringProject = false; }
            StatusMessage = importedWebGraph
                ? $"已从 Web/Python CircuitGraph 导入并转换为原生工程: {file.Name} (元件: {Elements.Count}, 导线: {Wires.Count}) · 请另存为原生工程"
                : $"工程已打开: {file.Name} (元件: {Elements.Count}, 导线: {Wires.Count})";
        }
        catch (Exception ex) { StatusMessage = $"打开工程失败: {ex.Message}"; }
        finally { _isProjectTransition = false; }
    }

    [RelayCommand]
    public async Task ExportSpiceNetlistAsync()
    {
        if (!HasActiveProject || _isSavingProject || IsAnalyzing) return;
        try
        {
            var snapshot = CollectProjectData();
            var (elements, wires, _) = snapshot.ToModel();
            var netlist = Engine.NetlistExtractor.Extract(elements, wires);
            var validation = netlist.Validate();
            if (!validation.IsValid) throw new InvalidOperationException(string.Join("\n", validation.Issues.Where(i => i.Severity == NetlistValidationSeverity.Error).Select(i => i.Message)));
            var unsupported = elements.Where(e => e.Type is not (ElementType.Resistor or ElementType.Capacitor or ElementType.CapacitorPolarized or
                ElementType.Inductor or ElementType.DcVoltageSource or ElementType.AcVoltageSource or ElementType.DcCurrentSource or
                ElementType.Diode or ElementType.BjtNpn or ElementType.BjtPnp or ElementType.Ground)).ToList();
            if (unsupported.Count > 0) throw new InvalidOperationException("SPICE 导出尚不支持：" + string.Join("、", unsupported.Select(e => e.Id)));
            var spice = netlist.ToSpiceNetlist();
            var suggestedName = $"{System.IO.Path.GetFileNameWithoutExtension(ProjectFileName)}.cir";
            var file = await ProjectManagerService.PickExportSpiceFileAsync(suggestedName);
            if (file != null)
            {
                await System.IO.File.WriteAllTextAsync(file.Path, spice);
                StatusMessage = $"SPICE 网表已成功导出: {file.Name}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出网表失败: {ex.Message}";
        }
    }

    [RelayCommand]
    public void DeleteSelectedWire()
    {
        if (SelectedWire != null)
        {
            PushUndo();
            var w = SelectedWire;
            CircuitWire.RemoveWithDescendants(Wires, new[] { w.Id });
            SelectedWire = null;
            IsProjectModified = true;
            StatusMessage = $"已删除导线连接: {w.Id}";
        }
    }

    [RelayCommand]
    public async Task ExportAnalysisReportAsync(string? formatName)
    {
        if (_isSavingProject || IsAnalyzing) return;
        if (_lastSolution == null || _lastNetlist == null)
        {
            StatusMessage = "请先完成一次求解，再导出分步分析报告。";
            return;
        }
        if (!Enum.TryParse<ReportFormat>(formatName, ignoreCase: true, out var format))
        {
            StatusMessage = $"未知的报告格式: {formatName}";
            return;
        }
        try
        {
            var content = new AnalysisReportContent
            {
                ProjectName = string.IsNullOrWhiteSpace(ProjectName) ? "未命名电路工程" : ProjectName,
                ProjectFilePath = CurrentProjectFilePath,
                GeneratedAt = DateTime.Now,
                Netlist = _lastNetlist,
                Solution = _lastSolution
            };
            var extension = ReportExportService.GetFileExtension(format);
            var description = ReportExportService.GetFormatDisplayName(format);
            var file = await ProjectManagerService.PickExportReportFileAsync(
                ReportExportService.CreateSuggestedFileName(content.ProjectName, format),
                description,
                new[] { extension });
            if (file == null) return;

            if (format == ReportFormat.Docx)
            {
                await System.IO.File.WriteAllBytesAsync(file.Path, ReportExportService.BuildDocx(content));
            }
            else
            {
                var text = format switch
                {
                    ReportFormat.Markdown => ReportExportService.BuildMarkdown(content),
                    ReportFormat.Latex => ReportExportService.BuildLatex(content),
                    ReportFormat.Html => ReportExportService.BuildHtml(content),
                    _ => throw new InvalidOperationException($"不支持的报告格式: {format}")
                };
                await System.IO.File.WriteAllTextAsync(file.Path, text, new System.Text.UTF8Encoding(false));
            }
            StatusMessage = $"分析报告已导出（{description}）：{file.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出分析报告失败: {ex.Message}";
        }
    }

    private CircuitProjectData CollectProjectData()
    {
        var parameters = new Dictionary<string, double>
        {
            ["VCC"] = VccNumber,
            ["Rb1"] = Rb1Number,
            ["Rb2"] = Rb2Number,
            ["Rc"] = RcNumber,
            ["Re"] = ReNumber,
            ["RL"] = RlNumber,
            ["C1"] = C1Number,
            ["C2"] = C2Number,
            ["Ce"] = CeNumber,
            ["Beta"] = BetaNumber,
            ["Freq"] = FreqNumber
        };

        var data = CircuitProjectData.From(
            ProjectName,
            ProjectFileName,
            Elements,
            Wires,
            parameters,
            _projectDescription);
        data.CreatedAt = _projectCreatedAt;
        data.IsTeachingTemplate = IsTeachingTemplate;
        data.ParameterUnits = new Dictionary<string, string>
        {
            ["VCC"] = SelectedVccUnit.Symbol, ["Rb1"] = SelectedRb1Unit.Symbol,
            ["Rb2"] = SelectedRb2Unit.Symbol, ["Rc"] = SelectedRcUnit.Symbol,
            ["Re"] = SelectedReUnit.Symbol, ["RL"] = SelectedRlUnit.Symbol,
            ["C1"] = SelectedC1Unit.Symbol, ["C2"] = SelectedC2Unit.Symbol,
            ["Ce"] = SelectedCeUnit.Symbol, ["Freq"] = SelectedFreqUnit.Symbol
        };
        return data;
    }

    private void ApplyParameters(Dictionary<string, double> parameters, Dictionary<string, string> units)
    {
        _suppressParamSync = true;
        try
        {
            SetDefaultParameters();
            UnitOption Unit(string key, UnitOption[] choices, UnitOption fallback) =>
                units.TryGetValue(key, out var symbol) ? choices.FirstOrDefault(u => u.Symbol == symbol) ?? fallback : fallback;
            SelectedVccUnit = Unit("VCC", VoltageUnits, SelectedVccUnit);
            SelectedRb1Unit = Unit("Rb1", ResistanceUnits, SelectedRb1Unit);
            SelectedRb2Unit = Unit("Rb2", ResistanceUnits, SelectedRb2Unit);
            SelectedRcUnit = Unit("Rc", ResistanceUnits, SelectedRcUnit);
            SelectedReUnit = Unit("Re", ResistanceUnits, SelectedReUnit);
            SelectedRlUnit = Unit("RL", ResistanceUnits, SelectedRlUnit);
            SelectedC1Unit = Unit("C1", CapacitanceUnits, SelectedC1Unit);
            SelectedC2Unit = Unit("C2", CapacitanceUnits, SelectedC2Unit);
            SelectedCeUnit = Unit("Ce", CapacitanceUnits, SelectedCeUnit);
            SelectedFreqUnit = Unit("Freq", FrequencyUnits, SelectedFreqUnit);
            if (parameters.TryGetValue("VCC", out var vcc)) VccNumber = vcc;
            if (parameters.TryGetValue("Rb1", out var rb1)) Rb1Number = rb1;
            if (parameters.TryGetValue("Rb2", out var rb2)) Rb2Number = rb2;
            if (parameters.TryGetValue("Rc", out var rc)) RcNumber = rc;
            if (parameters.TryGetValue("Re", out var re)) ReNumber = re;
            if (parameters.TryGetValue("RL", out var rl)) RlNumber = rl;
            if (parameters.TryGetValue("C1", out var c1)) C1Number = c1;
            if (parameters.TryGetValue("C2", out var c2)) C2Number = c2;
            if (parameters.TryGetValue("Ce", out var ce)) CeNumber = ce;
            if (parameters.TryGetValue("Beta", out var beta)) BetaNumber = beta;
            if (parameters.TryGetValue("Freq", out var freq)) FreqNumber = freq;
            // r_bb'/r_be(h_ie) are optional per-element parameters (0 = auto); older
            // files omit them, so loading always re-reads the element values.
            var bjtElement = Elements.FirstOrDefault(e => e.SupportsBjtSmallSignalParameters);
            if (bjtElement != null)
            {
                RbbNumber = bjtElement.BjtRbb;
                RbeNumber = bjtElement.BjtRbe;
            }
        }
        finally { _suppressParamSync = false; }
    }

    [RelayCommand]
    public async Task CloseProject()
    {
        if (_isProjectTransition) return;
        _isProjectTransition = true;
        try
        {
            if (!await ConfirmProjectTransitionAsync()) return;
            _isRestoringProject = true;
            HasActiveProject = false;
            IsTeachingTemplate = false;
            ProjectName = "未命名电路工程";
            ProjectFileName = "";
            CurrentProjectFilePath = null;
            IsProjectModified = false;
            SelectedSymbol = null;
            SelectedElement = null;
            SelectedWire = null;
            Elements.Clear();
            Wires.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            _lastSolution = null;
            _lastNetlist = null;
            Result = new AnalysisResult();
            _projectRevision++;
            StatusMessage = "工程已关闭 · 当前处于空白工作区，请添加新工程";
        }
        finally { _isRestoringProject = false; _isProjectTransition = false; }
    }

    [RelayCommand]
    public void SelectSymbol(CircuitSymbolItem item)
    {
        SelectedSymbol = item;
        // Clicking an item from the library also adds or places it onto the canvas!
        AddSymbolToCanvas(item);
    }

    [RelayCommand]
    public void AddSymbolToCanvas(CircuitSymbolItem? item) => AddSymbolToCanvasWithPosition(item, null);

    public void AddSymbolToCanvasWithPosition(CircuitSymbolItem? item, Vector2? position = null)
    {
        if (item == null) return;

        if (!HasActiveProject)
        {
            CreateNewProject("blank");
        }

        PushUndo();

        // Generate unique ID
        string prefix = item.Id switch
        {
            "passive.resistor" => "R",
            "passive.capacitor" => "C",
            "passive.capacitor_polarized" => "C",
            "passive.inductor" => "L",
            "bjt.npn" => "Q",
            "bjt.pnp" => "Q",
            "source.v_dc" => "V",
            "source.vcc" => "VCC",
            "reference.vcc_port" => "VCC",
            "source.i_dc" => "I",
            "source.v_ac_small_signal" => "VS",
            "reference.ground" => "GND",
            "diode.standard" => "D",
            var s when s.StartsWith("fet.mosfet") => "M",
            var s when s.StartsWith("fet.jfet") => "J",
            _ => "U"
        };

        int count = Elements.Count(e => e.Id.StartsWith(prefix)) + 1;
        string newId = $"{prefix}{count}";
        if (Elements.Any(e => e.Id == newId))
        {
            newId = $"{prefix}_{Guid.NewGuid().ToString().Substring(0, 4)}";
        }

        Vector2 targetPos = position ?? new Vector2(280f + (count * 20f % 160f), 220f + (count * 20f % 160f));

        var newElement = new CircuitElement
        {
            Id = newId,
            Name = $"{item.Name} {newId}",
            DefinitionId = item.Id,
            Category = item.Category,
            Description = item.Description,
            Type = item.ElementType,
            Material = item.DefaultMaterial,
            DisplayNumber = item.DefaultNumber,
            UnitSymbol = item.DefaultUnit,
            X = targetPos.X,
            Y = targetPos.Y,
            Width = 80,
            Height = 50,
            RotationDeg = 0,
            Orientation = "Horizontal",
            IsSelected = true
        };
        newElement.InitializeDefaultPins();

        foreach (var el in Elements)
        {
            el.IsSelected = false;
        }

        Elements.Add(newElement);
        SelectedElement = newElement;
        SelectedRightTabIndex = 0; // Switch to inspector
        StatusMessage = $"已添加新元件 {newElement.Name} 至画布 · 可拖拽调整位置或在右侧修改参数";
    }

    /// <summary>
    /// Pushes the editable BJT small-signal parameters r_bb' and r_be/h_ie (0 = auto)
    /// to the BJT element(s) of the current project. Teaching templates contain a
    /// single transistor, but applying to every BJT keeps hand-built or imported
    /// circuits consistent.
    /// </summary>
    private void SyncBjtSmallSignalToElements(double? rbb, double? rbe)
    {
        if (_suppressParamSync) return;
        foreach (var el in Elements.Where(e => e.SupportsBjtSmallSignalParameters))
        {
            if (rbb.HasValue && Math.Abs(el.BjtRbb - rbb.Value) > 1e-9) el.BjtRbb = rbb.Value;
            if (rbe.HasValue && Math.Abs(el.BjtRbe - rbe.Value) > 1e-9) el.BjtRbe = rbe.Value;
        }
    }

    /// <summary>
    /// Loads the r_bb'/r_be macro fields from the first BJT element after creating or
    /// opening a project (0 = auto when the file does not carry the optional values).
    /// </summary>
    private void RefreshBjtMacrosFromElements()
    {
        var bjt = Elements.FirstOrDefault(e => e.SupportsBjtSmallSignalParameters);
        bool previous = _suppressParamSync;
        _suppressParamSync = true;
        try
        {
            RbbNumber = bjt?.BjtRbb ?? 0.0;
            RbeNumber = bjt?.BjtRbe ?? 0.0;
        }
        finally { _suppressParamSync = previous; }
    }

    private void SyncParamToElement(string id, double num, string? unit)
    {
        if (_suppressParamSync) return;
        var el = Elements.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                                              (id == "Q1" && (e.Id == "Q1" || e.Id == "BJT")) ||
                                              (id == "Rb1" && e.Id == "Rb"));
        if (el != null)
        {
            if (Math.Abs(el.DisplayNumber - num) > 1e-9)
            {
                el.DisplayNumber = num;
            }
            if (!string.IsNullOrEmpty(unit) && el.UnitSymbol != unit)
            {
                el.UnitSymbol = unit;
            }
        }
    }

    public void OnCanvasElementSelected(CircuitElement? el)
    {
        SelectedElement = el;

        if (el != null)
        {
            SelectedRightTabIndex = 0;
            StatusMessage = $"已在画布选中元件: {el.Name} ({el.FormattedValueDisplay}) · 可按 空格 旋转、ALT+X 镜像、Ctrl+Z 撤销、Del 删除或拖动布局";
        }
    }

    private void SelectedElement_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is CircuitElement el && (e.PropertyName == nameof(CircuitElement.DisplayNumber) ||
                                            e.PropertyName == nameof(CircuitElement.UnitSymbol) ||
                                            e.PropertyName == nameof(CircuitElement.FrequencyHz) ||
                                            e.PropertyName == nameof(CircuitElement.BjtRbb) ||
                                            e.PropertyName == nameof(CircuitElement.BjtRbe)))
        {
            SyncElementToModelProperty(el);
        }
    }

    private void SyncElementToModelProperty(CircuitElement el)
    {
        bool previous = _suppressParamSync;
        _suppressParamSync = true;
        try
        {
            UnitOption Unit(UnitOption[] options, UnitOption fallback) => options.FirstOrDefault(u => u.Symbol == el.UnitSymbol) ?? fallback;
            switch (el.Id.ToUpperInvariant())
            {
                case "VCC": VccNumber = el.DisplayNumber; SelectedVccUnit = Unit(VoltageUnits, SelectedVccUnit); break;
                case "RB1": case "RB": Rb1Number = el.DisplayNumber; SelectedRb1Unit = Unit(ResistanceUnits, SelectedRb1Unit); break;
                case "RB2": Rb2Number = el.DisplayNumber; SelectedRb2Unit = Unit(ResistanceUnits, SelectedRb2Unit); break;
                case "RC": RcNumber = el.DisplayNumber; SelectedRcUnit = Unit(ResistanceUnits, SelectedRcUnit); break;
                case "RE": ReNumber = el.DisplayNumber; SelectedReUnit = Unit(ResistanceUnits, SelectedReUnit); break;
                case "RL": RlNumber = el.DisplayNumber; SelectedRlUnit = Unit(ResistanceUnits, SelectedRlUnit); break;
                case "C1": C1Number = el.DisplayNumber; SelectedC1Unit = Unit(CapacitanceUnits, SelectedC1Unit); break;
                case "C2": C2Number = el.DisplayNumber; SelectedC2Unit = Unit(CapacitanceUnits, SelectedC2Unit); break;
                case "CE": CeNumber = el.DisplayNumber; SelectedCeUnit = Unit(CapacitanceUnits, SelectedCeUnit); break;
                case "Q1": case "BJT": BetaNumber = el.DisplayNumber; break;
            }
            // BJT small-signal model parameters are per-element values (r_bb', r_be/h_ie)
            // rather than template macros; mirror them into the dedicated macro fields.
            if (el.SupportsBjtSmallSignalParameters)
            {
                RbbNumber = el.BjtRbb;
                RbeNumber = el.BjtRbe;
            }
            if (el.IsAcSource) { SelectedFreqUnit = FrequencyUnits[0]; FreqNumber = el.FrequencyHz; }
        }
        finally { _suppressParamSync = previous; }
    }

    [RelayCommand]
    public void RotateSelectedElement()
    {
        if (SelectedElement != null)
        {
            PushUndo();
            SelectedElement.RotationDeg = (SelectedElement.RotationDeg + 90) % 360;
            SelectedElement.Orientation = (SelectedElement.RotationDeg % 180 == 0) ? "Horizontal" : "Vertical";
            CircuitWireSync.SyncConnectedWiresToElement(Wires, SelectedElement);
            StatusMessage = $"元件 {SelectedElement.Id} 已顺时针旋转至 {SelectedElement.RotationDeg}° ({SelectedElement.Orientation})";
        }
    }

    /// <summary>Toggle horizontal mirror (ALT+X) for the selected element.</summary>
    [RelayCommand]
    public void MirrorSelectedElement()
    {
        if (SelectedElement != null)
        {
            PushUndo();
            SelectedElement.MirrorX = !SelectedElement.MirrorX;
            CircuitWireSync.SyncConnectedWiresToElement(Wires, SelectedElement);
            StatusMessage = SelectedElement.MirrorX
                ? $"元件 {SelectedElement.Id} 已水平镜像（ALT+X 可恢复）"
                : $"元件 {SelectedElement.Id} 已取消水平镜像";
        }
    }

    [RelayCommand]
    public void DeleteSelectedElement()
    {
        if (SelectedElement != null)
        {
            PushUndo();
            var el = SelectedElement;
            var attachedWires = Wires.Where(w => w.StartElementId == el.Id || w.EndElementId == el.Id).ToList();
            CircuitWire.RemoveWithDescendants(Wires, attachedWires.Select(w => w.Id));

            Elements.Remove(el);
            SelectedElement = null;
            StatusMessage = $"已从画布移除元件 {el.Name}";
        }
    }

    [RelayCommand]
    public void SelectElement(CircuitElement? el)
    {
        foreach (var item in Elements) item.IsSelected = (item == el);
        SelectedElement = el;
        if (el != null)
        {
            SelectedRightTabIndex = 0;
            StatusMessage = $"已选中元件: {el.Name} ({el.FormattedValueDisplay})";
        }
    }

    [RelayCommand]
    public void RotateElement(CircuitElement el)
    {
        PushUndo();
        el.RotationDeg = (el.RotationDeg + 90) % 360;
        el.Orientation = (el.RotationDeg % 180 == 0) ? "Horizontal" : "Vertical";
        CircuitWireSync.SyncConnectedWiresToElement(Wires, el);
        StatusMessage = $"元件 {el.Id} 已旋转至 {el.RotationDeg}° ({el.Orientation})";
    }

    /// <summary>Toggle horizontal mirror (ALT+X) for one element (canvas card button).</summary>
    [RelayCommand]
    public void MirrorElement(CircuitElement el)
    {
        if (el == null) return;
        PushUndo();
        el.MirrorX = !el.MirrorX;
        CircuitWireSync.SyncConnectedWiresToElement(Wires, el);
        StatusMessage = el.MirrorX
            ? $"元件 {el.Id} 已水平镜像（ALT+X 可恢复）"
            : $"元件 {el.Id} 已取消水平镜像";
    }

    [RelayCommand]
    public void DeleteElement(CircuitElement el)
    {
        PushUndo();
        var attachedWires = Wires.Where(w => w.StartElementId == el.Id || w.EndElementId == el.Id).ToList();
        CircuitWire.RemoveWithDescendants(Wires, attachedWires.Select(w => w.Id));

        Elements.Remove(el);
        if (SelectedElement == el) SelectedElement = null;
        StatusMessage = $"已从画布移除元件 {el.Name}";
    }

    [RelayCommand]
    public void ToggleBottomDrawer()
    {
        IsBottomDrawerOpen = !IsBottomDrawerOpen;
    }

    private CancellationTokenSource? _solveCancellation;

    /// <summary>
    /// Last pedagogical solution and the exact extracted netlist snapshot it was
    /// computed from. The native report exporter uses these so a report always
    /// matches the displayed derivation; both are cleared when the circuit or the
    /// active project changes.
    /// </summary>
    private PedagogicalSolution? _lastSolution;
    private ElectricalNetlist? _lastNetlist;

    [RelayCommand]
    public void CancelSolve() => _solveCancellation?.Cancel();

    [RelayCommand]
    public async Task SolveCircuitAsync()
    {
        if (IsAnalyzing || !HasActiveProject || Elements.Count == 0) return;
        IsAnalyzing = true;
        Result = new AnalysisResult { SummaryText = "正在分析当前电路…" };
        StatusMessage = "正在提取电路并检查拓扑与参数…";
        long revision = _projectRevision;
        using var cancellation = new CancellationTokenSource();
        var solveToken = cancellation.Token;
        _solveCancellation = cancellation;
        try
        {
            var snapshot = CollectProjectData();
            var (elements, wires, _) = snapshot.ToModel();
            if (elements.Count > 512 || wires.Count > 4096)
                throw new InvalidOperationException("当前教学求解支持最多 512 个元件、4096 根导线，请拆分电路后求解。");
            var (solution, netlist) = await Task.Run(async () =>
            {
                solveToken.ThrowIfCancellationRequested();
                var extracted = NetlistExtractor.ExtractNetlist(elements, wires);
                var validation = extracted.Validate();
                if (!validation.IsValid)
                    return (PedagogicalSolution.Failure(CircuitTopologyType.Unsupported,
                        string.Join("\n", validation.Issues.Where(i => i.Severity == NetlistValidationSeverity.Error).Select(i => i.Message))), extracted);
                var type = TopologyClassifier.Classify(extracted, out var strategy);
                solveToken.ThrowIfCancellationRequested();
                var parameters = BuildAnalysisParameters(extracted);
                PedagogicalSolution current = await strategy.SolveAsync(extracted, parameters, solveToken);
                // A dedicated teaching strategy only accepts narrow textbook
                // topologies. If it rejects the network, retry with the general
                // linear-network analyzer (DC steady state / AC phasor) instead
                // of surfacing a dead end for an otherwise solvable linear circuit.
                if (!current.IsSuccess && strategy.TopologyType != CircuitTopologyType.MnaLinear)
                {
                    solveToken.ThrowIfCancellationRequested();
                    var fallback = new CircuitAssistant.Engine.Strategies.MnaLinearStrategy();
                    var mna = await fallback.SolveAsync(extracted, parameters, solveToken);
                    if (mna.IsSuccess)
                    {
                        mna.SummaryText = "专用教学策略不适用（" + FirstSummaryLine(current.SummaryText) +
                                          "），已回退到通用线性网络 MNA 2.0 直流稳态/交流相量分析。\n" + mna.SummaryText;
                        current = mna;
                    }
                }
                var warnings = validation.Issues.Where(i => i.Severity == NetlistValidationSeverity.Warning).Select(i => i.Message).ToList();
                if (warnings.Count > 0) current.SummaryText += "\n提示：" + string.Join("；", warnings);
                return (current, extracted);
            }, solveToken).WaitAsync(TimeSpan.FromSeconds(15), solveToken);

            if (_projectRevision != revision)
            {
                StatusMessage = "电路在求解期间已修改，已丢弃旧结果，请重新求解。";
                return;
            }
            _lastSolution = solution;
            _lastNetlist = netlist;
            Result = solution.ToAnalysisResult();
            StatusMessage = solution.IsSuccess
                ? $"求解完成 · {GetTopologyDisplayName(solution.TopologyType, solution.SubtypeName)} · {solution.Steps.Count} 个步骤"
                : $"无法完成求解：{solution.SummaryText}";
            if (!solution.IsSuccess) AnalysisErrorMessage = StatusMessage;
            SelectedRightTabIndex = 2;
        }
        catch (OperationCanceledException) { StatusMessage = "求解已取消。"; }
        catch (TimeoutException)
        {
            cancellation.Cancel();
            _lastSolution = null;
            _lastNetlist = null;
            Result = new AnalysisResult { CircuitTitle = "求解超时", SummaryText = "分析超过 15 秒，请简化电路后重试。" };
            StatusMessage = Result.SummaryText;
            AnalysisErrorMessage = StatusMessage;
        }
        catch (Exception ex)
        {
            App.LogTrace($"[SolveCircuitAsync Exception] {ex}");
            Result = new AnalysisResult { CircuitTitle = "求解失败", SummaryText = ex.Message };
            StatusMessage = $"求解失败: {ex.Message}";
            AnalysisErrorMessage = StatusMessage;
            SelectedRightTabIndex = 2;
        }
        finally { _solveCancellation = null; IsAnalyzing = false; }
    }

    private static string FirstSummaryLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "未给出原因";
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        return line.Length > 80 ? line[..80] + "…" : line;
    }

    private static string GetTopologyDisplayName(CircuitTopologyType type, string subtype) => type switch
    {
        CircuitTopologyType.FirstOrderRc => "一阶 RC 暂态与滤波电路",
        CircuitTopologyType.FirstOrderRl => "一阶 RL 暂态与滤波电路",
        CircuitTopologyType.SecondOrderRlc => "二阶 RLC 谐振与暂态电路",
        CircuitTopologyType.BjtAmplifier => subtype switch
        {
            "CommonBase" => "晶体管共基极放大电路",
            "CommonCollector" => "晶体管共集极放大电路（射极跟随器）",
            "FixedBias" or "VoltageDividerSelfBias" or "CollectorFeedbackBias" => "晶体管共射极放大电路",
            _ => "晶体管放大电路"
        },
        CircuitTopologyType.MosfetAmplifier => subtype switch
        {
            "CommonSource" => "MOSFET 共源极放大电路",
            "CommonSourceWithSourceResistance" => "MOSFET 共源极放大电路（源极电阻未旁路）",
            "CommonSourceBypassed" => "MOSFET 共源极放大电路（源极电阻旁路）",
            "CommonDrain" => "MOSFET 共漏极放大电路（源极跟随器）",
            "CommonGate" => "MOSFET 共栅极放大电路",
            _ => "MOSFET 放大电路"
        },
        CircuitTopologyType.DiodeCircuit => "二极管非线性电路",
        CircuitTopologyType.MultiStageAmplifier => "多级放大电路（后续规划）",
        CircuitTopologyType.MnaLinear => "通用线性网络 (MNA 2.0 矩阵/相量分析)",
        _ => "线性网络"
    };

    private static AnalysisParameters BuildAnalysisParameters(ElectricalNetlist netlist)
    {
        var source = netlist.Elements.Values.FirstOrDefault(e => e.IsAcSource);
        return new AnalysisParameters
        {
            Frequency = source?.FrequencyHz ?? 1000.0,
            ThermalVoltageUt = 0.026
        };
    }

    [RelayCommand]
    public void ResetToDefaults()
    {
        if (!HasActiveProject || IsAnalyzing || _isSavingProject) return;
        if (!IsTeachingTemplate) { StatusMessage = "当前工程请通过元件检查器修改参数。"; return; }
        PushUndo();
        SetDefaultParameters();
        StatusMessage = "默认参数已恢复，工程布局保持不变，请重新求解。";
    }

    private void SetDefaultParameters()
    {
        VccNumber = 12.0;
        SelectedVccUnit = EngineeringUnits.VoltageUnits[1];

        Rb1Number = 51.0;
        SelectedRb1Unit = EngineeringUnits.ResistanceUnits[1];

        Rb2Number = 15.0;
        SelectedRb2Unit = EngineeringUnits.ResistanceUnits[1];

        RcNumber = 3.0;
        SelectedRcUnit = EngineeringUnits.ResistanceUnits[1];

        ReNumber = 1.0;
        SelectedReUnit = EngineeringUnits.ResistanceUnits[1];

        RlNumber = 10.0;
        SelectedRlUnit = EngineeringUnits.ResistanceUnits[1];

        C1Number = 10.0;
        SelectedC1Unit = EngineeringUnits.CapacitanceUnits[2];

        C2Number = 10.0;
        SelectedC2Unit = EngineeringUnits.CapacitanceUnits[2];

        CeNumber = 47.0;
        SelectedCeUnit = EngineeringUnits.CapacitanceUnits[2];

        BetaNumber = 100.0;

        RbbNumber = 0.0;
        RbeNumber = 0.0;

        FreqNumber = 1.0;
        SelectedFreqUnit = EngineeringUnits.FrequencyUnits[1];

    }
}
