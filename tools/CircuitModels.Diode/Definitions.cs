// ============================================================================
// File: tools/CircuitModels.Diode/Definitions.cs
// Description: 声明式定义 D01–D07 七个二极管教学电路（整流 / 滤波 / 限幅 / 钳位）。
//              使用 tools/CircuitModels.Core 提供的共享 DSL（契约 §8.2），
//              每个 CircuitSpec 只描述“元件 + 逻辑网表 + 期望分类”，布线坐标/
//              导线/WireTap/JSON 序列化/结构自检全部由共享构建器完成。
//
// 参数约定（契约 §9 参数要求）：
//   * 交流源：峰值 5–12 V、频率 50 Hz；
//   * 二极管：硅管 VF = 0.7 V（Material = Silicon）；
//   * 负载：1 kΩ（限幅/钳位为 10 kΩ，保证门限与钳位教学波形清晰）；
//   * 坐标：元件中心 10 px 网格，VCC/GND 轨按需（GND 轨 480/560，V+ 轨 120）。
// ============================================================================

using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace CircuitModels.Diode;

/// <summary>D01–D07 二极管教学电路定义集合。</summary>
internal static class DiodeCircuits
{
    private const double Vm10 = 10.0;   // 交流峰值 10 V
    private const double Vm5 = 5.0;     // 钳位电路交流峰值 5 V
    private const double Frequency = 50.0;
    private const double VfSilicon = 0.7;
    private const float GndRail480 = 480f;
    private const float GndRail560 = 560f;

    /// <summary>返回按 D01–D07 顺序排列的全部电路定义。</summary>
    public static IReadOnlyList<CircuitSpec> All() =>
    [
        D01HalfWaveRectifier(),
        D02HalfWaveRectifierFiltered(),
        D03BridgeRectifier(),
        D04BridgeRectifierFiltered(),
        D05SeriesLimiter(),
        D06DoubleLimiter(),
        D07Clamper(),
    ];

    // ========================================================================
    // D01 半波整流：V1 → D1 → RL → GND
    // ========================================================================
    private static CircuitSpec D01HalfWaveRectifier() => new()
    {
        Id = "D01",
        Title = "半波整流电路",
        FileName = "D01-half-wave-rectifier.circuit",
        Description =
            "知识点：二极管的单向导电性、半波整流。正半周 D1 导通，负载得到正半周电压；" +
            "负半周 D1 截止，负载电压为零。教材公式：输出平均值 Vavg = Vm/π ≈ 0.318Vm，" +
            "有效值 Vrms = Vm/2，二极管承受反向峰值电压 PIV = Vm，输出频率等于输入频率（50 Hz）。" +
            "教材参考：童诗白《模拟电子技术基础》二极管应用电路。",
        Elements =
        [
            AcSource("V1", "交流信号源 V1 (峰值 10 V / 50 Hz)", Vm10, 100, 320, 0,
                "50 Hz 正弦交流电压源，峰值 10 V，作为半波整流的输入激励。"),
            Diode("D1", "整流二极管 D1 (硅管 VF≈0.7 V)", 240, 285, 0,
                "硅整流二极管：正半周正向导通（压降约 0.7 V），负半周反向截止，实现半波整流。"),
            Resistor("RL", "负载电阻 RL (1 kΩ)", 1, 420, 325, 90,
                "整流负载电阻，把整流后的脉动电流转换为脉动电压。"),
            Ground("GND", "参考地 GND", 280, GndRail480),
        ],
        Nets =
        [
            Wire("VIN", "V1.pos", "D1.A"),
            Wire("VOUT", "D1.K", "RL.1"),
            Gnd(GndRail480, "V1.neg", "RL.2", "GND.GND"),
        ],
        Expected = Diode("HalfWaveRectifier"),
    };

