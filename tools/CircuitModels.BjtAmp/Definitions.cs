using System.Collections.Generic;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace CircuitModels.BjtAmp;

/// <summary>
/// B07–B16：BJT 放大/频率响应电路的声明式定义（契约 §9）。
/// 坐标均为 10 px 网格；VCC 轨 Y=80、GND 轨 Y=480（与桌面端模板风格一致）。
/// 元件命名沿用 <c>CircuitTemplateFactory</c>：vs/C1/Rb1/Rb2/Rc/Re/Ce/C2/RL/VCC/GND，
/// 两级电路使用 Rb3/Rb4、Rc1/Rc2、Re1/Re2、Ce1/Ce2、C2（级间）/C3（输出）。
/// </summary>
public static class BjtAmpDefinitions
{
    /// <summary>10 个电路定义，按 B07–B16 顺序。</summary>
    public static IReadOnlyList<CircuitSpec> All { get; } = new List<CircuitSpec>
    {
        B07(),
        B08(),
        B09(),
        B10(),
        B11(),
        B12(),
        B13(),
        B14(),
        B15(),
        B16()
    };

    // ------------------------------------------------------------------
    // 元件工厂：统一 DefinitionId / Category / 单位 / 尺寸（沿用桌面模板）
    // ------------------------------------------------------------------

    private static ElementSpec Resistor(string id, string name, double kiloOhm, float x, float y,
        int rotationDeg = 90, string description = "")
        => new()
        {
            Id = id,
            Type = ElementType.Resistor,
            Name = name,
            DefinitionId = "passive.resistor",
            Category = "无源器件",
            Description = description,
            Number = kiloOhm,
            Unit = "kΩ",
            X = x,
            Y = y,
            RotationDeg = rotationDeg,
            Width = 80,
            Height = 50
        };

    private static ElementSpec Capacitor(string id, string name, double microFarad, float x, float y,
        int rotationDeg = 0, string description = "")
        => new()
        {
            Id = id,
            Type = ElementType.Capacitor,
            Name = name,
            DefinitionId = "passive.capacitor",
            Category = "无源器件",
            Description = description,
            Number = microFarad,
            Unit = "μF",
            X = x,
            Y = y,
            RotationDeg = rotationDeg,
            Width = 80,
            Height = 50
        };

    /// <summary>电解电容（极性耦合电容）：引脚为 +/–，正极按电路图方向放置。</summary>
    private static ElementSpec PolarizedCapacitor(string id, string name, double microFarad, float x, float y,
        int rotationDeg = 0, string description = "")
        => new()
        {
            Id = id,
            Type = ElementType.CapacitorPolarized,
            Name = name,
            DefinitionId = "passive.capacitor_polarized",
            Category = "无源器件",
            Description = description,
            Number = microFarad,
            Unit = "μF",
            X = x,
            Y = y,
            RotationDeg = rotationDeg,
            Width = 80,
            Height = 50
        };

    private static ElementSpec AcSource(double millivolt, float x = 80, float y = 320, double frequencyHz = 1000)
        => new()
        {
            Id = "vs",
            Type = ElementType.AcVoltageSource,
            Name = "交流信号源 vs",
            DefinitionId = "source.v_ac_small_signal",
            Category = "激励电源",
            Description = "输入小信号交流电压源（峰值 10 mV、1 kHz）",
            Number = millivolt,
            Unit = "mV",
            FrequencyHz = frequencyHz,
            X = x,
            Y = y,
            Width = 60,
            Height = 60
        };

    private static ElementSpec DcSource(double volt, float x = 620, float y = 280)
        => new()
        {
            Id = "VCC",
            Type = ElementType.DcVoltageSource,
            Name = "直流电源 VCC",
            DefinitionId = "source.v_dc",
            Category = "激励电源",
            Description = "单电源直流偏置 VCC",
            Number = volt,
            Unit = "V",
            X = x,
            Y = y,
            Width = 60,
            Height = 60
        };

    private static ElementSpec Npn(string id, string name, double beta, float x, float y,
        int rotationDeg = 0, bool mirrorX = false)
        => new()
        {
            Id = id,
            Type = ElementType.BjtNpn,
            Name = name,
            DefinitionId = "bjt.npn",
            Category = "半导体管",
            Description = "NPN 双极型晶体管（硅管，VBE≈0.7 V）",
            Number = beta,
            Unit = "β",
            Material = SemiconductorMaterial.Silicon,
            RotationDeg = rotationDeg,
            MirrorX = mirrorX,
            X = x,
            Y = y,
            Width = 80,
            Height = 80
        };

