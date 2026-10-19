// ============================================================================
// File: apps/desktop-winui/Models/SymbolGeometry.cs
// Description: 国际标准元件符号矢量几何库（纯 C#，仅依赖 System.Numerics）。
//
// 设计目标
//   * 符号库卡片（Controls/CircuitSymbolPreview.xaml.cs）与画布
//     （Controls/CircuitCanvas.xaml.cs）从同一份几何数据渲染，避免两处手绘
//     符号不一致（此前 NPN/PNP 无法区分、两个 JFET 不正确即源于此）。
//   * 坐标系为“元件局部坐标”：原点 = 元件中心，+X 向右，+Y 向下（与
//     WinUI / Win2D 画布一致）；图元坐标端点与
//     Models/CircuitElement.InitializeDefaultPins() 的引脚 RelativeX/Y 精确重合，
//     误差 <= 0.01。不得修改 CircuitElement.cs 的引脚定义。
//
// 依据标准
//   * IEC 60617（GB/T 4728）《电气简图用图形符号》：R 矩形、C 平行板、
//     极性电容（正极直板 + 负极曲板，曲板凸向正极板）、L 半圆链、
//     二极管三角形+阴极竖线、GND 三横线、电源圆+极性标记、
//     受控源菱形、JFET 竖直沟道 + 栅极箭头（N 指向沟道 / P 背离沟道）、
//     MOSFET 分离栅极板 + 增强型三段断续沟道 / 耗尽型整段实线沟道 +
//     衬底箭头与源极短接（连接点实心圆点）。
//   * IEEE 315 / ANSI Y32.2：BJT 外圆+基极竖条+集电极/发射极斜引线 +
//     实心发射极箭头（NPN 向外、PNP 向内）。
//   * Multisim 约定（用户参考图）：VCC / 电源端口使用电源横杠（bar）+ 引线，
//     不使用向上空心三角箭头；VCC 文本由卡片/画布文本层另行绘制。
//   * GB/T 4728 与 IEC 60617 等同，故不再单独区分。
//
// 图元约定
//   * Polyline            —— 折线（Points 依次连接，不自动闭合）
//   * FilledPolygon       —— 实心多边形（箭头、极性点等；Thickness 仅作占位）
//   * Circle              —— 圆：Points[0]=圆心，Points[1]=圆上一点（半径点）
// ============================================================================

using System;
using System.Collections.Generic;
using System.Numerics;

namespace CircuitAssistant.Models;

/// <summary>符号图元类型。</summary>
public enum SymbolPrimitiveKind
{
    /// <summary>折线（线段链，不闭合）。</summary>
    Polyline,

    /// <summary>实心多边形（实心箭头、极性点等）。</summary>
    FilledPolygon,

    /// <summary>圆（Points[0]=圆心，Points[1]=半径点）。</summary>
    Circle
}

/// <summary>
/// 元件局部坐标系下的一个符号图元。
/// </summary>
public sealed class SymbolPrimitive
{
    /// <summary>图元类型。</summary>
    public SymbolPrimitiveKind Kind { get; init; }

    /// <summary>
    /// 图元控制点：折线/多边形为顶点序列，圆为 [圆心, 半径点]。
    /// </summary>
    public IReadOnlyList<Vector2> Points { get; init; } = Array.Empty<Vector2>();

    /// <summary>线宽（元件局部坐标单位；FilledPolygon 使用 1.0 占位）。</summary>
    public float Thickness { get; init; }

    /// <summary>是否填充（仅 Circle 有意义；FilledPolygon 恒为 true）。</summary>
    public bool Filled { get; init; }
}

/// <summary>
/// 国际标准元件符号几何库。每个 <see cref="ElementType"/> 对应一组图元，
/// 由符号库卡片和画布分别渲染成 XAML Shape / Win2D 几何。
/// </summary>
public static class SymbolGeometryLibrary
{
    // ---- 统一线宽（同一套笔画层级，避免每个符号各自为政）------------------
    /// <summary>引线线宽。</summary>
    public const float LeadThickness = 2.0f;

    /// <summary>器件轮廓线宽（外圆、极板、菱形、芯柱等）。</summary>
    public const float OutlineThickness = 2.2f;

