// ============================================================================
// File: tools/CircuitModels.Core/SchematicBuilder.cs
// Description: Turns a declarative CircuitSpec into a native CircuitProjectData
//              (Elements + Wires) and validates the resulting schematic against
//              the production NetlistExtractor so a generated file can never
//              silently change the intended net partition.
//
//              Routing rules (contract §8 contract):
//                * rail nets (VCC/GND) use one trunk wire whose id contains
//                  "_RAIL"; branch wires attach with a WireTap reference;
//                * free nets with 2 pins use one orthogonal wire; nets with 3+
//                  pins get an automatic trunk (id contains "_BUS") along the
//                  dominant axis with perpendicular tap wires;
//                * pins lying exactly on a rail/bus connect through
//                  NetlistExtractor's "pin resting on rail/bus segment" rule, so
//                  no zero-length wires are emitted;
//                * Ground/VccPort single-pin symbols are snapped onto their rail.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace CircuitModels;

public static class SchematicBuilder
{
    private const float Tolerance = 0.5f;

    // ------------------------------------------------------------------
    // Public API (contract §8.2)
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the project data for one circuit and immediately runs the structural
    /// self-check (Validate -> ToModel -> NetlistExtractor -> partition compare).
    /// </summary>
    public static CircuitProjectData Build(CircuitSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ValidateSpec(spec);

        List<CircuitElement> elements = spec.Elements.Select(CreateElement).ToList();
        Dictionary<string, CircuitElement> elementById = elements.ToDictionary(e => e.Id, StringComparer.Ordinal);
        AutoPlaceRailSymbols(spec, elementById);

        Dictionary<string, int> netIds = AssignNetIds(spec);
        var wires = new List<CircuitWire>();
        foreach (NetSpec net in spec.Nets)
        {
            BuildNet(spec, net, netIds[net.Name], elementById, wires);
        }

        CircuitProjectData data = CircuitProjectData.From(
            spec.Title,
            spec.FileName,
            elements,
            wires,
            parameters: null,
            description: spec.Description);
        data.Version = "1.2.0";
        data.IsTeachingTemplate = true;

        VerifyStructure(spec, data);
        return data;
    }

    /// <summary>Builds and writes the circuit as an indented native .circuit project file.</summary>
    public static void Write(CircuitProjectData data, string outputDirectory) =>
        CircuitFileWriter.Write(data, outputDirectory);

    /// <summary>Builds, validates and writes every specification, in order.</summary>
    public static void Generate(IEnumerable<CircuitSpec> specs, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(specs);
        foreach (CircuitSpec spec in specs)
        {
            CircuitProjectData data = Build(spec);
            Write(data, outputDirectory);
        }
    }

    // ------------------------------------------------------------------
    // Element creation
    // ------------------------------------------------------------------

    private static CircuitElement CreateElement(ElementSpec source)
    {
        (string defaultDefinitionId, string defaultCategory) = DefaultMeta(source.Type);
        var element = new CircuitElement
        {
            Id = source.Id,
            Name = string.IsNullOrWhiteSpace(source.Name) ? source.Id : source.Name,
            DefinitionId = string.IsNullOrWhiteSpace(source.DefinitionId) ? defaultDefinitionId : source.DefinitionId,
            Category = string.IsNullOrWhiteSpace(source.Category) ? defaultCategory : source.Category,
            Description = source.Description,
            Type = source.Type,
            DisplayNumber = source.Number,
            UnitSymbol = source.Unit,
            FrequencyHz = source.FrequencyHz > 0 ? source.FrequencyHz : 1000.0,
            Material = source.Material,
            MosfetK = source.MosfetK > 0 ? source.MosfetK : 1e-3,
            MosfetLambda = source.MosfetLambda >= 0 ? source.MosfetLambda : 0,
            X = source.X,
            Y = source.Y,
            Width = source.Width > 0 ? source.Width : 80,
            Height = source.Height > 0 ? source.Height : 50,
            RotationDeg = source.RotationDeg,
            Orientation = source.RotationDeg % 180 == 90 ? "Vertical" : "Horizontal",
            MirrorX = source.MirrorX
        };
        element.InitializeDefaultPins();
        return element;
    }

