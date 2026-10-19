using System.Collections.Generic;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace CircuitModels.Mos;

/// <summary>
/// M01–M06：MOSFET 放大电路的声明式定义（契约 §9）。
/// 坐标均为 10 px 网格；VDD 轨 Y=80、GND 轨 Y=480（与桌面端模板风格一致）。
/// 元件命名沿用 <c>CircuitTemplateFactory</c>：vs/C1/Rg1/Rg2/Rd/Rs/Cs/M1/VDD/GND，
/// 共漏级输出另加 C2/RL，共栅级栅极偏置另加 Cb。
/// 器件模型：增强型 MOSFET，平方律 Level-1，Vth(NMOS)=1.5 V、Vth(PMOS)=-1.5 V，
/// K=2 mA/V²，λ=0（ro=∞）。
/// </summary>
public static class MosDefinitions
{
    /// <summary>6 个电路定义，按 M01–M06 顺序。</summary>
    public static IReadOnlyList<CircuitSpec> All { get; } = new List<CircuitSpec>
    {
        M01(),
        M02(),
        M03(),
        M04(),
        M05(),
        M06()
    };

    // ------------------------------------------------------------------
    // 元件工厂：统一 DefinitionId / Category / 单位 / 尺寸（沿用桌面模板）
    // ------------------------------------------------------------------

    private static ElementSpec Resistor(string id, string name, double kiloOhm, float x, float y,
        string description = "")
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

    private static ElementSpec AcSource(double millivolt, float x, float y, double frequencyHz = 1000)
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

    private static ElementSpec DcSource(string id, string name, double volt, float x, float y)
        => new()
        {
            Id = id,
            Type = ElementType.DcVoltageSource,
            Name = name,
            DefinitionId = "source.v_dc",
            Category = "激励电源",
            Description = "单电源直流偏置",
            Number = volt,
            Unit = "V",
            X = x,
            Y = y,
            Width = 60,
            Height = 60
        };

    private static ElementSpec Nmos(double vth, double k, float x, float y, int rotationDeg = 0, bool mirrorX = false)
        => new()
        {
            Id = "M1",
            Type = ElementType.MosfetNEnh,
            Name = "NMOS 增强型 MOSFET M1",
            DefinitionId = "fet.mosfet_n_enh",
            Category = "半导体管",
            Description = "N 沟道增强型 MOSFET（平方律模型，Vth=1.5 V，K=2 mA/V²）",
            Number = vth,
            Unit = "V",
            MosfetK = k,
            MosfetLambda = 0,
            Material = SemiconductorMaterial.Silicon,
            X = x,
            Y = y,
            RotationDeg = rotationDeg,
            MirrorX = mirrorX,
            Width = 80,
            Height = 80
        };

