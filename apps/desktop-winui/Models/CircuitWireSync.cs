// ============================================================================
// File: apps/desktop-winui/Models/CircuitWireSync.cs
// Description: 元件变换（旋转 / 水平镜像）后，导线端点与分支节点（WireTap）的
//              统一重算逻辑。
//
// 背景
//   * 画布（Controls/CircuitCanvas.xaml.cs）和 ViewModel
//     （ViewModels/AnalysisStudioViewModel.cs）原先各有一份几乎相同的
//     SyncConnectedWiresToElement 实现；镜像功能若只接入其中一处，就会出现
//     “符号翻转了、焊点/导线还停在旧引脚位置”的问题。
//   * 现在两处都调用本类：导线端点始终取自
//     CircuitElement.GetPinWorldPosition()（镜像 + 旋转后的真实引脚坐标），
//     父线上的 WireTap 分支节点再沿父线重新投影，因此焊点/节点随元件符号一起变换。
//
// 纯模型层，不依赖 WinUI / Win2D，可被控制台 harness（tests/SymbolChecks）直接验证。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CircuitAssistant.Models;

/// <summary>
/// 导线与元件引脚变换的同步辅助。所有方法只修改导线端点/节点坐标，不增删导线。
/// </summary>
public static class CircuitWireSync
{
    private const string WireTapPinName = "WireTap";

    /// <summary>
    /// 元件旋转/镜像后，重算所有直连引脚导线的端点，以及以该元件为起点或终点、
    /// 另一端通过 WireTap 挂在父导线上的分支节点；随后沿 WireTap 依赖链传播。
    /// </summary>
    public static void SyncConnectedWiresToElement(IEnumerable<CircuitWire> wires, CircuitElement element)
    {
        if (wires == null || element == null) return;

        var wireList = wires as IList<CircuitWire> ?? wires.ToList();
        var movedWireIds = new HashSet<string>(StringComparer.Ordinal);

        // Phase 1: 直连引脚的导线端点 -> 变换后的引脚世界坐标。
        foreach (var wire in wireList)
        {
            bool moved = false;
            if (wire.StartElementId == element.Id && wire.StartPinName != WireTapPinName)
            {
                var pin = FindPin(element, wire.StartPinName);
                if (pin != null)
                {
                    var position = element.GetPinWorldPosition(pin);
                    wire.StartX = position.X;
                    wire.StartY = position.Y;
                    movedWireIds.Add(wire.Id);
                    moved = true;
                }
            }
            if (wire.EndElementId == element.Id && wire.EndPinName != WireTapPinName)
            {
                var pin = FindPin(element, wire.EndPinName);
                if (pin != null)
                {
                    var position = element.GetPinWorldPosition(pin);
                    wire.EndX = position.X;
                    wire.EndY = position.Y;
                    movedWireIds.Add(wire.Id);
                    moved = true;
                }
            }

            if (!moved || wire.Waypoints.Count == 0) continue;
            if (wire.Waypoints.Count <= 2)
            {
                wire.Waypoints.Clear();
            }
            else
            {
                wire.Waypoints = wire.GetPathPoints().Skip(1).SkipLast(1).ToList();
            }
        }

        // Phase 2A: 元件侧为实引脚、另一端 WireTap 挂在父导线上的分支。
        // 父线本身不动，分支节点沿父线垂直投影，避免把母线拉歪。
        foreach (var wire in wireList)
        {
            if (wire.StartElementId == element.Id && wire.EndPinName == WireTapPinName)
            {
                var parent = FindWire(wireList, wire.EndElementId);
                var pin = FindPin(element, wire.StartPinName);
                if (parent == null || pin == null) continue;

                var position = element.GetPinWorldPosition(pin);
                wire.StartX = position.X;
                wire.StartY = position.Y;
                var tap = parent.ProjectTapPerpendicular(new Vector2(wire.StartX, wire.StartY));
                wire.EndX = tap.X;
                wire.EndY = tap.Y;
                wire.Waypoints.Clear();
                movedWireIds.Add(wire.Id);
            }
            else if (wire.EndElementId == element.Id && wire.StartPinName == WireTapPinName)
            {
                var parent = FindWire(wireList, wire.StartElementId);
                var pin = FindPin(element, wire.EndPinName);
                if (parent == null || pin == null) continue;

                var position = element.GetPinWorldPosition(pin);
                wire.EndX = position.X;
                wire.EndY = position.Y;
                var tap = parent.ProjectTapPerpendicular(new Vector2(wire.EndX, wire.EndY));
                wire.StartX = tap.X;
                wire.StartY = tap.Y;
                wire.Waypoints.Clear();
                movedWireIds.Add(wire.Id);
            }
        }

        // Phase 2B: 挂在已移动父线上的更深层分支节点（最多 3 轮，避免环）。
        bool anyUpdated;
        int remainingPasses = 3;
        do
        {
            anyUpdated = false;
            foreach (var wire in wireList)
            {
                // 纯母线 / 总线不参与子分支传播。
                if (wire.Id.EndsWith("_RAIL", StringComparison.OrdinalIgnoreCase) ||
                    wire.Id.EndsWith("_BUS", StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrEmpty(wire.StartElementId) && string.IsNullOrEmpty(wire.EndElementId)))
                {
                    continue;
                }

                if (wire.EndPinName == WireTapPinName &&
                    movedWireIds.Contains(wire.EndElementId) && !movedWireIds.Contains(wire.Id))
                {
                    var parent = FindWire(wireList, wire.EndElementId);
                    if (parent == null) continue;
                    var tap = parent.ProjectTapPerpendicular(new Vector2(wire.StartX, wire.StartY));
                    wire.EndX = tap.X;
                    wire.EndY = tap.Y;
                    movedWireIds.Add(wire.Id);
                    anyUpdated = true;
                }
                else if (wire.StartPinName == WireTapPinName &&
                         movedWireIds.Contains(wire.StartElementId) && !movedWireIds.Contains(wire.Id))
                {
                    var parent = FindWire(wireList, wire.StartElementId);
                    if (parent == null) continue;
                    var tap = parent.ProjectTapPerpendicular(new Vector2(wire.EndX, wire.EndY));
                    wire.StartX = tap.X;
                    wire.StartY = tap.Y;
                    movedWireIds.Add(wire.Id);
                    anyUpdated = true;
                }
            }
            remainingPasses--;
        } while (anyUpdated && remainingPasses > 0);
    }

    private static CircuitPin? FindPin(CircuitElement element, string pinName) =>
        element.Pins.FirstOrDefault(pin => pin.Name == pinName || pin.Id == pinName);

    private static CircuitWire? FindWire(IList<CircuitWire> wires, string wireId) =>
        wires.FirstOrDefault(wire => wire.Id == wireId);
}
