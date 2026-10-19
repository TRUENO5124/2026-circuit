// ============================================================================
// File: tests/SymbolChecks/Program.cs
// Description: 元件符号几何库 / 变换 / 节点跟随验收 harness（task-6，按用户第三轮要求）。
//              校验 Models/SymbolGeometry.cs 是否严格符合
//              IEC 60617 / IEEE 315 / Multisim 约定，并锁定：
//                * 每种 ElementType 的引线端点与 CircuitElement.InitializeDefaultPins()
//                  的引脚局部坐标一致（误差 <= 0.01）；
//                * NPN 默认集电极在上/发射极在下，PNP 默认发射极在上/集电极在下；
//                * 每种元件的水平镜像（ALT+X）与旋转使用同一局部->世界变换，
//                  引脚、导线端点和 WireTap 分支节点都随符号一起变换；
//                * JFET / MOSFET 的沟道、栅极、衬底箭头方向与增强/耗尽区分；
//                * VCC 为 Multisim 电源横杠 + 引线（无箭头/三角形）；GND 为 IEC 三横线；
//                * 二极管三角形朝阴极、电源极性/受控源菱形存在；
//                * 全局：坐标有限、线宽>0、多边形点数>=3、无 NaN。
//              运行：dotnet run --project tests/SymbolChecks/SymbolChecks.csproj
//              每个断言打印 [PASS]/[FAIL]；末行 "Total=x Passed=y Failed=z"，
//              失败时退出码非 0。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using CircuitAssistant.Models;

namespace SymbolChecks;

internal static class Program
{
    private static int _total;
    private static int _passed;
    private static int _failed;
    private static readonly List<string> _failures = new();

