using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace CircuitAssistant.Models;

/// <summary>
/// Serializable Data Transfer Object for a complete Circuit Assistant project.
/// </summary>
public class CircuitProjectData
{
    /// <summary>
    /// Current project format. 1.2.0 adds the semiconductor material field and the
    /// optional element mirror flag (older 1.2.0 files load with MirrorX = false).
    /// </summary>
    public string Version { get; set; } = "1.2.0";
    public string ProjectName { get; set; } = "未命名电路工程";
    public string ProjectFileName { get; set; } = "Circuit.circuit";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
    public Dictionary<string, double> Parameters { get; set; } = new();
    public Dictionary<string, string> ParameterUnits { get; set; } = new();
    public bool IsTeachingTemplate { get; set; }
    public List<CircuitElementDto> Elements { get; set; } = new();
    public List<CircuitWireDto> Wires { get; set; } = new();

    public static CircuitProjectData From(
        string projectName,
        string fileName,
        IEnumerable<CircuitElement> elements,
        IEnumerable<CircuitWire> wires,
        Dictionary<string, double>? parameters = null,
        string description = "")
    {
        var data = new CircuitProjectData
        {
            Version = "1.2.0",
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? "未命名电路工程" : projectName,
            ProjectFileName = string.IsNullOrWhiteSpace(fileName) ? "Circuit.circuit" : fileName,
            Description = description,
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now,
            Parameters = parameters ?? new Dictionary<string, double>()
        };

        foreach (var el in elements)
        {
            var elDto = new CircuitElementDto
            {
                Id = el.Id,
                Name = el.Name,
                DefinitionId = el.DefinitionId,
                Category = el.Category,
                Description = el.Description,
                Value = el.Value,
                FrequencyHz = el.FrequencyHz,
                Material = el.Material,
                MosfetK = el.MosfetK,
                MosfetLambda = el.MosfetLambda,
                JfetIdss = el.JfetIdss,
                BjtRbb = el.BjtRbb,
                BjtRbe = el.BjtRbe,
                Type = el.Type,
                DisplayNumber = el.DisplayNumber,
                UnitSymbol = el.UnitSymbol,
                X = el.X,
                Y = el.Y,
                Width = el.Width,
                Height = el.Height,
                RotationDeg = el.RotationDeg,
                Orientation = el.Orientation,
                MirrorX = el.MirrorX
            };

            foreach (var pin in el.Pins)
            {
                elDto.Pins.Add(new CircuitPinDto
                {
                    Id = pin.Id,
                    Name = pin.Name,
                    RelativeX = pin.RelativeX,
                    RelativeY = pin.RelativeY,
                    ConnectedNetId = pin.ConnectedNetId
                });
            }

            data.Elements.Add(elDto);
        }

        foreach (var w in wires)
        {
            data.Wires.Add(new CircuitWireDto
            {
                Id = w.Id,
                StartElementId = w.StartElementId,
                StartPinName = w.StartPinName,
                EndElementId = w.EndElementId,
                EndPinName = w.EndPinName,
                StartX = w.StartX,
                StartY = w.StartY,
                EndX = w.EndX,
                EndY = w.EndY,
                NetId = w.NetId,
                FlipCorner = w.FlipCorner,
                CustomMid = w.CustomMid,
                Waypoints = (w.Waypoints != null && w.Waypoints.Count > 0)
                    ? w.Waypoints.Select(pt => new float[] { pt.X, pt.Y }).ToList()
                    : null
            });
        }

        return data;
    }

