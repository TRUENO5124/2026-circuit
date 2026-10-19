// ============================================================================
// File: apps/desktop-winui/Services/WebCircuitGraphImporter.cs
// Namespace: CircuitAssistant.Services
// Description: Third-party / prototype format compatibility adapter.
//
//   The Web/Python prototype (packages/schema circuitGraph + benchmarks/examples)
//   stores circuits as a logical graph: nodes + components (definitionId/ports/
//   parameters) + connections (nodeId/componentId/portId). It contains NO
//   geometry, while the native WinUI application works on CircuitProjectData
//   (elements with pins + wires with coordinates). This importer is the missing
//   bridge:
//
//     1. Parse  { id, title, circuitGraph: { nodes, components, connections } }
//        or a bare { nodes, components, connections } document.
//     2. Map Web component definitions/ports to native ElementType/pin ids and
//        convert SI parameters into user friendly DisplayNumber + UnitSymbol.
//     3. Auto-layout: nodes on a deterministic grid, components at the midpoint
//        of the connected nodes, one open-ended wire per connection.
//     4. Emit CircuitProjectData and run its Validate() self check.
//
//   The generated wires deliberately rely on the native NetlistExtractor
//   semantics: a wire whose Start is a component pin and whose End is an open
//   end (EndElementId == "") collapses all coincident open ends into one net.
//   Therefore every "connection" of the Web graph becomes exactly one native
//   wire, and the native netlist/classifier/strategy pipeline sees the same
//   topology as the prototype.
//
//   Pure .NET: no WinRT / WinUI dependency (only Models + System.Text.Json).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CircuitAssistant.Models;

namespace CircuitAssistant.Services;

/// <summary>
/// Converts Web/Python "CircuitGraph" JSON into a native <see cref="CircuitProjectData"/>.
/// </summary>
public static class WebCircuitGraphImporter
{
    /// <summary>Description written into the imported project snapshot.</summary>
    public const string ImportDescription = "从 Web/Python CircuitGraph 导入";

    // Deterministic auto-layout convention (JSON has no coordinates):
    //   * every ground/reference node is placed at (400, 480);
    //   * the remaining nodes are laid on a grid in node declaration order:
    //     x = 180 + 150 * (i % 5), y = 120 + 120 * (i / 5);
    //   * a component sits at the midpoint of its first two connected nodes
    //     (multi-terminal devices use the first two ports; a single-terminal
    //     ground/VCC symbol sits on its node);
    //   * rotation is always 0 (horizontal).
    public const float GroundNodeX = 400f;
    public const float GroundNodeY = 480f;
    public const float NodeGridOriginX = 180f;
    public const float NodeGridOriginY = 120f;
    public const float NodeGridStepX = 150f;
    public const float NodeGridStepY = 120f;
    public const int NodeGridColumns = 5;

    /// <summary>
    /// Returns true when <paramref name="json"/> looks like a Web/Python CircuitGraph
    /// document: the root object contains an object <c>circuitGraph</c>, or the root
    /// itself contains both a <c>components</c> array and a <c>connections</c> array.
    /// Never throws (malformed JSON simply returns false).
    /// </summary>
    public static bool LooksLikeWebCircuitGraph(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json, CreateJsonOptions());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (TryGetProperty(root, "circuitGraph", out var graphElement) &&
                graphElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            return TryGetProperty(root, "components", out var components) &&
                   components.ValueKind == JsonValueKind.Array &&
                   TryGetProperty(root, "connections", out var connections) &&
                   connections.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Imports Web/Python CircuitGraph JSON into a native project snapshot.
    /// </summary>
    /// <param name="json">Benchmark wrapper <c>{ title, circuitGraph: {...} }</c> or a bare graph document.</param>
    /// <param name="projectName">Optional project name override; otherwise title/id from the JSON is used.</param>
    /// <exception cref="InvalidDataException">The document is not a supported CircuitGraph or cannot be converted.</exception>
    public static CircuitProjectData Import(string json, string? projectName = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Web CircuitGraph JSON 为空，无法导入。");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, CreateJsonOptions());
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Web CircuitGraph JSON 解析失败：" + ex.Message, ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Web CircuitGraph 根节点必须是 JSON 对象。");

            bool wrapped = TryGetProperty(root, "circuitGraph", out var wrappedGraph) &&
                           wrappedGraph.ValueKind == JsonValueKind.Object;
            var graph = wrapped ? wrappedGraph : root;

            if (!TryGetProperty(graph, "components", out var componentsElement) ||
                componentsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "不是可导入的 Web/Python CircuitGraph：缺少 components 数组（支持 benchmark 包装 { circuitGraph: {...} } 或直接 { nodes, components, connections }）。");
            }
            if (!TryGetProperty(graph, "connections", out var connectionsElement) ||
                connectionsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Web CircuitGraph 缺少 connections 数组，无法恢复电气连接。");
            }

