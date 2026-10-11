import type {
  CircuitGraph,
  ComponentDefinition,
  ComponentInstance,
  ParameterDefinition,
  ParameterValue,
  ValidationIssue
} from "./schemas.js";

type IssueInput = Omit<ValidationIssue, "path"> & { path?: Array<string | number> };

const issue = ({ severity, code, message, path = [] }: IssueInput): ValidationIssue => ({
  severity,
  code,
  message,
  path
});

const addDuplicateIssues = (
  issues: ValidationIssue[],
  values: string[],
  code: string,
  label: string,
  pathPrefix: string
) => {
  const seen = new Set<string>();
  const duplicates = new Set<string>();

  values.forEach((value) => {
    if (seen.has(value)) {
      duplicates.add(value);
    }
    seen.add(value);
  });

  duplicates.forEach((value) => {
    issues.push(
      issue({
        severity: "error",
        code,
        message: `Duplicate ${label} id "${value}".`,
        path: [pathPrefix, value]
      })
    );
  });
};

const typeOfParameterValue = (value: ParameterValue["value"]) => {
  if (typeof value === "number") return "number";
  if (typeof value === "boolean") return "boolean";
  return "string";
};

const validateParameter = (
  parameter: ParameterValue,
  definition: ParameterDefinition,
  component: ComponentInstance,
  issues: ValidationIssue[]
) => {
  const actualType = typeOfParameterValue(parameter.value);

  if (actualType !== definition.valueType) {
    issues.push(
      issue({
        severity: "error",
        code: "invalid_parameter_type",
        message: `Component "${component.id}" parameter "${definition.id}" must be ${definition.valueType}, got ${actualType}.`,
        path: ["components", component.id, "parameters", definition.id]
      })
    );
  }

  if (definition.unit && parameter.unit && definition.unit !== parameter.unit) {
    issues.push(
      issue({
        severity: "error",
        code: "invalid_parameter_unit",
        message: `Component "${component.id}" parameter "${definition.id}" must use unit "${definition.unit}", got "${parameter.unit}".`,
        path: ["components", component.id, "parameters", definition.id, "unit"]
      })
    );
  }

  if (typeof parameter.value === "number") {
    if (definition.min !== undefined && parameter.value < definition.min) {
      issues.push(
        issue({
          severity: "error",
          code: "parameter_below_minimum",
          message: `Component "${component.id}" parameter "${definition.id}" is below minimum ${definition.min}.`,
          path: ["components", component.id, "parameters", definition.id]
        })
      );
    }

    if (definition.max !== undefined && parameter.value > definition.max) {
      issues.push(
        issue({
          severity: "error",
          code: "parameter_above_maximum",
          message: `Component "${component.id}" parameter "${definition.id}" is above maximum ${definition.max}.`,
          path: ["components", component.id, "parameters", definition.id]
        })
      );
    }
  }

  if (definition.allowedValues && !definition.allowedValues.includes(parameter.value)) {
    issues.push(
      issue({
        severity: "error",
        code: "parameter_value_not_allowed",
        message: `Component "${component.id}" parameter "${definition.id}" has unsupported value "${String(parameter.value)}".`,
        path: ["components", component.id, "parameters", definition.id]
      })
    );
  }
};