    /// <summary>强调笔画线宽（基极竖条、沟道、极板、电源横杠）。</summary>
    public const float PlateThickness = 3.2f;

    /// <summary>极性/控制标记线宽（+、- 等）。</summary>
    public const float MarkerThickness = 1.8f;

    /// <summary>电源横杠线宽（Multisim 风格电源 bar）。</summary>
    public const float RailThickness = 2.8f;

    /// <summary>BJT 外圆半径（IEEE 315 外圆包络）。</summary>
    public const float BjtCircleRadius = 26f;

    /// <summary>JFET 竖直沟道 X 坐标（D/S 引线经水平短接横线接至 x=20 引脚）。</summary>
    public const float JfetChannelX = 8f;

    /// <summary>JFET 沟道半高（沟道上下端略伸出 D/S 横线之外，符合 GB/T 4728 画法）。</summary>
    public const float JfetChannelHalfHeight = 24f;

    /// <summary>JFET / MOSFET 漏源横线（栅极板端点）Y 坐标。</summary>
    public const float FetStubY = 18f;

    /// <summary>MOSFET 栅极板 X 坐标（与沟道分离）。</summary>
    public const float MosGatePlateX = 1.5f;

    /// <summary>MOSFET 沟道 X 坐标。</summary>
    public const float MosChannelX = 8f;

    /// <summary>MOSFET 沟道半高（沟道（最外段）上下端点）。</summary>
    public const float MosChannelHalfHeight = 25.5f;

    private static readonly Dictionary<ElementType, IReadOnlyList<SymbolPrimitive>> Cache = new();
    private static readonly object CacheLock = new();
    private static IReadOnlyList<SymbolPrimitive>? _ansiResistor;

