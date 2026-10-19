using System.Collections.Generic;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace CircuitModels.BjtBias;

/// <summary>
/// B01–B06：BJT 偏置 / 静态工作点（Q 点）电路的声明式定义（契约 §8/§9）。
/// 坐标均为 10 px 网格；VCC 轨 Y=80、GND 轨 Y=480（与桌面模板
/// <c>Services/CircuitTemplateFactory.cs</c> 的风格一致）。
/// 元件命名沿用桌面模板：VCC/V1、Rb/Rb1/Rb2、Rc、Re、Ce、Cb、Q1、GND。
/// 六份文件均为直流偏置分析（无交流输入源），用于讲清 Q 点估算与偏置方式差异。
/// </summary>
public static class BjtBiasDefinitions
{
    /// <summary>6 个电路定义，按 B01–B06 顺序（文件名见契约 §9）。</summary>
    public static IReadOnlyList<CircuitSpec> All { get; } = new List<CircuitSpec>
    {
        B01(),
        B02(),
        B03(),
        B04(),
        B05(),
        B06()
    };

    // ------------------------------------------------------------------
    // 元件工厂：统一 DefinitionId / Category / 单位 / 尺寸（沿用桌面模板）
    // ------------------------------------------------------------------

    /// <summary>竖直电阻（RotationDeg=90）：引脚 1 在上、引脚 2 在下（Y∓40）。</summary>
    private static ElementSpec Resistor(string id, string name, double kiloOhm, float x, float y,
        string description)
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
            RotationDeg = 90,
            Width = 80,
            Height = 50
        };

    /// <summary>竖直电容（RotationDeg=90）：引脚 1 在上、引脚 2 在下（Y∓35）。</summary>
    private static ElementSpec Capacitor(string id, string name, double microFarad, float x, float y,
        string description)
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
            RotationDeg = 90,
            Width = 80,
            Height = 50
        };

    /// <summary>单电源直流电压源（竖直，正极在上）。</summary>
    private static ElementSpec DcSource(double volt, string description, float x = 640, float y = 280)
        => new()
        {
            Id = "VCC",
            Type = ElementType.DcVoltageSource,
            Name = "直流电源 VCC",
            DefinitionId = "source.v_dc",
            Category = "激励电源",
            Description = description,
            Number = volt,
            Unit = "V",
            FrequencyHz = 1000,
            X = x,
            Y = y,
            Width = 60,
            Height = 60
        };

    private static ElementSpec Npn(double beta, string description, float x = 320, float y = 260)
        => new()
        {
            Id = "Q1",
            Type = ElementType.BjtNpn,
            Name = "NPN 晶体管 Q1",
            DefinitionId = "bjt.npn",
            Category = "半导体管",
            Description = description,
            Number = beta,
            Unit = "β",
            Material = SemiconductorMaterial.Silicon,
            X = x,
            Y = y,
            Width = 80,
            Height = 80
        };

    /// <summary>PNP 晶体管：默认符号发射极在上（Y−35）、集电极在下（Y+35）、基极在左。</summary>
    private static ElementSpec Pnp(double beta, string description, float x = 320, float y = 260)
        => new()
        {
            Id = "Q1",
            Type = ElementType.BjtPnp,
            Name = "PNP 晶体管 Q1",
            DefinitionId = "bjt.pnp",
            Category = "半导体管",
            Description = description,
            Number = beta,
            Unit = "β",
            Material = SemiconductorMaterial.Silicon,
            X = x,
            Y = y,
            Width = 80,
            Height = 80
        };

    /// <summary>参考地符号：引脚在元件中心上方 22 px，故 Y=502 时引脚落在 Y=480 地轨上。</summary>
    private static ElementSpec Ground(float x)
        => new()
        {
            Id = "GND",
            Type = ElementType.Ground,
            Name = "参考地 GND",
            DefinitionId = "reference.ground",
            Category = "参考地",
            Description = "参考节点 0 V：直流通路的公共参考电位",
            Number = 0,
            Unit = "V",
            X = x,
            Y = 502,
            Width = 40,
            Height = 35
        };

    // ------------------------------------------------------------------
    // 网表工厂
    // ------------------------------------------------------------------

    /// <summary>普通信号网：由构建器在引脚之间自动正交布线（无固定电源轨）。</summary>
    private static NetSpec SignalNet(string name, params string[] pins)
        => new() { Name = name, Pins = pins, Rail = NetRail.None };

    /// <summary>顶部电源轨（Y=80）。</summary>
    private static NetSpec TopRail(string name, params string[] pins)
        => new() { Name = name, Pins = pins, Rail = NetRail.HorizontalTop, RailCoordinate = 80 };

    /// <summary>底部地轨（Y=480）：GND 网必须存在，并包含 GND.GND 参考地引脚。</summary>
    private static NetSpec BottomRail(string name, params string[] pins)
        => new() { Name = name, Pins = pins, Rail = NetRail.HorizontalBottom, RailCoordinate = 480 };

    // ------------------------------------------------------------------
    // B01 固定偏置共射 Q 点
    //   VCC=12 V、Rb=400 kΩ、Rc=3 kΩ、β=100
    //   IBQ=(VCC-UBE)/Rb≈28.3 μA、ICQ≈2.83 mA、UCEQ=VCC-ICQ·Rc≈3.53 V（放大区）
    // ------------------------------------------------------------------
    private static CircuitSpec B01() => new()
    {
        Id = "B01",
        Title = "固定偏置共射放大电路的静态工作点 (Q 点)",
        FileName = "B01-fixed-bias-common-emitter.circuit",
        Description = "固定偏置（固定基极电流）共射电路的直流工作点分析：基极电流只由 Rb 决定，"
                    + "IBQ=(VCC−UBE)/Rb、ICQ=β·IBQ、UCEQ=VCC−ICQ·Rc。"
                    + "本例 VCC=12 V、Rb=400 kΩ、Rc=3 kΩ、β=100，解得 IBQ≈28.3 μA、ICQ≈2.83 mA、"
                    + "UCEQ≈3.53 V，晶体管工作在放大区。固定偏置结构最简单，但 Q 点随 β 与温度变化而漂移，"
                    + "是理解“为什么需要稳定偏置”的起点。教材参考：童诗白、华成英《模拟电子技术基础》"
                    + "（第 5 版）第 2 章 基本放大电路（2.2 节 共射放大电路的工作原理与静态工作点）。",
        Elements = new List<ElementSpec>
        {
            DcSource(12, "单电源直流偏置 VCC=12 V：为基极偏置回路与集电极回路提供能量"),
            Resistor("Rb", "基极偏置电阻 Rb", 400, 240, 160,
                "固定偏置电阻：IBQ≈(VCC−UBE)/Rb，决定基极电流（本电路 Q 点的关键元件）"),
            Resistor("Rc", "集电极电阻 Rc", 3, 340, 140,
                "集电极负载电阻：把集电极电流转换为管压降 UCEQ=VCC−ICQ·Rc"),
            Npn(100, "NPN 硅管，β=100，UBE≈0.7 V；固定偏置下 Q 点应位于放大区"),
            Ground(440)
        },
        Nets = new List<NetSpec>
        {
            TopRail("VCC", "VCC.pos", "Rb.1", "Rc.1"),
            SignalNet("Base", "Rb.2", "Q1.B"),
            SignalNet("Collector", "Rc.2", "Q1.C"),
            BottomRail("GND", "VCC.neg", "Q1.E", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "FixedBias", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B02 分压偏置共射 Q 点（Re 不旁路）
    //   VCC=12 V、Rb1=100 kΩ、Rb2=50 kΩ、Rc=2.2 kΩ、Re=1 kΩ、β=100
    //   Vth=4 V、Rth≈33.3 kΩ、IBQ≈24.6 μA、ICQ≈2.46 mA、UCEQ≈4.11 V（放大区）
    // ------------------------------------------------------------------
    private static CircuitSpec B02() => new()
    {
        Id = "B02",
        Title = "分压式偏置共射放大电路的静态工作点（Re 不旁路）",
        FileName = "B02-voltage-divider-bias.circuit",
        Description = "分压式偏置（工作点稳定）共射电路的直流工作点：Rb1/Rb2 分压固定基极电位，"
                    + "Re 上的直流负反馈自动稳定 Q 点。戴维南等效后 Vth=VCC·Rb2/(Rb1+Rb2)、Rth=Rb1∥Rb2，"
                    + "IBQ=(Vth−UBE)/(Rth+(1+β)Re)，ICQ=β·IBQ，UCEQ=VCC−ICQ·Rc−(1+β)IBQ·Re。"
                    + "本例 VCC=12 V、Rb1=100 kΩ、Rb2=50 kΩ、Rc=2.2 kΩ、Re=1 kΩ、β=100，"
                    + "解得 Vth=4 V、Rth≈33.3 kΩ、IBQ≈24.6 μA、ICQ≈2.46 mA、UCEQ≈4.11 V（放大区）。"
                    + "本文件不装旁路电容，Re 同时引入交流负反馈；与 B03 对比可理解 Ce 的作用。"
                    + "教材参考：童诗白《模拟电子技术基础》（第 5 版）第 2 章（2.4 节 工作点稳定电路）。",
        Elements = new List<ElementSpec>
        {
            DcSource(12, "单电源直流偏置 VCC=12 V：为分压偏置网络与集电极回路供电"),
            Resistor("Rb1", "基极偏置电阻 Rb1", 100, 240, 160,
                "分压偏置上偏置电阻：与 Rb2 分压得到基极电位 Vth（VCC 侧）"),
            Resistor("Rb2", "基极偏置电阻 Rb2", 50, 240, 360,
                "分压偏置下偏置电阻：与 Rb1 分压得到基极电位 Vth（GND 侧）"),
            Resistor("Rc", "集电极电阻 Rc", 2.2, 340, 140,
                "集电极负载电阻：把集电极电流变化转换为电压"),
            Resistor("Re", "发射极电阻 Re", 1, 340, 400,
                "发射极电阻（本文件不旁路）：引入直流负反馈稳定 Q 点，同时引入交流负反馈"),
            Npn(100, "NPN 硅管，β=100，UBE≈0.7 V；分压偏置 + Re 负反馈使 Q 点稳定"),
            Ground(440)
        },
        Nets = new List<NetSpec>
        {
            TopRail("VCC", "VCC.pos", "Rb1.1", "Rc.1"),
            SignalNet("Base", "Rb1.2", "Rb2.1", "Q1.B"),
            SignalNet("Collector", "Rc.2", "Q1.C"),
            SignalNet("Emitter", "Q1.E", "Re.1"),
            BottomRail("GND", "VCC.neg", "Rb2.2", "Re.2", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B03 分压偏置共射 Q 点（带 Ce 旁路）
    //   VCC=12 V、Rb1=120 kΩ、Rb2=40 kΩ、Rc=3 kΩ、Re=1.5 kΩ、Ce=10 μF、β=100
    //   Vth=3 V、Rth=30 kΩ、IBQ≈12.7 μA、ICQ≈1.27 mA、UCEQ≈6.28 V（放大区）
    // ------------------------------------------------------------------
    private static CircuitSpec B03() => new()
    {
        Id = "B03",
        Title = "分压式偏置共射放大电路（带发射极旁路电容 Ce）的静态工作点",
        FileName = "B03-voltage-divider-bypassed.circuit",
        Description = "分压式偏置 + 发射极旁路电容 Ce 的共射电路：直流分析时 Ce 视为开路，"
                    + "Re 仍起稳定 Q 点的作用；交流中频段 Ce 容抗远小于 Re，把 Re 交流短路。"
                    + "Vth=VCC·Rb2/(Rb1+Rb2)、Rth=Rb1∥Rb2、IBQ=(Vth−UBE)/(Rth+(1+β)Re)。"
                    + "本例 VCC=12 V、Rb1=120 kΩ、Rb2=40 kΩ、Rc=3 kΩ、Re=1.5 kΩ、Ce=10 μF、β=100，"
                    + "解得 Vth=3 V、Rth=30 kΩ、IBQ≈12.7 μA、ICQ≈1.27 mA、UCEQ≈6.28 V（放大区）。"
                    + "与 B02 对比：Ce 不改变直流 Q 点，却使中频电压增益由 −βRc/[rbe+(1+β)Re] 提高到 −β·Rc/rbe。"
                    + "教材参考：童诗白《模拟电子技术基础》（第 5 版）第 2 章（2.4 节 旁路电容的作用）。",
        Elements = new List<ElementSpec>
        {
            DcSource(12, "单电源直流偏置 VCC=12 V：为分压偏置网络与集电极回路供电"),
            Resistor("Rb1", "基极偏置电阻 Rb1", 120, 240, 160,
                "分压偏置上偏置电阻：与 Rb2 分压得到基极电位 Vth（VCC 侧）"),
            Resistor("Rb2", "基极偏置电阻 Rb2", 40, 240, 360,
                "分压偏置下偏置电阻：与 Rb1 分压得到基极电位 Vth（GND 侧）"),
            Resistor("Rc", "集电极电阻 Rc", 3, 340, 140,
                "集电极负载电阻：把集电极电流变化转换为电压"),
            Resistor("Re", "发射极电阻 Re", 1.5, 340, 400,
                "发射极电阻（直流负反馈稳定 Q 点；中频段被 Ce 交流旁路）"),
            Capacitor("Ce", "发射极旁路电容 Ce", 10, 440, 400,
                "发射极旁路电容 Ce=10 μF：直流开路（不影响 Q 点），中频段把 Re 交流短路以提高增益"),
            Npn(100, "NPN 硅管，β=100，UBE≈0.7 V；分压偏置 + Re 稳定直流工作点"),
            Ground(340)
        },
        Nets = new List<NetSpec>
        {
            TopRail("VCC", "VCC.pos", "Rb1.1", "Rc.1"),
            SignalNet("Base", "Rb1.2", "Rb2.1", "Q1.B"),
            SignalNet("Collector", "Rc.2", "Q1.C"),
            SignalNet("Emitter", "Q1.E", "Re.1", "Ce.1"),
            BottomRail("GND", "VCC.neg", "Rb2.2", "Re.2", "Ce.2", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B04 集电极反馈偏置 Q 点
    //   VCC=12 V、Rb=180 kΩ（B–C）、Rc=2.4 kΩ、β=100
    //   IBQ=(VCC-UBE)/(Rb+(1+β)Rc)≈26.7 μA、ICQ≈2.67 mA、UCEQ≈5.52 V（放大区）
    // ------------------------------------------------------------------
    private static CircuitSpec B04() => new()
    {
        Id = "B04",
        Title = "集电极反馈偏置共射放大电路的静态工作点",
        FileName = "B04-collector-feedback-bias.circuit",
        Description = "集电极-基极电阻反馈偏置（电压反馈式偏置）共射电路：偏置电阻 Rb 跨接在集电极与基极之间，"
                    + "把集电极电压的变化反馈到基极，从而稳定 Q 点。基极回路 KVL 给出 "
                    + "IBQ=(VCC−UBE)/(Rb+(1+β)Rc)，ICQ=β·IBQ，UCEQ=VCC−(ICQ+IBQ)Rc。"
                    + "本例 VCC=12 V、Rb=180 kΩ、Rc=2.4 kΩ、β=100，解得 IBQ≈26.7 μA、"
                    + "ICQ≈2.67 mA、UCEQ≈5.52 V（放大区）。与固定偏置相比，Rb 接集电极使 Q 点对 β 的"
                    + "敏感度下降；Rb 还构成交流电压并联负反馈，使输入电阻降低、输出电阻约为 Rc∥Rb。"
                    + "教材参考：童诗白《模拟电子技术基础》（第 5 版）第 2 章（集电极-基极反馈偏置电路）。",
        Elements = new List<ElementSpec>
        {
            DcSource(12, "单电源直流偏置 VCC=12 V：为集电极回路与反馈偏置支路供电"),
            Resistor("Rb", "集电极反馈偏置电阻 Rb", 180, 240, 220,
                "集电极-基极反馈偏置电阻：既提供基极电流，又把 UCE 变化反馈到基极以稳定 Q 点"),
            Resistor("Rc", "集电极电阻 Rc", 2.4, 340, 140,
                "集电极负载电阻：同时参与反馈偏置方程 IBQ=(VCC−UBE)/(Rb+(1+β)Rc)"),
            Npn(100, "NPN 硅管，β=100，UBE≈0.7 V；反馈偏置使 Q 点更稳定"),
            Ground(440)
        },
        Nets = new List<NetSpec>
        {
            TopRail("VCC", "VCC.pos", "Rc.1"),
            SignalNet("Collector", "Rc.2", "Rb.1", "Q1.C"),
            SignalNet("Base", "Rb.2", "Q1.B"),
            BottomRail("GND", "VCC.neg", "Q1.E", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "CollectorFeedbackBias", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B05 共基极直流偏置
    //   VCC=12 V、Rb1=100 kΩ、Rb2=50 kΩ、Re=2 kΩ、Rc=2 kΩ、Cb=10 μF、β=100
    //   Vth=4 V、Rth≈33.3 kΩ、IBQ≈14.0 μA、IEQ≈1.43 mA、ICQ≈1.40 mA、UCEQ≈6.34 V（放大区）
    // ------------------------------------------------------------------
    private static CircuitSpec B05() => new()
    {
        Id = "B05",
        Title = "共基极放大电路的直流偏置与工作点",
        FileName = "B05-common-base-bias.circuit",
        Description = "共基极组态的直流偏置与 Q 点：Rb1/Rb2 分压固定基极电位，基极经旁路电容 Cb 对地交流短路，"
                    + "交流信号从发射极输入、集电极经 Rc 输出。直流分析时 Cb 视为开路，Q 点仍由分压偏置决定："
                    + "Vth=VCC·Rb2/(Rb1+Rb2)、Rth=Rb1∥Rb2、IBQ=(Vth−UBE)/(Rth+(1+β)Re)，"
                    + "IEQ=(1+β)IBQ、UCEQ=VCC−ICQ·Rc−IEQ·Re。"
                    + "本例 VCC=12 V、Rb1=100 kΩ、Rb2=50 kΩ、Re=2 kΩ、Rc=2 kΩ、Cb=10 μF、β=100，"
                    + "解得 Vth=4 V、Rth≈33.3 kΩ、IBQ≈14.0 μA、IEQ≈1.42 mA、ICQ≈1.40 mA、UCEQ≈6.36 V（放大区）。"
                    + "共基极电路输入电阻低、输出电阻高、高频特性好，Cb 保证基极交流接地是“共基”的关键。"
                    + "教材参考：童诗白《模拟电子技术基础》（第 5 版）第 2 章（共基极放大电路）。",
        Elements = new List<ElementSpec>
        {
            DcSource(12, "单电源直流偏置 VCC=12 V：为基极分压网络与集电极回路供电"),
            Resistor("Rb1", "基极偏置电阻 Rb1", 100, 260, 160,
                "分压偏置上偏置电阻：与 Rb2 分压得到基极电位 Vth（VCC 侧）"),
            Resistor("Rb2", "基极偏置电阻 Rb2", 50, 260, 360,
                "分压偏置下偏置电阻：与 Rb1 分压得到基极电位 Vth（GND 侧）"),
            Capacitor("Cb", "基极旁路电容 Cb", 10, 200, 260,
                "基极旁路电容 Cb=10 μF：中频段把基极交流接地，形成共基组态（直流开路）"),
            Resistor("Re", "发射极电阻 Re", 2, 340, 400,
                "发射极偏置电阻：提供直流通路，并构成低阻交流输入端"),
            Resistor("Rc", "集电极电阻 Rc", 2, 340, 140,
                "集电极负载电阻：把集电极电流变化转换为电压输出"),
            Npn(100, "NPN 硅管，β=100，UBE≈0.7 V；基极交流接地、信号由发射极输入"),
            Ground(340)
        },
        Nets = new List<NetSpec>
        {
            TopRail("VCC", "VCC.pos", "Rb1.1", "Rc.1"),
            SignalNet("Base", "Rb1.2", "Rb2.1", "Q1.B", "Cb.1"),
            SignalNet("Collector", "Rc.2", "Q1.C"),
            SignalNet("Emitter", "Q1.E", "Re.1"),
            BottomRail("GND", "VCC.neg", "Rb2.2", "Re.2", "Cb.2", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "CommonBase", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // B06 PNP 分压偏置 Q 点
    //   VCC=12 V、Rb1=100 kΩ(B–VCC)、Rb2=200 kΩ(B–GND)、Re=1 kΩ(E–VCC)、
    //   Rc=2 kΩ(C–GND)、β=100
    //   Vth=8 V、Rth≈66.7 kΩ、IBQ≈19.7 μA、ICQ≈1.97 mA、UECQ≈6.08 V（放大区）
    //   注：若 Rb2 仍取 50 kΩ，基极电位仅 4 V、发射极约 4.7 V，Re 压降过大导致 PNP 饱和，
    //       因此为保证放大区工作点，下偏置电阻取 200 kΩ（分压点 8 V，基极电位接近 VCC 侧）。
    // ------------------------------------------------------------------
    private static CircuitSpec B06() => new()
    {
        Id = "B06",
        Title = "PNP 分压偏置共射放大电路的静态工作点",
        FileName = "B06-pnp-voltage-divider-bias.circuit",
        Description = "PNP 管分压偏置共射电路的直流工作点：发射极经 Re 接 VCC、集电极经 Rc 接 GND，"
                    + "基极由 Rb1(B–VCC)/Rb2(B–GND) 分压。PNP 的电流方向与 NPN 相反（IE 从 VCC 经发射极流入，"
                    + "IB 自基极流出，IC 从集电极流出经 Rc 到地），偏置方程取镜像形式："
                    + "Vth=VCC·Rb2/(Rb1+Rb2)、Rth=Rb1∥Rb2、IBQ=(VCC−Vth−|UEB|)/(Rth+(1+β)Re)，"
                    + "ICQ=β·IBQ、UECQ=VCC−ICQ·Rc−IEQ·Re。"
                    + "本例 VCC=12 V、Rb1=100 kΩ、Rb2=200 kΩ、Re=1 kΩ、Rc=2 kΩ、β=100，"
                    + "解得 Vth=8 V、Rth≈66.7 kΩ、IBQ≈19.7 μA、ICQ≈1.97 mA、UECQ≈6.08 V（放大区）。"
                    + "说明：若 Rb2 仍取 50 kΩ，则基极电位仅 4 V、发射极约 4.7 V，Re 上压降过大使 PNP 进入饱和区，"
                    + "故本例取 Rb2=200 kΩ 使分压点（8 V）靠近 VCC 侧，符合 PNP 偏置“基极电位略低于发射极”的物理要求。"
                    + "符号默认发射极在上、集电极在下。教材参考：童诗白《模拟电子技术基础》（第 5 版）"
                    + "第 2 章 基本放大电路（PNP 放大电路与偏置的镜像分析）。",
        Elements = new List<ElementSpec>
        {
            DcSource(12, "单电源直流偏置 VCC=12 V：PNP 发射极经 Re 接 VCC 取得正偏电流"),
            Resistor("Rb1", "基极偏置电阻 Rb1", 100, 240, 160,
                "PNP 分压偏置上偏置电阻（B–VCC）：与 Rb2 分压得到基极电位 Vth"),
            Resistor("Rb2", "基极偏置电阻 Rb2", 200, 240, 360,
                "PNP 分压偏置下偏置电阻（B–GND）：取较大值使基极电位靠近 VCC，保证 PNP 工作在放大区"),
            Resistor("Re", "发射极电阻 Re", 1, 340, 140,
                "发射极电阻（E–VCC）：为 PNP 提供直流通路并引入直流负反馈"),
            Resistor("Rc", "集电极电阻 Rc", 2, 340, 400,
                "集电极负载电阻（C–GND）：把 PNP 集电极电流转换为电压 UECQ"),
            Pnp(100, "PNP 硅管，β=100，|UEB|≈0.7 V；符号默认发射极在上、集电极在下"),
            Ground(440)
        },
        Nets = new List<NetSpec>
        {
            TopRail("VCC", "VCC.pos", "Rb1.1", "Re.1"),
            SignalNet("Base", "Rb1.2", "Rb2.1", "Q1.B"),
            SignalNet("Emitter", "Re.2", "Q1.E"),
            SignalNet("Collector", "Rc.1", "Q1.C"),
            BottomRail("GND", "VCC.neg", "Rb2.2", "Rc.2", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.BjtAmplifier, "VoltageDividerSelfBias", SolveSucceeds: true)
    };
}