    private static (string DefinitionId, string Category) DefaultMeta(ElementType type) => type switch
    {
        ElementType.Resistor => ("passive.resistor", "无源器件"),
        ElementType.Capacitor => ("passive.capacitor", "无源器件"),
        ElementType.CapacitorPolarized => ("passive.capacitor_polarized", "无源器件"),
        ElementType.Inductor => ("passive.inductor", "无源器件"),
        ElementType.Transformer => ("passive.transformer", "无源器件"),
        ElementType.DcVoltageSource => ("source.v_dc", "激励电源"),
        ElementType.AcVoltageSource => ("source.v_ac_small_signal", "激励电源"),
        ElementType.DcCurrentSource => ("source.i_dc", "激励电源"),
        ElementType.AcCurrentSource => ("source.i_ac_small_signal", "激励电源"),
        ElementType.Diode => ("diode.standard", "半导体管"),
        ElementType.BjtNpn => ("bjt.npn", "半导体管"),
        ElementType.BjtPnp => ("bjt.pnp", "半导体管"),
        ElementType.Mosfet or ElementType.MosfetNEnh => ("fet.mosfet_n_enh", "增强型场效应管"),
        ElementType.MosfetPEnh => ("fet.mosfet_p_enh", "增强型场效应管"),
        ElementType.MosfetNDep => ("fet.mosfet_n_dep", "耗尽型场效应管"),
        ElementType.MosfetPDep => ("fet.mosfet_p_dep", "耗尽型场效应管"),
        ElementType.Jfet or ElementType.JfetN => ("fet.jfet_n", "结型场效应管"),
        ElementType.JfetP => ("fet.jfet_p", "结型场效应管"),
        ElementType.Vcvs => ("controlled.vcvs", "受控源"),
        ElementType.Vccs => ("controlled.vccs", "受控源"),
        ElementType.Ccvs => ("controlled.ccvs", "受控源"),
        ElementType.Cccs => ("controlled.cccs", "受控源"),
        ElementType.Ground => ("reference.ground", "参考地"),
        ElementType.VccPort => ("reference.vcc_port", "参考地"),
        _ => ("", "元件")
    };

    /// <summary>
    /// Single-pin rail symbols (GND / VCC port) are placed so their pin lands
    /// exactly on the rail segment; the canvas then draws the standard symbol
    /// directly on the rail without stub wires.
    /// </summary>
    private static void AutoPlaceRailSymbols(CircuitSpec spec, Dictionary<string, CircuitElement> elementById)
    {
        foreach (ElementSpec source in spec.Elements)
        {
            if (source.Type is not (ElementType.Ground or ElementType.VccPort)) continue;
            string pinId = source.Type == ElementType.Ground ? "GND" : "VCC";
            string pinRef = $"{source.Id}.{pinId}";
            NetSpec? net = spec.Nets.FirstOrDefault(n => n.Pins.Contains(pinRef, StringComparer.Ordinal));
            if (net == null || net.Rail == NetRail.None) continue;

            CircuitElement element = elementById[source.Id];
            bool horizontal = net.Rail is NetRail.HorizontalTop or NetRail.HorizontalBottom;
            if (horizontal)
            {
                element.Y = source.Type == ElementType.Ground
                    ? net.RailCoordinate + 22f     // Ground pin local offset is (0, -22)
                    : net.RailCoordinate - 22f;    // VCC port pin local offset is (0, +22)
            }
            else
            {
                element.X = net.RailCoordinate;    // both single pins have local X = 0
            }
        }
    }

    // ------------------------------------------------------------------
    // Net routing
    // ------------------------------------------------------------------