    public (List<CircuitElement> Elements, List<CircuitWire> Wires, Dictionary<string, double> Parameters) ToModel()
    {
        Validate();
        var elements = new List<CircuitElement>();
        var wires = new List<CircuitWire>();
        // PNP default symbol changed on 2026-10-18 (emitter now on top). Projects
        // saved before that change carry C at negative Y; those pins and their
        // attached wires are migrated to the new canonical layout after loading.
        var migratedPnpElementIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var elDto in Elements)
        {
            var el = new CircuitElement
            {
                Id = elDto.Id,
                Name = elDto.Name,
                DefinitionId = elDto.DefinitionId,
                Category = elDto.Category,
                Description = elDto.Description,
                Value = elDto.Value,
                FrequencyHz = elDto.FrequencyHz,
                Material = elDto.Material,
                MosfetK = elDto.MosfetK,
                MosfetLambda = elDto.MosfetLambda,
                JfetIdss = elDto.JfetIdss,
                BjtRbb = elDto.BjtRbb,
                BjtRbe = elDto.BjtRbe,
                Type = elDto.Type,
                DisplayNumber = elDto.DisplayNumber,
                UnitSymbol = elDto.UnitSymbol,
                X = elDto.X,
                Y = elDto.Y,
                Width = elDto.Width,
                Height = elDto.Height,
                RotationDeg = elDto.RotationDeg,
                Orientation = elDto.Orientation,
                MirrorX = elDto.MirrorX
            };

            el.Pins.Clear();
            if (elDto.Pins != null && elDto.Pins.Count > 0)
            {
                foreach (var pinDto in elDto.Pins)
                {
                    el.Pins.Add(new CircuitPin
                    {
                        Id = pinDto.Id,
                        Name = pinDto.Name,
                        RelativeX = pinDto.RelativeX,
                        RelativeY = pinDto.RelativeY,
                        ConnectedNetId = pinDto.ConnectedNetId
                    });
                }
            }
            else
            {
                el.InitializeDefaultPins();
            }

            if (el.Type == ElementType.BjtPnp)
            {
                var collector = el.Pins.FirstOrDefault(pin => pin.Id == "C" || pin.Name == "C");
                // Legacy files either store the old C(-35)/E(+35) layout or no pins
                // at all (very old 1.0.0/1.1.0). Both are re-canonicalized and their
                // wires/nodes are re-attached below.
                bool noStoredPins = elDto.Pins == null || elDto.Pins.Count == 0;
                bool legacyLayout = noStoredPins || (collector != null && collector.RelativeY < 0f);
                if (legacyLayout)
                {
                    el.ApplyDefaultPinPositions();
                    migratedPnpElementIds.Add(el.Id);
                }
            }

            elements.Add(el);
        }

        var pinMaps = elements.ToDictionary(el => el.Id, GetPinMap, StringComparer.Ordinal);
        string NormalizePin(string? elementId, string? pin) =>
            elementId != null && pin != null && pinMaps.TryGetValue(elementId, out var map) && map.TryGetValue(pin, out var canonical)
                ? canonical : pin ?? "";
        foreach (var wDto in Wires)
        {
            wires.Add(new CircuitWire
            {
                Id = wDto.Id,
                StartElementId = wDto.StartElementId ?? "",
                StartPinName = NormalizePin(wDto.StartElementId, wDto.StartPinName),
                EndElementId = wDto.EndElementId ?? "",
                EndPinName = NormalizePin(wDto.EndElementId, wDto.EndPinName),
                StartX = wDto.StartX,
                StartY = wDto.StartY,
                EndX = wDto.EndX,
                EndY = wDto.EndY,
                NetId = wDto.NetId,
                FlipCorner = wDto.FlipCorner,
                CustomMid = wDto.CustomMid,
                Waypoints = wDto.Waypoints != null
                    ? wDto.Waypoints.Select(arr => new System.Numerics.Vector2(arr[0], arr[1])).ToList()
                    : new System.Collections.Generic.List<System.Numerics.Vector2>()
            });
        }

        // Re-attach wires/nodes whose PNP pins were migrated from the legacy layout.
        if (migratedPnpElementIds.Count > 0)
        {
            foreach (var element in elements.Where(el => migratedPnpElementIds.Contains(el.Id)))
            {
                CircuitWireSync.SyncConnectedWiresToElement(wires, element);
            }
        }