    private static ElementSpec Pmos(double vth, double k, float x, float y, int rotationDeg = 0, bool mirrorX = false)
        => new()
        {
            Id = "M1",
            Type = ElementType.MosfetPEnh,
            Name = "PMOS 增强型 MOSFET M1",
            DefinitionId = "fet.mosfet_p_enh",
            Category = "半导体管",
            Description = "P 沟道增强型 MOSFET（平方模模型，Vth=-1.5 V，K=2 mA/V²；源极接 VDD）",
            Number = vth,
            Unit = "V",
            MosfetK = k,
            MosfetLambda = 0,
            Material = SemiconductorMaterial.Silicon,
            X = x,
            Y = y,
            RotationDeg = rotationDeg,
            MirrorX = mirrorX,
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
    // M01 NMOS 共源（分压式偏置，源极直接接地）
    // ------------------------------------------------------------------
    // 布局：vs(80,295) → C1(160,260) → M1 栅极；Rg1(240,150) 上偏置、Rg2(240,360) 下偏置；
    //       Rd(340,150) 接 VDD 轨；M1(320,260) 源极直接下接地轨。
    // 手算：VG=15·200/(1000+200)=2.5 V，VGS=2.5 V，Vov=1 V，ID=K·Vov²=2 mA，
    //       VDS=15-2m·3k=9 V > Vov（饱和区），gm=2K·Vov=4 mS，Av=-gm·Rd=-12。

    private static CircuitSpec M01() => new()
    {
        Id = "M01",
        Title = "NMOS 共源放大电路（分压式偏置）",
        FileName = "M01-nmos-common-source.circuit",
        Description = "NMOS 共源放大电路：Rg1/Rg2 分压把栅极固定在 2.5 V，源极直接接地，漏极经 Rd 接 VDD。"
                    + "静态工作点 Vov=VGS-Vth=1 V、ID=2 mA、VDS=9 V（饱和区），"
                    + "小信号 gm=2K·Vov=4 mS，中频电压增益 Av=-gm·Rd=-12（反相放大），"
                    + "输入电阻 Ri=Rg1∥Rg2≈167 kΩ，输出电阻 Ro≈Rd=3 kΩ。"
                    + "教材参考：童诗白《模拟电子技术基础》第 2 章（场效应管放大电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10, 80, 295),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 260, 0,
                "输入耦合电容：隔断直流、把交流小信号耦合到栅极"),
            Resistor("Rg1", "栅极上偏置电阻 Rg1", 1000, 240, 150,
                "分压偏置上偏置电阻：接 VDD，与 Rg2 固定栅极直流电位"),
            Resistor("Rg2", "栅极下偏置电阻 Rg2", 200, 240, 360,
                "分压偏置下偏置电阻：接地，与 Rg1 固定栅极直流电位"),
            Nmos(1.5, 2e-3, 320, 260),
            Resistor("Rd", "漏极负载电阻 Rd", 3, 340, 150,
                "漏极负载电阻：把漏极电流变化转换成电压输出"),
            DcSource("VDD", "直流电源 VDD", 15, 640, 280),
            Ground()
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("VG", "C1.2", "Rg1.2", "Rg2.1", "M1.G"),
            TopRail("VDD", "Rg1.1", "Rd.1", "VDD.pos"),
            SignalNet("VD", "Rd.2", "M1.D"),
            BottomRail("GND", "vs.neg", "Rg2.2", "M1.S", "VDD.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.MosfetAmplifier, "CommonSource", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // M02 NMOS 共源 + 源极电阻 Rs（无旁路）
    // ------------------------------------------------------------------
    // 手算：VG=15·300/(700+300)=4.5 V；解 ID=K(VG-Vth-ID·Rs)² 得 ID=2 mA（另一根对应截止，舍去），
    //       VGS=2.5 V，VS=2 V，VDS=15-2m·(3k+1k)=7 V > Vov=1 V（饱和区），
    //       gm=4 mS，Av=-gm·Rd/(1+gm·Rs)=-12/5=-2.4（Rs 引入交流电流负反馈）。

    private static CircuitSpec M02() => new()
    {
        Id = "M02",
        Title = "NMOS 共源放大电路（源极电阻 Rs 不旁路）",
        FileName = "M02-nmos-common-source-rs.circuit",
        Description = "带源极电阻的 NMOS 共源放大电路：Rg1/Rg2 分压偏置，源极经 Rs=1 kΩ 接地且无旁路电容。"
                    + "直流工作点由 ID=K(VG-Vth-ID·Rs)² 解得 ID=2 mA、VGS=2.5 V、VDS=7 V（饱和区）；"
                    + "交流上 Rs 引入电流负反馈，中频电压增益降为 Av=-gm·Rd/(1+gm·Rs)=-2.4，"
                    + "增益稳定性提高、输入电阻基本不变（Ri≈Rg1∥Rg2=210 kΩ）。"
                    + "教材参考：童诗白《模拟电子技术基础》第 2 章（场效应管放大电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10, 80, 275),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 240, 0,
                "输入耦合电容：隔断直流、把交流小信号耦合到栅极"),
            Resistor("Rg1", "栅极上偏置电阻 Rg1", 700, 240, 140,
                "分压偏置上偏置电阻：接 VDD，与 Rg2 固定栅极直流电位"),
            Resistor("Rg2", "栅极下偏置电阻 Rg2", 300, 240, 340,
                "分压偏置下偏置电阻：接地，与 Rg1 固定栅极直流电位"),
            Nmos(1.5, 2e-3, 320, 240),
            Resistor("Rd", "漏极负载电阻 Rd", 3, 340, 140,
                "漏极负载电阻：把漏极电流变化转换成电压输出"),
            Resistor("Rs", "源极电阻 Rs", 1, 340, 380,
                "源极电阻：提供直流负反馈稳定工作点，交流不旁路时降低增益"),
            DcSource("VDD", "直流电源 VDD", 15, 640, 280),
            Ground()
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("VG", "C1.2", "Rg1.2", "Rg2.1", "M1.G"),
            TopRail("VDD", "Rg1.1", "Rd.1", "VDD.pos"),
            SignalNet("VD", "Rd.2", "M1.D"),
            SignalNet("VS", "M1.S", "Rs.1"),
            BottomRail("GND", "vs.neg", "Rg2.2", "Rs.2", "VDD.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.MosfetAmplifier, "CommonSourceWithSourceResistance", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // M03 NMOS 共源 + Rs + 旁路电容 Cs
    // ------------------------------------------------------------------
    // 直流同 M02（Cs 隔直）：ID=2 mA、VGS=2.5 V、VDS=7 V；
    // 交流 Cs 把 Rs 短路：Av=-gm·(Rd∥RL)=-gm·Rd=-12，Ri=210 kΩ，Ro=3 kΩ。

    private static CircuitSpec M03() => new()
    {
        Id = "M03",
        Title = "NMOS 共源放大电路（源极电阻 Rs 被 Cs 旁路）",
        FileName = "M03-nmos-common-source-bypassed.circuit",
        Description = "源极电阻被旁路电容 Cs 交流短路的 NMOS 共源放大电路：直流偏置与 M02 相同"
                    + "（Cs 隔直，Rs 仍稳定 Q 点：ID=2 mA、VGS=2.5 V、VDS=7 V），"
                    + "中频段 Cs 容抗可忽略、Rs 被交流短路，电压增益恢复为 Av=-gm·Rd=-12。"
                    + "与 M02 对比可理解旁路电容对增益的影响。"
                    + "教材参考：童诗白《模拟电子技术基础》第 2 章（场效应管放大电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10, 80, 275),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 240, 0,
                "输入耦合电容：隔断直流、把交流小信号耦合到栅极"),
            Resistor("Rg1", "栅极上偏置电阻 Rg1", 700, 240, 140,
                "分压偏置上偏置电阻：接 VDD，与 Rg2 固定栅极直流电位"),
            Resistor("Rg2", "栅极下偏置电阻 Rg2", 300, 240, 340,
                "分压偏置下偏置电阻：接地，与 Rg1 固定栅极直流电位"),
            Nmos(1.5, 2e-3, 320, 240),
            Resistor("Rd", "漏极负载电阻 Rd", 3, 340, 140,
                "漏极负载电阻：把漏极电流变化转换成电压输出"),
            Resistor("Rs", "源极电阻 Rs", 1, 340, 380,
                "源极电阻：提供直流负反馈稳定工作点"),
            Capacitor("Cs", "源极旁路电容 Cs", 47, 420, 380, 90,
                "源极旁路电容：中频段把 Rs 交流短路，提高电压增益"),
            DcSource("VDD", "直流电源 VDD", 15, 640, 280),
            Ground()
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("VG", "C1.2", "Rg1.2", "Rg2.1", "M1.G"),
            TopRail("VDD", "Rg1.1", "Rd.1", "VDD.pos"),
            SignalNet("VD", "Rd.2", "M1.D"),
            SignalNet("VS", "M1.S", "Rs.1", "Cs.1"),
            BottomRail("GND", "vs.neg", "Rg2.2", "Rs.2", "Cs.2", "VDD.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.MosfetAmplifier, "CommonSourceBypassed", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // M04 NMOS 共漏（源极跟随器）
    // ------------------------------------------------------------------
    // 布局：M1 漏极直接接 VDD 轨（无 Rd）；源极经 Rs=2.5 kΩ 接地并作为输出端，
    //       C2 耦合到 RL=10 kΩ；Rg1=Rg2=1 MΩ 分压（VG=7.5 V）。
    // 手算：ID=2 mA、Vov=1 V、VS=5 V、VDS=10 V（饱和区），gm=4 mS，
    //       Av=gm(Rs∥RL)/[1+gm(Rs∥RL)]=8/9≈0.889（同相、略小于 1），
    //       Ri=500 kΩ，Ro=Rs∥(1/gm)=227 Ω。

    private static CircuitSpec M04() => new()
    {
        Id = "M04",
        Title = "NMOS 共漏放大电路（源极跟随器）",
        FileName = "M04-nmos-common-drain.circuit",
        Description = "NMOS 共漏（源极跟随器）电路：漏极直接接 VDD（无漏极电阻），输出取自源极电阻 Rs，"
                    + "再经耦合电容 C2 送到负载 RL。分压偏置 VG=7.5 V，静态 Vov=1 V、ID=2 mA、VDS=10 V（饱和区）。"
                    + "源极跟随器的电压增益 Av=gm(Rs∥RL)/[1+gm(Rs∥RL)]≈0.889（同相、略小于 1），"
                    + "输入电阻高（Ri≈500 kΩ）、输出电阻低（Ro=Rs∥(1/gm)≈227 Ω），常用于缓冲与阻抗匹配。"
                    + "教材参考：童诗白《模拟电子技术基础》第 2 章（场效应管放大电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10, 80, 295),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 260, 0,
                "输入耦合电容：把交流小信号耦合到栅极"),
            Resistor("Rg1", "栅极上偏置电阻 Rg1", 1000, 240, 150,
                "分压偏置上偏置电阻：接 VDD，与 Rg2 固定栅极直流电位"),
            Resistor("Rg2", "栅极下偏置电阻 Rg2", 1000, 240, 360,
                "分压偏置下偏置电阻：接地，与 Rg1 固定栅极直流电位"),
            Nmos(1.5, 2e-3, 320, 260),
            Resistor("Rs", "源极电阻 Rs", 2.5, 340, 380,
                "源极电阻：提供直流通路，同时是交流输出端"),
            Capacitor("C2", "输出耦合电容 C2", 10, 450, 340, 0,
                "输出耦合电容：把源极交流输出送给负载"),
            Resistor("RL", "负载电阻 RL", 10, 560, 380,
                "交流负载电阻：与 Rs 并联后决定源极跟随器增益"),
            DcSource("VDD", "直流电源 VDD", 15, 640, 280),
            Ground()
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("VG", "C1.2", "Rg1.2", "Rg2.1", "M1.G"),
            TopRail("VDD", "Rg1.1", "M1.D", "VDD.pos"),
            SignalNet("VS", "M1.S", "Rs.1", "C2.1"),
            SignalNet("VOUT", "C2.2", "RL.1"),
            BottomRail("GND", "vs.neg", "Rg2.2", "Rs.2", "RL.2", "VDD.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.MosfetAmplifier, "CommonDrain", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // M05 PMOS 共源
    // ------------------------------------------------------------------
    // 布局：M1 为 PMOS，RotationDeg=180 + MirrorX 使源极 S 在上（接 VDD）、漏极 D 在下；
    //       漏极经 Rd=3 kΩ 到地，Rg1=200 kΩ 接 VDD、Rg2=800 kΩ 接地分压。
    // 手算：VG=12·800/(200+800)=9.6 V，VSG=2.4 V，Vov=Vth-VGS=-1.5-(-2.4)=0.9 V，
    //       ID=K·Vov²=1.62 mA，VSD=12-1.62m·3k=7.14 V > Vov（饱和区），
    //       gm=2K·Vov=3.6 mS，Av=-gm·Rd=-10.8。

    private static CircuitSpec M05() => new()
    {
        Id = "M05",
        Title = "PMOS 共源放大电路（分压式偏置）",
        FileName = "M05-pmos-common-source.circuit",
        Description = "PMOS 共源放大电路：源极接 VDD，漏极经 Rd 接地，栅极由 Rg1/Rg2 分压固定在 9.6 V。"
                    + "静态 Vov=|Vth|-|VGS|=0.9 V、ID=1.62 mA、VSD=7.14 V（饱和区）；"
                    + "小信号 gm=2K·Vov=3.6 mS，中频电压增益 Av=-gm·Rd=-10.8（反相放大）。"
                    + "用于对比 NMOS 共源电路的偏置极性与电流方向。"
                    + "教材参考：童诗白《模拟电子技术基础》第 2 章（场效应管放大电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10, 80, 295),
            Capacitor("C1", "输入耦合电容 C1", 10, 160, 260, 0,
                "输入耦合电容：隔断直流、把交流小信号耦合到栅极"),
            Resistor("Rg1", "栅极上偏置电阻 Rg1", 200, 240, 150,
                "分压偏置上偏置电阻：接 VDD，与 Rg2 固定栅极直流电位"),
            Resistor("Rg2", "栅极下偏置电阻 Rg2", 800, 240, 360,
                "分压偏置下偏置电阻：接地，与 Rg1 固定栅极直流电位"),
            Pmos(-1.5, 2e-3, 320, 260, rotationDeg: 180, mirrorX: true),
            Resistor("Rd", "漏极负载电阻 Rd", 3, 340, 380,
                "漏极负载电阻：PMOS 漏极经 Rd 接地，把漏极电流变化转换成电压输出"),
            DcSource("VDD", "直流电源 VDD", 12, 640, 280),
            Ground()
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("VG", "C1.2", "Rg1.2", "Rg2.1", "M1.G"),
            TopRail("VDD", "Rg1.1", "M1.S", "VDD.pos"),
            SignalNet("VD", "M1.D", "Rd.1"),
            BottomRail("GND", "vs.neg", "Rg2.2", "Rd.2", "VDD.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.MosfetAmplifier, "CommonSource", SolveSucceeds: true)
    };

    // ------------------------------------------------------------------
    // M06 NMOS 共栅
    // ------------------------------------------------------------------
    // 布局：M1 水平镜像（Gate 在右），输入经 C1 从左侧到源极（Rs=1 kΩ 偏置到地）；
    //       栅极由 Rg1/Rg2 分压（VG=4.5 V）并由旁路电容 Cb 交流接地；
    //       漏极经 Rd=3 kΩ 接 VDD。
    // 手算：ID=2 mA（解二次方程）、VGS=2.5 V、VS=2 V、VDS=7 V（饱和区），
    //       gm=4 mS，Av=+gm·Rd=+12（同相），Ri≈1/gm=250 Ω，Ro≈Rd=3 kΩ。

    private static CircuitSpec M06() => new()
    {
        Id = "M06",
        Title = "NMOS 共栅放大电路",
        FileName = "M06-nmos-common-gate.circuit",
        Description = "NMOS 共栅放大电路：输入信号经 C1 加到源极，栅极由 Rg1/Rg2 分压提供直流偏置"
                    + "（VG=4.5 V）并经旁路电容 Cb 对地交流短路（共栅），输出取自漏极 Rd。"
                    + "静态 ID=2 mA、VGS=2.5 V、VDS=7 V（饱和区）；小信号 Av=+gm·Rd=+12（同相），"
                    + "输入电阻低（Ri≈(1/gm)∥Rs≈200 Ω，工程近似 1/gm=250 Ω）、输出电阻高（Ro≈Rd=3 kΩ），"
                    + "适用于高频/阻抗变换场合。"
                    + "教材参考：童诗白《模拟电子技术基础》第 2 章（场效应管放大电路）。",
        Elements = new List<ElementSpec>
        {
            AcSource(10, 80, 355),
            Capacitor("C1", "输入耦合电容 C1", 10, 200, 320, 0,
                "输入耦合电容：把交流信号耦合到源极"),
            Resistor("Rg1", "栅极上偏置电阻 Rg1", 700, 540, 150,
                "分压偏置上偏置电阻：接 VDD，与 Rg2 固定栅极直流电位"),
            Resistor("Rg2", "栅极下偏置电阻 Rg2", 300, 540, 360,
                "分压偏置下偏置电阻：接地，与 Rg1 固定栅极直流电位"),
            Capacitor("Cb", "栅极旁路电容 Cb", 47, 620, 320, 90,
                "栅极旁路电容：把栅极交流接地，形成共栅组态"),
            Nmos(1.5, 2e-3, 380, 260, mirrorX: true),
            Resistor("Rd", "漏极负载电阻 Rd", 3, 360, 150,
                "漏极负载电阻：把漏极电流变化转换成电压输出"),
            Resistor("Rs", "源极电阻 Rs", 1, 360, 360,
                "源极电阻：提供直流偏置通路，输入端经 C1 接源极"),
            DcSource("VDD", "直流电源 VDD", 15, 720, 280),
            Ground()
        },
        Nets = new List<NetSpec>
        {
            SignalNet("Vin", "vs.pos", "C1.1"),
            SignalNet("VS", "C1.2", "M1.S", "Rs.1"),
            SignalNet("VG", "M1.G", "Rg1.2", "Rg2.1", "Cb.1"),
            TopRail("VDD", "Rg1.1", "Rd.1", "VDD.pos"),
            SignalNet("VD", "Rd.2", "M1.D"),
            BottomRail("GND", "vs.neg", "Rs.2", "Rg2.2", "Cb.2", "VDD.neg", "GND.GND")
        },
        Expected = new ExpectedOutcome(CircuitTopologyType.MosfetAmplifier, "CommonGate", SolveSucceeds: true)
    };
}