            // ---- 1. Nodes + deterministic layout ---------------------------------
            var nodeOrder = new List<string>();
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            var groundNodeIds = new HashSet<string>(StringComparer.Ordinal);
            if (TryGetProperty(graph, "nodes", out var nodesElement))
            {
                if (nodesElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Web CircuitGraph 的 nodes 字段必须是数组。");
                foreach (var nodeElement in nodesElement.EnumerateArray())
                {
                    if (nodeElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Web CircuitGraph 的 nodes 数组包含非对象元素。");
                    string nodeId = RequireString(nodeElement, "id", "节点缺少 id。");
                    if (!nodeIds.Add(nodeId))
                        throw new InvalidDataException($"Web CircuitGraph 包含重复节点 id：{nodeId}。");
                    nodeOrder.Add(nodeId);
                    if (IsGroundNode(nodeId, GetString(nodeElement, "label"), GetString(nodeElement, "kind")))
                        groundNodeIds.Add(nodeId);
                }
            }

            // ---- 2. Components ----------------------------------------------------
            var componentSpecs = new List<WebComponentSpec>();
            var specById = new Dictionary<string, WebComponentSpec>(StringComparer.Ordinal);
            foreach (var componentElement in componentsElement.EnumerateArray())
            {
                var spec = ParseComponent(componentElement);
                if (spec.Id.Contains('.') || spec.Id.Contains(':'))
                    throw new InvalidDataException(
                        $"Web 元件 id '{spec.Id}' 含有保留分隔符 '.' 或 ':'，原生工程不允许该字符，无法导入。");
                if (specById.ContainsKey(spec.Id))
                    throw new InvalidDataException($"Web CircuitGraph 包含重复元件 id：{spec.Id}。");
                specById.Add(spec.Id, spec);
                componentSpecs.Add(spec);
            }

            // ---- 3. Connections ----------------------------------------------------
            var connections = new List<WebConnection>();
            foreach (var connectionElement in connectionsElement.EnumerateArray())
            {
                if (connectionElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Web CircuitGraph 的 connections 数组包含非对象元素。");
                string nodeId = RequireString(connectionElement, "nodeId", "连接缺少 nodeId。");
                string componentId = RequireString(connectionElement, "componentId", "连接缺少 componentId。");
                string portId = RequireString(connectionElement, "portId", "连接缺少 portId。");
                if (!specById.ContainsKey(componentId))
                    throw new InvalidDataException($"连接引用了不存在的元件 '{componentId}'。");
                // Nodes referenced by connections but not declared in "nodes" still get a
                // deterministic layout slot, appended after the declared nodes.
                if (nodeIds.Add(nodeId)) nodeOrder.Add(nodeId);
                connections.Add(new WebConnection(
                    GetString(connectionElement, "id"), nodeId, componentId, portId));
            }

            var nodeCoordinates = BuildNodeLayout(nodeOrder, groundNodeIds);

            // Group connections per component, ordered by the component's declared port
            // order (fallback: connection declaration order).
            var connectionsByComponent = new Dictionary<string, List<WebConnection>>(StringComparer.Ordinal);
            foreach (var connection in connections)
            {
                if (!connectionsByComponent.TryGetValue(connection.ComponentId, out var list))
                {
                    list = new List<WebConnection>();
                    connectionsByComponent.Add(connection.ComponentId, list);
                }
                if (list.Any(c => string.Equals(c.PortId, connection.PortId, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(
                        $"元件 '{connection.ComponentId}' 的端口 '{connection.PortId}' 存在重复连接，数据不一致。");
                list.Add(connection);
            }

            // ---- 4. Build native elements -----------------------------------------
            var elements = new List<CircuitElement>();
            var unknownDefinitions = new List<string>();
            var fallbackLayoutIndex = 0;
            foreach (var spec in componentSpecs)
            {
                ElementType? type = TryMapDefinitionId(spec.DefinitionId);
                if (type == null)
                {
                    if (!unknownDefinitions.Contains(spec.DefinitionId, StringComparer.Ordinal))
                        unknownDefinitions.Add(spec.DefinitionId);
                    continue;
                }

                connectionsByComponent.TryGetValue(spec.Id, out var componentConnections);
                var orderedConnections = OrderConnectionsByDeclaredPorts(componentConnections, spec.Ports);
                var (x, y) = ComputeComponentPosition(orderedConnections, nodeCoordinates, fallbackLayoutIndex++);

                var element = new CircuitElement
                {
                    Id = spec.Id,
                    Name = string.IsNullOrWhiteSpace(spec.Label) ? spec.Id : spec.Label,
                    DefinitionId = spec.DefinitionId,
                    Category = GetDefinitionFamily(spec.DefinitionId),
                    Description = $"{ImportDescription}（{spec.DefinitionId}）",
                    Type = type.Value,
                    X = x,
                    Y = y,
                    Width = 80f,
                    Height = 50f,
                    RotationDeg = 0,
                    Orientation = "Horizontal"
                };
                ApplyElementParameters(element, spec);
                element.InitializeDefaultPins();
                elements.Add(element);
            }

            if (unknownDefinitions.Count > 0)
            {
                throw new InvalidDataException(
                    "Web CircuitGraph 包含当前原生应用不支持的元件定义 id：" +
                    string.Join(", ", unknownDefinitions) +
                    "（请先补充 packages/components 到 ElementType 的映射）。");
            }

            // ---- 5. Build native wires (one per connection) -----------------------
            var wires = new List<CircuitWire>();
            var elementIds = new HashSet<string>(elements.Select(e => e.Id), StringComparer.Ordinal);
            var wireIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in elements)
            {
                connectionsByComponent.TryGetValue(element.Id, out var componentConnections);
                var orderedConnections = OrderConnectionsByDeclaredPorts(
                    componentConnections, specById[element.Id].Ports);
                foreach (var connection in orderedConnections)
                {
                    string pinId = MapPortIdToNativePin(element, connection.PortId);
                    var pin = element.Pins.FirstOrDefault(p => p.Id == pinId)
                              ?? throw new InvalidDataException(
                                  $"元件 '{element.Id}' 缺少原生引脚 '{pinId}'（端口 '{connection.PortId}' 无法落位）。");
                    var pinWorld = element.GetPinWorldPosition(pin);
                    var node = nodeCoordinates[connection.NodeId];
                    wires.Add(new CircuitWire
                    {
                        Id = AllocateWireId(connection, element.Id, wireIds, elementIds),
                        StartElementId = element.Id,
                        StartPinName = pin.Id,
                        StartX = pinWorld.X,
                        StartY = pinWorld.Y,
                        // Open end: the native NetlistExtractor merges coincident open ends
                        // (and only open ends may merge geometrically) into one electrical net.
                        EndElementId = string.Empty,
                        EndPinName = string.Empty,
                        EndX = node.X,
                        EndY = node.Y,
                        NetId = -1,
                        FlipCorner = false,
                        CustomMid = 0f
                    });
                }
            }

            // ---- 6. Assemble project snapshot + self check ------------------------
            string resolvedName = FirstNonEmpty(
                projectName,
                GetString(root, "title"),
                GetString(graph, "title"),
                GetString(root, "id"),
                GetString(graph, "id"),
                "未命名电路工程")!;

            string fileNameToken = SanitizeFileNameToken(
                FirstNonEmpty(GetString(graph, "id"), GetString(root, "id"), resolvedName));
            if (string.IsNullOrEmpty(fileNameToken)) fileNameToken = "WebCircuitGraph";
            if (fileNameToken.Length > 80) fileNameToken = fileNameToken[..80];

            var data = CircuitProjectData.From(
                resolvedName,
                fileNameToken + ".json",
                elements,
                wires,
                new Dictionary<string, double>(),
                ImportDescription);

            try
            {
                data.Validate();
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException(
                    "导入的 Web CircuitGraph 转换为原生工程后自检失败：" + ex.Message, ex);
            }
            return data;
        }
    }

    /// <summary>
    /// Convenience overload: reads a UTF-8 JSON file and imports it.
    /// </summary>
    public static CircuitProjectData ImportFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("文件路径不可为空。", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"CircuitGraph 文件未找到: {path}", path);
        return Import(File.ReadAllText(path, Encoding.UTF8));
    }

    // ========================================================================
    // Parsing helpers
    // ========================================================================

    private readonly record struct WebParameter(double Value, string? Unit);

    private readonly record struct WebConnection(string? Id, string NodeId, string ComponentId, string PortId);

    private sealed class WebComponentSpec
    {
        public string Id { get; init; } = string.Empty;
        public string DefinitionId { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public List<string> Ports { get; } = new();
        public Dictionary<string, WebParameter> Parameters { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private static JsonDocumentOptions CreateJsonOptions() => new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static WebComponentSpec ParseComponent(JsonElement componentElement)
    {
        if (componentElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Web CircuitGraph 的 components 数组包含非对象元素。");

        string id = RequireString(componentElement, "id", "Web 元件缺少 id。");
        string definitionId = RequireString(componentElement, "definitionId",
            $"Web 元件 '{id}' 缺少 definitionId。");

        var spec = new WebComponentSpec
        {
            Id = id,
            DefinitionId = definitionId,
            Label = GetString(componentElement, "label") ?? GetString(componentElement, "name") ?? id
        };

        if (TryGetProperty(componentElement, "ports", out var portsElement))
        {
            if (portsElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Web 元件 '{id}' 的 ports 字段必须是数组。");
            foreach (var portElement in portsElement.EnumerateArray())
            {
                if (portElement.ValueKind == JsonValueKind.String)
                {
                    spec.Ports.Add(portElement.GetString()!);
                }
                else if (portElement.ValueKind == JsonValueKind.Object)
                {
                    spec.Ports.Add(RequireString(portElement, "id", $"Web 元件 '{id}' 的端口缺少 id。"));
                }
                else
                {
                    throw new InvalidDataException($"Web 元件 '{id}' 的 ports 数组包含非法元素。");
                }
            }
        }

        if (TryGetProperty(componentElement, "parameters", out var parametersElement) &&
            parametersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var parameterProperty in parametersElement.EnumerateObject())
            {
                if (parameterProperty.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException(
                        $"Web 元件 '{id}' 的参数 '{parameterProperty.Name}' 必须是对象（含 value/unit）。");
                if (!TryGetProperty(parameterProperty.Value, "value", out var valueElement))
                    throw new InvalidDataException(
                        $"Web 元件 '{id}' 的参数 '{parameterProperty.Name}' 缺少 value。");
                double value = ReadNumber(valueElement,
                    $"Web 元件 '{id}' 的参数 '{parameterProperty.Name}' 的 value 不是有效数值。");
                string? unit = TryGetProperty(parameterProperty.Value, "unit", out var unitElement) &&
                               unitElement.ValueKind == JsonValueKind.String
                    ? unitElement.GetString()
                    : null;
                spec.Parameters[parameterProperty.Name] = new WebParameter(value, unit);
            }
        }

        return spec;
    }

    private static double ReadNumber(JsonElement element, string errorMessage)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
        {
            if (!double.IsFinite(number)) throw new InvalidDataException(errorMessage);
            return number;
        }
        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed))
        {
            return parsed;
        }
        throw new InvalidDataException(errorMessage);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string RequireString(JsonElement element, string name, string errorMessage)
    {
        var value = GetString(element, name);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException(errorMessage);
        return value!;
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    // ========================================================================
    // Node layout
    // ========================================================================

    private static bool IsGroundNode(string nodeId, string? label, string? kind)
    {
        bool IsGroundToken(string? token) =>
            !string.IsNullOrWhiteSpace(token) &&
            (token.Contains("gnd", StringComparison.OrdinalIgnoreCase) ||
             token.Contains("ground", StringComparison.OrdinalIgnoreCase));

        return IsGroundToken(nodeId) || IsGroundToken(label) ||
               string.Equals(kind, "reference", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, (float X, float Y)> BuildNodeLayout(
        List<string> nodeOrder, HashSet<string> groundNodeIds)
    {
        var coordinates = new Dictionary<string, (float X, float Y)>(StringComparer.Ordinal);
        int nonGroundIndex = 0;
        foreach (var nodeId in nodeOrder)
        {
            if (groundNodeIds.Contains(nodeId))
            {
                coordinates[nodeId] = (GroundNodeX, GroundNodeY);
                continue;
            }
            coordinates[nodeId] = (
                NodeGridOriginX + NodeGridStepX * (nonGroundIndex % NodeGridColumns),
                NodeGridOriginY + NodeGridStepY * (nonGroundIndex / NodeGridColumns));
            nonGroundIndex++;
        }
        return coordinates;
    }

    private static List<WebConnection> OrderConnectionsByDeclaredPorts(
        List<WebConnection>? connections, List<string> declaredPorts)
    {
        if (connections == null || connections.Count == 0) return new List<WebConnection>();
        if (declaredPorts.Count == 0) return new List<WebConnection>(connections);

        var portOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < declaredPorts.Count; i++) portOrder.TryAdd(declaredPorts[i], i);
        // Stable sort: connections of undeclared ports keep their declaration order.
        return connections
            .OrderBy(c => portOrder.TryGetValue(c.PortId, out var index) ? index : int.MaxValue)
            .ToList();
    }

    private static (float X, float Y) ComputeComponentPosition(
        List<WebConnection> orderedConnections,
        Dictionary<string, (float X, float Y)> nodeCoordinates,
        int fallbackLayoutIndex)
    {
        if (orderedConnections.Count == 0)
        {
            // Floating component (no connections): place it on a separate row so it
            // never collides with the connected circuit. Kept deterministic on purpose.
            return (
                NodeGridOriginX + NodeGridStepX * (fallbackLayoutIndex % NodeGridColumns),
                GroundNodeY + NodeGridStepY + NodeGridStepY * (fallbackLayoutIndex / NodeGridColumns));
        }
        if (orderedConnections.Count == 1)
        {
            return nodeCoordinates[orderedConnections[0].NodeId];
        }
        // Two-terminal and multi-terminal devices sit at the midpoint of the first two
        // connected nodes (ports are already ordered by the component's port list).
        var first = nodeCoordinates[orderedConnections[0].NodeId];
        var second = nodeCoordinates[orderedConnections[1].NodeId];
        return ((first.X + second.X) / 2f, (first.Y + second.Y) / 2f);
    }

    private static string AllocateWireId(
        WebConnection connection,
        string elementId,
        HashSet<string> usedWireIds,
        HashSet<string> elementIds)
    {
        string token = SanitizeFileNameToken(connection.Id);
        if (string.IsNullOrEmpty(token))
            token = SanitizeFileNameToken($"{elementId}_{connection.PortId}");
        if (string.IsNullOrEmpty(token)) token = "connection";

        string baseId = "w_" + token;
        string candidate = baseId;
        int suffix = 2;
        while (!usedWireIds.Add(candidate) || elementIds.Contains(candidate))
        {
            candidate = baseId + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }
        return candidate;
    }

    private static string SanitizeFileNameToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var builder = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }
        return builder.ToString().Trim('_', '-');
    }

    private static string GetDefinitionFamily(string definitionId)
    {
        int dot = definitionId.IndexOf('.');
        return dot > 0 ? definitionId[..dot] : definitionId;
    }

    // ========================================================================
    // Definition / port mapping (must stay in sync with packages/components)
    // ========================================================================

    private static ElementType? TryMapDefinitionId(string definitionId) =>
        definitionId.Trim().ToLowerInvariant() switch
        {
            "passive.resistor" => ElementType.Resistor,
            "passive.capacitor" => ElementType.Capacitor,
            "passive.capacitor_polarized" => ElementType.CapacitorPolarized,
            "passive.inductor" => ElementType.Inductor,
            "passive.transformer" => ElementType.Transformer,
            // source.vcc is an accepted alias for an ideal DC supply symbol.
            "source.v_dc" or "source.vcc" => ElementType.DcVoltageSource,
            "source.i_dc" => ElementType.DcCurrentSource,
            "source.v_ac_small_signal" => ElementType.AcVoltageSource,
            "source.i_ac_small_signal" => ElementType.AcCurrentSource,
            "source.v_ac" => ElementType.AcVoltageSource,
            "source.i_ac" => ElementType.AcCurrentSource,
            "diode.standard" => ElementType.Diode,
            "bjt.npn" => ElementType.BjtNpn,
            "bjt.pnp" => ElementType.BjtPnp,
            "fet.mosfet" => ElementType.Mosfet,
            "fet.mosfet_n_enh" => ElementType.MosfetNEnh,
            "fet.mosfet_p_enh" => ElementType.MosfetPEnh,
            "fet.mosfet_n_dep" => ElementType.MosfetNDep,
            "fet.mosfet_p_dep" => ElementType.MosfetPDep,
            "fet.jfet" => ElementType.Jfet,
            "fet.jfet_n" => ElementType.JfetN,
            "fet.jfet_p" => ElementType.JfetP,
            "reference.ground" => ElementType.Ground,
            "reference.vcc_port" => ElementType.VccPort,
            "controlled.vcvs" => ElementType.Vcvs,
            "controlled.vccs" => ElementType.Vccs,
            "controlled.ccvs" => ElementType.Ccvs,
            "controlled.cccs" => ElementType.Cccs,
            _ => null
        };

    private static string MapPortIdToNativePin(CircuitElement element, string webPortId)
    {
        string port = webPortId.Trim().ToLowerInvariant();

        string MapOrThrow(string mapped) => mapped;
        string Unknown() => throw new InvalidDataException(
            $"元件 '{element.Id}'（{element.Type}）的 Web 端口 '{webPortId}' 无法映射到原生引脚，已停止导入。");

        switch (element.Type)
        {
            case ElementType.Resistor:
            case ElementType.Capacitor:
            case ElementType.Inductor:
                return port switch
                {
                    "p" or "1" or "pos" or "positive" or "+" => MapOrThrow("1"),
                    "n" or "2" or "neg" or "negative" or "-" => MapOrThrow("2"),
                    _ => Unknown()
                };

            case ElementType.CapacitorPolarized:
                return port switch
                {
                    "p" or "1" or "pos" or "positive" or "+" => MapOrThrow("+"),
                    "n" or "2" or "neg" or "negative" or "-" => MapOrThrow("-"),
                    _ => Unknown()
                };

            case ElementType.DcVoltageSource:
            case ElementType.AcVoltageSource:
                return port switch
                {
                    "p" or "1" or "pos" or "positive" or "+" => MapOrThrow("pos"),
                    "n" or "2" or "neg" or "negative" or "-" => MapOrThrow("neg"),
                    _ => Unknown()
                };

            case ElementType.DcCurrentSource:
            case ElementType.AcCurrentSource:
                return port switch
                {
                    "p" or "1" or "in" or "pos" or "positive" => MapOrThrow("in"),
                    "n" or "2" or "out" or "neg" or "negative" => MapOrThrow("out"),
                    _ => Unknown()
                };

            case ElementType.Diode:
                return port switch
                {
                    "anode" or "a" or "p" or "1" or "+" => MapOrThrow("A"),
                    "cathode" or "k" or "n" or "2" or "-" => MapOrThrow("K"),
                    _ => Unknown()
                };

            case ElementType.BjtNpn:
            case ElementType.BjtPnp:
                return port switch
                {
                    "collector" or "c" => MapOrThrow("C"),
                    "base" or "b" => MapOrThrow("B"),
                    "emitter" or "e" => MapOrThrow("E"),
                    _ => Unknown()
                };

            case ElementType.Mosfet:
            case ElementType.MosfetNEnh:
            case ElementType.MosfetPEnh:
            case ElementType.MosfetNDep:
            case ElementType.MosfetPDep:
            case ElementType.Jfet:
            case ElementType.JfetN:
            case ElementType.JfetP:
                return port switch
                {
                    "drain" or "d" => MapOrThrow("D"),
                    "gate" or "g" => MapOrThrow("G"),
                    "source" or "s" => MapOrThrow("S"),
                    // The native teaching model is a 3-pin FET; the 4th Web "bulk" port
                    // is folded onto the source pin (body tied to source) and documented
                    // as a known limitation.
                    "bulk" or "body" or "b" or "substrate" => MapOrThrow("S"),
                    _ => Unknown()
                };

            case ElementType.Ground:
                return port switch
                {
                    "ref" or "gnd" or "ground" or "reference" or "p" or "1" or "0" => MapOrThrow("GND"),
                    _ => Unknown()
                };

            case ElementType.VccPort:
                return port switch
                {
                    "ref" or "vcc" or "vdd" or "reference" or "p" or "1" => MapOrThrow("VCC"),
                    _ => Unknown()
                };

            case ElementType.Transformer:
                return port switch
                {
                    "p1" or "primary_positive" => MapOrThrow("P1"),
                    "n1" or "primary_negative" => MapOrThrow("P2"),
                    "p2" or "secondary_positive" => MapOrThrow("S1"),
                    "n2" or "secondary_negative" => MapOrThrow("S2"),
                    _ => Unknown()
                };

            // VCVS/VCCS: p/n are output terminals, cp/cn are control terminals.
            case ElementType.Vcvs:
            case ElementType.Vccs:
                return port switch
                {
                    "p" or "out1" or "output_positive" or "+" => MapOrThrow("OUT1"),
                    "n" or "out2" or "output_negative" or "-" => MapOrThrow("OUT2"),
                    "cp" or "in1" or "control_positive" => MapOrThrow("IN1"),
                    "cn" or "in2" or "control_negative" => MapOrThrow("IN2"),
                    _ => Unknown()
                };

            // CCVS/CCCS: p/n are output terminals, sp/sn are sense (control) terminals.
            case ElementType.Ccvs:
            case ElementType.Cccs:
                return port switch
                {
                    "p" or "out1" or "output_positive" or "+" => MapOrThrow("OUT1"),
                    "n" or "out2" or "output_negative" or "-" => MapOrThrow("OUT2"),
                    "sp" or "in1" or "sense_positive" => MapOrThrow("IN1"),
                    "sn" or "in2" or "sense_negative" => MapOrThrow("IN2"),
                    _ => Unknown()
                };

            default:
                return Unknown();
        }
    }

    // ========================================================================
    // Parameter mapping: SI -> user friendly DisplayNumber + UnitSymbol
    // ========================================================================

    private static void ApplyElementParameters(CircuitElement element, WebComponentSpec spec)
    {
        switch (element.Type)
        {
            case ElementType.Resistor:
            {
                double si = GetSiValue(spec, "resistance", required: true, fallback: double.NaN, "ohm");
                ApplyEngineering(element, si, ResistanceDisplayUnits, "Ω");
                break;
            }
            case ElementType.Capacitor:
            case ElementType.CapacitorPolarized:
            {
                double si = GetSiValue(spec, "capacitance", required: true, fallback: double.NaN, "farad");
                ApplyEngineering(element, si, CapacitanceDisplayUnits, "F");
                break;
            }
            case ElementType.Inductor:
            {
                double si = GetSiValue(spec, "inductance", required: true, fallback: double.NaN, "henry");
                ApplyEngineering(element, si, InductanceDisplayUnits, "H");
                break;
            }
            case ElementType.Transformer:
            {
                double si = GetSiValue(spec, "turns_ratio", required: false, fallback: 1.0, "unitless");
                element.DisplayNumber = RoundSignificant(si);
                element.UnitSymbol = string.Empty;
                element.Value = element.DisplayNumber;
                element.FrequencyHz = 1000.0;
                break;
            }
            case ElementType.DcVoltageSource:
            {
                double si = GetSiValue(spec, "voltage", required: true, fallback: double.NaN, "volt");
                ApplyEngineering(element, si, VoltageDisplayUnits, "V");
                element.FrequencyHz = 1000.0;
                break;
            }
            case ElementType.AcVoltageSource:
            {
                double si = GetSiValue(spec, new[] { "amplitude", "voltage" }, required: true,
                    fallback: double.NaN, "volt");
                ApplyEngineering(element, si, VoltageDisplayUnits, "V");
                element.FrequencyHz = GetSiValue(spec, "frequency", required: false, fallback: 1000.0, "hertz");
                ValidateFrequency(element);
                break;
            }
            case ElementType.DcCurrentSource:
            {
                double si = GetSiValue(spec, "current", required: true, fallback: double.NaN, "ampere");
                ApplyEngineering(element, si, CurrentDisplayUnits, "A");
                element.FrequencyHz = 1000.0;
                break;
            }
            case ElementType.AcCurrentSource:
            {
                double si = GetSiValue(spec, new[] { "amplitude", "current" }, required: true,
                    fallback: double.NaN, "ampere");
                ApplyEngineering(element, si, CurrentDisplayUnits, "A");
                element.FrequencyHz = GetSiValue(spec, "frequency", required: false, fallback: 1000.0, "hertz");
                ValidateFrequency(element);
                break;
            }
            case ElementType.Diode:
            {
                // Native Diode has no dedicated parameter slot; DisplayNumber/Value carry
                // the forward voltage drop in volts (schema marks it optional, default 0.7).
                double si = GetSiValue(spec, "forward_voltage", required: false, fallback: 0.7, "volt");
                element.DisplayNumber = RoundSignificant(si);
                element.UnitSymbol = "V";
                element.Value = element.DisplayNumber;
                element.FrequencyHz = 1000.0;
                break;
            }
            case ElementType.BjtNpn:
            case ElementType.BjtPnp:
            {
                // Native BJT strategies read beta from DisplayNumber.
                double beta = GetSiValue(spec, "beta", required: false, fallback: 100.0, "unitless");
                if (!(beta > 0) || !double.IsFinite(beta))
                    throw new InvalidDataException($"Web 元件 '{spec.Id}' 的 beta 必须大于 0。");
                element.DisplayNumber = RoundSignificant(beta);
                element.UnitSymbol = string.Empty;
                element.Value = element.DisplayNumber;
                element.FrequencyHz = 1000.0;
                break;
            }
            case ElementType.Mosfet:
            case ElementType.MosfetNEnh:
            case ElementType.MosfetPEnh:
            case ElementType.MosfetNDep:
            case ElementType.MosfetPDep:
            case ElementType.Jfet:
            case ElementType.JfetN:
            case ElementType.JfetP:
            {
                // Native FET teaching strategies are not implemented yet; keep the
                // transconductance for round-tripping and future use.
                double gm = GetSiValue(spec, "transconductance", required: false, fallback: 1e-3, "siemens");
                element.DisplayNumber = RoundSignificant(gm);
                element.UnitSymbol = "S";
                element.Value = element.DisplayNumber;
                element.FrequencyHz = 1000.0;
                break;
            }
            case ElementType.Vcvs:
            case ElementType.Cccs:
            {
                double gain = GetSiValue(spec, "gain", required: false, fallback: 1.0, "unitless");
                element.DisplayNumber = RoundSignificant(gain);
                element.UnitSymbol = string.Empty;
                element.Value = element.DisplayNumber;
                element.FrequencyHz = 1000.0;
                break;
            }
            case ElementType.Vccs:
            {
                double gm = GetSiValue(spec, "transconductance", required: false, fallback: 1e-3, "siemens");
                element.DisplayNumber = RoundSignificant(gm);
                element.UnitSymbol = "S";
                element.Value = element.DisplayNumber;
                element.FrequencyHz = 1000.0;
                break;
            }
            case ElementType.Ccvs:
            {
                double r = GetSiValue(spec, "transresistance", required: false, fallback: 1.0, "ohm");
                element.DisplayNumber = RoundSignificant(r);
                element.UnitSymbol = "Ω";
                element.Value = element.DisplayNumber;
                element.FrequencyHz = 1000.0;
                break;
            }
            case ElementType.Ground:
            case ElementType.VccPort:
            default:
                element.DisplayNumber = 0d;
                element.UnitSymbol = "V";
                element.Value = 0d;
                element.FrequencyHz = 1000.0;
                break;
        }
    }

    private static double GetSiValue(
        WebComponentSpec spec, string parameterName, bool required, double fallback, string quantity)
        => GetSiValue(spec, new[] { parameterName }, required, fallback, quantity);

    private static double GetSiValue(
        WebComponentSpec spec, string[] parameterNames, bool required, double fallback, string quantity)
    {
        foreach (var parameterName in parameterNames)
        {
            if (!spec.Parameters.TryGetValue(parameterName, out var parameter)) continue;
            double si = ConvertToSi(parameter.Value, parameter.Unit, quantity, spec.Id, parameterName);
            if (!double.IsFinite(si))
                throw new InvalidDataException($"Web 元件 '{spec.Id}' 的参数 '{parameterName}' 换算后不是有限数值。");
            return si;
        }
        if (required)
            throw new InvalidDataException(
                $"Web 元件 '{spec.Id}'（{spec.DefinitionId}）缺少必需参数 '{parameterNames[0]}'。");
        return fallback;
    }

    private static void ValidateFrequency(CircuitElement element)
    {
        if (!double.IsFinite(element.FrequencyHz) || element.FrequencyHz <= 0)
            throw new InvalidDataException(
                $"Web 元件 '{element.Id}' 的 frequency 必须为大于 0 的有限赫兹值。");
    }

    private static void ApplyEngineering(
        CircuitElement element, double siValue, (string Symbol, double Factor)[] displayUnits, string baseUnit)
    {
        if (!double.IsFinite(siValue))
            throw new InvalidDataException($"Web 元件 '{element.Id}' 的数值不是有限值。");
        var display = PickEngineeringDisplay(siValue, displayUnits, baseUnit);
        element.DisplayNumber = display.Display;
        element.UnitSymbol = display.Unit;
        element.Value = RoundSignificant(siValue);
    }

    private static readonly (string Symbol, double Factor)[] ResistanceDisplayUnits =
    [
        ("MΩ", 1e6),
        ("kΩ", 1e3),
        ("Ω", 1.0)
    ];

    // Ordered from the largest unit to the smallest: the first unit that keeps the
    // mantissa >= 1 wins, so 1e-6 F renders as 1 μF and 4.7e-9 F as 4.7 nF.
    private static readonly (string Symbol, double Factor)[] CapacitanceDisplayUnits =
    [
        ("F", 1.0),
        ("mF", 1e-3),
        ("μF", 1e-6),
        ("nF", 1e-9),
        ("pF", 1e-12)
    ];

    private static readonly (string Symbol, double Factor)[] InductanceDisplayUnits =
    [
        ("H", 1.0),
        ("mH", 1e-3),
        ("μH", 1e-6)
    ];

    private static readonly (string Symbol, double Factor)[] VoltageDisplayUnits =
    [
        ("kV", 1e3),
        ("V", 1.0),
        ("mV", 1e-3)
    ];

    private static readonly (string Symbol, double Factor)[] CurrentDisplayUnits =
    [
        ("A", 1.0),
        ("mA", 1e-3),
        ("μA", 1e-6)
    ];

    private readonly record struct EngineeringDisplay(double Display, string Unit);

    private static EngineeringDisplay PickEngineeringDisplay(
        double siValue, (string Symbol, double Factor)[] candidates, string zeroUnit)
    {
        if (siValue == 0) return new EngineeringDisplay(0d, zeroUnit);
        foreach (var candidate in candidates)
        {
            double scaled = siValue / candidate.Factor;
            if (Math.Abs(scaled) >= 1.0 - 1e-12)
                return new EngineeringDisplay(RoundSignificant(scaled), candidate.Symbol);
        }
        var smallest = candidates[^1];
        return new EngineeringDisplay(RoundSignificant(siValue / smallest.Factor), smallest.Symbol);
    }

    private static double RoundSignificant(double value, int significantDigits = 10)
    {
        if (!double.IsFinite(value) || value == 0) return value;
        int exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));
        int decimals = Math.Clamp(significantDigits - 1 - exponent, 0, 15);
        return Math.Round(value, decimals);
    }

    // ========================================================================
    // Unit conversion (Web parameter unit token -> SI multiplier)
    // ========================================================================

    private static double ConvertToSi(
        double value, string? unit, string quantity, string elementId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(unit)) return value; // schema stores SI base units already
        double? multiplier = GetUnitMultiplier(unit);
        if (multiplier == null)
            throw new InvalidDataException(
                $"Web 元件 '{elementId}' 的参数 '{parameterName}' 单位 '{unit}' 无法识别（期望 {quantity}）。");
        return value * multiplier.Value;
    }

    private static double? GetUnitMultiplier(string unit)
    {
        string normalized = unit.Trim().Replace('µ', 'μ').Replace(" ", string.Empty);
        switch (normalized)
        {
            // Resistance (ohm family)
            case "Ω": case "ohm": case "ohms": case "Ohm": case "Ohms": return 1.0;
            case "kΩ": case "kohm": case "kOhm": case "kOhms": case "kohms": return 1e3;
            case "MΩ": case "megohm": case "Megohm": case "Mohm": case "megaohm": return 1e6;
            case "mΩ": case "milliohm": case "mohm": return 1e-3;
            // Capacitance (farad family)
            case "F": case "f": case "farad": case "farads": return 1.0;
            case "mF": case "mf": case "millifarad": return 1e-3;
            case "μF": case "uF": case "uf": case "microfarad": return 1e-6;
            case "nF": case "nf": case "nanofarad": return 1e-9;
            case "pF": case "pf": case "picofarad": return 1e-12;
            // Inductance (henry family)
            case "H": case "h": case "henry": case "henries": return 1.0;
            case "mH": case "mh": case "millihenry": return 1e-3;
            case "μH": case "uH": case "uh": case "microhenry": return 1e-6;
            // Voltage (volt family)
            case "V": case "v": case "volt": case "volts": return 1.0;
            case "kV": case "kv": case "kilovolt": case "kilovolts": return 1e3;
            case "mV": case "mv": case "millivolt": case "millivolts": return 1e-3;
            // Current (ampere family)
            case "A": case "a": case "ampere": case "amperes": case "amp": case "amps": return 1.0;
            case "mA": case "ma": case "milliampere": case "milliamperes": return 1e-3;
            case "μA": case "uA": case "ua": case "microampere": case "microamperes": return 1e-6;
            // Frequency (hertz family)
            case "Hz": case "hz": case "hertz": return 1.0;
            case "kHz": case "khz": case "kilohertz": return 1e3;
            case "MHz": case "mhz": case "megahertz": return 1e6;
            case "GHz": case "ghz": case "gigahertz": return 1e9;
            // Transconductance / transresistance
            case "S": case "s": case "siemens": return 1.0;
            case "mS": case "ms": case "millisiemens": return 1e-3;
            case "μS": case "uS": case "us": case "microsiemens": return 1e-6;
            // Dimensionless
            case "": case "unitless": case "ratio": case "β": case "beta": return 1.0;
            default: return null;
        }
    }
}