        return (elements, wires, Parameters ?? new Dictionary<string, double>());
    }

    public void Validate()
    {
        if (Version is not ("1.0.0" or "1.1.0" or "1.2.0"))
            throw new InvalidDataException($"不支持工程版本 {Version}，请使用兼容版本的软件打开。");
        if (Elements == null || Wires == null || Parameters == null || ParameterUnits == null)
            throw new InvalidDataException("工程缺少元件、导线或参数集合。");
        if (string.IsNullOrWhiteSpace(ProjectName) || CreatedAt == default || ModifiedAt == default)
            throw new InvalidDataException("工程名称或时间信息无效。");
        var pinIds = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var el in Elements)
        {
            if (el == null || string.IsNullOrWhiteSpace(el.Id) || pinIds.ContainsKey(el.Id) || !Enum.IsDefined(el.Type) || !Enum.IsDefined(el.Material))
                throw new InvalidDataException("工程包含空元件、重复 ID 或未知元件类型/材料。");
            if (el.Id.Contains('.') || el.Id.Contains(':'))
                throw new InvalidDataException($"元件 ID {el.Id} 包含保留分隔符。请使用字母、数字或下划线编号。");
            if (!double.IsFinite(el.DisplayNumber) || !double.IsFinite(el.Value) ||
                !double.IsFinite(el.FrequencyHz) || el.FrequencyHz <= 0 ||
                !float.IsFinite(el.X) || !float.IsFinite(el.Y) ||
                !float.IsFinite(el.Width) || !float.IsFinite(el.Height) || el.Width <= 0 || el.Height <= 0)
                throw new InvalidDataException($"元件 {el.Id} 的数值、频率或几何数据无效。");
            if (el.Type is ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetPEnh
                or ElementType.MosfetNDep or ElementType.MosfetPDep
                or ElementType.Jfet or ElementType.JfetN or ElementType.JfetP)
            {
                if (el.Type is not (ElementType.Jfet or ElementType.JfetN or ElementType.JfetP)
                    && (!double.IsFinite(el.MosfetK) || el.MosfetK <= 0))
                    throw new InvalidDataException($"MOS 元件 {el.Id} 的跨导参数 K 必须为有限正数（A/V²）。");
                if (!double.IsFinite(el.MosfetLambda) || el.MosfetLambda < 0)
                    throw new InvalidDataException($"MOS 元件 {el.Id} 的沟道长度调制系数 λ 必须为有限非负数（1/V）。");
            }
            if (el.Type is ElementType.Jfet or ElementType.JfetN or ElementType.JfetP)
            {
                if (!double.IsFinite(el.JfetIdss) || el.JfetIdss < 0)
                    throw new InvalidDataException($"JFET 元件 {el.Id} 的 IDSS 必须为有限非负数（A，0=尚未填写）。");
            }
            if (el.Type is ElementType.BjtNpn or ElementType.BjtPnp)
            {
                // r_bb' / r_be(h_ie) are optional教学参数: 0 = use the built-in default
                // (r_bb' auto) or compute r_be from the Q point (r_be auto).
                if (!double.IsFinite(el.BjtRbb) || el.BjtRbb < 0)
                    throw new InvalidDataException($"BJT 元件 {el.Id} 的基区体电阻 r_bb' 必须为有限非负数（Ω，0=自动）。");
                if (!double.IsFinite(el.BjtRbe) || el.BjtRbe < 0)
                    throw new InvalidDataException($"BJT 元件 {el.Id} 的 r_be/h_ie 必须为有限非负数（Ω，0=按 Q 点计算）。");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (el.Pins == null) throw new InvalidDataException($"元件 {el.Id} 的引脚集合无效。");
            foreach (var pin in el.Pins)
            {
                if (pin == null || string.IsNullOrWhiteSpace(pin.Id) || !ids.Add(pin.Id) ||
                    !float.IsFinite(pin.RelativeX) || !float.IsFinite(pin.RelativeY))
                    throw new InvalidDataException($"元件 {el.Id} 包含无效或重复引脚。");
            }
            var model = new CircuitElement { Type = el.Type };
            if (el.Pins.Count == 0) model.InitializeDefaultPins();
            else foreach (var pin in el.Pins)
                model.Pins.Add(new CircuitPin { Id = pin.Id, Name = pin.Name });
            pinIds.Add(el.Id, GetPinMap(model));
        }
        var wireIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in Wires)
        {
            if (w == null || string.IsNullOrWhiteSpace(w.Id) || !wireIds.Add(w.Id) || pinIds.ContainsKey(w.Id) ||
                !float.IsFinite(w.StartX) || !float.IsFinite(w.StartY) ||
                !float.IsFinite(w.EndX) || !float.IsFinite(w.EndY) || !float.IsFinite(w.CustomMid) ||
                (w.Waypoints != null && w.Waypoints.Any(pt => pt == null || pt.Length != 2 || pt.Any(v => !float.IsFinite(v)))))
                throw new InvalidDataException("工程包含无效导线、重复 ID 或非法路径坐标。");
        }
        foreach (var w in Wires)
        {
            CheckEndpoint(w, w.StartElementId, w.StartPinName);
            CheckEndpoint(w, w.EndElementId, w.EndPinName);
        }
        if (Parameters.Any(kv => !double.IsFinite(kv.Value)) || ParameterUnits.Any(kv => kv.Value == null))
            throw new InvalidDataException("工程参数或单位无效。");

        void CheckEndpoint(CircuitWireDto wire, string? id, string? pin)
        {
            bool free = string.IsNullOrEmpty(id) || id.Equals("None", StringComparison.OrdinalIgnoreCase);
            if (pin == "WireTap")
            {
                if (free || !wireIds.Contains(id!) || id == wire.Id)
                    throw new InvalidDataException($"导线 {wire.Id} 引用了无效父导线。");
            }
            else if (!free && (!pinIds.TryGetValue(id!, out var pins) || pin == null || !pins.ContainsKey(pin)))
                throw new InvalidDataException($"导线 {wire.Id} 引用了不存在的元件引脚。");
        }
    }

    private static Dictionary<string, string> GetPinMap(CircuitElement element)
    {
        var map = element.Pins.ToDictionary(pin => pin.Id, pin => pin.Id, StringComparer.Ordinal);
        foreach (var pin in element.Pins)
            if (!string.IsNullOrWhiteSpace(pin.Name)) map.TryAdd(pin.Name, pin.Id);
        // Older projects bind polarized capacitors using the ordinary capacitor pin names.
        if (element.Type == ElementType.CapacitorPolarized)
        {
            if (map.ContainsKey("+")) { map.TryAdd("1", "+"); map.TryAdd("pos", "+"); }
            if (map.ContainsKey("-")) { map.TryAdd("2", "-"); map.TryAdd("neg", "-"); }
        }
        return map;
    }
}