    private static void BuildNet(
        CircuitSpec spec,
        NetSpec net,
        int netId,
        Dictionary<string, CircuitElement> elementById,
        List<CircuitWire> wires)
    {
        var pinPositions = new List<(string ElementId, string PinId, Vector2 Position)>();
        foreach (string pinRef in net.Pins)
        {
            (string elementId, string pinId) = ParsePinRef(pinRef, spec.Id);
            if (!elementById.TryGetValue(elementId, out CircuitElement? element))
                throw new InvalidOperationException($"[{spec.Id}] 网 '{net.Name}' 引用了不存在的元件 '{elementId}'。");
            CircuitPin? pin = element.Pins.FirstOrDefault(p => p.Id == pinId || p.Name == pinId);
            if (pin == null)
                throw new InvalidOperationException($"[{spec.Id}] 元件 '{elementId}' 没有引脚 '{pinId}'。");
            pinPositions.Add((elementId, pin.Id, element.GetPinWorldPosition(pin)));
        }
        if (pinPositions.Count < 2)
            throw new InvalidOperationException($"[{spec.Id}] 网 '{net.Name}' 至少需要 2 个引脚，实际 {pinPositions.Count}。");

        string safeNetName = Sanitize(net.Name);
        if (net.Rail is NetRail.None)
        {
            BuildFreeNet(spec, net, safeNetName, netId, pinPositions, wires);
            return;
        }

        bool horizontalRail = net.Rail is NetRail.HorizontalTop or NetRail.HorizontalBottom;
        string trunkId = $"W_{safeNetName}_RAIL";
        if (horizontalRail)
        {
            float railY = net.RailCoordinate;
            float minX = pinPositions.Min(p => p.Position.X) - net.Margin;
            float maxX = pinPositions.Max(p => p.Position.X) + net.Margin;
            wires.Add(NewWire(trunkId, "", "", minX, railY, "", "", maxX, railY, netId));
            foreach ((string elementId, string pinId, Vector2 position) in pinPositions)
            {
                if (MathF.Abs(position.Y - railY) <= Tolerance) continue; // pin rests on the rail
                wires.Add(NewWire(
                    $"W_{safeNetName}_{Sanitize(elementId)}_{Sanitize(pinId)}",
                    elementId, pinId, position.X, position.Y,
                    trunkId, "WireTap", position.X, railY, netId));
            }
        }
        else
        {
            float railX = net.RailCoordinate;
            float minY = pinPositions.Min(p => p.Position.Y) - net.Margin;
            float maxY = pinPositions.Max(p => p.Position.Y) + net.Margin;
            wires.Add(NewWire(trunkId, "", "", railX, minY, "", "", railX, maxY, netId));
            foreach ((string elementId, string pinId, Vector2 position) in pinPositions)
            {
                if (MathF.Abs(position.X - railX) <= Tolerance) continue;
                wires.Add(NewWire(
                    $"W_{safeNetName}_{Sanitize(elementId)}_{Sanitize(pinId)}",
                    elementId, pinId, position.X, position.Y,
                    trunkId, "WireTap", railX, position.Y, netId));
            }
        }
    }

    private static void BuildFreeNet(
        CircuitSpec spec,
        NetSpec net,
        string safeNetName,
        int netId,
        List<(string ElementId, string PinId, Vector2 Position)> pins,
        List<CircuitWire> wires)
    {
        if (pins.Count == 2)
        {
            (string elementIdA, string pinIdA, Vector2 a) = pins[0];
            (string elementIdB, string pinIdB, Vector2 b) = pins[1];
            if (Vector2.Distance(a, b) < Tolerance)
                throw new InvalidOperationException($"[{spec.Id}] 网 '{net.Name}' 的两个引脚坐标重合。");
            wires.Add(NewWire($"W_{safeNetName}_{Sanitize(elementIdA)}_{Sanitize(pinIdA)}_{Sanitize(elementIdB)}",
                elementIdA, pinIdA, a.X, a.Y, elementIdB, pinIdB, b.X, b.Y, netId));
            return;
        }

        float spreadX = pins.Max(p => p.Position.X) - pins.Min(p => p.Position.X);
        float spreadY = pins.Max(p => p.Position.Y) - pins.Min(p => p.Position.Y);
        string trunkId = $"W_{safeNetName}_BUS";

        if (spreadX <= Tolerance || spreadY <= Tolerance || spreadY >= spreadX)
        {
            // Vertical trunk at the median X; pins project horizontally.
            float trunkX = Median(pins.Select(p => p.Position.X).ToList());
            float minY = pins.Min(p => p.Position.Y);
            float maxY = pins.Max(p => p.Position.Y);
            (string startElement, string startPin) = FindPinAt(pins, new Vector2(trunkX, minY));
            (string endElement, string endPin) = FindPinAt(pins, new Vector2(trunkX, maxY));
            wires.Add(NewWire(trunkId, startElement, startPin, trunkX, minY, endElement, endPin, trunkX, maxY, netId));
            foreach ((string elementId, string pinId, Vector2 position) in pins)
            {
                if (MathF.Abs(position.X - trunkX) <= Tolerance) continue;
                wires.Add(NewWire(
                    $"W_{safeNetName}_{Sanitize(elementId)}_{Sanitize(pinId)}",
                    elementId, pinId, position.X, position.Y,
                    trunkId, "WireTap", trunkX, position.Y, netId));
            }
        }
        else
        {
            // Horizontal trunk at the median Y; pins project vertically.
            float trunkY = Median(pins.Select(p => p.Position.Y).ToList());
            float minX = pins.Min(p => p.Position.X);
            float maxX = pins.Max(p => p.Position.X);
            (string startElement, string startPin) = FindPinAt(pins, new Vector2(minX, trunkY));
            (string endElement, string endPin) = FindPinAt(pins, new Vector2(maxX, trunkY));
            wires.Add(NewWire(trunkId, startElement, startPin, minX, trunkY, endElement, endPin, maxX, trunkY, netId));
            foreach ((string elementId, string pinId, Vector2 position) in pins)
            {
                if (MathF.Abs(position.Y - trunkY) <= Tolerance) continue;
                wires.Add(NewWire(
                    $"W_{safeNetName}_{Sanitize(elementId)}_{Sanitize(pinId)}",
                    elementId, pinId, position.X, position.Y,
                    trunkId, "WireTap", position.X, trunkY, netId));
            }
        }
    }