    /// <summary>
    /// 取得指定元件类型在元件局部坐标系下的符号图元。
    /// 返回的集合为只读缓存，调用方不得修改。
    /// </summary>
    public static IReadOnlyList<SymbolPrimitive> Build(ElementType type)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(type, out var cached)) return cached;
            var built = BuildCore(type);
            Cache[type] = built;
            return built;
        }
    }

    /// <summary>
    /// ANSI / IEEE 315 锯齿形电阻（符号库卡片中 passive.resistor_ansi 使用；
    /// 画布放置的 ElementType.Resistor 按 IEC 60617 使用矩形电阻）。
    /// </summary>
    public static IReadOnlyList<SymbolPrimitive> BuildAnsiResistor()
    {
        lock (CacheLock)
        {
            return _ansiResistor ??= new SymbolPrimitive[]
            {
                Polyline(OutlineThickness,
                    V(-40, 0), V(-24, 0), V(-20, -9), V(-12, 9), V(-4, -9),
                    V(4, 9), V(12, -9), V(20, 0), V(40, 0))
            };
        }
    }

    /// <summary>
    /// 计算图元集合的局部坐标包围盒（含圆的半径扩展）。
    /// 符号库卡片用它自动缩放、居中到 100x100 画布。
    /// </summary>
    public static (Vector2 Min, Vector2 Max) GetBounds(IReadOnlyList<SymbolPrimitive>? primitives)
    {
        if (primitives == null || primitives.Count == 0)
        {
            return (Vector2.Zero, Vector2.Zero);
        }

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var primitive in primitives)
        {
            var points = primitive.Points;
            if (points == null || points.Count == 0) continue;

            if (primitive.Kind == SymbolPrimitiveKind.Circle && points.Count >= 2)
            {
                var center = points[0];
                var radius = Vector2.Distance(points[0], points[1]);
                min = Vector2.Min(min, center - new Vector2(radius));
                max = Vector2.Max(max, center + new Vector2(radius));
                continue;
            }

            foreach (var point in points)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }

        return (min, max);
    }

    // =====================================================================
    // 类型分派
    // =====================================================================
    private static IReadOnlyList<SymbolPrimitive> BuildCore(ElementType type) => type switch
    {
        ElementType.Resistor => BuildResistor(),
        ElementType.Capacitor => BuildCapacitor(),
        ElementType.CapacitorPolarized => BuildCapacitorPolarized(),
        ElementType.Inductor => BuildInductor(),
        ElementType.Transformer => BuildTransformer(),

        ElementType.BjtNpn => BuildBjt(npn: true),
        ElementType.BjtPnp => BuildBjt(npn: false),

        ElementType.Mosfet => BuildMosfet(nChannel: true, enhancement: true),
        ElementType.MosfetNEnh => BuildMosfet(nChannel: true, enhancement: true),
        ElementType.MosfetPEnh => BuildMosfet(nChannel: false, enhancement: true),
        ElementType.MosfetNDep => BuildMosfet(nChannel: true, enhancement: false),
        ElementType.MosfetPDep => BuildMosfet(nChannel: false, enhancement: false),

        ElementType.Jfet => BuildJfet(nChannel: true),
        ElementType.JfetN => BuildJfet(nChannel: true),
        ElementType.JfetP => BuildJfet(nChannel: false),

        ElementType.Diode => BuildDiode(),

        ElementType.DcVoltageSource => BuildVoltageSource(alternating: false),
        ElementType.AcVoltageSource => BuildVoltageSource(alternating: true),
        ElementType.DcCurrentSource => BuildCurrentSource(alternating: false),
        ElementType.AcCurrentSource => BuildCurrentSource(alternating: true),

        ElementType.Vcvs => BuildControlledSource(voltageOutput: true, voltageControl: true),
        ElementType.Vccs => BuildControlledSource(voltageOutput: false, voltageControl: true),
        ElementType.Ccvs => BuildControlledSource(voltageOutput: true, voltageControl: false),
        ElementType.Cccs => BuildControlledSource(voltageOutput: false, voltageControl: false),

        ElementType.Ground => BuildGround(),
        ElementType.VccPort => BuildVccPort(),

        _ => BuildResistor()
    };

    // =====================================================================
    // 无源器件（IEC 60617 / GB/T 4728）
    // =====================================================================

    // 电阻：IEC 矩形符号；引脚 (��40, 0)。
    private static IReadOnlyList<SymbolPrimitive> BuildResistor() => new SymbolPrimitive[]
    {
        Line(-40, 0, -20, 0, LeadThickness),
        ClosedPolyline(OutlineThickness, V(-20, -10), V(20, -10), V(20, 10), V(-20, 10)),
        Line(20, 0, 40, 0, LeadThickness)
    };

    // 电容：两条平行极板；引脚 (��35, 0)。
    private static IReadOnlyList<SymbolPrimitive> BuildCapacitor() => new SymbolPrimitive[]
    {
        Line(-35, 0, -6, 0, LeadThickness),
        Line(-6, -18, -6, 18, PlateThickness),
        Line(6, -18, 6, 18, PlateThickness),
        Line(6, 0, 35, 0, LeadThickness)
    };

    // 极性电容（GB/T 4728 / IEC 60617）：正极直板（左）+ 负极曲板（右）。
    //   曲板凸向正极直板（弧心在中垂线右侧、弧顶靠近正极板），
    //   即参考图中负极曲板"包向"正极板的方向；若画成相反方向即为错误。
    //   引脚 "-"(+35, 0) 接在负极曲板弧顶（x=8, y=0）；"+" 标记绘制在正极直板旁。
    private static IReadOnlyList<SymbolPrimitive> BuildCapacitorPolarized() => new SymbolPrimitive[]
    {
        Line(-35, 0, -6, 0, LeadThickness),
        Line(-6, -18, -6, 18, PlateThickness),
        // 负极曲板：圆心 (38,0)、半径 30 的圆弧，从 (14,-18) 经弧顶 (8,0) 到 (14,18)，
        // 弧顶（最靠近正极直板 x=-6 的点）朝向正极板。
        Polyline(OutlineThickness,
            CircleArcPoints(38f, 0f, 30f, MathF.PI + 0.6435011f, MathF.PI - 0.6435011f, segments: 12)),
        // 正极性标记 "+"（IEC 60617 极性电容的极性标识）
        Line(-16, -12, -10, -12, MarkerThickness),
        Line(-13, -15, -13, -9, MarkerThickness),
        Line(8, 0, 35, 0, LeadThickness)
    };

    // 电感：4 个上半圆线圈（IEC 60617 半圆链）；引脚 (��35, 0)。
    private static IReadOnlyList<SymbolPrimitive> BuildInductor()
    {
        var result = new List<SymbolPrimitive>
        {
            Line(-35, 0, -24, 0, LeadThickness)
        };
        for (int i = 0; i < 4; i++)
        {
            result.Add(Polyline(OutlineThickness, HalfCircleUp(-18f + 12f * i, 6f, segments: 6)));
        }
        result.Add(Line(24, 0, 35, 0, LeadThickness));
        return result;
    }

    // 变压器：两个绕组（各 3 个半圆）+ 双线芯柱 + 同名端极性点；
    // 引脚 P1(-35,-15) P2(-35,15) S1(35,-15) S2(35,15)。
    private static IReadOnlyList<SymbolPrimitive> BuildTransformer()
    {
        var result = new List<SymbolPrimitive>
        {
            // 一次侧 / 二次侧引线（进入绕组端点）
            Line(-35, -15, -10, -15, LeadThickness),
            Line(-35, 15, -10, 15, LeadThickness),
            Line(10, -15, 35, -15, LeadThickness),
            Line(10, 15, 35, 15, LeadThickness),
            // 双线芯柱（IEC 60617 变压器磁芯）
            Line(-2, -20, -2, 20, OutlineThickness),
            Line(2, -20, 2, 20, OutlineThickness),
            // 同名端极性点（实心圆点）
            Dot(-6, -18, 1.7f),
            Dot(6, -18, 1.7f)
        };

        for (int i = 0; i < 3; i++)
        {
            float centerY = -10f + 10f * i;
            result.Add(Polyline(OutlineThickness, VerticalHalfCircle(-10, centerY, 5f, bulgeLeft: true, segments: 6)));
            result.Add(Polyline(OutlineThickness, VerticalHalfCircle(10, centerY, 5f, bulgeLeft: false, segments: 6)));
        }

        return result;
    }

    // =====================================================================
    // 双极型三极管（IEEE 315 / ANSI Y32.2，Multisim 2N3904 参考一致）
    //   外圆 + 基极引线 + 基极竖条 + 集电极/发射极斜引线；
    //   发射极箭头为实心三角形：NPN 指向发射极引脚（向外），PNP 指向基极（向内）。
    //   默认朝向（2026-10-18 用户要求）：
    //     * NPN：集电极在上（C=20,-35），发射极在下（E=20,35）；
    //     * PNP：发射极在上（E=20,-35），集电极在下（C=20,35）。
    //   因此 NPN 的发射极箭头位于下方斜引线，PNP 的发射极箭头位于上方斜引线。
    //   引脚：NPN B(-35,0) C(20,-35) E(20,35)；PNP B(-35,0) E(20,-35) C(20,35)。
    // =====================================================================
    private static IReadOnlyList<SymbolPrimitive> BuildBjt(bool npn)
    {
        return new SymbolPrimitive[]
        {
            Circle(0, 0, BjtCircleRadius, OutlineThickness), // IEEE 315 外圆
            Line(-35, 0, -8, 0, LeadThickness),              // 基极引线
            Line(-8, -12, -8, 12, PlateThickness),           // 基极竖条
            Line(-8, -6, 20, -35, OutlineThickness),         // 上方斜引线（NPN 集电极 / PNP 发射极）
            Line(-8, 6, 20, 35, OutlineThickness),           // 下方斜引线（NPN 发射极 / PNP 集电极）
            BjtEmitterArrow(npn)                             // 发射极实心箭头
        };
    }

    private static SymbolPrimitive BjtEmitterArrow(bool npn)
    {
        // NPN：发射极斜线从 (-8, 6) 指向下方引脚 (20, 35)；
        // PNP：发射极斜线从 (-8, -6) 指向上方引脚 (20, -35)。
        var origin = npn ? V(-8, 6) : V(-8, -6);
        var pin = npn ? V(20, 35) : V(20, -35);
        var direction = pin - origin;
        var unit = Vector2.Normalize(direction);
        var normal = new Vector2(-unit.Y, unit.X);

        // NPN：箭头尖端靠近发射极引脚（离基极更远）；PNP：尖端靠近基极（离基极更近）。
        // 箭头整体位于 IEEE 315 外圆内部（与 Multisim 2N3904 参考图一致）。
        float tipT = npn ? 0.60f : 0.30f;
        float baseT = npn ? 0.38f : 0.52f;
        float halfWidth = 4.5f;

        var tip = origin + direction * tipT;
        var baseCenter = origin + direction * baseT;
        return FilledPolygon(tip, baseCenter + normal * halfWidth, baseCenter - normal * halfWidth);
    }

    // =====================================================================
    // 结型场效应管（GB/T 4728 / IEC 60617）
    //   * 竖直沟道，沟道线略长于 D/S 横线间距；
    //   * 栅极引线以实心箭头直接指向沟道中点，不画 MOSFET 式独立栅极条
    //     （独立栅极条属于绝缘栅器件，JFET 无此结构，旧实现画法错误）；
    //   * 箭头方向：N 沟道箭头指向沟道，P 沟道箭头背离沟道；
    //   * 漏/源引线先竖直引出，再经水平短接横线接至沟道上下端（标准画法；
    //     旧实现把 D/S 引线直接叠在沟道延长线上，不符合标准）；
    //   * 引脚 G(-35,0) D(20,-35) S(20,35)。
    // =====================================================================
    private static IReadOnlyList<SymbolPrimitive> BuildJfet(bool nChannel)
    {
        // 栅极箭头位于栅极引线上、紧贴沟道：N 沟道尖端在沟道侧 (x=8)，
        // P 沟道尖端背离沟道 (x=-1)，另一端为箭头底边。
        var tipX = nChannel ? JfetChannelX : JfetChannelX - 9f;
        var baseX = nChannel ? JfetChannelX - 9f : JfetChannelX;

        return new SymbolPrimitive[]
        {
            Line(-35, 0, JfetChannelX, 0, LeadThickness),                        // 栅极引线（直达沟道）
            Line(JfetChannelX, -JfetChannelHalfHeight,
                 JfetChannelX, JfetChannelHalfHeight, PlateThickness),            // 竖直沟道
            // 漏极：水平短接横线 + 竖直引线
            Line(JfetChannelX, -FetStubY, 20, -FetStubY, LeadThickness),
            Line(20, -FetStubY, 20, -35, LeadThickness),
            // 源极：水平短接横线 + 竖直引线
            Line(JfetChannelX, FetStubY, 20, FetStubY, LeadThickness),
            Line(20, FetStubY, 20, 35, LeadThickness),
            // 栅极 PN 结实心箭头（贴住沟道中点）
            FilledPolygon(V(tipX, 0), V(baseX, -4.2f), V(baseX, 4.2f))
        };
    }

    // =====================================================================
    // 绝缘栅场效应管（GB/T 4728 / IEC 60617，3 端、衬底与源极短接）
    //   * 栅极板与沟道分离，栅极板长度 = D/S 横线间距（不再与沟道等高）；
    //   * 增强型沟道为 3 段断续竖线，耗尽型为 1 条整段实线；
    //   * D/S 横线接在沟道顶部/底部线段的中点，而不是线段端点；
    //   * 衬底箭头位于衬底横线上（N 指向沟道、P 背离沟道），衬底经竖线
    //     与源极横线/源极引线短接，连接点画实心圆点；
    //   * 引脚 G(-35,0) D(20,-35) S(20,35)。
    // =====================================================================
    private static IReadOnlyList<SymbolPrimitive> BuildMosfet(bool nChannel, bool enhancement)
    {
        var result = new List<SymbolPrimitive>
        {
            Line(-35, 0, MosGatePlateX, 0, LeadThickness),                          // 栅极引线
            Line(MosGatePlateX, -FetStubY, MosGatePlateX, FetStubY, PlateThickness) // 栅极板（与沟道分离）
        };

        if (enhancement)
        {
            // 增强型：3 段断续沟道；中间段中点与衬底箭头对齐，
            // 上/下段中点与 D/S 横线对齐（-18 / 0 / +18）。
            result.Add(Line(MosChannelX, -MosChannelHalfHeight, MosChannelX, -(FetStubY - 7.5f), PlateThickness));
            result.Add(Line(MosChannelX, -7.5f, MosChannelX, 7.5f, PlateThickness));
            result.Add(Line(MosChannelX, FetStubY - 7.5f, MosChannelX, MosChannelHalfHeight, PlateThickness));
        }
        else
        {
            // 耗尽型：整段实线沟道
            result.Add(Line(MosChannelX, -MosChannelHalfHeight, MosChannelX, MosChannelHalfHeight, PlateThickness));
        }

        // 漏极：沟道段中点 -> 水平横线 -> 竖直引线至 D 脚
        result.Add(Line(MosChannelX, -FetStubY, 20, -FetStubY, LeadThickness));
        result.Add(Line(20, -FetStubY, 20, -35, LeadThickness));
        // 源极：沟道段中点 -> 水平横线 -> 竖直引线至 S 脚
        result.Add(Line(MosChannelX, FetStubY, 20, FetStubY, LeadThickness));
        result.Add(Line(20, FetStubY, 20, 35, LeadThickness));
        // 衬底-源短接：衬底横线 -> 竖直短线，在源极横线处与源极引线相交
        result.Add(Polyline(LeadThickness, V(MosChannelX, 0), V(20, 0), V(20, FetStubY)));
        result.Add(MosfetBulkArrow(nChannel));
        // 衬底与源极短接的电气连接点（标准画法中的实心圆点）
        result.Add(Dot(20, FetStubY, 1.7f));
        return result;
    }

    private static SymbolPrimitive MosfetBulkArrow(bool nChannel)
    {
        // 箭头位于衬底横线 (channelX, 0) - (20, 0) 上，占横线靠沟道一侧。
        var tipX = nChannel ? MosChannelX : MosChannelX + 8f;
        var baseX = nChannel ? MosChannelX + 8f : MosChannelX;
        return FilledPolygon(V(tipX, 0), V(baseX, -2.6f), V(baseX, 2.6f));
    }

    // =====================================================================
    // 二极管（IEC 60617）：三角形 + 阴极竖线；三角形尖端指向阴极。
    //   引脚 A(-35,0) K(35,0)。
    // =====================================================================
    private static IReadOnlyList<SymbolPrimitive> BuildDiode() => new SymbolPrimitive[]
    {
        Line(-35, 0, -10, 0, LeadThickness),                                   // 阳极引线
        ClosedPolyline(OutlineThickness, V(-10, -10), V(10, 0), V(-10, 10)),   // 三角形（尖端朝阴极）
        Line(10, -10, 10, 10, PlateThickness),                                 // 阴极竖线
        Line(10, 0, 35, 0, LeadThickness)                                      // 阴极引线
    };

    // =====================================================================
    // 独立电源（IEC 60617）：圆 + 极性/波形标记；引脚 pos(0,-35) neg(0,35)。
    // =====================================================================
    private static IReadOnlyList<SymbolPrimitive> BuildVoltageSource(bool alternating)
    {
        var result = new List<SymbolPrimitive>
        {
            Line(0, -35, 0, -18, LeadThickness), // 上引线（正端）
            Circle(0, 0, 18, OutlineThickness),  // 电源圆
            Line(0, 18, 0, 35, LeadThickness)    // 下引线（负端）
        };

        if (alternating)
        {
            // 交流：圆内正弦波
            result.Add(Polyline(OutlineThickness, SineWave(0, 0, 10, 7, samples: 16)));
        }
        else
        {
            // 直流：极性标记 "+"（上）/"-"（下）
            result.Add(Line(-5, -8, 5, -8, MarkerThickness));
            result.Add(Line(0, -13, 0, -3, MarkerThickness));
            result.Add(Line(-5, 8, 5, 8, MarkerThickness));
        }

        return result;
    }

    private static IReadOnlyList<SymbolPrimitive> BuildCurrentSource(bool alternating)
    {
        var result = new List<SymbolPrimitive>
        {
            Line(0, -35, 0, -18, LeadThickness), // 上引线（in）
            Circle(0, 0, 18, OutlineThickness),  // 电源圆
            Line(0, 18, 0, 35, LeadThickness)    // 下引线（out）
        };

        if (alternating)
        {
            // 交流电流源：电流方向箭头（源内 out->in，向上）+ 正弦波
            result.Add(Line(0, 6, 0, -4, OutlineThickness));
            result.Add(FilledPolygon(V(0, -11), V(-4, -4), V(4, -4)));
            result.Add(Polyline(OutlineThickness, SineWave(0, 10, 7, 2.2f, samples: 12)));
        }
        else
        {
            // 直流电流源：电流方向箭头（源内 out->in，向上）
            result.Add(Line(0, 8, 0, -4, OutlineThickness));
            result.Add(FilledPolygon(V(0, -12), V(-4.5f, -4), V(4.5f, -4)));
        }

        return result;
    }

    // =====================================================================
    // 受控源（IEC 60617 / IEEE 315 菱形符号）
    //   * 菱形本体；
    //   * 控制端 IN1(-35,-15)/IN2(-35,15)、输出端 OUT1(35,-15)/OUT2(35,15)
    //     的引线均与引脚精确对齐（旧画布的引线是悬空的，已修正）；
    //   * 控制端标记：电压控制为 "+"/"-"，电流控制为控制支路电流箭头；
    //   * 输出端标记：电压输出为 "+"/"-"，电流输出为电流方向箭头。
    // =====================================================================
    private static IReadOnlyList<SymbolPrimitive> BuildControlledSource(bool voltageOutput, bool voltageControl)
    {
        var result = new List<SymbolPrimitive>
        {
            // 菱形本体
            ClosedPolyline(OutlineThickness, V(-16, 0), V(0, -16), V(16, 0), V(0, 16)),
            // 控制端引线（与 IN1/IN2 引脚对齐）
            Line(-35, -15, -8, -8, LeadThickness),
            Line(-35, 15, -8, 8, LeadThickness),
            // 输出端引线（与 OUT1/OUT2 引脚对齐）
            Line(8, -8, 35, -15, LeadThickness),
            Line(8, 8, 35, 15, LeadThickness)
        };

        if (voltageOutput)
        {
            // 电压输出：菱形内 "+"（上，对应 OUT1）/ "-"（下，对应 OUT2）
            result.Add(Line(-3.5f, -8, 3.5f, -8, MarkerThickness));
            result.Add(Line(0, -11.5f, 0, -4.5f, MarkerThickness));
            result.Add(Line(-3.5f, 8, 3.5f, 8, MarkerThickness));
        }
        else
        {
            // 电流输出：菱形内向下箭头（电流 OUT1 -> OUT2）
            result.Add(Line(0, -8, 0, 2, OutlineThickness));
            result.Add(FilledPolygon(V(0, 10), V(-3.5f, 3), V(3.5f, 3)));
        }

        if (voltageControl)
        {
            // 电压控制：控制端 "+"（上，IN1）/ "-"（下，IN2）
            result.Add(Line(-27.5f, -18, -20.5f, -18, MarkerThickness));
            result.Add(Line(-24, -21.5f, -24, -14.5f, MarkerThickness));
            result.Add(Line(-27.5f, 18, -20.5f, 18, MarkerThickness));
        }
        else
        {
            // 电流控制：控制支路（IN1->IN2）电流箭头，向下
            result.Add(Line(-28, -13.2f, -28, 5, OutlineThickness));
            result.Add(FilledPolygon(V(-28, 13.2f), V(-31.5f, 5), V(-24.5f, 5)));
        }

        return result;
    }

    // =====================================================================
    // 参考地（IEC 60617 三横线）与电源端口（Multisim 风格电源横杠）
    // =====================================================================

    // GND：三横线长度递减；引脚 GND(0, -22) 在上方，符号向下展开。
    private static IReadOnlyList<SymbolPrimitive> BuildGround() => new SymbolPrimitive[]
    {
        Line(0, -22, 0, -8, LeadThickness),   // 引脚引线
        Line(-14, -8, 14, -8, PlateThickness),// 第 1 横线（最长）
        Line(-9, -2, 9, -2, OutlineThickness),// 第 2 横线
        Line(-4, 4, 4, 4, OutlineThickness)   // 第 3 横线（最短）
    };

    // VCC 电源端口（Multisim 参考图）：电源横杠（bar）+ 向下引线至引脚 (0, 22)；
    // 符号本体不含向上空心三角箭头，"VCC" 文本由卡片/画布文本层绘制。
    private static IReadOnlyList<SymbolPrimitive> BuildVccPort() => new SymbolPrimitive[]
    {
        Line(-14, 0, 14, 0, RailThickness), // 电源横杠
        Line(0, 0, 0, 22, LeadThickness)    // 引线（引脚在横杠下方）
    };

    // =====================================================================
    // 图元构造辅助
    // =====================================================================
    private static Vector2 V(float x, float y) => new(x, y);

    private static SymbolPrimitive Line(float x1, float y1, float x2, float y2, float thickness) => new()
    {
        Kind = SymbolPrimitiveKind.Polyline,
        Points = new[] { V(x1, y1), V(x2, y2) },
        Thickness = thickness
    };

    private static SymbolPrimitive Polyline(float thickness, params Vector2[] points) => new()
    {
        Kind = SymbolPrimitiveKind.Polyline,
        Points = points,
        Thickness = thickness
    };

    private static SymbolPrimitive ClosedPolyline(float thickness, params Vector2[] points)
    {
        var closed = new Vector2[points.Length + 1];
        Array.Copy(points, closed, points.Length);
        closed[^1] = points[0];
        return new SymbolPrimitive
        {
            Kind = SymbolPrimitiveKind.Polyline,
            Points = closed,
            Thickness = thickness
        };
    }

    private static SymbolPrimitive FilledPolygon(params Vector2[] points) => new()
    {
        Kind = SymbolPrimitiveKind.FilledPolygon,
        Points = points,
        Thickness = 1.0f,
        Filled = true
    };

    private static SymbolPrimitive Circle(float centerX, float centerY, float radius, float thickness) => new()
    {
        Kind = SymbolPrimitiveKind.Circle,
        Points = new[] { V(centerX, centerY), V(centerX + radius, centerY) },
        Thickness = thickness,
        Filled = false
    };

    private static SymbolPrimitive Dot(float centerX, float centerY, float radius) => new()
    {
        Kind = SymbolPrimitiveKind.Circle,
        Points = new[] { V(centerX, centerY), V(centerX + radius, centerY) },
        Thickness = 1.0f,
        Filled = true
    };

    // 上半圆（从 (cx - r, 0) 经 (cx, -r) 到 (cx + r, 0)，+Y 向下故 -r 在上方）。
    private static Vector2[] HalfCircleUp(float centerX, float radius, int segments)
    {
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = MathF.PI - i * MathF.PI / segments;
            points[i] = new Vector2(
                centerX + radius * MathF.Cos(angle),
                -radius * MathF.Sin(angle));
        }
        return points;
    }

    // 竖直半圆：从 (x, cy - r) 经 (x ∓ r, cy) 到 (x, cy + r)。
    private static Vector2[] VerticalHalfCircle(float x, float centerY, float radius, bool bulgeLeft, int segments)
    {
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = -MathF.PI / 2f + i * MathF.PI / segments;
            float offsetX = radius * MathF.Cos(t);
            points[i] = new Vector2(
                bulgeLeft ? x - offsetX : x + offsetX,
                centerY + radius * MathF.Sin(t));
        }
        return points;
    }

    // 圆上圆弧采样点：角度按 +Y 向下的画布坐标解释（x = cx + r·cosθ，
    // y = cy + r·sinθ）。用于极性电容负极曲板等标准圆弧笔画。
    private static Vector2[] CircleArcPoints(
        float centerX, float centerY, float radius, float startAngle, float endAngle, int segments)
    {
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = startAngle + (endAngle - startAngle) * i / segments;
            points[i] = new Vector2(
                centerX + radius * MathF.Cos(angle),
                centerY + radius * MathF.Sin(angle));
        }
        return points;
    }

    // 一个完整周期的正弦波（IEC 交流电源波形标记）。
    private static Vector2[] SineWave(float centerX, float centerY, float halfWidth, float amplitude, int samples)
    {
        var points = new Vector2[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            points[i] = new Vector2(
                centerX - halfWidth + 2f * halfWidth * t,
                centerY - amplitude * MathF.Sin(2f * MathF.PI * t));
        }
        return points;
    }
}
