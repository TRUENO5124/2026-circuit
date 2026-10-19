// ============================================================================
// File: tests/NativeBenchmark30/CircuitGraphExporter.cs
// Description: Supplemental evidence for the native 30-question benchmark:
//              serializes a benchmark fixture (in-memory ElectricalNetlist built by
//              NetlistBuilder) into a Web/Python CircuitGraph JSON document that
//              matches the shape of benchmarks/examples/*.json. The JSON is then
//              re-imported through the production Services/WebCircuitGraphImporter
//              so RoundtripChecks can prove the fixture circuit can be transported
//              as an importable project without changing its electrical topology or
//              numerical results.
//
//              Mapping follows WebCircuitGraphImporter exactly:
//                passive.resistor / passive.capacitor / passive.inductor
//                source.v_dc / source.i_dc / source.v_ac_small_signal / source.i_ac_small_signal
//                bjt.npn
//                controlled.vcvs / controlled.vccs / controlled.ccvs / controlled.cccs
//                reference.ground
//              Port ids: passive p/n; sources p/n; BJT collector/base/emitter;
//              VCVS/VCCS cp/cn (control) + p/n (output); CCVS/CCCS sp/sn (sense) + p/n;
//              ground ref.
// ============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace NativeBenchmark30;

internal static class CircuitGraphExporter
{
    // Readable UTF-8 artifacts (Chinese labels/notes are not escaped to \uXXXX) so the
    // generated CircuitGraph JSON can be reviewed and imported like the hand-written
    // benchmarks/examples documents.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Serializes one fixture netlist into a benchmark-wrapper CircuitGraph JSON document.</summary>
    public static string ExportJson(ElectricalNetlist netlist, string problemId, string title)
    {
        string nodePrefix = "node_";
        var root = new JsonObject
        {
            ["id"] = "bench.native30." + problemId,
            ["title"] = "原生 30 题教材 Benchmark — " + problemId + " " + title,
            ["source"] = new JsonObject
            {
                ["kind"] = "synthetic",
                ["note"] = "CircuitAssistant NativeBenchmark30 fixture（由 tests/NativeBenchmark30/Problems.cs 的手算电路定义导出，用于 CircuitGraph 导入 round-trip 验证）"
            },
            ["problemText"] = "见 tests/NativeBenchmark30/Problems.cs 中 " + problemId + " 的教材推导注释。",
            ["imageRefs"] = new JsonArray(),
            ["circuitGraph"] = BuildGraph(netlist, problemId, title, nodePrefix),
            ["groundTruth"] = BuildGroundTruth(netlist, nodePrefix)
        };
        return root.ToJsonString(WriteOptions);
    }

    private static JsonObject BuildGraph(ElectricalNetlist netlist, string problemId, string title, string nodePrefix)
    {
        var nodes = new JsonArray();
        var ports = new JsonArray();
        foreach (ElectricalNet net in netlist.Nets.OrderBy(n => n.NetId))
        {
            var node = new JsonObject
            {
                ["id"] = nodePrefix + net.NetId,
                ["label"] = net.IsGround ? "GND" : net.Name
            };
            if (net.IsGround)
            {
                node["kind"] = "reference";
            }
            nodes.Add(node);
            ports.Add(new JsonObject
            {
                ["id"] = "p_" + net.NetId,
                ["label"] = net.IsGround ? "Reference" : net.Name,
                ["nodeId"] = nodePrefix + net.NetId,
                ["role"] = net.IsGround ? "reference" : "probe"
            });
        }

        var components = new JsonArray();
        var connections = new JsonArray();
        foreach (CircuitElement element in netlist.Elements.Values)
        {
            if (element.Type == ElementType.Ground) continue;
            components.Add(BuildComponent(element));
            foreach (CircuitPin pin in element.Pins)
            {
                ElectricalNet? net = netlist.FindNetForPin(element.Id, pin.Id);
                if (net == null) continue;
                connections.Add(new JsonObject
                {
                    ["id"] = "c_" + element.Id + "_" + pin.Id,
                    ["nodeId"] = nodePrefix + net.NetId,
                    ["componentId"] = element.Id,
                    ["portId"] = MapPinToWebPort(element.Type, pin.Id)
                });
            }
        }

        // The fixture NetlistBuilder represents the reference ground by net 0 (no GND
        // symbol element). CircuitGraph import needs an explicit reference component, so a
        // synthetic ground symbol is added on the ground net. The production extractor
        // merges that symbol into the same net, so the imported electrical topology is
        // identical to the fixture topology.
        ElectricalNet? groundNet = netlist.GroundNet;
        if (groundNet != null)
        {
            components.Add(new JsonObject
            {
                ["id"] = "GND",
                ["definitionId"] = "reference.ground",
                ["label"] = "GND",
                ["ports"] = new JsonArray("ref"),
                ["parameters"] = new JsonObject(),
                ["modelVariant"] = "ideal_ground"
            });
            connections.Add(new JsonObject
            {
                ["id"] = "c_GND_ref",
                ["nodeId"] = nodePrefix + groundNet.NetId,
                ["componentId"] = "GND",
                ["portId"] = "ref"
            });
        }

        return new JsonObject
        {
            ["id"] = "graph.native30." + problemId,
            ["version"] = "0.1.0",
            ["title"] = "原生 30 题 Benchmark fixture " + problemId,
            ["nodes"] = nodes,
            ["components"] = components,
            ["ports"] = ports,
            ["connections"] = connections
        };
    }