public class CircuitElementDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public double Value { get; set; }
    public double FrequencyHz { get; set; } = 1000.0;
    /// <summary>Semiconductor material for diodes/BJTs (silicon/germanium).</summary>
    public SemiconductorMaterial Material { get; set; } = SemiconductorMaterial.Silicon;
    /// <summary>
    /// MOSFET square-law parameter K in A/V² (½·μn·Cox·W/L). Optional in older
    /// files; the default 1 mA/V² keeps them loadable.
    /// </summary>
    public double MosfetK { get; set; } = 1e-3;
    /// <summary>MOSFET channel-length modulation λ in 1/V (0 = ignored).</summary>
    public double MosfetLambda { get; set; }
    public double JfetIdss { get; set; }
    /// <summary>
    /// BJT base spreading resistance r_bb' in Ω (optional; 0 = built-in teaching
    /// default 200 Ω for CE/CC and 50 Ω for common base). Older files omit it.
    /// </summary>
    public double BjtRbb { get; set; }
    /// <summary>
    /// BJT directly-given r_be / h_ie in Ω (optional; 0 = compute from the Q point
    /// with r_be = r_bb' + (1+β)·U_T/I_EQ). Older files omit it.
    /// </summary>
    public double BjtRbe { get; set; }
    public ElementType Type { get; set; }
    public double DisplayNumber { get; set; }
    public string UnitSymbol { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 80;
    public float Height { get; set; } = 50;
    public int RotationDeg { get; set; }
    public string Orientation { get; set; } = "Horizontal";
    /// <summary>Horizontal mirror flag (ALT+X). Absent in older files -> false.</summary>
    public bool MirrorX { get; set; }
    public List<CircuitPinDto> Pins { get; set; } = new();
}

public class CircuitPinDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float RelativeX { get; set; }
    public float RelativeY { get; set; }
    public int ConnectedNetId { get; set; } = -1;
}

public class CircuitWireDto
{
    public string Id { get; set; } = "";
    public string StartElementId { get; set; } = "";
    public string StartPinName { get; set; } = "";
    public string EndElementId { get; set; } = "";
    public string EndPinName { get; set; } = "";
    public float StartX { get; set; }
    public float StartY { get; set; }
    public float EndX { get; set; }
    public float EndY { get; set; }
    public int NetId { get; set; }
    public bool FlipCorner { get; set; }
    public float CustomMid { get; set; }
    public System.Collections.Generic.List<float[]>? Waypoints { get; set; }
}