    // ========================================================================
    // D02 半波整流 + 电容滤波：在 RL 两端并联滤波电容 C1
    // ========================================================================
    private static CircuitSpec D02HalfWaveRectifierFiltered() => new()
    {
        Id = "D02",
        Title = "半波整流电容滤波电路",
        FileName = "D02-half-wave-rectifier-filtered.circuit",
        Description =
            "知识点：电容滤波原理与纹波估算。D1 导通时电源给 C1 充电并给 RL 供电；D1 截止时 C1 向 RL 放电，" +
            "输出趋于平滑。教材近似：Vdc ≈ Vm − Vr/2，纹波 Vr ≈ Vm/(f·RL·C)（半波），" +
            "纹波系数 γ = Vr/(√2·Vdc)，二极管 PIV ≈ 2Vm。取 C = 100 μF、RL = 1 kΩ，f·RL·C = 5，纹波适中便于观察。" +
            "教材参考：童诗白《模拟电子技术基础》直流电源（电容滤波）。",
        Elements =
        [
            AcSource("V1", "交流信号源 V1 (峰值 10 V / 50 Hz)", Vm10, 100, 320, 0,
                "50 Hz 正弦交流电压源，峰值 10 V。"),
            Diode("D1", "整流二极管 D1 (硅管 VF≈0.7 V)", 240, 285, 0,
                "半波整流二极管，正半周给滤波电容充电。"),
            Capacitor("C1", "滤波电容 C1 (100 μF)", 100, 350, 320, 90,
                "滤波电容器：并联在负载两端，利用充放电平滑整流输出。"),
            Resistor("RL", "负载电阻 RL (1 kΩ)", 1, 470, 325, 90,
                "滤波电路的负载电阻。"),
            Ground("GND", "参考地 GND", 280, GndRail480),
        ],
        Nets =
        [
            Wire("VIN", "V1.pos", "D1.A"),
            // 输出节点：D1.K、C1 上端、RL 上端同在 y = 285 一条水平线上，用一条输出母线连接。
            Rail("VOUT", NetRail.HorizontalTop, 285, 20, "D1.K", "C1.1", "RL.1"),
            Gnd(GndRail480, "V1.neg", "C1.2", "RL.2", "GND.GND"),
        ],
        Expected = Diode("HalfWaveRectifierFiltered"),
    };

    // ========================================================================
    // D03 桥式全波整流：D1–D4 组成桥式整流，RL 跨接在 V+ 与 GND 之间。
    //   正半周：V1.pos → D1 → V+ → RL → GND → D3 → V1.neg，D2/D4 截止；
    //   负半周：V1.neg → D2 → V+ → RL → GND → D4 → V1.pos，D1/D3 截止。
    // ========================================================================
    private static CircuitSpec D03BridgeRectifier() => new()
    {
        Id = "D03",
        Title = "桥式全波整流电路",
        FileName = "D03-bridge-rectifier.circuit",
        Description =
            "知识点：桥式全波整流的工作原理与四个二极管的轮换导通。交流正、负半周分别经 D1/D3 与 D2/D4 " +
            "成对导通，负载 RL 始终流过同一方向的电流，输出为全波脉动电压。教材公式：" +
            "Vavg = 2Vm/π ≈ 0.637Vm、Vrms = Vm/√2、每只二极管 PIV = Vm（桥式）、输出频率 2f = 100 Hz。" +
            "教材参考：童诗白《模拟电子技术基础》直流电源（桥式整流）。",
        Elements =
        [
            AcSource("V1", "交流信号源 V1 (峰值 10 V / 50 Hz)", Vm10, 320, 330, 0,
                "50 Hz 正弦交流电压源，浮地接在桥式整流器的两个交流输入端之间。"),
            Diode("D1", "桥式整流二极管 D1", 220, 240, 270,
                "桥臂 D1：交流输入端 AC1 → 直流正输出端 V+。"),
            Diode("D2", "桥式整流二极管 D2", 420, 240, 270,
                "桥臂 D2：交流输入端 AC2 → 直流正输出端 V+。"),
            Diode("D3", "桥式整流二极管 D3", 220, 420, 270,
                "桥臂 D3：直流负输出端 GND → 交流输入端 AC1。"),
            Diode("D4", "桥式整流二极管 D4", 420, 420, 270,
                "桥臂 D4：直流负输出端 GND → 交流输入端 AC2。"),
            Resistor("RL", "负载电阻 RL (1 kΩ)", 1, 560, 200, 90,
                "整流负载电阻，接在直流输出端 V+ 与 GND 之间。"),
            Ground("GND", "参考地 GND", 340, GndRail560),
        ],
        Nets =
        [
            Rail("AC1", NetRail.VerticalLeft, 200, 20, "V1.pos", "D1.A", "D3.K"),
            Rail("AC2", NetRail.VerticalRight, 440, 20, "V1.neg", "D2.A", "D4.K"),
            Rail("VPLUS", NetRail.HorizontalTop, 120, 20, "D1.K", "D2.K", "RL.1"),
            Gnd(GndRail560, "D3.A", "D4.A", "RL.2", "GND.GND"),
        ],
        Expected = Diode("BridgeRectifier"),
    };