    private static CircuitWire NewWire(
        string id,
        string startElementId,
        string startPinName,
        float startX,
        float startY,
        string endElementId,
        string endPinName,
        float endX,
        float endY,
        int netId) => new()
    {
        Id = id,
        StartElementId = startElementId,
        StartPinName = startPinName,
        StartX = startX,
        StartY = startY,
        EndElementId = endElementId,
        EndPinName = endPinName,
        EndX = endX,
        EndY = endY,
        NetId = netId,
        FlipCorner = false,
        CustomMid = 0,
        Waypoints = new List<Vector2>()
    };

    private static float Median(List<float> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2f;
    }

    /// <summary>
    /// Returns the component pin that sits exactly on <paramref name="position"/>
    /// (used to bind trunk endpoints to pins). Binding a trunk end to a component
    /// pin is important: an endpoint bound to a component pin is not allowed to
    /// geometrically tap another wire, which prevents accidental shorts when a
    /// nearby parallel drop wire runs within the tap tolerance.
    /// </summary>
    private static (string ElementId, string PinId) FindPinAt(
        List<(string ElementId, string PinId, Vector2 Position)> pins,
        Vector2 position)
    {
        foreach ((string elementId, string pinId, Vector2 candidate) in pins)
        {
            if (Vector2.Distance(candidate, position) <= Tolerance) return (elementId, pinId);
        }
        return (string.Empty, string.Empty);
    }

    // ------------------------------------------------------------------
    // Structural verification
    // ------------------------------------------------------------------

    private static void VerifyStructure(CircuitSpec spec, CircuitProjectData data)
    {
        data.Validate();
        (List<CircuitElement> elements, List<CircuitWire> wires, _) = data.ToModel();
        ElectricalNetlist netlist = NetlistExtractor.Extract(elements, wires);

        NetlistValidationResult validation = netlist.Validate();
        if (!validation.IsValid)
        {
            string issues = string.Join("; ", validation.Issues
                .Where(i => i.Severity == NetlistValidationSeverity.Error)
                .Select(i => i.Message));
            string pinMap = string.Join("; ", elements.SelectMany(el => el.Pins.Select(pin =>
                $"{el.Id}.{pin.Id}->{netlist.FindNetForPin(el.Id, pin.Id)?.Name ?? "?"}")));
            throw new InvalidOperationException($"[{spec.Id}] 网表结构自检失败：{issues}\n引脚映射：{pinMap}");
        }

        var declared = new Dictionary<string, int>(StringComparer.Ordinal);
        var pinOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (NetSpec net in spec.Nets)
        {
            var extractedIds = new List<int>();
            foreach (string pinRef in net.Pins)
            {
                (string elementId, string pinId) = ParsePinRef(pinRef, spec.Id);
                ElectricalNet? extracted = netlist.FindNetForPin(elementId, pinId);
                if (extracted == null)
                    throw new InvalidOperationException($"[{spec.Id}] 网 '{net.Name}' 的引脚 {pinRef} 未提取到电气网。");
                string pinKey = $"{elementId}.{pinId}";
                if (pinOwner.ContainsKey(pinKey))
                    throw new InvalidOperationException($"[{spec.Id}] 引脚 {pinKey} 被多个声明网引用。");
                pinOwner[pinKey] = net.Name;
                extractedIds.Add(extracted.NetId);
            }
            if (extractedIds.Distinct().Count() != 1)
            {
                throw new InvalidOperationException(
                    $"[{spec.Id}] 声明的网 '{net.Name}' 在提取网表中分裂为多个网（{string.Join(",", extractedIds.Distinct())}）。");
            }
            declared[net.Name] = extractedIds[0];
        }

        if (declared.Values.Distinct().Count() != declared.Count)
        {
            throw new InvalidOperationException($"[{spec.Id}] 不同声明网在提取网表中被短接：{FormatNetMap(declared)}");
        }
        if (!declared.TryGetValue("GND", out int groundId))
            throw new InvalidOperationException($"[{spec.Id}] 缺少名为 GND 的参考地网。");
        ElectricalNet? groundNet = netlist.Nets.FirstOrDefault(n => n.NetId == groundId);
        if (groundNet == null || !groundNet.IsGround)
            throw new InvalidOperationException($"[{spec.Id}] 声明网 GND 未提取为参考地网。");

        foreach (CircuitElement element in elements)
        {
            foreach (CircuitPin pin in element.Pins)
            {
                string pinKey = $"{element.Id}.{pin.Id}";
                if (!pinOwner.ContainsKey(pinKey))
                    throw new InvalidOperationException($"[{spec.Id}] 元件引脚 {pinKey} 未出现在任何声明网中。");
            }
        }

        if (netlist.Nets.Count(n => n.ConnectedPins.Count > 0) != declared.Count)
        {
            throw new InvalidOperationException(
                $"[{spec.Id}] 声明网数量 {declared.Count} 与提取网数量 {netlist.Nets.Count} 不一致：{FormatNetMap(declared)}");
        }
    }