export const validateCircuitGraph = (
  graph: CircuitGraph,
  definitions: ComponentDefinition[]
): ValidationIssue[] => {
  const issues: ValidationIssue[] = [];
  const nodeIds = graph.nodes.map((node) => node.id);
  const componentIds = graph.components.map((component) => component.id);
  const connectionIds = graph.connections.map((connection) => connection.id);
  const graphPortIds = graph.ports.map((port) => port.id);

  addDuplicateIssues(issues, nodeIds, "duplicate_node_id", "node", "nodes");
  addDuplicateIssues(issues, componentIds, "duplicate_component_id", "component", "components");
  addDuplicateIssues(issues, connectionIds, "duplicate_connection_id", "connection", "connections");
  addDuplicateIssues(issues, graphPortIds, "duplicate_graph_port_id", "graph port", "ports");

  const nodesById = new Map(graph.nodes.map((node) => [node.id, node]));
  const componentsById = new Map(graph.components.map((component) => [component.id, component]));
  const definitionsById = new Map(definitions.map((definition) => [definition.id, definition]));

  graph.ports.forEach((port) => {
    if (!nodesById.has(port.nodeId)) {
      issues.push(
        issue({
          severity: "error",
          code: "graph_port_unknown_node",
          message: `Graph port "${port.id}" references unknown node "${port.nodeId}".`,
          path: ["ports", port.id, "nodeId"]
        })
      );
    }
  });

  graph.components.forEach((component) => {
    const definition = definitionsById.get(component.definitionId);

    if (!definition) {
      issues.push(
        issue({
          severity: "error",
          code: "unknown_component_definition",
          message: `Component "${component.id}" references unknown definition "${component.definitionId}".`,
          path: ["components", component.id, "definitionId"]
        })
      );
      return;
    }

    const definitionPortIds = new Set(definition.ports.map((port) => port.id));
    const instancePortIds = new Set(component.ports);

    definition.ports
      .filter((port) => port.required)
      .forEach((port) => {
        if (!instancePortIds.has(port.id)) {
          issues.push(
            issue({
              severity: "error",
              code: "missing_required_port",
              message: `Component "${component.id}" is missing required port "${port.id}".`,
              path: ["components", component.id, "ports"]
            })
          );
        }
      });

    component.ports.forEach((portId) => {
      if (!definitionPortIds.has(portId)) {
        issues.push(
          issue({
            severity: "error",
            code: "invalid_component_port",
            message: `Component "${component.id}" uses port "${portId}" not defined by "${definition.id}".`,
            path: ["components", component.id, "ports", portId]
          })
        );
      }
    });

    if (!definition.allowedModelVariants.includes(component.modelVariant)) {
      issues.push(
        issue({
          severity: "error",
          code: "invalid_model_variant",
          message: `Component "${component.id}" model variant "${component.modelVariant}" is not allowed by "${definition.id}".`,
          path: ["components", component.id, "modelVariant"]
        })
      );
    }

    definition.parameters
      .filter((parameter) => parameter.required)
      .forEach((parameter) => {
        if (!(parameter.id in component.parameters)) {
          issues.push(
            issue({
              severity: "error",
              code: "missing_required_parameter",
              message: `Component "${component.id}" is missing required parameter "${parameter.id}".`,
              path: ["components", component.id, "parameters"]
            })
          );
        }
      });

    const parametersById = new Map(definition.parameters.map((parameter) => [parameter.id, parameter]));
    Object.entries(component.parameters).forEach(([parameterId, value]) => {
      const parameterDefinition = parametersById.get(parameterId);
      if (!parameterDefinition) {
        issues.push(
          issue({
            severity: "warning",
            code: "unknown_parameter",
            message: `Component "${component.id}" has parameter "${parameterId}" not defined by "${definition.id}".`,
            path: ["components", component.id, "parameters", parameterId]
          })
        );
        return;
      }

      validateParameter(value, parameterDefinition, component, issues);
    });
  });

  const connectedPortKeys = new Set<string>();
  const connectionCountByNode = new Map<string, number>();

  graph.connections.forEach((connection) => {
    const component = componentsById.get(connection.componentId);

    if (!nodesById.has(connection.nodeId)) {
      issues.push(
        issue({
          severity: "error",
          code: "connection_unknown_node",
          message: `Connection "${connection.id}" references unknown node "${connection.nodeId}".`,
          path: ["connections", connection.id, "nodeId"]
        })
      );
    } else {
      connectionCountByNode.set(connection.nodeId, (connectionCountByNode.get(connection.nodeId) ?? 0) + 1);
    }

    if (!component) {
      issues.push(
        issue({
          severity: "error",
          code: "connection_unknown_component",
          message: `Connection "${connection.id}" references unknown component "${connection.componentId}".`,
          path: ["connections", connection.id, "componentId"]
        })
      );
      return;
    }

    if (!component.ports.includes(connection.portId)) {
      issues.push(
        issue({
          severity: "error",
          code: "connection_unknown_port",
          message: `Connection "${connection.id}" references port "${connection.portId}" not present on component "${component.id}".`,
          path: ["connections", connection.id, "portId"]
        })
      );
      return;
    }

    const portKey = `${connection.componentId}:${connection.portId}`;
    if (connectedPortKeys.has(portKey)) {
      issues.push(
        issue({
          severity: "error",
          code: "port_connected_multiple_times",
          message: `Port "${portKey}" is connected more than once.`,
          path: ["connections", connection.id]
        })
      );
    }
    connectedPortKeys.add(portKey);
  });

  graph.components.forEach((component) => {
    component.ports.forEach((portId) => {
      const portKey = `${component.id}:${portId}`;
      if (!connectedPortKeys.has(portKey)) {
        issues.push(
          issue({
            severity: "error",
            code: "unconnected_component_port",
            message: `Component "${component.id}" port "${portId}" is not connected to any node.`,
            path: ["components", component.id, "ports", portId]
          })
        );
      }
    });
  });

  graph.nodes.forEach((node) => {
    const connectionCount = connectionCountByNode.get(node.id) ?? 0;
    const hasGraphPort = graph.ports.some((port) => port.nodeId === node.id);
    if (node.kind !== "reference" && !hasGraphPort && connectionCount < 2) {
      issues.push(
        issue({
          severity: "error",
          code: "dangling_node",
          message: `Node "${node.id}" has only ${connectionCount} connection(s).`,
          path: ["nodes", node.id]
        })
      );
    }
  });

  return issues;
};