    // ========================================================================
    // D04 桥式全波整流 + 电容滤波：D03 基础上在 V+ 与 GND 之间并联 C1。
    // ========================================================================
    private static CircuitSpec D04BridgeRectifierFiltered() => new()
    {
        Id = "D04",
        Title = "桥式整流电容滤波电路",
        FileName = "D04-bridge-rectifier-filtered.circuit",
        Description =
            "知识点：全波整流 + 电容滤波。每个半周整流电流都对 C1 补充电荷，输出更平滑；" +
            "教材近似：Vdc ≈ Vm − Vr/2，纹波 Vr ≈ Vm/(2f·RL·C)（全波，纹波频率 100 Hz），" +
            "纹波系数 γ = Vr/(√2·Vdc)，每只二极管 PIV ≈ Vm。取 C = 100 μF、RL = 1 kΩ，2f·RL·C = 10。" +
            "教材参考：童诗白《模拟电子技术基础》直流电源（桥式整流电容滤波）。",
        Elements =
        [
            AcSource("V1", "交流信号源 V1 (峰值 10 V / 50 Hz)", Vm10, 320, 330, 0,
                "50 Hz 正弦交流电压源，浮地接在桥式整流器的两个交流输入端之间。"),
            Diode("D1", "桥式整流二极管 D1", 220, 240, 270,
                "桥臂 D1：AC1 → V+。"),
            Diode("D2", "桥式整流二极管 D2", 420, 240, 270,
                "桥臂 D2：AC2 → V+。"),
            Diode("D3", "桥式整流二极管 D3", 220, 420, 270,
                "桥臂 D3：GND → AC1。"),
            Diode("D4", "桥式整流二极管 D4", 420, 420, 270,
                "桥臂 D4：GND → AC2。"),
            Capacitor("C1", "滤波电容 C1 (100 μF)", 100, 480, 200, 90,
                "滤波电容器：并联在直流输出端 V+ 与 GND 之间，减小纹波。"),
            Resistor("RL", "负载电阻 RL (1 kΩ)", 1, 560, 200, 90,
                "整流负载电阻。"),
            Ground("GND", "参考地 GND", 340, GndRail560),
        ],
        Nets =
        [
            Rail("AC1", NetRail.VerticalLeft, 200, 20, "V1.pos", "D1.A", "D3.K"),
            Rail("AC2", NetRail.VerticalRight, 440, 20, "V1.neg", "D2.A", "D4.K"),
            Rail("VPLUS", NetRail.HorizontalTop, 120, 20, "D1.K", "D2.K", "C1.1", "RL.1"),
            Gnd(GndRail560, "D3.A", "D4.A", "C1.2", "RL.2", "GND.GND"),
        ],
        Expected = Diode("BridgeRectifierFiltered"),
    };

    // ========================================================================
    // D05 串联限幅（上限幅）：V1 → R1 → Vout，D1（阳极接 Vout、阴极接地）在
    //   Vout > VF 时导通，把输出正半周限制在 ≈ +0.7 V；负半周 D1 截止，输出
    //   跟随输入。教学限幅模型将输出定义在二极管两端（串联限流电阻 R1 为唯一电阻）。
    // ========================================================================
    private static CircuitSpec D05SeriesLimiter() => new()
    {
        Id = "D05",
        Title = "串联限幅电路（上限幅）",
        FileName = "D05-series-limiter.circuit",
        Description =
            "知识点：二极管限幅电路。R1 为串联限流电阻，D1 反向并联在输出端与地之间（阳极接输出、" +
            "阴极接地）。当输入正半周使 Vout 超过二极管导通压降（约 0.7 V）时 D1 导通，输出被钳位在 " +
            "≈ +0.7 V；负半周 D1 截止，输出跟随输入，因此传输特性为“上限幅”。" +
            "教材参考：童诗白《模拟电子技术基础》二极管限幅电路。",
        Elements =
        [
            AcSource("V1", "交流信号源 V1 (峰值 10 V / 50 Hz)", Vm10, 100, 320, 0,
                "50 Hz 正弦交流电压源，峰值 10 V。"),
            Resistor("R1", "限幅串联电阻 R1 (1 kΩ)", 1, 240, 285, 0,
                "串联限流电阻，限制二极管导通后的电流；与二极管串联构成分压式限幅支路。"),
            Diode("D1", "限幅二极管 D1 (硅管 VF≈0.7 V)", 380, 320, 90,
                "上限幅二极管：阳极接输出节点、阴极接地，输出正半周被限制在约 +0.7 V。"),
            Ground("GND", "参考地 GND", 240, GndRail480),
        ],
        Nets =
        [
            Wire("VIN", "V1.pos", "R1.1"),
            Wire("VOUT", "R1.2", "D1.A"),
            Gnd(GndRail480, "V1.neg", "D1.K", "GND.GND"),
        ],
        Expected = Diode("SeriesLimiter"),
    };