    private static ElementSpec Ground(float x = 340, float y = 502)
        => new()
        {
            Id = "GND",
            Type = ElementType.Ground,
            Name = "参考地 GND",
            DefinitionId = "reference.ground",
            Category = "参考地",
            Description = "参考节点 0 V",
            Number = 0,
            Unit = "V",
            X = x,
            Y = y,
            Width = 40,
            Height = 35
        };

    // ------------------------------------------------------------------
    // 网表工厂
    // ------------------------------------------------------------------

    /// <summary>普通信号网：由构建器在引脚之间自动布线（无固定电源轨）。</summary>
    private static NetSpec SignalNet(string name, params string[] pins)
        => new() { Name = name, Pins = pins, Rail = NetRail.None };

    /// <summary>顶部电源轨（Y=80）。</summary>
    private static NetSpec TopRail(string name, params string[] pins)
        => new() { Name = name, Pins = pins, Rail = NetRail.HorizontalTop, RailCoordinate = 80 };

    /// <summary>底部地轨（Y=480，GND 必须存在）。</summary>
    private static NetSpec BottomRail(string name, params string[] pins)
        => new() { Name = name, Pins = pins, Rail = NetRail.HorizontalBottom, RailCoordinate = 480 };

    // ------------------------------------------------------------------
    // 单管共射（分压偏置 + 可选 Ce 旁路 + 输出耦合 C2 + RL）
    // ------------------------------------------------------------------

    /// <summary>
    /// 标准分压偏置共射级的布局（与桌面模板 CreateCommonEmitterTemplate 对齐）：
    /// vs(80,320) → C1(160,260) → Q1 基极；Rb1(240,160)/Rb2(240,360) 分压；
    /// Rc(340,140) 接 VCC；Re(320,400) 接 GND；Ce(420,400) 旁路；C2(440,200) → RL(540,280)。
    /// </summary>
    private static List<ElementSpec> CommonEmitterStage(
        double vcc, double beta, double rb1, double rb2, double rc, double re,
        double c1, double c2, double rl, double vsMv, double ce = 0)
    {
        var elements = new List<ElementSpec>
        {
            AcSource(vsMv),
            Capacitor("C1", "输入耦合电容 C1", c1, 160, 260, 0,
                "输入耦合电容：隔断直流、传递交流小信号"),
            Resistor("Rb1", "基极偏置电阻 Rb1", rb1, 240, 160, 90,
                "分压偏置上偏置电阻：与 Rb2 共同固定基极直流电位"),
            Resistor("Rb2", "基极偏置电阻 Rb2", rb2, 240, 360, 90,
                "分压偏置下偏置电阻：与 Rb1 共同固定基极直流电位"),
            Npn("Q1", "NPN 晶体管 Q1", beta, 320, 260),
            Resistor("Rc", "集电极电阻 Rc", rc, 340, 140, 90,
                "集电极负载电阻：把集电极电流变化转换成电压输出"),
            Resistor("Re", "发射极电阻 Re", re, 320, 400, 90,
                "发射极负反馈电阻：稳定直流工作点"),
            Capacitor("C2", "输出耦合电容 C2", c2, 440, 200, 0,
                "输出耦合电容：隔断直流、把交流输出送给负载"),
            Resistor("RL", "负载电阻 RL", rl, 540, 280, 90,
                "交流负载电阻"),
            DcSource(vcc),
            Ground()
        };
        if (ce > 0)
        {
            elements.Add(Capacitor("Ce", "发射极旁路电容 Ce", ce, 420, 400, 90,
                "发射极旁路电容：中频段把 Re 交流短路，提高电压增益"));
        }
        return elements;
    }