    private static int Main()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* console may not allow it */ }

        Console.WriteLine("================================================================================");
        Console.WriteLine("SYMBOL CHECKS: 国际标准元件符号几何 (IEC 60617 / IEEE 315 / Multisim)");
        Console.WriteLine("================================================================================");

        RunSection("几何库完整性 / 引脚局部坐标一致性", CheckLibraryIntegrity);
        RunSection("BJT: NPN/PNP 区分与实心发射极箭头", CheckBjt);
        RunSection("JFET: 竖直沟道 / 栅极条 / 实心箭头", CheckJfet);
        RunSection("MOSFET: 四类增强/耗尽与 N/P 区分", CheckMosfet);
        RunSection("VCC / GND 参考符号", CheckReferenceSymbols);
        RunSection("二极管与无源器件", CheckDiodeAndPassives);
        RunSection("独立电源与受控源", CheckSourcesAndControlledSources);
        RunSection("元件镜像/旋转：引脚世界坐标与克隆", CheckElementTransforms);
        RunSection("节点跟随：镜像后导线端点与 WireTap 分支", CheckWireAndNodeFollowing);
        RunSection("工程持久化：MirrorX 与旧 PNP 布局迁移", CheckProjectPersistenceAndMigration);
        RunSection("全局约束（有限性 / 线宽 / 多边形 / 唯一性）", CheckGlobalConstraints);

        Console.WriteLine();
        Console.WriteLine($"Total={_total} Passed={_passed} Failed={_failed}");
        if (_failed > 0)
        {
            Console.WriteLine("FAILURES:");
            foreach (var failure in _failures) Console.WriteLine("  " + failure);
            return 1;
        }

        return 0;
    }

    private static void RunSection(string name, Action section)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {name} ---");
        try
        {
            section();
        }
        catch (Exception ex)
        {
            Assert(false, $"{name}: 段内未处理异常", ex.ToString());
        }
    }

    private static void Assert(bool condition, string name, string details = "")
    {
        _total++;
        if (condition)
        {
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        else
        {
            _failed++;
            string line = string.IsNullOrWhiteSpace(details) ? name : $"{name} :: {details}";
            _failures.Add(line);
            Console.WriteLine($"[FAIL] {line}");
        }
    }

    // =====================================================================
    // 1) 几何库完整性 + 引脚一致性
    // =====================================================================
    private static void CheckLibraryIntegrity()
    {
        foreach (var type in Enum.GetValues<ElementType>())
        {
            var geometry = SymbolGeometryLibrary.Build(type);
            Assert(geometry.Count > 0, $"{type}: 几何非空", $"count={geometry.Count}");

            // 引脚局部坐标必须与 CircuitElement.InitializeDefaultPins() 完全一致。
            var element = new CircuitElement { Type = type };
            element.InitializeDefaultPins();
            var missingPins = new List<string>();
            foreach (var pin in element.Pins)
            {
                var expected = new Vector2(pin.RelativeX, pin.RelativeY);
                if (!HasPrimitivePoint(geometry, expected, 0.01f))
                {
                    missingPins.Add($"{pin.Name}({pin.RelativeX},{pin.RelativeY})");
                }
            }
            Assert(missingPins.Count == 0,
                $"{type}: 引线端点与 InitializeDefaultPins 引脚一致 (±0.01)",
                string.Join(", ", missingPins));

            // 图元合法性：有限坐标、无 NaN、线宽 > 0、图元形状合法。
            var problems = new List<string>();
            foreach (var primitive in geometry)
            {
                problems.AddRange(ValidatePrimitive(primitive));
            }
            Assert(problems.Count == 0, $"{type}: 图元合法性（有限坐标/线宽/形状）",
                string.Join("; ", problems.Take(3)));
        }
    }

    // =====================================================================
    // 2) BJT
    // =====================================================================
    private static void CheckBjt()
    {
        var npn = SymbolGeometryLibrary.Build(ElementType.BjtNpn);
        var pnp = SymbolGeometryLibrary.Build(ElementType.BjtPnp);

        Assert(Signature(npn) != Signature(pnp), "BJT: NPN 与 PNP 几何集合不同（可区分）");

        var npnArrows = FilledPolygons(npn);
        var pnpArrows = FilledPolygons(pnp);
        Assert(npnArrows.Count == 1 && pnpArrows.Count == 1,
            "BJT: NPN 与 PNP 各恰有 1 个实心箭头（实心三角形）",
            $"npn={npnArrows.Count}, pnp={pnpArrows.Count}");

        // 非箭头图元集合一致 —— 唯一差异只能是箭头（lead 要求的回归项）。
        Assert(Signature(NonArrow(npn)) == Signature(NonArrow(pnp)),
            "BJT: NPN 与 PNP 的非箭头图元集合一致（外圆/基极竖条/引线相同）");

        var (npnTip, npnBaseCenter) = ArrowTipAndBase(npnArrows[0], tipHasMaxX: true);
        var (pnpTip, pnpBaseCenter) = ArrowTipAndBase(pnpArrows[0], tipHasMaxX: false);
        var npnDirection = Vector2.Normalize(npnTip - npnBaseCenter);
        var pnpDirection = Vector2.Normalize(pnpTip - pnpBaseCenter);

        // 2026-10-18 用户要求：PNP 默认发射极在上、集电极在下。NPN 的发射极箭头
        // 位于下方斜引线（+Y），PNP 的发射极箭头位于上方斜引线（-Y）。
        Assert(npnArrows[0].Points.Average(point => point.Y) > 5f,
            "BJT: NPN 发射极箭头位于下方斜引线（发射极在下）",
            $"avgY={npnArrows[0].Points.Average(point => point.Y):F2}");
        Assert(pnpArrows[0].Points.Average(point => point.Y) < -5f,
            "BJT: PNP 发射极箭头位于上方斜引线（发射极在上）",
            $"avgY={pnpArrows[0].Points.Average(point => point.Y):F2}");

        // 默认引脚布局：NPN C 在上 / E 在下；PNP E 在上 / C 在下。
        var npnElement = new CircuitElement { Type = ElementType.BjtNpn };
        npnElement.InitializeDefaultPins();
        var pnpElement = new CircuitElement { Type = ElementType.BjtPnp };
        pnpElement.InitializeDefaultPins();
        Assert(npnElement.Pins.Single(pin => pin.Id == "C").RelativeY < 0 &&
               npnElement.Pins.Single(pin => pin.Id == "E").RelativeY > 0,
            "BJT: NPN 默认引脚 C 在上、E 在下");
        Assert(pnpElement.Pins.Single(pin => pin.Id == "E").RelativeY < 0 &&
               pnpElement.Pins.Single(pin => pin.Id == "C").RelativeY > 0,
            "BJT: PNP 默认引脚 E 在上、C 在下");

        const float baseBarX = -8f;
        float npnTipDistance = MathF.Abs(npnTip.X - baseBarX);
        float pnpTipDistance = MathF.Abs(pnpTip.X - baseBarX);
        Assert(npnTipDistance > pnpTipDistance + 5f,
            "BJT: NPN 发射极箭头尖端离基极竖条更远、PNP 更近",
            $"npn={npnTipDistance:F2}, pnp={pnpTipDistance:F2}");

        var npnEmitterAxis = Vector2.Normalize(new Vector2(20, 35) - new Vector2(-8, 6));
        var pnpEmitterAxis = Vector2.Normalize(new Vector2(20, -35) - new Vector2(-8, -6));
        Assert(Vector2.Dot(npnDirection, npnEmitterAxis) > 0.9f,
            "BJT: NPN 发射极箭头指向下方发射极引脚（向外）",
            $"dot={Vector2.Dot(npnDirection, npnEmitterAxis):F3}");
        Assert(Vector2.Dot(pnpDirection, pnpEmitterAxis) < -0.9f,
            "BJT: PNP 发射极箭头指向上方基极方向（向内）",
            $"dot={Vector2.Dot(pnpDirection, pnpEmitterAxis):F3}");

        Assert(Circles(npn).Count == 1 && Circles(pnp).Count == 1,
            "BJT: 含 IEEE 315 外圆（与 Multisim 2N3904 参考一致）");
    }

    // =====================================================================
    // 3) JFET
    // =====================================================================
    private static void CheckJfet()
    {
        var n = SymbolGeometryLibrary.Build(ElementType.JfetN);
        var p = SymbolGeometryLibrary.Build(ElementType.JfetP);

        Assert(Signature(SymbolGeometryLibrary.Build(ElementType.Jfet)) == Signature(n),
            "JFET: 通用 Jfet 类型复用 N 沟道标准符号");

        float channelX = FindChannelX(n);
        Assert(float.IsFinite(channelX) && MathF.Abs(channelX - SymbolGeometryLibrary.JfetChannelX) < 0.01f,
            "JFET: 竖直沟道位于 x=8（与 D/S 引脚 x=20 分离，经水平横线连接）", $"channelX={channelX}");

        // 标准画法：D/S 引线先从引脚竖直引出，再经水平短接横线接至沟道上下端。
        Assert(TryFindVerticalLeadFromPin(n, new Vector2(20, -35), out var drainInner)
               && MathF.Abs(drainInner.X - 20f) < 0.01f
               && MathF.Abs(drainInner.Y - (-SymbolGeometryLibrary.FetStubY)) < 0.01f,
            "JFET: 漏极引线先从 D 脚竖直引出至 D/S 横线层",
            $"inner={Format(drainInner)}");

        Assert(TryFindVerticalLeadFromPin(n, new Vector2(20, 35), out var sourceInner)
               && MathF.Abs(sourceInner.X - 20f) < 0.01f
               && MathF.Abs(sourceInner.Y - SymbolGeometryLibrary.FetStubY) < 0.01f,
            "JFET: 源极引线先从 S 脚竖直引出至 D/S 横线层",
            $"inner={Format(sourceInner)}");

        float stubY = SymbolGeometryLibrary.FetStubY;
        Assert(HasTwoPointLine(n, new Vector2(channelX, -stubY), new Vector2(20, -stubY))
               && HasTwoPointLine(n, new Vector2(channelX, stubY), new Vector2(20, stubY)),
            "JFET: 漏/源引线经水平短接横线接至沟道上下端（标准画法）");

        // 标准 JFET 无独立栅极条：独立栅极条是 MOSFET 绝缘栅结构，旧实现误加。
        Assert(!HasVerticalSegmentInXRange(n, 0.5f, channelX - 0.5f, 8f),
            "JFET: 沟道左侧无独立栅极条（栅极箭头直接指向沟道）");

        var nArrows = FilledPolygons(n);
        var pArrows = FilledPolygons(p);
        Assert(nArrows.Count == 1 && pArrows.Count == 1,
            "JFET: N/P 各恰有 1 个实心栅极箭头（实心三角形）",
            $"n={nArrows.Count}, p={pArrows.Count}");

        var (nTip, nBase) = ArrowTipAndBase(nArrows[0], tipHasMaxX: true);
        Assert(nTip.X > nBase.X && MathF.Abs(nTip.X - channelX) < MathF.Abs(nBase.X - channelX),
            "JFET: N 沟道栅极箭头尖端指向沟道",
            $"tip={Format(nTip)}, base={Format(nBase)}, channelX={channelX}");

        var (pTip, pBase) = ArrowTipAndBase(pArrows[0], tipHasMaxX: false);
        Assert(pTip.X < pBase.X && MathF.Abs(pTip.X - channelX) > MathF.Abs(pBase.X - channelX),
            "JFET: P 沟道栅极箭头背离沟道",
            $"tip={Format(pTip)}, base={Format(pBase)}, channelX={channelX}");

        Assert(Signature(NonArrow(n)) == Signature(NonArrow(p)),
            "JFET: N/P 除栅极箭头外的沟道/漏源引线几何一致");
    }

    // =====================================================================
    // 4) MOSFET
    // =====================================================================
    private static void CheckMosfet()
    {
        var nEnh = SymbolGeometryLibrary.Build(ElementType.MosfetNEnh);
        var pEnh = SymbolGeometryLibrary.Build(ElementType.MosfetPEnh);
        var nDep = SymbolGeometryLibrary.Build(ElementType.MosfetNDep);
        var pDep = SymbolGeometryLibrary.Build(ElementType.MosfetPDep);

        float channelX = FindChannelX(nDep);
        Assert(float.IsFinite(channelX) && MathF.Abs(channelX - SymbolGeometryLibrary.MosChannelX) < 0.01f,
            "MOSFET: 沟道 x 坐标可由耗尽型实线沟道推导（x=8）", $"channelX={channelX}");

        // 标准画法：D/S 横线接在顶部/底部沟道段的中点（±18），沟道段向上下伸出横线之外。
        float stubY = SymbolGeometryLibrary.FetStubY;
        var (nEnhMinY, nEnhMaxY) = VerticalExtentAt(nEnh, channelX);
        var (nDepMinY, nDepMaxY) = VerticalExtentAt(nDep, channelX);
        Assert(nEnhMinY < -stubY - 2f && nEnhMaxY > stubY + 2f,
            "MOSFET: 增强型沟道上下端伸出 D/S 横线之外（横线接在顶/底段中点）",
            $"minY={nEnhMinY:F2}, maxY={nEnhMaxY:F2}, stubY={stubY}");
        Assert(nDepMinY < -stubY - 2f && nDepMaxY > stubY + 2f,
            "MOSFET: 耗尽型整段沟道同样伸出 D/S 横线之外",
            $"minY={nDepMinY:F2}, maxY={nDepMaxY:F2}, stubY={stubY}");

        foreach (var geometry in new[] { nEnh, pEnh, nDep, pDep })
        {
            Assert(HasTwoPointLine(geometry, new Vector2(channelX, -stubY), new Vector2(20, -stubY))
                   && HasTwoPointLine(geometry, new Vector2(channelX, stubY), new Vector2(20, stubY)),
                "MOSFET: D/S 横线接在沟道顶部/底部线段中点（±18）");
        }

        // 衬底与源极短接处应有实心连接点（标准画法）。
        Assert(Circles(nEnh).Any(circle => circle.Filled
                && Vector2.Distance(circle.Points[0], new Vector2(20, stubY)) < 0.01f),
            "MOSFET: 衬底-源短接处有实心连接点");

        Assert(CountVerticalSegmentsAt(nEnh, channelX) == 3,
            "MOSFET: N 增强型沟道为断续 3 段", $"count={CountVerticalSegmentsAt(nEnh, channelX)}");
        Assert(CountVerticalSegmentsAt(pEnh, channelX) == 3,
            "MOSFET: P 增强型沟道为断续 3 段", $"count={CountVerticalSegmentsAt(pEnh, channelX)}");
        Assert(CountVerticalSegmentsAt(nDep, channelX) == 1,
            "MOSFET: N 耗尽型沟道为 1 条实线", $"count={CountVerticalSegmentsAt(nDep, channelX)}");
        Assert(CountVerticalSegmentsAt(pDep, channelX) == 1,
            "MOSFET: P 耗尽型沟道为 1 条实线", $"count={CountVerticalSegmentsAt(pDep, channelX)}");

        // 栅极板与沟道分离（IEC 60617 绝缘栅符号的关键特征）。
        foreach (var (type, geometry) in new[]
                 {
                     (ElementType.MosfetNEnh, nEnh), (ElementType.MosfetPEnh, pEnh),
                     (ElementType.MosfetNDep, nDep), (ElementType.MosfetPDep, pDep)
                 })
        {
            float gatePlateX = FindGatePlateX(geometry);
            Assert(float.IsFinite(gatePlateX) && channelX - gatePlateX >= 4f,
                $"MOSFET: {type} 栅极板与沟道分离（间隙 >= 4）",
                $"gatePlateX={gatePlateX}, channelX={channelX}");
        }

        var nEnhArrow = FilledPolygons(nEnh);
        var pEnhArrow = FilledPolygons(pEnh);
        var nDepArrow = FilledPolygons(nDep);
        var pDepArrow = FilledPolygons(pDep);
        Assert(nEnhArrow.Count == 1 && pEnhArrow.Count == 1 && nDepArrow.Count == 1 && pDepArrow.Count == 1,
            "MOSFET: 四类各恰有 1 个实心衬底/源箭头",
            $"nEnh={nEnhArrow.Count}, pEnh={pEnhArrow.Count}, nDep={nDepArrow.Count}, pDep={pDepArrow.Count}");

        foreach (var (type, geometry, tipHasMaxX) in new[]
                 {
                     (ElementType.MosfetNEnh, nEnh, false), (ElementType.MosfetNDep, nDep, false),
                     (ElementType.MosfetPEnh, pEnh, true), (ElementType.MosfetPDep, pDep, true)
                 })
        {
            var arrow = geometry.First(primitive => primitive.Kind == SymbolPrimitiveKind.FilledPolygon);
            var (tip, baseCenter) = ArrowTipAndBase(arrow, tipHasMaxX);
            bool pointsToChannel = MathF.Abs(tip.X - channelX) < MathF.Abs(baseCenter.X - channelX);
            bool shouldPointToChannel = type is ElementType.MosfetNEnh or ElementType.MosfetNDep;
            Assert(pointsToChannel == shouldPointToChannel,
                $"MOSFET: {type} 衬底/源实心箭头{(shouldPointToChannel ? "指向沟道" : "背离沟道")}",
                $"tip={Format(tip)}, base={Format(baseCenter)}, channelX={channelX}");
        }

        int distinctVariants = new[]
        {
            Signature(nEnh), Signature(pEnh), Signature(nDep), Signature(pDep)
        }.Distinct().Count();
        Assert(distinctVariants == 4, "MOSFET: 四类（N/P × 增强/耗尽）几何两两不同",
            $"distinct={distinctVariants}");

        Assert(Signature(SymbolGeometryLibrary.Build(ElementType.Mosfet)) == Signature(nEnh),
            "MOSFET: 通用 Mosfet 类型复用 N 增强型标准符号");
    }

    // =====================================================================
    // 5) VCC / GND
    // =====================================================================
    private static void CheckReferenceSymbols()
    {
        var vcc = SymbolGeometryLibrary.Build(ElementType.VccPort);

        var bars = TwoPointPolylines(vcc)
            .Where(line => MathF.Abs(line.Points[0].Y - line.Points[1].Y) < 0.01f
                           && MathF.Abs(line.Points[0].X - line.Points[1].X) >= 24f)
            .ToList();
        Assert(bars.Count == 1, "VCC: 含 1 条水平电源横杠（长度 >= 24，Multisim 风格）",
            $"count={bars.Count}");

        Assert(TwoPointPolylines(vcc).Any(line =>
                MathF.Abs(line.Points[0].X - line.Points[1].X) < 0.01f
                && MathF.Abs(line.Points[0].Y - line.Points[1].Y) >= 20f),
            "VCC: 含竖直引线（横杠与引线垂直相交）");

        Assert(HasPrimitivePoint(vcc, new Vector2(0, 22), 0.01f),
            "VCC: 引线端点 (0,22) 与 VccPort 引脚一致");

        // lead 要求：VCC 几何中不得出现任何实心三角形/箭头。
        Assert(FilledPolygons(vcc).Count == 0,
            "VCC: 几何不含任何 FilledPolygon（无箭头/三角形）",
            $"count={FilledPolygons(vcc).Count}");

        Assert(vcc.All(primitive => primitive.Points.All(point => point.Y >= -0.01f)),
            "VCC: 几何不含向上箭头（所有图元均在横杠 y=0 下方或线上）");

        // ---- GND ----
        var ground = SymbolGeometryLibrary.Build(ElementType.Ground);
        var groundBars = TwoPointPolylines(ground)
            .Where(line => MathF.Abs(line.Points[0].Y - line.Points[1].Y) < 0.01f)
            .ToList();
        Assert(groundBars.Count == 3, "GND: 为 IEC 60617 三横线符号", $"count={groundBars.Count}");

        if (groundBars.Count == 3)
        {
            var lengths = groundBars.Select(LineLength).OrderByDescending(length => length).ToList();
            Assert(lengths[0] > lengths[1] && lengths[1] > lengths[2],
                "GND: 三条横线长度依次递减", string.Join(", ", lengths.Select(length => length.ToString("F1"))));

            Assert(groundBars.All(line => MathF.Abs((line.Points[0].X + line.Points[1].X) * 0.5f) < 0.01f),
                "GND: 三条横线居中于 x=0");
        }

        Assert(HasPrimitivePoint(ground, new Vector2(0, -22), 0.01f),
            "GND: 引线端点 (0,-22) 与 Ground 引脚一致");
    }

    // =====================================================================
    // 6) 二极管与无源器件
    // =====================================================================
    private static void CheckDiodeAndPassives()
    {
        // ---- 二极管：三角形尖端朝阴极竖线 ----
        var diode = SymbolGeometryLibrary.Build(ElementType.Diode);
        var triangle = ClosedPolylines(diode).FirstOrDefault(polyline => polyline.Points.Count == 4);
        Assert(triangle != null, "DIODE: 含闭合三角形（IEC 二极管符号）");
        if (triangle != null)
        {
            var uniquePoints = DistinctPoints(triangle).ToList();
            Assert(uniquePoints.Count == 3, "DIODE: 三角形有 3 个顶点", $"count={uniquePoints.Count}");
            var tip = uniquePoints.OrderByDescending(point => point.X).First();
            float minX = uniquePoints.Min(point => point.X);
            Assert(tip.X > minX + 10f, "DIODE: 三角形尖端指向 +x（阳极 -> 阴极）", $"tip={Format(tip)}");

            var cathodeBar = TwoPointPolylines(diode).FirstOrDefault(line =>
                MathF.Abs(line.Points[0].X - line.Points[1].X) < 0.01f
                && MathF.Abs(line.Points[0].X - tip.X) < 0.01f
                && LineLength(line) >= 20f);
            Assert(cathodeBar != null, "DIODE: 三角形尖端抵达阴极竖线（尖端 x = 阴极线 x）",
                $"tip={Format(tip)}");
        }

        // ---- 电阻：IEC 矩形 ----
        var resistor = SymbolGeometryLibrary.Build(ElementType.Resistor);
        var rectangle = ClosedPolylines(resistor).FirstOrDefault(polyline => polyline.Points.Count == 5);
        Assert(rectangle != null, "RESISTOR: 含 IEC 60617 矩形（闭合折线）");
        if (rectangle != null)
        {
            var bounds = BoundsOfDistinctPoints(rectangle);
            Assert(MathF.Abs(bounds.Max.X - bounds.Min.X - 40f) < 0.01f
                   && MathF.Abs(bounds.Max.Y - bounds.Min.Y - 20f) < 0.01f,
                "RESISTOR: 矩形尺寸 40x20（IEC/GB 标准比例）",
                $"w={bounds.Max.X - bounds.Min.X}, h={bounds.Max.Y - bounds.Min.Y}");
        }

        // ---- ANSI 锯齿形电阻（卡片 passive.resistor_ansi）----
        var ansi = SymbolGeometryLibrary.BuildAnsiResistor();
        var zigzag = ansi.FirstOrDefault(primitive => primitive.Kind == SymbolPrimitiveKind.Polyline);
        Assert(zigzag != null && zigzag.Points.Count >= 9,
            "RESISTOR_ANSI: ANSI/IEEE 315 锯齿形电阻折线（>= 9 个顶点）",
            $"points={(zigzag == null ? 0 : zigzag.Points.Count)}");
        if (zigzag != null)
        {
            Assert(Vector2.Distance(zigzag.Points[0], new Vector2(-40, 0)) < 0.01f
                   && Vector2.Distance(zigzag.Points[^1], new Vector2(40, 0)) < 0.01f,
                "RESISTOR_ANSI: 引线端点 (-40,0)/(40,0) 与 Resistor 引脚一致");
            Assert(zigzag.Points.Min(point => point.Y) < -2f
                   && zigzag.Points.Max(point => point.Y) > 2f,
                "RESISTOR_ANSI: 锯齿跨越基线上下两侧");
        }

        // ---- 电容：两块平行极板 ----
        var capacitor = SymbolGeometryLibrary.Build(ElementType.Capacitor);
        var plates = TwoPointPolylines(capacitor)
            .Where(line => MathF.Abs(line.Points[0].X - line.Points[1].X) < 0.01f && LineLength(line) >= 30f)
            .OrderBy(line => line.Points[0].X)
            .ToList();
        Assert(plates.Count == 2, "CAPACITOR: 含两块平行极板", $"count={plates.Count}");
        if (plates.Count == 2)
        {
            Assert(MathF.Abs(plates[0].Points[0].X + 6f) < 0.01f
                   && MathF.Abs(plates[1].Points[0].X - 6f) < 0.01f,
                "CAPACITOR: 两块极板对称位于 x=±6（IEC 平行板电容）");
        }

        // ---- 极性电容 ----
        var polarized = SymbolGeometryLibrary.Build(ElementType.CapacitorPolarized);
        var curvedPlates = polarized
            .Where(primitive => primitive.Kind == SymbolPrimitiveKind.Polyline && primitive.Points.Count >= 7)
            .ToList();
        Assert(curvedPlates.Count >= 1, "CAPACITOR_POLARIZED: 含负极曲板（多点折线近似圆弧）");
        if (curvedPlates.Count >= 1)
        {
            var curve = curvedPlates[0].Points;
            // 负极曲板必须位于正极直板（x=-6）右侧。
            Assert(curve.Min(point => point.X) >= 6f,
                "CAPACITOR_POLARIZED: 负极曲板位于正极直板右侧");

            // 曲板弯曲方向（用户验收项）：弧顶（靠近 y=0 的中点）应比两端更靠近
            // 正极直板，即曲板凸向正极板；若弧顶比两端更远离正极板则为错误方向。
            var vertex = curve.OrderBy(point => MathF.Abs(point.Y)).First();
            var ends = curve.Where(point => MathF.Abs(point.Y) > 10f).ToList();
            float endX = ends.Count > 0 ? ends.Average(point => point.X) : curve.Max(point => point.X);
            Assert(vertex.X < endX - 1f,
                "CAPACITOR_POLARIZED: 负极曲板弧顶凸向正极直板（弯曲方向正确）",
                $"vertex={Format(vertex)}, endX={endX:F2}");
            Assert(MathF.Abs(vertex.X - 8f) < 0.5f && MathF.Abs(vertex.Y) < 0.5f,
                "CAPACITOR_POLARIZED: 弧顶位于负极引线起点 (8,0)",
                $"vertex={Format(vertex)}");
        }
        Assert(polarized.Count(primitive => MathF.Abs(primitive.Thickness - SymbolGeometryLibrary.MarkerThickness) < 0.01f) >= 2,
            "CAPACITOR_POLARIZED: 含 '+' 极性标记（横竖两笔）");

        // ---- 电感：IEC 半圆链 ----
        var inductor = SymbolGeometryLibrary.Build(ElementType.Inductor);
        var arcs = inductor
            .Where(primitive => primitive.Kind == SymbolPrimitiveKind.Polyline && primitive.Points.Count >= 5)
            .ToList();
        Assert(arcs.Count == 4, "INDUCTOR: 含 4 个半圆线圈（IEC 60617 半圆链）", $"count={arcs.Count}");
        Assert(arcs.All(arc => arc.Points.Min(point => point.Y) < -2f),
            "INDUCTOR: 4 个线圈朝基线同一侧（上方）凸起");

        // ---- 变压器 ----
        var transformer = SymbolGeometryLibrary.Build(ElementType.Transformer);
        var coreLines = TwoPointPolylines(transformer)
            .Where(line => MathF.Abs(line.Points[0].X - line.Points[1].X) < 0.01f
                           && LineLength(line) >= 30f
                           && MathF.Abs(line.Points[0].X) < 3f)
            .ToList();
        Assert(coreLines.Count == 2, "TRANSFORMER: 含双线磁芯（IEC 变压器符号）", $"count={coreLines.Count}");
        var windings = transformer
            .Where(primitive => primitive.Kind == SymbolPrimitiveKind.Polyline && primitive.Points.Count >= 5)
            .ToList();
        Assert(windings.Count == 6, "TRANSFORMER: 一/二次侧各 3 段绕组", $"count={windings.Count}");
    }

    // =====================================================================
    // 7) 独立电源与受控源
    // =====================================================================
    private static void CheckSourcesAndControlledSources()
    {
        // ---- 直流电压源：圆 + "+"/"-" 极性 ----
        var dcVoltage = SymbolGeometryLibrary.Build(ElementType.DcVoltageSource);
        Assert(Circles(dcVoltage).Count == 1, "V_DC: 含电源圆（IEC 电源符号）");
        var dcHorizontal = TwoPointPolylines(dcVoltage)
            .Where(line => MathF.Abs(line.Points[0].Y - line.Points[1].Y) < 0.01f)
            .ToList();
        Assert(dcHorizontal.Any(line => MathF.Abs(line.Points[0].Y + 8f) < 0.01f
                                        && LineLength(line) >= 9f && LineLength(line) <= 11f),
            "V_DC: 含 '+' 号横笔（位于正端一侧）");
        Assert(TwoPointPolylines(dcVoltage).Any(line =>
                MathF.Abs(line.Points[0].X - line.Points[1].X) < 0.01f
                && MathF.Abs(line.Points[0].X) < 0.01f
                && line.Points.All(point => point.Y < 0)
                && LineLength(line) >= 9f && LineLength(line) <= 11f),
            "V_DC: 含 '+' 号竖笔（与横笔组成正极性标记）");
        Assert(dcHorizontal.Any(line => MathF.Abs(line.Points[0].Y - 8f) < 0.01f
                                        && LineLength(line) >= 9f && LineLength(line) <= 11f),
            "V_DC: 含 '-' 号横笔（位于负端一侧）");

        // ---- 交流电压源：圆 + 正弦波 ----
        var acVoltage = SymbolGeometryLibrary.Build(ElementType.AcVoltageSource);
        Assert(acVoltage.Any(primitive =>
                primitive.Kind == SymbolPrimitiveKind.Polyline
                && primitive.Points.Count >= 9
                && primitive.Points.Min(point => point.Y) <= -3f
                && primitive.Points.Max(point => point.Y) >= 3f),
            "V_AC: 圆内含正弦波形标记");

        // ---- 直流电流源：圆 + 实心电流箭头（out -> in，向上）----
        var dcCurrent = SymbolGeometryLibrary.Build(ElementType.DcCurrentSource);
        var dcCurrentArrows = FilledPolygons(dcCurrent);
        Assert(dcCurrentArrows.Count == 1, "I_DC: 含 1 个实心电流方向箭头", $"count={dcCurrentArrows.Count}");
        if (dcCurrentArrows.Count == 1)
        {
            var (tip, baseCenter) = ArrowTipAndBaseVertical(dcCurrentArrows[0], tipHasMinY: true);
            // 画布 +Y 向下；源内电流 out(下) -> in(上) 故箭头向上（tip.y < base.y）。
            Assert(tip.Y < baseCenter.Y - 2f, "I_DC: 电流箭头向上（out 引脚 -> in 引脚）",
                $"tip={Format(tip)}, base={Format(baseCenter)}");
        }

        // ---- 交流电流源：圆 + 箭头 + 正弦 ----
        var acCurrent = SymbolGeometryLibrary.Build(ElementType.AcCurrentSource);
        Assert(FilledPolygons(acCurrent).Count == 1, "I_AC: 含 1 个实心电流方向箭头");
        Assert(acCurrent.Any(primitive =>
                primitive.Kind == SymbolPrimitiveKind.Polyline
                && primitive.Points.Count >= 9
                && primitive.Points.Max(point => point.Y) - primitive.Points.Min(point => point.Y) >= 2f),
            "I_AC: 含正弦波形标记（区分交流电流源）");

        // ---- 四类受控源：菱形 + 控制端/输出端标记 ----
        var controlledTypes = new[]
        {
            ElementType.Vcvs, ElementType.Vccs, ElementType.Ccvs, ElementType.Cccs
        };
        foreach (var type in controlledTypes)
        {
            var geometry = SymbolGeometryLibrary.Build(type);
            Assert(ContainsDiamond(geometry), $"{type}: 含受控源菱形符号");
        }

        var vcvs = SymbolGeometryLibrary.Build(ElementType.Vcvs);
        var ccvs = SymbolGeometryLibrary.Build(ElementType.Ccvs);
        var vccs = SymbolGeometryLibrary.Build(ElementType.Vccs);
        var cccs = SymbolGeometryLibrary.Build(ElementType.Cccs);

        Assert(CountMarkerLines(vcvs, insideDiamond: true) >= 2,
            "VCVS: 电压输出用菱形内 '+'/'-' 极性标记");
        Assert(CountMarkerLines(ccvs, insideDiamond: true) >= 2,
            "CCVS: 电压输出用菱形内 '+'/'-' 极性标记");
        Assert(CountMarkerLines(vcvs, insideDiamond: false) >= 2,
            "VCVS: 电压控制端含 '+'/'-' 标记");
        Assert(CountMarkerLines(vccs, insideDiamond: false) >= 2,
            "VCCS: 电压控制端含 '+'/'-' 标记");

        Assert(FilledPolygons(vccs).Any(arrow => MathF.Abs(Centroid(arrow).X) < 5f),
            "VCCS: 电流输出用菱形内实心箭头");
        Assert(FilledPolygons(cccs).Any(arrow => MathF.Abs(Centroid(arrow).X) < 5f),
            "CCCS: 电流输出用菱形内实心箭头");
        Assert(FilledPolygons(ccvs).Any(arrow => Centroid(arrow).X < -20f),
            "CCVS: 电流控制端含控制支路电流箭头");
        Assert(FilledPolygons(cccs).Any(arrow => Centroid(arrow).X < -20f),
            "CCCS: 电流控制端含控制支路电流箭头");
    }

    // =====================================================================
    // 9) 元件镜像/旋转：统一坐标变换
    // =====================================================================
    private static void CheckElementTransforms()
    {
        foreach (var type in Enum.GetValues<ElementType>())
        {
            var element = new CircuitElement { Type = type, X = 123.4f, Y = -56.7f };
            element.InitializeDefaultPins();

            float plainError = MaxPinError(element);
            element.MirrorX = true;
            float mirrorError = MaxPinError(element);
            element.RotationDeg = 90;
            float mirrorRotatedError = MaxPinError(element);
            element.MirrorX = false;
            float rotatedError = MaxPinError(element);

            float error = MathF.Max(MathF.Max(plainError, mirrorError), MathF.Max(mirrorRotatedError, rotatedError));
            Assert(error <= 1e-3f,
                $"{type}: 普通/镜像/镜像+旋转/旋转后的引脚世界坐标与统一变换矩阵一致",
                $"maxError={error:F6}");
        }

        // 克隆必须保留镜像状态，否则撤销/重做会丢失镜像。
        var source = new CircuitElement { Type = ElementType.BjtPnp, X = 10, Y = 20, RotationDeg = 270, MirrorX = true };
        source.InitializeDefaultPins();
        var clone = source.Clone();
        bool cloneOk = clone.MirrorX && clone.RotationDeg == 270 &&
                       source.Pins.All(pin =>
                       {
                           var clonePin = clone.Pins.FirstOrDefault(p => p.Id == pin.Id);
                           return clonePin != null &&
                                  Vector2.Distance(source.GetPinWorldPosition(pin), clone.GetPinWorldPosition(clonePin)) < 1e-3f;
                       });
        Assert(cloneOk, "TRANSFORM: Clone 保留 MirrorX/旋转，镜像后引脚世界坐标一致");

        // HitTest 必须使用逆变换（先逆旋转，再对 X 取反）。
        var hitElement = new CircuitElement { Type = ElementType.Resistor, X = 0, Y = 0, Width = 80, Height = 50, MirrorX = true };
        hitElement.InitializeDefaultPins();
        var mirroredHit = hitElement.GetPinWorldPosition(hitElement.Pins[0]); // (-40,0) -> (+40,0)
        Assert(mirroredHit.X > 0 && hitElement.HitTest(mirroredHit),
            "TRANSFORM: 镜像后引脚命中测试使用逆变换",
            $"pinWorld={Format(mirroredHit)}");
    }

    private static float MaxPinError(CircuitElement element)
    {
        float max = 0f;
        var matrix = element.GetSymbolTransformMatrix();
        foreach (var pin in element.Pins)
        {
            var local = new Vector2(pin.RelativeX, pin.RelativeY);
            if (element.MirrorX) local.X = -local.X;
            var rad = (float)(element.RotationDeg * Math.PI / 180.0);
            var expected = new Vector2(element.X, element.Y) +
                           Vector2.Transform(local, Matrix3x2.CreateRotation(rad));
            var actual = element.GetPinWorldPosition(pin);
            // GetSymbolTransformMatrix maps world-space symbol geometry around the
            // element centre (the canvas draws center + localPoint), so feed it the
            // untransformed world point rather than the centre-relative local point.
            var worldPoint = new Vector2(element.X + pin.RelativeX, element.Y + pin.RelativeY);
            var matrixActual = Vector2.Transform(worldPoint, matrix);
            max = MathF.Max(max, Vector2.Distance(expected, actual));
            max = MathF.Max(max, Vector2.Distance(actual, matrixActual));
        }
        return max;
    }

    // =====================================================================
    // 10) 节点跟随：镜像/旋转后导线端点与 WireTap 分支
    // =====================================================================
    private static void CheckWireAndNodeFollowing()
    {
        var element = new CircuitElement { Id = "Q1", Type = ElementType.BjtPnp, X = 300, Y = 220 };
        element.InitializeDefaultPins();
        var pinB = element.Pins.Single(pin => pin.Id == "B");
        var pinC = element.Pins.Single(pin => pin.Id == "C");
        var pinE = element.Pins.Single(pin => pin.Id == "E");

        var b = element.GetPinWorldPosition(pinB);
        var c = element.GetPinWorldPosition(pinC);
        var e = element.GetPinWorldPosition(pinE);

        var wires = new List<CircuitWire>
        {
            // 直连基极和集电极的导线
            new() { Id = "W_B", StartElementId = "Q1", StartPinName = "B", StartX = b.X, StartY = b.Y, EndX = 100, EndY = b.Y },
            new() { Id = "W_C", StartElementId = "", StartPinName = "", StartX = 100, StartY = c.Y, EndElementId = "Q1", EndPinName = "C", EndX = c.X, EndY = c.Y },
            // 水平母线（不属于任何元件）与发射极 WireTap 分支
            new() { Id = "VCC_RAIL", StartElementId = "", StartPinName = "", StartX = 0, StartY = 100, EndElementId = "", EndPinName = "", EndX = 500, EndY = 100 },
            new() { Id = "BRANCH", StartElementId = "Q1", StartPinName = "E", StartX = e.X, StartY = e.Y, EndElementId = "VCC_RAIL", EndPinName = "WireTap", EndX = 300, EndY = 100 },
            // 挂在 BRANCH 上的二级分支（WireTap 依赖链传播）
            new() { Id = "TAP_CHILD", StartElementId = "BRANCH", StartPinName = "WireTap", StartX = 300, StartY = 100, EndElementId = "", EndPinName = "", EndX = 300, EndY = 180 }
        };

        foreach (var state in new[] { "镜像", "镜像+旋转" })
        {
            if (state == "镜像") element.MirrorX = true;
            else element.RotationDeg = 90;
            CircuitWireSync.SyncConnectedWiresToElement(wires, element);

            var bNow = element.GetPinWorldPosition(pinB);
            var cNow = element.GetPinWorldPosition(pinC);
            var eNow = element.GetPinWorldPosition(pinE);
            var wireB = wires.Single(w => w.Id == "W_B");
            var wireC = wires.Single(w => w.Id == "W_C");
            var branch = wires.Single(w => w.Id == "BRANCH");
            var child = wires.Single(w => w.Id == "TAP_CHILD");
            var rail = wires.Single(w => w.Id == "VCC_RAIL");

            Assert(Vector2.Distance(new Vector2(wireB.StartX, wireB.StartY), bNow) < 1e-3f,
                $"{state}: 直连基极导线端点跟随镜像/旋转后的引脚",
                $"wire={Format(new Vector2(wireB.StartX, wireB.StartY))}, pin={Format(bNow)}");
            Assert(Vector2.Distance(new Vector2(wireC.EndX, wireC.EndY), cNow) < 1e-3f,
                $"{state}: 集电极导线端点跟随镜像/旋转后的引脚",
                $"wire={Format(new Vector2(wireC.EndX, wireC.EndY))}, pin={Format(cNow)}");
            Assert(Vector2.Distance(new Vector2(branch.StartX, branch.StartY), eNow) < 1e-3f,
                $"{state}: WireTap 分支起点跟随发射极引脚",
                $"wire={Format(new Vector2(branch.StartX, branch.StartY))}, pin={Format(eNow)}");

            var tap = new Vector2(branch.EndX, branch.EndY);
            Assert(DistanceToPath(rail, tap) < 1e-3f,
                $"{state}: 分支节点仍投影在父母线路径上（焊点不脱离母线）",
                $"tap={Format(tap)}, dist={DistanceToPath(rail, tap):F4}");
            var childTap = new Vector2(child.StartX, child.StartY);
            Assert(DistanceToPath(branch, childTap) < 1e-3f,
                $"{state}: 二级 WireTap 节点跟随已移动的父分支",
                $"tap={Format(childTap)}, dist={DistanceToPath(branch, childTap):F4}");
        }
    }

    private static float DistanceToPath(CircuitWire wire, Vector2 point)
    {
        var points = wire.GetPathPoints();
        float best = float.MaxValue;
        for (int i = 0; i < points.Count - 1; i++)
        {
            best = MathF.Min(best, CircuitWire.DistanceToSegment(point, points[i], points[i + 1]));
        }
        return best;
    }

    // =====================================================================
    // 11) 工程持久化：MirrorX 与旧 PNP 布局迁移
    // =====================================================================
    private static void CheckProjectPersistenceAndMigration()
    {
        // ---- MirrorX 写入/读回 ----
        var pnp = new CircuitElement
        {
            Id = "Q1",
            Name = "PNP Q1",
            Type = ElementType.BjtPnp,
            X = 200,
            Y = 150,
            RotationDeg = 270,
            MirrorX = true
        };
        pnp.InitializeDefaultPins();
        var emitter = pnp.GetPinWorldPosition(pnp.Pins.Single(pin => pin.Id == "E"));
        var wire = new CircuitWire
        {
            Id = "W1",
            StartElementId = "Q1",
            StartPinName = "E",
            StartX = emitter.X,
            StartY = emitter.Y,
            EndElementId = "",
            EndPinName = "",
            EndX = emitter.X,
            EndY = emitter.Y + 40
        };
        var data = CircuitProjectData.From("镜像工程", "mirror.circuit", new[] { pnp }, new[] { wire });
        Assert(data.Version == "1.2.0" && data.Elements.Single().MirrorX,
            "PROJECT: From 写入 MirrorX，版本保持 1.2.0");

        string json = System.Text.Json.JsonSerializer.Serialize(data);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<CircuitProjectData>(json);
        Assert(reloaded != null && reloaded.Elements.Single().MirrorX,
            "PROJECT: JSON 序列化/反序列化保留 MirrorX（旧文件缺省 false）");

        // Older JSON does not contain the element-level MirrorX property at all.
        var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        jsonNode["Elements"]!.AsArray()[0]!.AsObject().Remove("MirrorX");
        var missingMirror = System.Text.Json.JsonSerializer.Deserialize<CircuitProjectData>(jsonNode.ToJsonString());
        Assert(missingMirror != null && !missingMirror.Elements.Single().MirrorX,
            "PROJECT: 缺少 MirrorX 字段的旧 JSON 按 false 读取");

        var (elements, wires, _) = data.ToModel();
        var loadedPnp = elements.Single();
        var loadedWire = wires.Single();
        var expectedEmitter = loadedPnp.GetPinWorldPosition(loadedPnp.Pins.Single(pin => pin.Id == "E"));
        Assert(loadedPnp.MirrorX && loadedPnp.RotationDeg == 270 &&
               Vector2.Distance(new Vector2(loadedWire.StartX, loadedWire.StartY), expectedEmitter) < 1e-3f,
            "PROJECT: ToModel 还原 MirrorX/旋转，导线端点仍与发射极引脚重合");

        // ---- 旧 PNP 工程（C 在上、E 在下）迁移 ----
        var oldPnpDto = new CircuitElementDto
        {
            Id = "Q1",
            Name = "旧 PNP",
            Type = ElementType.BjtPnp,
            X = 0,
            Y = 0,
            Width = 80,
            Height = 50,
            Pins =
            {
                new CircuitPinDto { Id = "B", Name = "B", RelativeX = -35, RelativeY = 0 },
                new CircuitPinDto { Id = "C", Name = "C", RelativeX = 20, RelativeY = -35 },
                new CircuitPinDto { Id = "E", Name = "E", RelativeX = 20, RelativeY = 35 }
            }
        };
        var legacy = new CircuitProjectData
        {
            Version = "1.2.0",
            ProjectName = "旧 PNP 工程",
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now,
            Elements = { oldPnpDto },
            Wires =
            {
                new CircuitWireDto
                {
                    Id = "W_C",
                    StartElementId = "Q1",
                    StartPinName = "C",
                    StartX = 20,
                    StartY = -35,
                    EndX = 20,
                    EndY = 80
                },
                new CircuitWireDto
                {
                    Id = "VCC_RAIL",
                    StartElementId = "",
                    StartPinName = "",
                    StartX = -200,
                    StartY = 100,
                    EndElementId = "",
                    EndPinName = "",
                    EndX = 300,
                    EndY = 100
                },
                new CircuitWireDto
                {
                    Id = "W_BRANCH",
                    StartElementId = "Q1",
                    StartPinName = "E",
                    StartX = 20,
                    StartY = 35,
                    EndElementId = "VCC_RAIL",
                    EndPinName = "WireTap",
                    EndX = 100,
                    EndY = 100
                }
            }
        };
        var (legacyElements, legacyWires, _) = legacy.ToModel();
        var migrated = legacyElements.Single();
        var migratedC = migrated.Pins.Single(pin => pin.Id == "C");
        var migratedE = migrated.Pins.Single(pin => pin.Id == "E");
        var migratedWire = legacyWires.Single(wire => wire.Id == "W_C");
        var migratedBranch = legacyWires.Single(wire => wire.Id == "W_BRANCH");
        var migratedRail = legacyWires.Single(wire => wire.Id == "VCC_RAIL");
        var expectedC = migrated.GetPinWorldPosition(migratedC);
        var expectedE = migrated.GetPinWorldPosition(migratedE);
        Assert(migratedC.RelativeY > 0 && migratedE.RelativeY < 0,
            "PROJECT: 旧 PNP 工程迁移为发射极在上/集电极在下",
            $"C.y={migratedC.RelativeY}, E.y={migratedE.RelativeY}");
        Assert(Vector2.Distance(new Vector2(migratedWire.StartX, migratedWire.StartY), expectedC) < 1e-3f,
            "PROJECT: 旧 PNP 工程迁移后集电极导线跟随新引脚",
            $"wire={Format(new Vector2(migratedWire.StartX, migratedWire.StartY))}, pin={Format(expectedC)}");
        Assert(Vector2.Distance(new Vector2(migratedBranch.StartX, migratedBranch.StartY), expectedE) < 1e-3f &&
               DistanceToPath(migratedRail, new Vector2(migratedBranch.EndX, migratedBranch.EndY)) < 1e-3f,
            "PROJECT: 旧 PNP 工程迁移后 WireTap 分支节点重挂到母线",
            $"branchStart={Format(new Vector2(migratedBranch.StartX, migratedBranch.StartY))}, " +
            $"tap={Format(new Vector2(migratedBranch.EndX, migratedBranch.EndY))}");

        // Legacy 1.0.0/1.1.0 files may have no stored pin coordinates at all.
        var noPinData = new CircuitProjectData
        {
            Version = "1.0.0",
            ProjectName = "无引脚旧 PNP 工程",
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now,
            Elements =
            {
                new CircuitElementDto
                {
                    Id = "Q1",
                    Name = "旧 PNP",
                    Type = ElementType.BjtPnp,
                    X = 0,
                    Y = 0,
                    Width = 80,
                    Height = 50
                }
            },
            Wires =
            {
                new CircuitWireDto
                {
                    Id = "W_C",
                    StartElementId = "Q1",
                    StartPinName = "C",
                    StartX = 20,
                    StartY = -35,
                    EndX = 20,
                    EndY = 80
                }
            }
        };
        var (noPinElements, noPinWires, _) = noPinData.ToModel();
        var noPinPnp = noPinElements.Single();
        var noPinC = noPinPnp.Pins.Single(pin => pin.Id == "C");
        var expectedNoPinC = noPinPnp.GetPinWorldPosition(noPinC);
        Assert(noPinC.RelativeY > 0 &&
               Vector2.Distance(new Vector2(noPinWires.Single().StartX, noPinWires.Single().StartY), expectedNoPinC) < 1e-3f,
            "PROJECT: 无引脚坐标的旧 PNP 工程按新默认布局迁移并重挂导线",
            $"C.y={noPinC.RelativeY}, wire={Format(new Vector2(noPinWires.Single().StartX, noPinWires.Single().StartY))}");

        // Rotation + mirror must also be honored when migrating a legacy PNP layout.
        var rotatedOld = new CircuitElementDto
        {
            Id = "Q1",
            Name = "旋转镜像旧 PNP",
            Type = ElementType.BjtPnp,
            X = 100,
            Y = 50,
            Width = 80,
            Height = 50,
            RotationDeg = 90,
            MirrorX = true,
            Pins =
            {
                new CircuitPinDto { Id = "B", Name = "B", RelativeX = -35, RelativeY = 0 },
                new CircuitPinDto { Id = "C", Name = "C", RelativeX = 20, RelativeY = -35 },
                new CircuitPinDto { Id = "E", Name = "E", RelativeX = 20, RelativeY = 35 }
            }
        };
        var rotatedLegacy = new CircuitProjectData
        {
            Version = "1.2.0",
            ProjectName = "旋转镜像旧 PNP 工程",
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now,
            Elements = { rotatedOld },
            Wires =
            {
                new CircuitWireDto
                {
                    Id = "W_E",
                    StartElementId = "Q1",
                    StartPinName = "E",
                    StartX = 0,
                    StartY = 0,
                    EndX = 0,
                    EndY = 0
                }
            }
        };
        var (rotatedElements, rotatedWires, _) = rotatedLegacy.ToModel();
        var rotatedPnp = rotatedElements.Single();
        var rotatedPinE = rotatedPnp.GetPinWorldPosition(rotatedPnp.Pins.Single(pin => pin.Id == "E"));
        var rotatedWire = rotatedWires.Single();
        Assert(rotatedPnp.MirrorX && rotatedPnp.RotationDeg == 90 &&
               rotatedPnp.Pins.Single(pin => pin.Id == "E").RelativeY < 0 &&
               Vector2.Distance(new Vector2(rotatedWire.StartX, rotatedWire.StartY), rotatedPinE) < 1e-3f,
            "PROJECT: 旋转+镜像的旧 PNP 工程迁移后导线跟随变换后引脚",
            $"pinE={Format(rotatedPinE)}, wire={Format(new Vector2(rotatedWire.StartX, rotatedWire.StartY))}");
    }

    // =====================================================================
    // 12) 全局约束
    // =====================================================================
    private static void CheckGlobalConstraints()
    {
        var allTypes = Enum.GetValues<ElementType>();
        var allPoints = allTypes.SelectMany(type => AllPoints(SymbolGeometryLibrary.Build(type))).ToList();

        Assert(allPoints.All(point => float.IsFinite(point.X) && float.IsFinite(point.Y)
                                      && !float.IsNaN(point.X) && !float.IsNaN(point.Y)),
            "GLOBAL: 所有几何点坐标有限且无 NaN", $"points={allPoints.Count}");
        Assert(allPoints.All(point => MathF.Abs(point.X) <= 45f),
            "GLOBAL: 所有符号位于 |x| <= 45 的元件局部坐标范围内");
        Assert(allPoints.All(point => MathF.Abs(point.Y) <= 45f),
            "GLOBAL: 所有符号位于 |y| <= 45 的元件局部坐标范围内");

        Assert(allTypes.All(type => SymbolGeometryLibrary.Build(type).Count >= 2),
            "GLOBAL: 每种元件至少 2 个图元（引线 + 符号本体）");
        Assert(allTypes.All(type => !HasConsecutiveDuplicatePoints(SymbolGeometryLibrary.Build(type))),
            "GLOBAL: 折线/多边形不存在连续重复点");
        Assert(allTypes.All(type => SymbolGeometryLibrary.Build(type).Any(
                primitive => primitive.Kind == SymbolPrimitiveKind.Polyline)),
            "GLOBAL: 每种元件至少含 1 条折线图元");

        var thicknesses = allTypes
            .SelectMany(type => SymbolGeometryLibrary.Build(type))
            .Select(primitive => primitive.Thickness)
            .ToList();
        Assert(thicknesses.All(thickness => thickness >= 1.0f - 1e-4f),
            "GLOBAL: 所有图元线宽 >= 1.0（统一线宽，无 0 线宽）",
            $"min={thicknesses.Min():F2}");
        Assert(thicknesses.All(thickness => thickness <= 4.0f),
            "GLOBAL: 所有图元线宽 <= 4.0（线宽层级统一）",
            $"max={thicknesses.Max():F2}");

        var signatures = allTypes.Select(type => Signature(SymbolGeometryLibrary.Build(type))).ToList();
        Assert(signatures.Distinct().Count() == 24,
            "GLOBAL: 26 个 ElementType 中仅 Mosfet/Jfet 两个泛型复用标准型（其余两两不同）",
            $"distinct={signatures.Distinct().Count()}");
        Assert(Signature(SymbolGeometryLibrary.Build(ElementType.Mosfet))
               == Signature(SymbolGeometryLibrary.Build(ElementType.MosfetNEnh))
               && Signature(SymbolGeometryLibrary.Build(ElementType.Jfet))
               == Signature(SymbolGeometryLibrary.Build(ElementType.JfetN)),
            "GLOBAL: 泛型 Mosfet/Jfet 分别复用 N 增强型 / N 沟道标准符号");

        Assert(ReferenceEquals(SymbolGeometryLibrary.Build(ElementType.BjtNpn),
                SymbolGeometryLibrary.Build(ElementType.BjtNpn)),
            "GLOBAL: 几何库按类型缓存，重复调用返回同一只读实例");
    }

    // =====================================================================
    // 断言辅助
    // =====================================================================
    private static IEnumerable<string> ValidatePrimitive(SymbolPrimitive primitive)
    {
        if (primitive.Points == null || primitive.Points.Count == 0)
        {
            yield return "空点集";
            yield break;
        }

        if (!(primitive.Thickness > 0f) || primitive.Thickness > 4f)
        {
            yield return $"线宽异常 {primitive.Thickness}";
        }

        foreach (var point in primitive.Points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)
                || float.IsNaN(point.X) || float.IsNaN(point.Y))
            {
                yield return $"非有限坐标 ({point.X},{point.Y})";
                break;
            }
        }

        switch (primitive.Kind)
        {
            case SymbolPrimitiveKind.Polyline when primitive.Points.Count < 2:
                yield return "折线点数 < 2";
                break;
            case SymbolPrimitiveKind.FilledPolygon when primitive.Points.Count < 3:
                yield return "多边形点数 < 3";
                break;
            case SymbolPrimitiveKind.Circle:
                if (primitive.Points.Count != 2)
                {
                    yield return "圆必须由 [圆心, 半径点] 两个点描述";
                }
                else if (Vector2.Distance(primitive.Points[0], primitive.Points[1]) <= 0.1f)
                {
                    yield return "圆半径 <= 0.1";
                }
                break;
        }
    }

    private static bool HasPrimitivePoint(IReadOnlyList<SymbolPrimitive> geometry, Vector2 point, float tolerance)
    {
        foreach (var primitive in geometry)
        {
            foreach (var candidate in primitive.Points)
            {
                if (Vector2.Distance(candidate, point) <= tolerance) return true;
            }
        }
        return false;
    }

    private static List<Vector2> AllPoints(IReadOnlyList<SymbolPrimitive> geometry) =>
        geometry.SelectMany(primitive => primitive.Points).ToList();

    private static List<SymbolPrimitive> FilledPolygons(IReadOnlyList<SymbolPrimitive> geometry) =>
        geometry.Where(primitive => primitive.Kind == SymbolPrimitiveKind.FilledPolygon).ToList();

    private static List<SymbolPrimitive> Circles(IReadOnlyList<SymbolPrimitive> geometry) =>
        geometry.Where(primitive => primitive.Kind == SymbolPrimitiveKind.Circle).ToList();

    private static List<SymbolPrimitive> NonArrow(IReadOnlyList<SymbolPrimitive> geometry) =>
        geometry.Where(primitive => primitive.Kind != SymbolPrimitiveKind.FilledPolygon).ToList();

    private static List<SymbolPrimitive> TwoPointPolylines(IReadOnlyList<SymbolPrimitive> geometry) =>
        geometry.Where(primitive =>
            primitive.Kind == SymbolPrimitiveKind.Polyline && primitive.Points.Count == 2).ToList();

    private static List<SymbolPrimitive> ClosedPolylines(IReadOnlyList<SymbolPrimitive> geometry) =>
        geometry.Where(primitive =>
                primitive.Kind == SymbolPrimitiveKind.Polyline
                && primitive.Points.Count >= 4
                && Vector2.Distance(primitive.Points[0], primitive.Points[^1]) < 0.01f)
            .ToList();

    private static float LineLength(SymbolPrimitive line) =>
        Vector2.Distance(line.Points[0], line.Points[1]);

    /// <summary>几何中是否存在连接 a-b 的两点线段（端点顺序无关）。</summary>
    private static bool HasTwoPointLine(IReadOnlyList<SymbolPrimitive> geometry, Vector2 a, Vector2 b)
    {
        foreach (var line in TwoPointPolylines(geometry))
        {
            if ((Vector2.Distance(line.Points[0], a) < 0.01f && Vector2.Distance(line.Points[1], b) < 0.01f)
                || (Vector2.Distance(line.Points[0], b) < 0.01f && Vector2.Distance(line.Points[1], a) < 0.01f))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>几何中是否存在 x∈(xMin,xMax)、长度≥minLength 的竖直两点线段。</summary>
    private static bool HasVerticalSegmentInXRange(
        IReadOnlyList<SymbolPrimitive> geometry, float xMin, float xMax, float minLength)
    {
        foreach (var line in TwoPointPolylines(geometry))
        {
            if (MathF.Abs(line.Points[0].X - line.Points[1].X) > 0.01f) continue;
            float x = line.Points[0].X;
            if (x > xMin && x < xMax && LineLength(line) >= minLength) return true;
        }
        return false;
    }

    /// <summary>指定 x 处所有竖直两点线段的 y 范围（无则返回 NaN）。</summary>
    private static (float MinY, float MaxY) VerticalExtentAt(IReadOnlyList<SymbolPrimitive> geometry, float x)
    {
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        bool found = false;
        foreach (var line in TwoPointPolylines(geometry))
        {
            if (MathF.Abs(line.Points[0].X - line.Points[1].X) > 0.01f) continue;
            if (MathF.Abs(line.Points[0].X - x) > 0.01f) continue;
            found = true;
            minY = MathF.Min(minY, MathF.Min(line.Points[0].Y, line.Points[1].Y));
            maxY = MathF.Max(maxY, MathF.Max(line.Points[0].Y, line.Points[1].Y));
        }
        return found ? (minY, maxY) : (float.NaN, float.NaN);
    }

    private static (Vector2 Min, Vector2 Max) BoundsOfDistinctPoints(SymbolPrimitive primitive)
    {
        var points = DistinctPoints(primitive).ToList();
        var min = new Vector2(points.Min(point => point.X), points.Min(point => point.Y));
        var max = new Vector2(points.Max(point => point.X), points.Max(point => point.Y));
        return (min, max);
    }

    private static IEnumerable<Vector2> DistinctPoints(SymbolPrimitive primitive)
    {
        var seen = new List<Vector2>();
        foreach (var point in primitive.Points)
        {
            if (!seen.Any(existing => Vector2.Distance(existing, point) < 0.01f)) seen.Add(point);
        }
        return seen;
    }

    /// <summary>
    /// 实心三角形箭头：沿 x 取极值顶点作为尖端（N 指向沟道者为 maxX，
    /// P/PNP 等背离方向者为 minX），另外两个顶点的中点作为箭头底边中心。
    /// </summary>
    private static (Vector2 Tip, Vector2 BaseCenter) ArrowTipAndBase(SymbolPrimitive triangle, bool tipHasMaxX)
    {
        var points = triangle.Points;
        int tipIndex = 0;
        for (int i = 1; i < points.Count; i++)
        {
            bool better = tipHasMaxX
                ? points[i].X > points[tipIndex].X
                : points[i].X < points[tipIndex].X;
            if (better) tipIndex = i;
        }

        var baseCenter = Vector2.Zero;
        int baseCount = 0;
        for (int i = 0; i < points.Count; i++)
        {
            if (i == tipIndex) continue;
            baseCenter += points[i];
            baseCount++;
        }
        if (baseCount > 0) baseCenter /= baseCount;
        return (points[tipIndex], baseCenter);
    }

    /// <summary>竖直方向箭头（电源圆内的电流方向箭头）：沿 y 取极值顶点作为尖端。</summary>
    private static (Vector2 Tip, Vector2 BaseCenter) ArrowTipAndBaseVertical(
        SymbolPrimitive triangle, bool tipHasMinY)
    {
        var points = triangle.Points;
        int tipIndex = 0;
        for (int i = 1; i < points.Count; i++)
        {
            bool better = tipHasMinY
                ? points[i].Y < points[tipIndex].Y
                : points[i].Y > points[tipIndex].Y;
            if (better) tipIndex = i;
        }

        var baseCenter = Vector2.Zero;
        int baseCount = 0;
        for (int i = 0; i < points.Count; i++)
        {
            if (i == tipIndex) continue;
            baseCenter += points[i];
            baseCount++;
        }
        if (baseCount > 0) baseCenter /= baseCount;
        return (points[tipIndex], baseCenter);
    }

    /// <summary>由长竖直双点折线推导沟道 x 坐标（取最靠右者，栅极板在其左侧）。</summary>
    private static float FindChannelX(IReadOnlyList<SymbolPrimitive> geometry)
    {
        float channelX = float.NaN;
        foreach (var line in TwoPointPolylines(geometry))
        {
            if (MathF.Abs(line.Points[0].X - line.Points[1].X) > 0.01f) continue;
            if (LineLength(line) < 30f) continue;
            if (!float.IsFinite(channelX) || line.Points[0].X > channelX) channelX = line.Points[0].X;
        }
        return channelX;
    }

    /// <summary>栅极板 x 坐标：长竖直双点折线中最靠左者。</summary>
    private static float FindGatePlateX(IReadOnlyList<SymbolPrimitive> geometry)
    {
        float gatePlateX = float.NaN;
        foreach (var line in TwoPointPolylines(geometry))
        {
            if (MathF.Abs(line.Points[0].X - line.Points[1].X) > 0.01f) continue;
            if (LineLength(line) < 30f) continue;
            if (!float.IsFinite(gatePlateX) || line.Points[0].X < gatePlateX) gatePlateX = line.Points[0].X;
        }
        return gatePlateX;
    }

    private static int CountVerticalSegmentsAt(IReadOnlyList<SymbolPrimitive> geometry, float x)
    {
        int count = 0;
        foreach (var line in TwoPointPolylines(geometry))
        {
            if (MathF.Abs(line.Points[0].X - line.Points[1].X) < 0.01f
                && MathF.Abs(line.Points[0].X - x) < 0.01f)
            {
                count++;
            }
        }
        return count;
    }

    private static bool TryFindVerticalLeadFromPin(
        IReadOnlyList<SymbolPrimitive> geometry, Vector2 pin, out Vector2 innerEndpoint)
    {
        innerEndpoint = Vector2.Zero;
        foreach (var line in TwoPointPolylines(geometry))
        {
            if (MathF.Abs(line.Points[0].X - line.Points[1].X) > 0.01f) continue;
            if (Vector2.Distance(line.Points[0], pin) < 0.01f)
            {
                innerEndpoint = line.Points[1];
                return true;
            }
            if (Vector2.Distance(line.Points[1], pin) < 0.01f)
            {
                innerEndpoint = line.Points[0];
                return true;
            }
        }
        return false;
    }

    private static bool ContainsDiamond(IReadOnlyList<SymbolPrimitive> geometry)
    {
        foreach (var polyline in ClosedPolylines(geometry))
        {
            var unique = DistinctPoints(polyline).ToList();
            if (unique.Count != 4) continue;
            if (MathF.Abs(PolygonArea(unique)) < 1f) continue;
            // 菱形：4 个顶点在局部坐标中呈 (0,-d)/(d,0)/(0,d)/(-d,0) 旋转对称形态。
            bool hasTop = unique.Any(point => MathF.Abs(point.X) < 0.01f && point.Y < -8f);
            bool hasBottom = unique.Any(point => MathF.Abs(point.X) < 0.01f && point.Y > 8f);
            bool hasLeft = unique.Any(point => point.X < -8f && MathF.Abs(point.Y) < 0.01f);
            bool hasRight = unique.Any(point => point.X > 8f && MathF.Abs(point.Y) < 0.01f);
            if (hasTop && hasBottom && hasLeft && hasRight) return true;
        }
        return false;
    }

    private static float PolygonArea(IReadOnlyList<Vector2> points)
    {
        float area = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % points.Count];
            area += current.X * next.Y - next.X * current.Y;
        }
        return area * 0.5f;
    }

    /// <summary>统计受控源标记笔划数：insideDiamond=true 统计菱形内部 |x|&lt;8 的短线；
    /// false 统计控制端 x∈[-30,-18] 的短线。</summary>
    private static int CountMarkerLines(IReadOnlyList<SymbolPrimitive> geometry, bool insideDiamond)
    {
        int count = 0;
        foreach (var line in TwoPointPolylines(geometry))
        {
            if (MathF.Abs(line.Thickness - SymbolGeometryLibrary.MarkerThickness) > 0.01f) continue;
            float centerX = (line.Points[0].X + line.Points[1].X) * 0.5f;
            if (insideDiamond)
            {
                if (MathF.Abs(centerX) < 8f) count++;
            }
            else if (centerX < -18f && centerX > -30f)
            {
                count++;
            }
        }
        return count;
    }

    private static Vector2 Centroid(SymbolPrimitive primitive)
    {
        var center = Vector2.Zero;
        if (primitive.Points.Count == 0) return center;
        foreach (var point in primitive.Points) center += point;
        return center / primitive.Points.Count;
    }

    private static bool HasConsecutiveDuplicatePoints(IReadOnlyList<SymbolPrimitive> geometry)
    {
        foreach (var primitive in geometry)
        {
            for (int i = 1; i < primitive.Points.Count; i++)
            {
                if (Vector2.Distance(primitive.Points[i - 1], primitive.Points[i]) < 0.01f) return true;
            }
        }
        return false;
    }

    private static string Signature(IReadOnlyList<SymbolPrimitive> geometry)
    {
        var builder = new StringBuilder();
        foreach (var primitive in geometry)
        {
            builder.Append(primitive.Kind).Append('|')
                .Append(primitive.Thickness.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(primitive.Filled).Append(':');
            foreach (var point in primitive.Points)
            {
                builder.Append(point.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(point.Y.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static string Format(Vector2 point) =>
        $"({point.X.ToString("F2", CultureInfo.InvariantCulture)},{point.Y.ToString("F2", CultureInfo.InvariantCulture)})";
}