    private static string FormatNetMap(Dictionary<string, int> declared) =>
        string.Join(", ", declared.Select(kv => $"{kv.Key}->N{kv.Value}"));

    // ------------------------------------------------------------------
    // Spec validation helpers
    // ------------------------------------------------------------------

    private static void ValidateSpec(CircuitSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Id)) throw new ArgumentException("CircuitSpec.Id 不能为空。");
        if (string.IsNullOrWhiteSpace(spec.Title)) throw new ArgumentException($"[{spec.Id}] CircuitSpec.Title 不能为空。");
        if (string.IsNullOrWhiteSpace(spec.FileName) || !spec.FileName.EndsWith(".circuit", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"[{spec.Id}] CircuitSpec.FileName 必须为 .circuit 文件。");
        if (spec.Elements.Count == 0) throw new ArgumentException($"[{spec.Id}] 电路没有元件。");
        if (spec.Nets.Count == 0) throw new ArgumentException($"[{spec.Id}] 电路没有声明网。");

        var elementIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ElementSpec element in spec.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id)) throw new ArgumentException($"[{spec.Id}] 存在空元件 Id。");
            if (element.Id.Contains('.') || element.Id.Contains(':'))
                throw new ArgumentException($"[{spec.Id}] 元件 Id '{element.Id}' 含保留分隔符。");
            if (!elementIds.Add(element.Id)) throw new ArgumentException($"[{spec.Id}] 元件 Id '{element.Id}' 重复。");
        }
        var netNames = new HashSet<string>(StringComparer.Ordinal);
        var pinKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (NetSpec net in spec.Nets)
        {
            if (string.IsNullOrWhiteSpace(net.Name)) throw new ArgumentException($"[{spec.Id}] 存在空网名。");
            if (!netNames.Add(net.Name)) throw new ArgumentException($"[{spec.Id}] 网名 '{net.Name}' 重复。");
            if (net.Pins.Length < 2) throw new ArgumentException($"[{spec.Id}] 网 '{net.Name}' 少于 2 个引脚。");
            foreach (string pinRef in net.Pins)
            {
                (string elementId, _) = ParsePinRef(pinRef, spec.Id);
                if (!elementIds.Contains(elementId))
                    throw new ArgumentException($"[{spec.Id}] 网 '{net.Name}' 引用了不存在的元件 '{elementId}'。");
                if (!pinKeys.Add(pinRef))
                    throw new ArgumentException($"[{spec.Id}] 引脚 '{pinRef}' 被多个网引用。");
            }
        }
        if (!netNames.Contains("GND")) throw new ArgumentException($"[{spec.Id}] 缺少名为 GND 的参考地网。");
    }

    private static (string ElementId, string PinId) ParsePinRef(string pinRef, string circuitId)
    {
        int separator = pinRef.IndexOf('.');
        if (separator <= 0 || separator == pinRef.Length - 1)
            throw new InvalidOperationException($"[{circuitId}] 引脚引用 '{pinRef}' 应形如 'ElementId.PinId'。");
        return (pinRef[..separator], pinRef[(separator + 1)..]);
    }

    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        for (int i = 0; i < value.Length; i++)
            buffer[i] = char.IsLetterOrDigit(value[i]) ? value[i] : '_';
        return new string(buffer);
    }

    private static Dictionary<string, int> AssignNetIds(CircuitSpec spec)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int next = 1;
        foreach (NetSpec net in spec.Nets)
            ids[net.Name] = net.Name.Equals("GND", StringComparison.OrdinalIgnoreCase) ? 0 : next++;
        return ids;
    }
}