    private static JsonObject BuildGroundTruth(ElectricalNetlist netlist, string nodePrefix)
    {
        var componentIds = new JsonArray();
        foreach (string id in netlist.Elements.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            componentIds.Add(id);
        }
        componentIds.Add("GND");

        var nodeIds = new JsonArray();
        var netPinCounts = new JsonArray();
        foreach (ElectricalNet net in netlist.Nets.OrderBy(n => n.NetId))
        {
            nodeIds.Add(nodePrefix + net.NetId);
            netPinCounts.Add(new JsonObject
            {
                ["nodeId"] = nodePrefix + net.NetId,
                ["pinCount"] = net.ConnectedPins.Count
            });
        }

        int connectionCount = netlist.Elements.Values
            .Where(e => e.Type != ElementType.Ground)
            .Sum(e => e.Pins.Count) + 1; // + synthetic ground reference connection

        return new JsonObject
        {
            ["expectedNodeIds"] = nodeIds,
            ["expectedComponentIds"] = componentIds,
            ["expectedConnectionCount"] = connectionCount,
            ["expectedNetPinCounts"] = netPinCounts
        };
    }

    private static JsonObject BuildComponent(CircuitElement element)
    {
        var component = new JsonObject
        {
            ["id"] = element.Id,
            ["definitionId"] = MapDefinitionId(element.Type),
            ["label"] = string.IsNullOrWhiteSpace(element.Name) ? element.Id : element.Name,
            ["ports"] = new JsonArray(WebPorts(element.Type).Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
            ["parameters"] = BuildParameters(element)
        };
        component["modelVariant"] = ModelVariant(element.Type);
        return component;
    }

    private static JsonObject BuildParameters(CircuitElement element)
    {
        switch (element.Type)
        {
            case ElementType.Resistor:
                return Parameter("resistance", element.DisplayNumber, NormalizeUnit(element.UnitSymbol, "ohm"));
            case ElementType.Capacitor:
            case ElementType.CapacitorPolarized:
                return Parameter("capacitance", element.DisplayNumber, NormalizeUnit(element.UnitSymbol, "farad"));
            case ElementType.Inductor:
                return Parameter("inductance", element.DisplayNumber, NormalizeUnit(element.UnitSymbol, "henry"));
            case ElementType.DcVoltageSource:
                return Parameter("voltage", element.DisplayNumber, NormalizeUnit(element.UnitSymbol, "volt"));
            case ElementType.AcVoltageSource:
            {
                JsonObject parameters = Parameter("amplitude", element.DisplayNumber, NormalizeUnit(element.UnitSymbol, "volt"));
                parameters["frequency"] = Quantity(element.FrequencyHz, "Hz");
                return parameters;
            }
            case ElementType.DcCurrentSource:
                return Parameter("current", element.DisplayNumber, NormalizeUnit(element.UnitSymbol, "ampere"));
            case ElementType.AcCurrentSource:
            {
                JsonObject parameters = Parameter("amplitude", element.DisplayNumber, NormalizeUnit(element.UnitSymbol, "ampere"));
                parameters["frequency"] = Quantity(element.FrequencyHz, "Hz");
                return parameters;
            }
            case ElementType.BjtNpn:
            case ElementType.BjtPnp:
            {
                JsonObject parameters = Parameter("beta", element.DisplayNumber, "unitless");
                parameters["vbe_on"] = Quantity(0.7, "volt"); // native BjtAmplifierStrategy model constant
                return parameters;
            }
            case ElementType.Vcvs:
            case ElementType.Cccs:
                // VCVS gain is a voltage ratio; CCCS gain is a current ratio (both unitless).
                return Parameter("gain", element.DisplayNumber, "unitless");
            case ElementType.Vccs:
                // VCCS transconductance: fixture uses mS/S units, importer converts to S.
                return Parameter("transconductance", element.DisplayNumber, NormalizeUnit(element.UnitSymbol, "siemens"));
            case ElementType.Ccvs:
                // CCVS transresistance: fixture uses Ω/kΩ units, importer converts to Ω.
                return Parameter("transresistance", element.DisplayNumber, NormalizeUnit(element.UnitSymbol, "ohm"));
            default:
                return new JsonObject();
        }
    }

    private static JsonObject Parameter(string name, double value, string unit) =>
        new() { [name] = Quantity(value, unit) };

    private static JsonObject Quantity(double value, string unit) => new()
    {
        ["value"] = value,
        ["unit"] = unit,
        ["source"] = "benchmark"
    };

    private static string NormalizeUnit(string unitSymbol, string fallbackSiUnit)
    {
        if (string.IsNullOrWhiteSpace(unitSymbol)) return fallbackSiUnit;
        return unitSymbol.Trim() switch
        {
            "倍" => "unitless",
            "uF" => "μF",
            "uH" => "μH",
            "uA" => "μA",
            "uS" => "μS",
            _ => unitSymbol.Trim()
        };
    }

    private static string MapDefinitionId(ElementType type) => type switch
    {
        ElementType.Resistor => "passive.resistor",
        ElementType.Capacitor => "passive.capacitor",
        ElementType.CapacitorPolarized => "passive.capacitor_polarized",
        ElementType.Inductor => "passive.inductor",
        ElementType.DcVoltageSource => "source.v_dc",
        ElementType.AcVoltageSource => "source.v_ac_small_signal",
        ElementType.DcCurrentSource => "source.i_dc",
        ElementType.AcCurrentSource => "source.i_ac_small_signal",
        ElementType.BjtNpn => "bjt.npn",
        ElementType.BjtPnp => "bjt.pnp",
        ElementType.Vcvs => "controlled.vcvs",
        ElementType.Vccs => "controlled.vccs",
        ElementType.Ccvs => "controlled.ccvs",
        ElementType.Cccs => "controlled.cccs",
        ElementType.Ground => "reference.ground",
        _ => throw new InvalidOperationException($"CircuitGraphExporter 不支持元件类型 {type}。")
    };

    private static string ModelVariant(ElementType type) => type switch
    {
        ElementType.Resistor => "ideal_resistor",
        ElementType.Capacitor or ElementType.CapacitorPolarized => "ideal_capacitor",
        ElementType.Inductor => "ideal_inductor",
        ElementType.DcVoltageSource or ElementType.AcVoltageSource => "ideal_voltage_source",
        ElementType.DcCurrentSource or ElementType.AcCurrentSource => "ideal_current_source",
        ElementType.BjtNpn or ElementType.BjtPnp => "npn_large_signal",
        ElementType.Vcvs or ElementType.Vccs or ElementType.Ccvs or ElementType.Cccs => "ideal_controlled_source",
        ElementType.Ground => "ideal_ground",
        _ => "ideal"
    };

    private static string[] WebPorts(ElementType type) => type switch
    {
        ElementType.Resistor or ElementType.Capacitor or ElementType.CapacitorPolarized
            or ElementType.Inductor or ElementType.DcVoltageSource or ElementType.AcVoltageSource
            or ElementType.DcCurrentSource or ElementType.AcCurrentSource => new[] { "p", "n" },
        ElementType.BjtNpn or ElementType.BjtPnp => new[] { "collector", "base", "emitter" },
        ElementType.Vcvs or ElementType.Vccs => new[] { "cp", "cn", "p", "n" },
        ElementType.Ccvs or ElementType.Cccs => new[] { "sp", "sn", "p", "n" },
        ElementType.Ground => new[] { "ref" },
        _ => throw new InvalidOperationException($"CircuitGraphExporter 不支持元件类型 {type} 的端口映射。")
    };

    private static string MapPinToWebPort(ElementType type, string pinId) => type switch
    {
        ElementType.Resistor or ElementType.Capacitor or ElementType.CapacitorPolarized
            or ElementType.Inductor => pinId switch { "1" => "p", "2" => "n", _ => throw Unknown(type, pinId) },
        ElementType.DcVoltageSource or ElementType.AcVoltageSource => pinId switch
        {
            "pos" => "p", "neg" => "n", _ => throw Unknown(type, pinId)
        },
        ElementType.DcCurrentSource or ElementType.AcCurrentSource => pinId switch
        {
            "in" => "p", "out" => "n", _ => throw Unknown(type, pinId)
        },
        ElementType.BjtNpn or ElementType.BjtPnp => pinId switch
        {
            "C" => "collector", "B" => "base", "E" => "emitter", _ => throw Unknown(type, pinId)
        },
        ElementType.Vcvs or ElementType.Vccs => pinId switch
        {
            "IN1" => "cp", "IN2" => "cn", "OUT1" => "p", "OUT2" => "n", _ => throw Unknown(type, pinId)
        },
        ElementType.Ccvs or ElementType.Cccs => pinId switch
        {
            "IN1" => "sp", "IN2" => "sn", "OUT1" => "p", "OUT2" => "n", _ => throw Unknown(type, pinId)
        },
        ElementType.Ground => pinId switch { "GND" => "ref", _ => throw Unknown(type, pinId) },
        _ => throw new InvalidOperationException($"CircuitGraphExporter 不支持元件类型 {type}。")
    };

    private static InvalidOperationException Unknown(ElementType type, string pinId) =>
        new($"CircuitGraphExporter 无法把 {type} 的引脚 '{pinId}' 映射到 Web 端口。");
}