    // ========================================================================
    // D06 双向限幅：D1 上限幅（阴极接 +2 V 偏置）、D2 下限幅（阳极接 −2 V 偏置）。
    //   输出在 [−2.7 V, +2.7 V] 之间被双向钳位。
    // ========================================================================
    private static CircuitSpec D06DoubleLimiter() => new()
    {
        Id = "D06",
        Title = "双向限幅电路（双管双偏置）",
        FileName = "D06-double-limiter.circuit",
        Description =
            "知识点：双向限幅电路。D1 阳极接输出、阴极接 +2 V 正偏置，当 Vout 超过 2 V + VF = 2.7 V 时" +
            "导通，形成上限幅；D2 阴极接输出、阳极接 −2 V 负偏置，当 Vout 低于 −2 V − VF = −2.7 V 时导通，" +
            "形成下限幅。输出被限制在约 ±2.7 V，传输特性为双向折线。" +
            "教材参考：童诗白《模拟电子技术基础》二极管双向限幅电路。",
        Elements =
        [
            AcSource("V1", "交流信号源 V1 (峰值 10 V / 50 Hz)", Vm10, 100, 320, 0,
                "50 Hz 正弦交流电压源，峰值 10 V。"),
            Resistor("R1", "限幅串联电阻 R1 (1 kΩ)", 1, 240, 285, 0,
                "串联限流电阻，两只二极管导通时限制电流；与二极管节点串联构成双向限幅支路。"),
            Diode("D1", "上限幅二极管 D1 (硅管 VF≈0.7 V)", 500, 250, 270,
                "上限幅二极管：阳极接输出、阴极经 V2 接 +2 V 偏置，门限 +2.7 V。"),
            Diode("D2", "下限幅二极管 D2 (硅管 VF≈0.7 V)", 600, 320, 270,
                "下限幅二极管：阴极接输出、阳极经 V3 接 −2 V 偏置，门限 −2.7 V。"),
            DcSource("V2", "正偏置电源 V2 (+2 V)", 2, 800, 240, 0,
                "直流偏置电源：给 D1 阴极提供 +2 V 上限幅偏置。"),
            DcSource("V3", "负偏置电源 V3 (−2 V)", -2, 660, 500, 0,
                "直流偏置电源：给 D2 阳极提供 −2 V 下限幅偏置。"),
            Ground("GND", "参考地 GND", 300, GndRail560),
        ],
        Nets =
        [
            Wire("VIN", "V1.pos", "R1.1"),
            // 输出节点：R1.2、D1.A、D2.K 同在 y = 285 一条水平线上，用一条输出母线连接。
            Rail("VOUT", NetRail.HorizontalTop, 285, 20, "R1.2", "D1.A", "D2.K"),
            Rail("VB1", NetRail.HorizontalTop, 140, 20, "D1.K", "V2.pos"),
            Rail("VB2", NetRail.HorizontalBottom, 440, 20, "D2.A", "V3.pos"),
            Gnd(GndRail560, "V1.neg", "V2.neg", "V3.neg", "GND.GND"),
        ],
        Expected = Diode("DoubleLimiter"),
    };

    // ========================================================================
    // D07 二极管钳位：C1 与 D1 构成正向钳位电路，把输入波形整体上移约
    //   Vm − VF（输出下限被钳位在约 −0.7 V）。
    // ========================================================================
    private static CircuitSpec D07Clamper() => new()
    {
        Id = "D07",
        Title = "二极管钳位电路",
        FileName = "D07-clamper.circuit",
        Description =
            "知识点：二极管钳位（直流恢复）电路。电容 C1 与二极管 D1 串联/并联构成钳位电路：输入负半周" +
            "使 D1 导通，C1 被充电到约 Vm − VF；此后输入正半周与电容电压叠加，输出波形整体上移，" +
            "输出最小值被钳位在约 −0.7 V、最大值约为 2Vm − VF，峰峰值基本保持不变。" +
            "教材参考：童诗白《模拟电子技术基础》二极管钳位电路。",
        Elements =
        [
            AcSource("V1", "交流信号源 V1 (峰值 5 V / 50 Hz)", Vm5, 100, 320, 0,
                "50 Hz 正弦交流电压源，峰值 5 V。"),
            Capacitor("C1", "钳位电容 C1 (10 μF)", 10, 240, 285, 0,
                "钳位电容器：与输入串联，在二极管导通半周被充电，形成直流偏移电压。"),
            Diode("D1", "钳位二极管 D1 (硅管 VF≈0.7 V)", 400, 320, 270,
                "钳位二极管：阴极接输出节点、阳极接地，输出下限被钳位在约 −0.7 V。"),
            Resistor("RL", "输出负载电阻 RL (10 kΩ)", 10, 500, 325, 90,
                "输出负载电阻，时间常数 RL·C1 远大于输入周期，保持钳位电平。"),
            Ground("GND", "参考地 GND", 260, GndRail480),
        ],
        Nets =
        [
            Wire("VIN", "V1.pos", "C1.1"),
            // 输出节点：C1.2、D1.K、RL.1 同在 y = 285 一条水平线上，用一条输出母线连接。
            Rail("VOUT", NetRail.HorizontalTop, 285, 20, "C1.2", "D1.K", "RL.1"),
            Gnd(GndRail480, "V1.neg", "D1.A", "RL.2", "GND.GND"),
        ],
        Expected = Diode("Clamper"),
    };

