using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

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

    [ObservableProperty]
    private string _statusMessage = "";

    // Project State
    [ObservableProperty]
    private bool _hasActiveProject;

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

    // Interactive Canvas Elements
    public ObservableCollection<CircuitElement> Elements { get; } = new();

    public ObservableCollection<CircuitSymbolItem> AllSymbols { get; } = new();
    public ObservableCollection<CircuitSymbolItem> FilteredSymbols { get; } = new();

    public string[] SymbolCategories => new[] { "全部", "无源器件", "激励电源", "半导体管", "受控源", "参考地" };

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
            Category = "半导体管",
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
            Category = "半导体管",
            Subtitle = "β = 100",
            ValueText = "β = 100",
            Glyph = "\uE943",
            Description = "双极型三极管 PNP 管，互补对称推挽放大",
            Standard = "GB / IEC",
            DefaultNumber = 100,
            DefaultUnit = "β",
            ElementType = ElementType.BjtPnp
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "fet.mosfet",
            Name = "NMOS 增强型管",
            Category = "半导体管",
            Subtitle = "Vth = 1.5 V",
            ValueText = "Vth = 1.5 V",
            Glyph = "\uE8EF",
            Description = "四端绝缘栅场效应管，压控电流单极型半导体",
            Standard = "ANSI / IEC",
            DefaultNumber = 1.5,
            DefaultUnit = "V",
            ElementType = ElementType.Mosfet
        });

        AllSymbols.Add(new CircuitSymbolItem
        {
            Id = "fet.jfet",
            Name = "JFET 结型管",
            Category = "半导体管",
            Subtitle = "Vp = -2 V",
            ValueText = "Vp = -2 V",
            Glyph = "\uE8EF",
            Description = "耗尽型结型场效应管，高输入阻抗压控元件",
            Standard = "ANSI / IEC",
            DefaultNumber = -2.0,
            DefaultUnit = "V",
            ElementType = ElementType.Jfet
        });

        // 5. Controlled Sources (受控源)
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

        // 1. VCC Supply Rail
        Elements.Add(new CircuitElement
        {
            Id = "VCC",
            Name = "直流电源 VCC",
            DefinitionId = "source.v_dc",
            Category = "激励电源",
            Type = ElementType.DcVoltageSource,
            DisplayNumber = VccNumber,
            UnitSymbol = SelectedVccUnit.Symbol,
            X = 530,
            Y = 75,
            Width = 90,
            Height = 40
        });

        // 2. GND Ground
        Elements.Add(new CircuitElement
        {
            Id = "GND",
            Name = "参考地 GND",
            DefinitionId = "reference.ground",
            Category = "参考地",
            Type = ElementType.Ground,
            DisplayNumber = 0,
            UnitSymbol = "V",
            X = 330,
            Y = 445,
            Width = 40,
            Height = 35
        });

        // 3. AC Signal Source vs
        Elements.Add(new CircuitElement
        {
            Id = "vs",
            Name = "交流信号源 vs",
            DefinitionId = "source.v_ac_small_signal",
            Category = "激励电源",
            Type = ElementType.AcVoltageSource,
            DisplayNumber = FreqNumber,
            UnitSymbol = SelectedFreqUnit.Symbol,
            X = 70,
            Y = 280,
            Width = 60,
            Height = 60
        });

        // 4. Bias Resistor Rb1
        Elements.Add(new CircuitElement
        {
            Id = "Rb1",
            Name = "基极偏置电阻 Rb1",
            DefinitionId = "passive.resistor",
            Category = "无源器件",
            Type = ElementType.Resistor,
            DisplayNumber = Rb1Number,
            UnitSymbol = SelectedRb1Unit.Symbol,
            Orientation = "Vertical",
            X = 200,
            Y = 150,
            Width = 45,
            Height = 65
        });

        // 5. Bias Resistor Rb2
        Elements.Add(new CircuitElement
        {
            Id = "Rb2",
            Name = "基极偏置电阻 Rb2",
            DefinitionId = "passive.resistor",
            Category = "无源器件",
            Type = ElementType.Resistor,
            DisplayNumber = Rb2Number,
            UnitSymbol = SelectedRb2Unit.Symbol,
            Orientation = "Vertical",
            X = 200,
            Y = 340,
            Width = 45,
            Height = 65
        });

        // 6. Collector Resistor Rc
        Elements.Add(new CircuitElement
        {
            Id = "Rc",
            Name = "集电极电阻 Rc",
            DefinitionId = "passive.resistor",
            Category = "无源器件",
            Type = ElementType.Resistor,
            DisplayNumber = RcNumber,
            UnitSymbol = SelectedRcUnit.Symbol,
            Orientation = "Vertical",
            X = 350,
            Y = 130,
            Width = 45,
            Height = 65
        });

        // 7. Emitter Resistor Re
        Elements.Add(new CircuitElement
        {
            Id = "Re",
            Name = "发射极电阻 Re",
            DefinitionId = "passive.resistor",
            Category = "无源器件",
            Type = ElementType.Resistor,
            DisplayNumber = ReNumber,
            UnitSymbol = SelectedReUnit.Symbol,
            Orientation = "Vertical",
            X = 330,
            Y = 385,
            Width = 45,
            Height = 65
        });

        // 8. Load Resistor RL
        Elements.Add(new CircuitElement
        {
            Id = "RL",
            Name = "负载电阻 RL",
            DefinitionId = "passive.resistor",
            Category = "无源器件",
            Type = ElementType.Resistor,
            DisplayNumber = RlNumber,
            UnitSymbol = SelectedRlUnit.Symbol,
            Orientation = "Vertical",
            X = 500,
            Y = 280,
            Width = 45,
            Height = 65
        });

        // 9. Input Coupling Capacitor C1
        Elements.Add(new CircuitElement
        {
            Id = "C1",
            Name = "输入耦合电容 C1",
            DefinitionId = "passive.capacitor",
            Category = "无源器件",
            Type = ElementType.Capacitor,
            DisplayNumber = C1Number,
            UnitSymbol = SelectedC1Unit.Symbol,
            Orientation = "Horizontal",
            X = 130,
            Y = 250,
            Width = 60,
            Height = 45
        });

        // 10. Output Coupling Capacitor C2
        Elements.Add(new CircuitElement
        {
            Id = "C2",
            Name = "输出耦合电容 C2",
            DefinitionId = "passive.capacitor",
            Category = "无源器件",
            Type = ElementType.Capacitor,
            DisplayNumber = C2Number,
            UnitSymbol = SelectedC2Unit.Symbol,
            Orientation = "Horizontal",
            X = 440,
            Y = 180,
            Width = 60,
            Height = 45
        });

        // 11. Bypass Capacitor Ce
        Elements.Add(new CircuitElement
        {
            Id = "Ce",
            Name = "发射极旁路电容 Ce",
            DefinitionId = "passive.capacitor",
            Category = "无源器件",
            Type = ElementType.Capacitor,
            DisplayNumber = CeNumber,
            UnitSymbol = SelectedCeUnit.Symbol,
            Orientation = "Vertical",
            X = 380,
            Y = 385,
            Width = 45,
            Height = 65
        });

        // 12. BJT Transistor Q1
        Elements.Add(new CircuitElement
        {
            Id = "Q1",
            Name = "NPN 晶体管 Q1",
            DefinitionId = "bjt.npn",
            Category = "半导体管",
            Type = ElementType.BjtNpn,
            DisplayNumber = BetaNumber,
            UnitSymbol = "β",
            X = 330,
            Y = 250,
            Width = 75,
            Height = 75
        });
    }

    [RelayCommand]
    public void CreateNewProject(string? templateType = null)
    {
        HasActiveProject = true;
        if (templateType == "blank")
        {
            ProjectName = "空白电路工程";
            ProjectFileName = "Blank_Circuit.sch";
            Elements.Clear();
            SelectedElement = null;
            StatusMessage = "空白电路工程已就绪 · 可从左侧元件族点击或拖拽添加元件至画布";
        }
        else if (templateType == "ac_coupled")
        {
            ProjectName = "阻容耦合单管放大电路";
            ProjectFileName = "RC_Coupled_Amp.sch";
            ResetToDefaults();
            StatusMessage = $"工程已载入: {ProjectName}";
        }
        else
        {
            ProjectName = "分压式偏置共射放大电路";
            ProjectFileName = "CommonEmitter_Amp.sch";
            ResetToDefaults();
            StatusMessage = $"工程已载入: {ProjectName}";
        }
    }

    [RelayCommand]
    public void CreateBlankProject() => CreateNewProject("blank");

    [RelayCommand]
    public void CreateDefaultProject() => CreateNewProject("default");

    [RelayCommand]
    public void CreateAcCoupledProject() => CreateNewProject("ac_coupled");

    [RelayCommand]
    public void CloseProject()
    {
        HasActiveProject = false;
        ProjectName = "未命名电路工程";
        ProjectFileName = "";
        SelectedSymbol = null;
        SelectedElement = null;
        Elements.Clear();
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
            "fet.mosfet" => "M",
            "fet.jfet" => "J",
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
            Width = 60,
            Height = 45,
            IsSelected = true
        };

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
                                              (id == "Q1" && (e.Id == "Q1" || e.Id == "BJT")));
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
        else if (id == "RB1") Rb1Number = el.DisplayNumber;
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
            SelectedElement.Orientation = (SelectedElement.Orientation == "Vertical") ? "Horizontal" : "Vertical";
            StatusMessage = $"元件 {SelectedElement.Id} 已旋转至 {SelectedElement.RotationDeg}°";
        }
    }

    [RelayCommand]
    public void DeleteSelectedElement()
    {
        if (SelectedElement != null)
        {
            var el = SelectedElement;
            Elements.Remove(el);
            SelectedElement = null;
            StatusMessage = $"已从画布移除元件 {el.Name}";
        }
    }

    [RelayCommand]
    public void ToggleBottomDrawer()
    {
        IsBottomDrawerOpen = !IsBottomDrawerOpen;
    }

    [RelayCommand]
    public async Task SolveCircuitAsync()
    {
        IsAnalyzing = true;
        StatusMessage = "正在构建微变等效模型并执行高精度符号推导...";

        // Sync element values if user modified them on canvas
        SyncElementValuesToModel();

        // Convert all input parameters to SI base units
        double vcc = VccNumber * SelectedVccUnit.Multiplier;
        double rb1 = Rb1Number * SelectedRb1Unit.Multiplier;
        double rb2 = Rb2Number * SelectedRb2Unit.Multiplier;
        double rc = RcNumber * SelectedRcUnit.Multiplier;
        double re = ReNumber * SelectedReUnit.Multiplier;
        double rl = RlNumber * SelectedRlUnit.Multiplier;
        double c1 = C1Number * SelectedC1Unit.Multiplier;
        double c2 = C2Number * SelectedC2Unit.Multiplier;
        double ce = CeNumber * SelectedCeUnit.Multiplier;
        double beta = BetaNumber;
        double freq = FreqNumber * SelectedFreqUnit.Multiplier;

        // Perform async analysis
        var res = await _apiService.AnalyzeCircuitAsync(
            vcc, rb1, rb2, rc, re, rl, c1, c2, ce, beta, freq);

        Result = res;
        IsAnalyzing = false;
        StatusMessage = $"求解成功 — 已生成 {res.Steps.Count} 个教学解析步骤与完整频响指标";
    }

    private void SyncElementValuesToModel()
    {
        foreach (var el in Elements)
        {
            if (el.Id == "VCC") VccNumber = el.DisplayNumber;
            else if (el.Id == "Rb1") Rb1Number = el.DisplayNumber;
            else if (el.Id == "Rb2") Rb2Number = el.DisplayNumber;
            else if (el.Id == "Rc") RcNumber = el.DisplayNumber;
            else if (el.Id == "Re") ReNumber = el.DisplayNumber;
            else if (el.Id == "RL") RlNumber = el.DisplayNumber;
            else if (el.Id == "C1") C1Number = el.DisplayNumber;
            else if (el.Id == "C2") C2Number = el.DisplayNumber;
            else if (el.Id == "Ce") CeNumber = el.DisplayNumber;
            else if (el.Id == "Q1" || el.Id == "BJT") BetaNumber = el.DisplayNumber;
        }
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

        InitializeSymbols();
        InitializeDefaultCircuitElements();

        // Solve synchronously for instant preview on startup
        Result = CircuitApiService.GeneratePedagogicalSolution(
            12.0, 51000, 15000, 3000, 1000, 10000, 10e-6, 10e-6, 47e-6, 100.0, 1000.0);
        StatusMessage = $"推导完成 — 共 {Result.Steps.Count} 个教学解析步骤";
    }
}