    private static List<NetSpec> CommonEmitterNets(bool bypassed)
    {
        var emitterPins = new List<string> { "Q1.E", "Re.1" };
        var gndPins = new List<string> { "vs.neg", "Rb2.2", "Re.2" };
        if (bypassed)
        {
            emitterPins.Add("Ce.1");
            gndPins.Add("Ce.2");
        }
        gndPins.AddRange(new[] { "RL.2", "VCC.neg", "GND.GND" });

        return new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("Base", "C1.2", "Rb1.2", "Rb2.1", "Q1.B"),
            TopRail("VCC", "Rb1.1", "Rc.1", "VCC.pos"),
            SignalNet("Collector", "Rc.2", "Q1.C", "C2.1"),
            SignalNet("Emitter", emitterPins.ToArray()),
            SignalNet("Out", "C2.2", "RL.1"),
            BottomRail("GND", gndPins.ToArray())
        };
    }

    // ------------------------------------------------------------------
    // B07 共射（分压偏置 + Ce 旁路 + 信号源内阻 Rs + 电解输入耦合电容 C3）
    // ------------------------------------------------------------------

    private static CircuitSpec B07() => new()
    {
        Id = "B07",
        Title = "分压式偏置共射放大电路（Ce 旁路）",
        FileName = "B07-common-emitter-bypassed.circuit",
        Description = "共射放大电路的教学变体：输入侧加入信号源内阻 Rs（R6=50 Ω）与 47 μF 电解输入耦合电容 C3（正极接基极），"
                    + "用于说明源电压放大倍数 Aus=Av·Ri/(Rs+Ri) 以及源内阻对下限截止频率的影响（τ1=(Rs+Ri)C1）。"
                    + "分压式偏置稳定静态工作点，Ce 把 Re 交流旁路；由 Q 点求 rbe，再计算 Ri=Rb1∥Rb2∥rbe、Ro≈Rc 与频响。"
                    + "教材参考：童诗白《模拟电子技术基础》第 2 章（共射放大电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10, 60, 440),
            Resistor("R6", "信号源内阻 R6", 0.05, 62.43184f, 329.16092f, 90,
                "信号源内阻 Rs=50 Ω：与放大器输入电阻构成分压，引出源电压放大倍数 Aus=Av·Ri/(Rs+Ri)"),
            PolarizedCapacitor("C3", "电解输入耦合电容 C3", 47, 143.9952f, 260.2022f, 180,
                "电解输入耦合电容：隔断直流、传递交流小信号；正极接基极，兼作电解电容直流偏置极性教学点"),
            Resistor("Rb1", "基极偏置电阻 Rb1", 33, 220, 160, 90,
                "分压偏置上偏置电阻：与 Rb2 共同固定基极直流电位"),
            Resistor("Rb2", "基极偏置电阻 Rb2", 10, 220, 360, 90,
                "分压偏置下偏置电阻：与 Rb1 共同固定基极直流电位"),
            Npn("Q1", "NPN 晶体管 Q1", 100, 320, 260),
            Resistor("Rc", "集电极电阻 Rc", 1, 340, 140, 90,
                "集电极负载电阻：把集电极电流变化转换成电压输出"),
            Resistor("Re", "发射极电阻 Re", 1, 320, 400, 90,
                "发射极负反馈电阻：稳定直流工作点"),
            Capacitor("Ce", "发射极旁路电容 Ce", 47, 420, 400, 90,
                "发射极旁路电容：中频段把 Re 交流短路，提高电压增益"),
            Capacitor("C2", "输出耦合电容 C2", 10, 440, 200, 0,
                "输出耦合电容：隔断直流、把交流输出送给负载"),
            Resistor("RL", "负载电阻 RL", 1, 540, 280, 90,
                "交流负载电阻"),
            DcSource(12, 640, 280),
            Ground()
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "R6.2"),
            SignalNet("SourceMid", "R6.1", "C3.-"),
            SignalNet("Base", "C3.+", "Rb1.2", "Rb2.1", "Q1.B"),
            TopRail("VCC", "Rb1.1", "Rc.1", "VCC.pos"),
            SignalNet("Collector", "Rc.2", "Q1.C", "C2.1"),
            SignalNet("Emitter", "Q1.E", "Re.1", "Ce.1"),
            SignalNet("Out", "C2.2", "RL.1"),
            BottomRail("GND", "vs.neg", "Rb2.2", "Re.2", "Ce.2", "RL.2", "VCC.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B08 共射（Re 不旁路，无 Ce）
    // ------------------------------------------------------------------

    private static CircuitSpec B08() => new()
    {
        Id = "B08",
        Title = "分压式偏置共射放大电路（Re 不旁路）",
        FileName = "B08-common-emitter-unbypassed.circuit",
        Description = "发射极电阻 Re 不旁路时的共射放大电路：Re 引入交流电流负反馈，"
                    + "中频增益降为 Av≈-β(Rc∥RL)/[rbe+(1+β)Re]，输入电阻提高为 Ri=Rb1∥Rb2∥[rbe+(1+β)Re]，"
                    + "用于对比 B07 理解负反馈对增益与输入电阻的影响。教材参考：童诗白《模拟电子技术基础》第 2 章。",
        Elements = CommonEmitterStage(
            vcc: 12, beta: 100, rb1: 51, rb2: 15, rc: 3, re: 1,
            c1: 10, c2: 10, rl: 10, vsMv: 10, ce: 0),
        Nets = CommonEmitterNets(bypassed: false),
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B09 共射 + 输出耦合 RL（负载效应）
    // ------------------------------------------------------------------

    private static CircuitSpec B09() => new()
    {
        Id = "B09",
        Title = "共射放大电路（输出耦合与负载效应）",
        FileName = "B09-common-emitter-loaded.circuit",
        Description = "输出端经耦合电容 C2 接入交流负载 RL 的共射放大电路："
                    + "负载使集电极交流等效电阻由 Rc 降为 Rc∥RL，电压增益 Av=-β(Rc∥RL)/rbe 明显下降，"
                    + "同时输出电阻变为 Ro≈Rc（不含 RL 本身）。用于理解负载效应与多级电路级联时的增益下降。"
                    + "教材参考：童诗白《模拟电子技术基础》第 2 章。",
        Elements = CommonEmitterStage(
            vcc: 12, beta: 100, rb1: 51, rb2: 15, rc: 3, re: 1,
            c1: 10, c2: 10, rl: 2.2, vsMv: 10, ce: 47),
        Nets = CommonEmitterNets(bypassed: true),
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B10 共集电极（射极跟随器）：集电极直接接 VCC，无 Rc
    // ------------------------------------------------------------------

    private static CircuitSpec B10() => new()
    {
        Id = "B10",
        Title = "共集电极放大电路（射极跟随器）",
        FileName = "B10-common-collector.circuit",
        Description = "共集电极（射极跟随器）电路：集电极直接接 VCC（无 Rc），交流输出取自发射极，"
                    + "经耦合电容 C2 送到负载 RL；分压式偏置由 Rb1/Rb2 提供。中频特性 Av≈(1+β)Re′/[rbe+(1+β)Re′]<1（略小于 1、同相），"
                    + "输入电阻高、输出电阻低，常用于缓冲与阻抗匹配。教材参考：童诗白《模拟电子技术基础》第 2 章（共集电极电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 260, 0,
                "输入耦合电容：把交流信号耦合到基极"),
            Resistor("Rb1", "基极偏置电阻 Rb1", 51, 240, 160, 90,
                "分压偏置上偏置电阻：与 Rb2 固定基极直流电位"),
            Resistor("Rb2", "基极偏置电阻 Rb2", 15, 240, 360, 90,
                "分压偏置下偏置电阻：与 Rb1 固定基极直流电位"),
            Npn("Q1", "NPN 晶体管 Q1", 100, 320, 260),
            Resistor("Re", "发射极电阻 Re", 2, 340, 400, 90,
                "发射极负载电阻：提供直流通路并作为交流输出端"),
            Capacitor("C2", "输出耦合电容 C2", 10, 460, 360, 0,
                "输出耦合电容：把发射极交流输出送给负载"),
            Resistor("RL", "负载电阻 RL", 10, 560, 400, 90,
                "交流负载电阻"),
            DcSource(12),
            Ground()
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("Base", "C1.2", "Rb1.2", "Rb2.1", "Q1.B"),
            TopRail("VCC", "Rb1.1", "Q1.C", "VCC.pos"),
            SignalNet("Emitter", "Q1.E", "Re.1", "C2.1"),
            SignalNet("Out", "C2.2", "RL.1"),
            BottomRail("GND", "vs.neg", "Rb2.2", "Re.2", "RL.2", "VCC.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "CommonCollector", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B11 共基极放大电路：输入到发射极，基极经 Cb 旁路，Rc 输出
    // ------------------------------------------------------------------

    private static CircuitSpec B11() => new()
    {
        Id = "B11",
        Title = "共基极放大电路",
        FileName = "B11-common-base-amplifier.circuit",
        Description = "共基极放大电路：输入信号经 C1 加到发射极，基极经旁路电容 Cb 对地交流短路（共基），"
                    + "集电极经 Rc 输出、再经 C2 耦合到负载 RL。中频电压增益 Au≈β(Rc∥RL)/hie 为正（同相），"
                    + "输入电阻低（ri′≈Re∥hib）、输出电阻高（ro′≈Rc），高频特性较好。"
                    + "教材参考：童诗白《模拟电子技术基础》第 2 章（共基极电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10, 80, 300),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 240, 0,
                "输入耦合电容：把交流信号耦合到发射极"),
            Resistor("Re", "发射极电阻 Re", 1.5, 220, 390, 90,
                "发射极偏置电阻：提供直流偏置，并与发射极构成低阻输入端"),
            Resistor("Rb1", "基极偏置电阻 Rb1", 51, 480, 160, 90,
                "分压偏置上偏置电阻：设置基极直流电位"),
            Resistor("Rb2", "基极偏置电阻 Rb2", 15, 320, 390, 90,
                "分压偏置下偏置电阻：设置基极直流电位"),
            Capacitor("Cb", "基极旁路电容 Cb", 47, 480, 390, 90,
                "基极旁路电容：中频段把基极交流接地，构成共基组态"),
            // 镜像后旋转 90°：E 朝左、C 朝右、B 朝下，沿用教材共基极画法。
            Npn("Q1", "NPN 晶体管 Q1", 80, 320, 260, rotationDeg: 90, mirrorX: true),
            Resistor("Rc", "集电极电阻 Rc", 3, 400, 160, 90,
                "集电极负载电阻：把集电极电流变化转变为电压输出"),
            Capacitor("C2", "输出耦合电容 C2", 10, 620, 240, 0,
                "输出耦合电容：隔直并驱动负载"),
            Resistor("RL", "负载电阻 RL", 3, 720, 360, 90,
                "交流负载电阻"),
            DcSource(12, 840, 300),
            Ground(320)
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("EmitterIn", "C1.2", "Q1.E", "Re.1"),
            new() { Name = "Base", Pins = ["Q1.B", "Rb1.2", "Rb2.1", "Cb.1"],
                Rail = NetRail.HorizontalBottom, RailCoordinate = 320, Margin = 0 },
            TopRail("VCC", "Rb1.1", "Rc.1", "VCC.pos"),
            SignalNet("Collector", "Rc.2", "Q1.C", "C2.1"),
            SignalNet("Out", "C2.2", "RL.1"),
            BottomRail("GND", "vs.neg", "Cb.2", "Re.2", "Rb2.2", "RL.2", "VCC.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "CommonBase", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B12 阻容耦合两级共射
    // ------------------------------------------------------------------

    private static CircuitSpec B12() => new()
    {
        Id = "B12",
        Title = "阻容耦合两级共射放大电路",
        FileName = "B12-rc-coupled-two-stage.circuit",
        Description = "阻容耦合两级共射放大电路：Q1、Q2 各自构成分压式偏置共射级，级间由耦合电容 C2 连接"
                    + "（前级集电极经 C2 接后级基极），输出经 C3 耦合到 RL。两级放大可提高总增益，"
                    + "但级间耦合电容与后级输入电阻会带来低频衰减与增益下降。"
                    + "说明：多级级联求解属后续开发规划，当前引擎仅预留 MultiStageAmplifier/Cascade 接口并明确拒绝，"
                    + "本文件用于展示可打开、可编辑的两级拓扑。教材参考：童诗白《模拟电子技术基础》第 2、3 章。",
        Elements = new List<ElementSpec>
        {
            AcSource(10),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 260, 0,
                "第一级输入耦合电容"),
            Resistor("Rb1", "第一级上偏置电阻 Rb1", 51, 240, 160, 90,
                "第一级分压偏置上偏置电阻"),
            Resistor("Rb2", "第一级下偏置电阻 Rb2", 15, 240, 360, 90,
                "第一级分压偏置下偏置电阻"),
            Npn("Q1", "第一级 NPN 晶体管 Q1", 100, 320, 260),
            Resistor("Rc1", "第一级集电极电阻 Rc1", 3, 340, 140, 90,
                "第一级集电极负载电阻"),
            Resistor("Re1", "第一级发射极电阻 Re1", 1, 320, 400, 90,
                "第一级发射极电阻"),
            Capacitor("Ce1", "第一级旁路电容 Ce1", 47, 420, 400, 90,
                "第一级发射极旁路电容：中频段旁路 Re1"),
            Capacitor("C2", "级间耦合电容 C2", 10, 480, 200, 0,
                "级间耦合电容：把第一级集电极交流输出耦合到第二级基极"),
            Resistor("Rb3", "第二级上偏置电阻 Rb3", 51, 600, 160, 90,
                "第二级分压偏置上偏置电阻"),
            Resistor("Rb4", "第二级下偏置电阻 Rb4", 15, 600, 360, 90,
                "第二级分压偏置下偏置电阻"),
            Npn("Q2", "第二级 NPN 晶体管 Q2", 100, 680, 260),
            Resistor("Rc2", "第二级集电极电阻 Rc2", 3, 700, 140, 90,
                "第二级集电极负载电阻"),
            Resistor("Re2", "第二级发射极电阻 Re2", 1, 680, 400, 90,
                "第二级发射极电阻"),
            Capacitor("Ce2", "第二级旁路电容 Ce2", 47, 780, 400, 90,
                "第二级发射极旁路电容：中频段旁路 Re2"),
            Capacitor("C3", "输出耦合电容 C3", 10, 820, 200, 0,
                "输出耦合电容：把第二级集电极交流输出送给负载"),
            Resistor("RL", "负载电阻 RL", 10, 920, 280, 90,
                "交流负载电阻"),
            DcSource(12, 1000, 280),
            Ground(680, 502)
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("Base1", "C1.2", "Rb1.2", "Rb2.1", "Q1.B"),
            TopRail("VCC", "Rb1.1", "Rc1.1", "Rb3.1", "Rc2.1", "VCC.pos"),
            SignalNet("Collector1", "Rc1.2", "Q1.C", "C2.1"),
            SignalNet("Emitter1", "Q1.E", "Re1.1", "Ce1.1"),
            SignalNet("Base2", "C2.2", "Rb3.2", "Rb4.1", "Q2.B"),
            SignalNet("Emitter2", "Q2.E", "Re2.1", "Ce2.1"),
            SignalNet("Collector2", "Rc2.2", "Q2.C", "C3.1"),
            SignalNet("Out", "C3.2", "RL.1"),
            BottomRail("GND", "vs.neg", "Rb2.2", "Re1.2", "Ce1.2", "Rb4.2", "Re2.2", "Ce2.2", "RL.2", "VCC.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.MultiStageAmplifier, "Cascade", SolveSucceeds: false)
    };

    // ------------------------------------------------------------------
    // B13 直接耦合两级共射（级间无电容）
    // ------------------------------------------------------------------

    private static CircuitSpec B13() => new()
    {
        Id = "B13",
        Title = "直接耦合两级共射放大电路",
        FileName = "B13-direct-coupled-two-stage.circuit",
        Description = "直接耦合两级共射放大电路：第一级集电极直接与第二级基极相连（级间无耦合电容），"
                    + "低频响应可延伸到直流，但各级静态工作点相互影响、零点漂移更明显；第二级由 Rb3/Rb4 分压偏置，"
                    + "输出经 C2 耦合到 RL。说明：多级级联求解属后续开发规划，当前引擎仅预留 MultiStageAmplifier/Cascade 接口并明确拒绝。"
                    + "教材参考：童诗白《模拟电子技术基础》第 3 章（多级放大电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 260, 0,
                "第一级输入耦合电容"),
            Resistor("Rb1", "第一级上偏置电阻 Rb1", 51, 240, 160, 90,
                "第一级分压偏置上偏置电阻"),
            Resistor("Rb2", "第一级下偏置电阻 Rb2", 15, 240, 360, 90,
                "第一级分压偏置下偏置电阻"),
            Npn("Q1", "第一级 NPN 晶体管 Q1", 100, 320, 260),
            Resistor("Rc1", "第一级集电极电阻 Rc1", 3, 340, 140, 90,
                "第一级集电极负载电阻"),
            Resistor("Re1", "第一级发射极电阻 Re1", 1, 320, 400, 90,
                "第一级发射极电阻：稳定直流工作点"),
            Resistor("Rb3", "第二级上偏置电阻 Rb3", 51, 600, 160, 90,
                "第二级分压偏置上偏置电阻"),
            Resistor("Rb4", "第二级下偏置电阻 Rb4", 15, 600, 360, 90,
                "第二级分压偏置下偏置电阻"),
            Npn("Q2", "第二级 NPN 晶体管 Q2", 100, 680, 260),
            Resistor("Rc2", "第二级集电极电阻 Rc2", 3, 700, 140, 90,
                "第二级集电极负载电阻"),
            Resistor("Re2", "第二级发射极电阻 Re2", 1, 680, 400, 90,
                "第二级发射极电阻：稳定直流工作点"),
            Capacitor("C2", "输出耦合电容 C2", 10, 820, 200, 0,
                "输出耦合电容：把第二级集电极交流输出送给负载"),
            Resistor("RL", "负载电阻 RL", 10, 920, 280, 90,
                "交流负载电阻"),
            DcSource(12, 1000, 280),
            Ground(680, 502)
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("Base1", "C1.2", "Rb1.2", "Rb2.1", "Q1.B"),
            TopRail("VCC", "Rb1.1", "Rc1.1", "Rb3.1", "Rc2.1", "VCC.pos"),
            SignalNet("Collector1_Base2", "Rc1.2", "Q1.C", "Q2.B", "Rb3.2", "Rb4.1"),
            SignalNet("Emitter1", "Q1.E", "Re1.1"),
            SignalNet("Emitter2", "Q2.E", "Re2.1"),
            SignalNet("Collector2", "Rc2.2", "Q2.C", "C2.1"),
            SignalNet("Out", "C2.2", "RL.1"),
            BottomRail("GND", "vs.neg", "Rb2.2", "Re1.2", "Rb4.2", "Re2.2", "RL.2", "VCC.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.MultiStageAmplifier, "Cascade", SolveSucceeds: false)
    };

    // ------------------------------------------------------------------
    // B14 共射–共集两级（第一级共射 + 第二级射极跟随器）
    // ------------------------------------------------------------------

    private static CircuitSpec B14() => new()
    {
        Id = "B14",
        Title = "共射–共集两级放大电路",
        FileName = "B14-ce-cc-two-stage.circuit",
        Description = "共射–共集两级放大电路：第一级为分压偏置共射放大（Rc1 输出、Ce1 旁路 Re1），"
                    + "经级间耦合电容 C2 驱动第二级共集电极（射极跟随器）；第二级集电极直接接 VCC、"
                    + "输出取自发射极 Re2 并经 C3 耦合到 RL。该组合兼具较高电压增益与低输出电阻（缓冲级）。"
                    + "说明：多级级联求解属后续开发规划，当前引擎仅预留 MultiStageAmplifier/Cascade 接口并明确拒绝。"
                    + "教材参考：童诗白《模拟电子技术基础》第 3 章（多级放大电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 260, 0,
                "第一级输入耦合电容"),
            Resistor("Rb1", "第一级上偏置电阻 Rb1", 51, 240, 160, 90,
                "第一级分压偏置上偏置电阻"),
            Resistor("Rb2", "第一级下偏置电阻 Rb2", 15, 240, 360, 90,
                "第一级分压偏置下偏置电阻"),
            Npn("Q1", "第一级 NPN 晶体管 Q1", 100, 320, 260),
            Resistor("Rc1", "第一级集电极电阻 Rc1", 3, 340, 140, 90,
                "第一级集电极负载电阻"),
            Resistor("Re1", "第一级发射极电阻 Re1", 1, 320, 400, 90,
                "第一级发射极电阻"),
            Capacitor("Ce1", "第一级旁路电容 Ce1", 47, 420, 400, 90,
                "第一级发射极旁路电容：中频段旁路 Re1"),
            Capacitor("C2", "级间耦合电容 C2", 10, 480, 200, 0,
                "级间耦合电容：把第一级集电极交流输出耦合到第二级基极"),
            Resistor("Rb3", "第二级上偏置电阻 Rb3", 51, 600, 160, 90,
                "第二级分压偏置上偏置电阻"),
            Resistor("Rb4", "第二级下偏置电阻 Rb4", 15, 600, 360, 90,
                "第二级分压偏置下偏置电阻"),
            Npn("Q2", "第二级 NPN 晶体管 Q2", 100, 680, 260),
            Resistor("Re2", "第二级发射极电阻 Re2", 2, 700, 400, 90,
                "第二级发射极电阻：射极跟随器输出端"),
            Capacitor("C3", "输出耦合电容 C3", 10, 820, 360, 0,
                "输出耦合电容：把射极跟随器输出送给负载"),
            Resistor("RL", "负载电阻 RL", 10, 920, 400, 90,
                "交流负载电阻"),
            DcSource(12, 1000, 280),
            Ground(680, 502)
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("Base1", "C1.2", "Rb1.2", "Rb2.1", "Q1.B"),
            TopRail("VCC", "Rb1.1", "Rc1.1", "Q2.C", "Rb3.1", "VCC.pos"),
            SignalNet("Collector1", "Rc1.2", "Q1.C", "C2.1"),
            SignalNet("Emitter1", "Q1.E", "Re1.1", "Ce1.1"),
            SignalNet("Base2", "C2.2", "Rb3.2", "Rb4.1", "Q2.B"),
            SignalNet("Emitter2", "Q2.E", "Re2.1", "C3.1"),
            SignalNet("Out", "C3.2", "RL.1"),
            BottomRail("GND", "vs.neg", "Rb2.2", "Re1.2", "Ce1.2", "Rb4.2", "Re2.2", "RL.2", "VCC.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.MultiStageAmplifier, "Cascade", SolveSucceeds: false)
    };

    // ------------------------------------------------------------------
    // B15 耦合/旁路电容低频响应（单管共射，突出 fL）
    // ------------------------------------------------------------------

    private static CircuitSpec B15() => new()
    {
        Id = "B15",
        Title = "共射放大电路的耦合/旁路电容低频响应",
        FileName = "B15-low-frequency-response.circuit",
        Description = "共射放大电路的低频响应：输入耦合电容 C1、输出耦合电容 C2 与发射极旁路电容 Ce 分别与"
                    + "输入电阻、输出回路电阻、发射极等效电阻构成下限截止频率 fL1、fL2、fLe，总下限截止频率 "
                    + "fL=√(fL1²+fL2²+fLe²)。本文件取较大的电容值（C1=C2=10 μF、Ce=100 μF）以突出 fL 的作用，"
                    + "并与 B16 高频响应共同说明放大电路的频率特性。教材参考：童诗白《模拟电子技术基础》第 2 章（频率响应）。",
        Elements = CommonEmitterStage(
            vcc: 12, beta: 100, rb1: 51, rb2: 15, rc: 3, re: 1,
            c1: 10, c2: 10, rl: 10, vsMv: 10, ce: 100),
        Nets = CommonEmitterNets(bypassed: true),
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B16 共射放大电路高频响应（模型估算 Cπ/Cμ）
    // ------------------------------------------------------------------

    private static CircuitSpec B16() => new()
    {
        Id = "B16",
        Title = "共射放大电路的高频响应（模型估算）",
        FileName = "B16-high-frequency-response.circuit",
        Description = "共射放大电路的高频响应：晶体管极间结电容 Cπ 与 Cμ 在高频段形成主极点，"
                    + "Cμ 经密勒效应倍增为 (1+|Av|)Cμ 并与输入回路电阻构成上限截止频率 fH≈1/(2πRin′Cin′)。"
                    + "说明：本轮高频参数取模型估算值（Cπ=30 pF、Cμ=3 pF），并非具体器件的实测参数，"
                    + "fH 与通频带 BW 为教学估算结果。教材参考：童诗白《模拟电子技术基础》第 2 章（频率响应）。",
        Elements = CommonEmitterStage(
            vcc: 12, beta: 100, rb1: 51, rb2: 15, rc: 3, re: 1,
            c1: 10, c2: 10, rl: 10, vsMv: 10, ce: 47),
        Nets = CommonEmitterNets(bypassed: true),
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", SolveSucceeds: true)
    };
}