    // ========================================================================
    // 元件工厂（统一 DefinitionId / Category / 显示尺寸，与模板命名一致）
    // ========================================================================
    private static ExpectedOutcome Diode(string subtype) =>
        new(CircuitTopologyType.DiodeCircuit, subtype, SolveSucceeds: true);

    private static ElementSpec AcSource(string id, string name, double peakVolts, float x, float y,
        int rotationDeg, string description) => new()
    {
        Id = id,
        Type = ElementType.AcVoltageSource,
        Name = name,
        DefinitionId = "source.v_ac_small_signal",
        Category = "激励电源",
        Description = description,
        Number = peakVolts,
        Unit = "V",
        X = x,
        Y = y,
        RotationDeg = rotationDeg,
        Width = 60,
        Height = 60,
        FrequencyHz = Frequency,
    };

    private static ElementSpec DcSource(string id, string name, double volts, float x, float y,
        int rotationDeg, string description) => new()
    {
        Id = id,
        Type = ElementType.DcVoltageSource,
        Name = name,
        DefinitionId = "source.v_dc",
        Category = "激励电源",
        Description = description,
        Number = volts,
        Unit = "V",
        X = x,
        Y = y,
        RotationDeg = rotationDeg,
        Width = 60,
        Height = 60,
    };

    private static ElementSpec Diode(string id, string name, float x, float y, int rotationDeg,
        string description) => new()
    {
        Id = id,
        Type = ElementType.Diode,
        Name = name,
        DefinitionId = "diode.standard",
        Category = "半导体管",
        Description = description,
        Number = VfSilicon,
        Unit = "V",
        X = x,
        Y = y,
        RotationDeg = rotationDeg,
        Width = 80,
        Height = 50,
        Material = SemiconductorMaterial.Silicon,
    };

    private static ElementSpec Resistor(string id, string name, double kiloOhm, float x, float y,
        int rotationDeg, string description) => new()
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
        Height = 50,
    };

    private static ElementSpec Capacitor(string id, string name, double microFarad, float x, float y,
        int rotationDeg, string description) => new()
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
        Height = 50,
    };

    private static ElementSpec Ground(string id, string name, float x, float railY) => new()
    {
        Id = id,
        Type = ElementType.Ground,
        Name = name,
        DefinitionId = "reference.ground",
        Category = "参考地",
        Description = "基准零电位参考节点，统一电路地电位基准。",
        Number = 0,
        Unit = "V",
        X = x,
        Y = railY + 22,
        Width = 40,
        Height = 35,
    };

    // ========================================================================
    // 网表工厂
    // ========================================================================

    /// <summary>两端同网（无轨）连接：构建器用一条直导线或一条带 L 形 Waypoint 的导线连接。</summary>
    private static NetSpec Wire(string name, params string[] pins) => new()
    {
        Name = name,
        Pins = pins,
    };

    /// <summary>带水平/垂直母线（Rail）的多端网络：构建器先建母线，再为每个引脚生成 WireTap 支线。</summary>
    private static NetSpec Rail(string name, NetRail rail, float coordinate, float margin,
        params string[] pins) => new()
    {
        Name = name,
        Pins = pins,
        Rail = rail,
        RailCoordinate = coordinate,
        Margin = margin,
    };

    /// <summary>参考地网络：GND 轨 + 所有接地引脚由构建器生成垂直支线并 WireTap 到轨上。</summary>
    private static NetSpec Gnd(float railY, params string[] pins) => new()
    {
        Name = "GND",
        Pins = pins,
        Rail = NetRail.HorizontalBottom,
        RailCoordinate = railY,
        Margin = 20,
    };
}
