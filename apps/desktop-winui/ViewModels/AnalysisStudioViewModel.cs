using System;
using System.Collections.ObjectModel;
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
    private readonly CircuitApiService _apiService;

    public AnalysisStudioViewModel()
    {
        App.LogTrace("AnalysisStudioViewModel.ctor: Starting");
        _apiService = new CircuitApiService();
        ResetToDefaults();
        StatusMessage = "工程已就绪 · 分压式偏置共射放大电路 (支持单击选中、拖拽调整与从左侧器件族添加)";
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

    partial void OnFreqNumberChanged(double value) => SyncParamToElement("vs", value, SelectedFreqUnit?.Symbol);
    partial void OnSelectedFreqUnitChanged(UnitOption value) => SyncParamToElement("vs", FreqNumber, value.Symbol);

    // State
    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _showMnaLogs;

    [ObservableProperty]
    private AnalysisResult _result = new();

    public ObservableCollection<AnalysisStep> SolutionSteps => Result.Steps;

    partial void OnResultChanged(AnalysisResult value)
    {
        OnPropertyChanged(nameof(SolutionSteps));
    }

    [ObservableProperty]
    private string _statusMessage = "";

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
    private string _cursorCoordinateText = "X: 310 mil | Y: 260 mil";

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

    public UnitOption[] SelectedElementAvailableUnits => SelectedElement?.AvailableUnits ?? [];

    partial void OnSelectedElementChanged(CircuitElement? value)
    {
        OnPropertyChanged(nameof(SelectedElementAvailableUnits));
    }

    // Interactive Canvas Elements & Wires
    public ObservableCollection<CircuitElement> Elements { get; } = new();
    public ObservableCollection<CircuitWire> Wires { get; } = new();

    public ObservableCollection<CircuitSymbolItem> AllSymbols { get; } = new();
    public ObservableCollection<CircuitSymbolItem> FilteredSymbols { get; } = new();

    public string[] SymbolCategories => new[] { "全部", "无源器件", "激励电源", "双极型三极管", "增强型场效应管", "耗尽型场效应管", "结型场效应管", "受控源", "参考地" };

    private void InitializeSymbols()
    {
        AllSymbols.Clear();

        // 1. Reference / Ground (参考地)
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
            Subtitle = "0.7 V",
            ValueText = "0.7 V",
            Glyph = "\uE8EF",
            Description = "单向导电限幅整流器件，PN 结伏安特性",
            Standard = "IEC",
            DefaultNumber = 0.7,
            DefaultUnit = "V",
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
            Description = "双极型三极管 NPN 管，电流受控放大核心元件",
            Standard = "GB / IEC",
            DefaultNumber = 100,
            DefaultUnit = "β",
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
            Description = "双极型三极管 PNP 管，互补对称推挽放大",
            Standard = "GB / IEC",
            DefaultNumber = 100,
            DefaultUnit = "β",
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
            Subtitle = "Vp = -2.0 V",
            ValueText = "Vp = -2.0 V",
            Glyph = "\uE8EF",
            Description = "N沟道绝缘栅耗尽型场效应管 (NMOS Dep)，零栅压导通，夹断电压负值",
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
            Subtitle = "Vp = 2.0 V",
            ValueText = "Vp = 2.0 V",
            Glyph = "\uE8EF",
            Description = "P沟道绝缘栅耗尽型场效应管 (PMOS Dep)，零栅压导通，夹断电压正值",
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
            Subtitle = "Vp = -2.5 V",
            ValueText = "Vp = -2.5 V",
            Glyph = "\uE8EF",
            Description = "N沟道耗尽型结型场效应管 (N-JFET)，反偏PN结控制沟道，低噪声",
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
            Subtitle = "Vp = 2.5 V",
            ValueText = "Vp = 2.5 V",
            Glyph = "\uE8EF",
            Description = "P沟道耗尽型结型场效应管 (P-JFET)，反偏PN结控制沟道",
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
            C1Number, C2Number, CeNumber, BetaNumber, FreqNumber);

        foreach (var el in template.Elements)
        {
            Elements.Add(el);
        }

        foreach (var w in template.Wires)
        {
            Wires.Add(w);
        }
    }

    [RelayCommand]
    public void CreateNewProject(string? templateType = null)
    {
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
            IsTeachingTemplate = true;
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
                VccNumber, 300.0, RcNumber, RlNumber, C1Number, C2Number, BetaNumber, FreqNumber);
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
            ResetToDefaults();
            StatusMessage = $"工程已载入: {ProjectName}";
        }
    }

    [RelayCommand]
    public void CreateBlankProject() => CreateNewProject("blank");

    [RelayCommand]
    public void CreateDefaultProject() => CreateNewProject("common_emitter");

    [RelayCommand]
    public void CreateAcCoupledProject() => CreateNewProject("ac_coupled");

    [RelayCommand]
    public void CreateRcTransientProject() => CreateNewProject("rc_transient");

    [RelayCommand]
    public void CloseProject()
    {
        HasActiveProject = false;
        IsTeachingTemplate = false;
        ProjectName = "未命名电路工程";
        ProjectFileName = "";
        SelectedSymbol = null;
        SelectedElement = null;
        Elements.Clear();
        Wires.Clear();
        StatusMessage = "工程已关闭 · 当前处于空白工作区，请添加新工程";
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

        // Generate unique ID
        string prefix = item.Id switch
        {
            "passive.resistor" => "R",
            "passive.capacitor" => "C",
            "passive.inductor" => "L",
            "bjt.npn" => "Q",
            "bjt.pnp" => "Q",
            "source.v_dc" => "V",
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

    private void SyncParamToElement(string id, double num, string? unit)
    {
        var el = Elements.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                                              (id == "Q1" && (e.Id == "Q1" || e.Id == "BJT")) ||
                                              (id == "Rb1" && (e.Id == "Rb" || e.Id == "R1")));
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
        if (SelectedElement != null)
        {
            SelectedElement.PropertyChanged -= SelectedElement_PropertyChanged;
        }

        SelectedElement = el;

        if (el != null)
        {
            el.PropertyChanged += SelectedElement_PropertyChanged;
            SelectedRightTabIndex = 0;
            StatusMessage = $"已在画布选中元件: {el.Name} ({el.FormattedValueDisplay}) · 可按 R 旋转、Del 删除或拖动布局";
        }
    }

    private void SelectedElement_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is CircuitElement el && (e.PropertyName == nameof(CircuitElement.DisplayNumber) || e.PropertyName == nameof(CircuitElement.UnitSymbol)))
        {
            SyncElementToModelProperty(el);
        }
    }

    private void SyncElementToModelProperty(CircuitElement el)
    {
        string id = el.Id.ToUpperInvariant();
        if (id == "VCC") VccNumber = el.DisplayNumber;
        else if (id == "RB1" || id == "RB" || id == "R1") Rb1Number = el.DisplayNumber;
        else if (id == "RB2") Rb2Number = el.DisplayNumber;
        else if (id == "RC") RcNumber = el.DisplayNumber;
        else if (id == "RE") ReNumber = el.DisplayNumber;
        else if (id == "RL") RlNumber = el.DisplayNumber;
        else if (id == "C1") C1Number = el.DisplayNumber;
        else if (id == "C2") C2Number = el.DisplayNumber;
        else if (id == "CE") CeNumber = el.DisplayNumber;
        else if (id == "Q1" || id == "BJT") BetaNumber = el.DisplayNumber;
        else if (id == "VS") FreqNumber = el.DisplayNumber;
    }

    [RelayCommand]
    public void RotateSelectedElement()
    {
        if (SelectedElement != null)
        {
            SelectedElement.RotationDeg = (SelectedElement.RotationDeg + 90) % 360;
            SelectedElement.Orientation = (SelectedElement.RotationDeg % 180 == 0) ? "Horizontal" : "Vertical";
            SyncConnectedWiresToElement(SelectedElement);
            StatusMessage = $"元件 {SelectedElement.Id} 已顺时针旋转至 {SelectedElement.RotationDeg}° ({SelectedElement.Orientation})";
        }
    }

    [RelayCommand]
    public void DeleteSelectedElement()
    {
        if (SelectedElement != null)
        {
            var el = SelectedElement;
            var attachedWires = Wires.Where(w => w.StartElementId == el.Id || w.EndElementId == el.Id).ToList();
            foreach (var w in attachedWires) Wires.Remove(w);

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
        el.RotationDeg = (el.RotationDeg + 90) % 360;
        el.Orientation = (el.RotationDeg % 180 == 0) ? "Horizontal" : "Vertical";
        SyncConnectedWiresToElement(el);
        StatusMessage = $"元件 {el.Id} 已旋转至 {el.RotationDeg}° ({el.Orientation})";
    }

    [RelayCommand]
    public void DeleteElement(CircuitElement el)
    {
        var attachedWires = Wires.Where(w => w.StartElementId == el.Id || w.EndElementId == el.Id).ToList();
        foreach (var w in attachedWires) Wires.Remove(w);

        Elements.Remove(el);
        if (SelectedElement == el) SelectedElement = null;
        StatusMessage = $"已从画布移除元件 {el.Name}";
    }

    private void SyncConnectedWiresToElement(CircuitElement el)
    {
        var movedWireIds = new System.Collections.Generic.HashSet<string>();

        // Phase 1: Update direct pin-connected wires with 10px grid snap
        foreach (var w in Wires)
        {
            if (w.StartElementId == el.Id && w.StartPinName != "WireTap")
            {
                var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
                if (pin != null)
                {
                    var pPos = el.GetPinWorldPosition(pin);
                    w.StartX = pPos.X;
                    w.StartY = pPos.Y;
                    movedWireIds.Add(w.Id);
                }
            }
            if (w.EndElementId == el.Id && w.EndPinName != "WireTap")
            {
                var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
                if (pin != null)
                {
                    var pPos = el.GetPinWorldPosition(pin);
                    w.EndX = pPos.X;
                    w.EndY = pPos.Y;
                    movedWireIds.Add(w.Id);
                }
            }
        }

        // Phase 2A: Branch wires starting or ending at el and tapping into a parent rail
        // Protect parent trunk bus rails from distortion by sliding tap perpendicularly along rail
        foreach (var w in Wires)
        {
            if (w.StartElementId == el.Id && w.EndPinName == "WireTap")
            {
                var parent = Wires.FirstOrDefault(pw => pw.Id == w.EndElementId);
                if (parent != null)
                {
                    var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
                    if (pin != null)
                    {
                        var pPos = el.GetPinWorldPosition(pin);
                        w.StartX = pPos.X;
                        w.StartY = pPos.Y;
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.StartX, w.StartY));
                        w.EndX = newTap.X;
                        w.EndY = newTap.Y;
                        movedWireIds.Add(w.Id);
                    }
                }
            }
            else if (w.EndElementId == el.Id && w.StartPinName == "WireTap")
            {
                var parent = Wires.FirstOrDefault(pw => pw.Id == w.StartElementId);
                if (parent != null)
                {
                    var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
                    if (pin != null)
                    {
                        var pPos = el.GetPinWorldPosition(pin);
                        w.EndX = pPos.X;
                        w.EndY = pPos.Y;
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.EndX, w.EndY));
                        w.StartX = newTap.X;
                        w.StartY = newTap.Y;
                        movedWireIds.Add(w.Id);
                    }
                }
            }
        }

        // Phase 2B: Child wires tapping into parent wires that moved in Phase 1 or 2A
        bool anyUpdated;
        int maxPasses = 3;
        do
        {
            anyUpdated = false;
            foreach (var w in Wires)
            {
                // Pure trunk rails or wires ending with _RAIL or _BUS are never child branches of feeder lines
                if (w.Id.EndsWith("_RAIL", StringComparison.OrdinalIgnoreCase) ||
                    w.Id.EndsWith("_BUS", StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrEmpty(w.StartElementId) && string.IsNullOrEmpty(w.EndElementId)))
                {
                    continue;
                }

                if (w.EndPinName == "WireTap" && movedWireIds.Contains(w.EndElementId) && !movedWireIds.Contains(w.Id))
                {
                    var parent = Wires.FirstOrDefault(pw => pw.Id == w.EndElementId);
                    if (parent != null)
                    {
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.StartX, w.StartY));
                        w.EndX = newTap.X;
                        w.EndY = newTap.Y;
                        movedWireIds.Add(w.Id);
                        anyUpdated = true;
                    }
                }
                else if (w.StartPinName == "WireTap" && movedWireIds.Contains(w.StartElementId) && !movedWireIds.Contains(w.Id))
                {
                    var parent = Wires.FirstOrDefault(pw => pw.Id == w.StartElementId);
                    if (parent != null)
                    {
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.EndX, w.EndY));
                        w.StartX = newTap.X;
                        w.StartY = newTap.Y;
                        movedWireIds.Add(w.Id);
                        anyUpdated = true;
                    }
                }
            }
            maxPasses--;
        } while (anyUpdated && maxPasses > 0);
    }

    [RelayCommand]
    public void ToggleBottomDrawer()
    {
        IsBottomDrawerOpen = !IsBottomDrawerOpen;
    }

    [RelayCommand]
    public async Task SolveCircuitAsync()
    {
        // 1. Guard check: Empty canvas
        if (Elements.Count == 0)
        {
            StatusMessage = "当前画布无元器件，请先从左侧库添加元件并连接导线";
            return;
        }

        IsAnalyzing = true;
        StatusMessage = "正在构建等效网络图并提取拓扑结构...";

        try
        {
            // 2. Industrial Netlist Extraction via Union-Find graph connectivity
            var netlist = NetlistExtractor.ExtractNetlist(Elements, Wires);

            // 3. Topology Classification
            var topologyType = TopologyClassifier.Classify(netlist, out var strategy);

            // 4. Edge Case: Disconnected / Open Circuit
            if (topologyType == CircuitTopologyType.Disconnected || topologyType == CircuitTopologyType.OpenCircuit)
            {
                Result = new AnalysisResult
                {
                    IsSuccess = false,
                    CircuitTitle = "电路开路 / 未闭合回路",
                    SummaryText = "检测到电路中存在未连接导线的悬空引脚或未闭合网络。请使用导线连接各元件引脚构成完整的闭合回路后重新求解。"
                };
                StatusMessage = "求解中止：电路未形成闭合电气回路，请检查导线连接";
                return;
            }

            StatusMessage = $"已识别电路拓扑: {GetTopologyDisplayName(topologyType)} · 正在执行高精度符号推导...";

            // 5. Build dynamic parameters from elements on canvas (not hardcoded IDs)
            var parameters = BuildAnalysisParameters(netlist);

            // 6. Execute Analytical Solving Strategy
            var solution = await strategy.SolveAsync(netlist, parameters);

            // 7. Update ViewModel UI State
            Result = solution.ToAnalysisResult();
            StatusMessage = $"求解成功 · 拓扑: {GetTopologyDisplayName(topologyType)} — 已生成 {solution.Steps.Count} 个教学解析步骤 (电路含 {Elements.Count} 个元件)";

            // Automatically switch right panel to "解题推导" (Tab 3) for immediate student feedback
            SelectedRightTabIndex = 2;
        }
        catch (Exception ex)
        {
            App.LogTrace($"[SolveCircuitAsync Exception] {ex}");
            Result = new AnalysisResult
            {
                IsSuccess = false,
                CircuitTitle = "求解异常",
                SummaryText = $"分析引擎在解算过程中发生异常：{ex.Message}\n请检查电路拓扑是否包含异常短路或奇异矩阵结构。"
            };
            StatusMessage = $"求解失败: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private static string GetTopologyDisplayName(CircuitTopologyType type) => type switch
    {
        CircuitTopologyType.FirstOrderRc => "一阶 RC 暂态与滤波电路",
        CircuitTopologyType.FirstOrderRl => "一阶 RL 暂态与滤波电路",
        CircuitTopologyType.SecondOrderRlc => "二阶 RLC 谐振与暂态电路",
        CircuitTopologyType.BjtAmplifier => "晶体管共射极放大电路",
        CircuitTopologyType.MosfetAmplifier => "MOSFET 共源极放大电路",
        CircuitTopologyType.MnaLinear => "通用线性网络 (MNA 矩阵分析)",
        _ => "线性网络"
    };

    private static AnalysisParameters BuildAnalysisParameters(ElectricalNetlist netlist)
    {
        double freq = 1000.0;
        var vsElem = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.AcVoltageSource);
        if (vsElem != null)
        {
            double mult = 1.0;
            foreach (var opt in EngineeringUnits.FrequencyUnits)
            {
                if (opt.Symbol == vsElem.UnitSymbol) mult = opt.Multiplier;
            }
            freq = vsElem.DisplayNumber * mult;
        }

        return new AnalysisParameters
        {
            Frequency = freq > 0 ? freq : 1000.0,
            ThermalVoltageUt = 0.026
        };
    }

    [RelayCommand]
    public void ResetToDefaults()
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

        FreqNumber = 1.0;
        SelectedFreqUnit = EngineeringUnits.FrequencyUnits[1];

        ProjectName = "分压式偏置共射放大电路";
        ProjectFileName = "CommonEmitter_Amp.sch";

        InitializeSymbols();
        InitializeDefaultCircuitElements();

        // Solve synchronously for instant preview on startup
        Result = CircuitApiService.GeneratePedagogicalSolution(
            12.0, 51000, 15000, 3000, 1000, 10000, 10e-6, 10e-6, 47e-6, 100.0, 1000.0);
        StatusMessage = $"推导完成 — 共 {Result.Steps.Count} 个教学解析步骤";
    }
}
