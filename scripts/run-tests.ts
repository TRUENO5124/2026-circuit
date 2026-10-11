import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import {
  benchmarkCaseSchema,
  circuitGraphSchema,
  componentDefinitionSchema,
  validateCircuitGraph,
  type BenchmarkCase,
  type CircuitGraph
} from "../packages/schema/src/index.js";
import { componentDefinitions } from "../packages/components/src/index.js";

type TestCase = {
  name: string;
  run: () => void;
};

const resistorDefinition = componentDefinitions.find((definition) => definition.id === "passive.resistor");
const voltageSourceDefinition = componentDefinitions.find((definition) => definition.id === "source.v_dc");
const groundDefinition = componentDefinitions.find((definition) => definition.id === "reference.ground");
const mosfetDefinition = componentDefinitions.find((definition) => definition.id === "fet.mosfet");
const acSourceDefinition = componentDefinitions.find((definition) => definition.id === "source.v_ac_small_signal");
const circuitNodeSource = fs.readFileSync(
  path.resolve(process.cwd(), "packages/editor/src/components/nodes/CircuitNode.tsx"),
  "utf8"
);
const circuitCanvasSource = fs.readFileSync(
  path.resolve(process.cwd(), "packages/editor/src/components/editor/CircuitCanvas.tsx"),
  "utf8"
);

const validGraph: CircuitGraph = {
  id: "graph.valid_voltage_divider",
  version: "0.1.0",
  nodes: [
    { id: "n_in", kind: "electrical" },
    { id: "n_mid", kind: "electrical" },
    { id: "n_gnd", kind: "reference" }
  ],
  components: [
    {
      id: "v1",
      definitionId: "source.v_dc",
      label: "V1",
      ports: ["p", "n"],
      parameters: { voltage: { value: 5, unit: "volt" } },
      modelVariant: "ideal_dc_voltage_source"
    },
    {
      id: "r1",
      definitionId: "passive.resistor",
      label: "R1",
      ports: ["p", "n"],
      parameters: { resistance: { value: 1000, unit: "ohm" } },
      modelVariant: "ideal_resistor"
    },
    {
      id: "r2",
      definitionId: "passive.resistor",
      label: "R2",
      ports: ["p", "n"],
      parameters: { resistance: { value: 1000, unit: "ohm" } },
      modelVariant: "ideal_resistor"
    },
    {
      id: "gnd",
      definitionId: "reference.ground",
      label: "GND",
      ports: ["ref"],
      parameters: {},
      modelVariant: "ideal_ground"
    }
  ],
  ports: [
    { id: "input", label: "Input", nodeId: "n_in", role: "input" },
    { id: "output", label: "Output", nodeId: "n_mid", role: "output" },
    { id: "reference", label: "Reference", nodeId: "n_gnd", role: "reference" }
  ],
  connections: [
    { id: "c_v1_p", nodeId: "n_in", componentId: "v1", portId: "p" },
    { id: "c_v1_n", nodeId: "n_gnd", componentId: "v1", portId: "n" },
    { id: "c_r1_p", nodeId: "n_in", componentId: "r1", portId: "p" },
    { id: "c_r1_n", nodeId: "n_mid", componentId: "r1", portId: "n" },
    { id: "c_r2_p", nodeId: "n_mid", componentId: "r2", portId: "p" },
    { id: "c_r2_n", nodeId: "n_gnd", componentId: "r2", portId: "n" },
    { id: "c_gnd_ref", nodeId: "n_gnd", componentId: "gnd", portId: "ref" }
  ],
  metadata: {},
  validationIssues: []
};

const validBenchmark: BenchmarkCase = {
  id: "bench.valid_001",
  title: "Valid benchmark",
  source: { kind: "synthetic" },
  problemText: "Find the output voltage.",
  imageRefs: [],
  circuitGraph: validGraph,
  groundTruth: {
    expectedComponentIds: ["v1", "r1", "r2", "gnd"],
    expectedNodeIds: ["n_in", "n_mid", "n_gnd"],
    expectedConnectionCount: 7,
    equations: [],
    expectedResults: [],
    acceptedEquivalentExpressions: [],
    teachingTags: []
  },
  difficultyTags: ["test"]
};

const tests: TestCase[] = [
  {
    name: "CircuitGraph schema accepts valid graph",
    run: () => assert.doesNotThrow(() => circuitGraphSchema.parse(validGraph))
  },
  {
    name: "ComponentDefinition schema accepts valid definition",
    run: () => {
      assert.ok(resistorDefinition);
      assert.doesNotThrow(() => componentDefinitionSchema.parse(resistorDefinition));
    }
  },
  {
    name: "BenchmarkCase schema accepts valid benchmark",
    run: () => assert.doesNotThrow(() => benchmarkCaseSchema.parse(validBenchmark))
  },
  {
    name: "schema rejects malformed ids",
    run: () => assert.equal(circuitGraphSchema.safeParse({ ...validGraph, id: "1 bad id" }).success, false)
  },
  {
    name: "imageRefs can stay empty until textbook photos are provided",
    run: () => assert.equal(benchmarkCaseSchema.safeParse({ ...validBenchmark, imageRefs: [] }).success, true)
  },
  {
    name: "component library includes DC and AC small-signal independent sources",
    run: () => {
      assert.ok(voltageSourceDefinition);
      assert.ok(acSourceDefinition);
    }
  },
  {
    name: "component library includes ground and MOSFET model variants",
    run: () => {
      assert.ok(groundDefinition);
      assert.ok(mosfetDefinition);
      assert.equal(
        ["n_enhancement", "p_enhancement", "n_depletion", "p_depletion"].every((variant) =>
          mosfetDefinition.allowedModelVariants.includes(variant)
        ),
        true
      );
    }
  },
  {
    name: "editor ports can initiate loose wire connections",
    run: () => {
      assert.match(circuitNodeSource, /<Handle[^>]*type="source"/s);
      assert.match(circuitCanvasSource, /connectionMode=\{ConnectionMode\.Loose\}/);
    }
  },
  {
    name: "JFET is modeled as FET family, not MOS subtype",
    run: () => {
      const jfet = componentDefinitions.find((definition) => definition.id === "fet.jfet");
      assert.equal(jfet?.family, "fet");
      assert.equal(jfet?.id.includes("mos"), false);
    }
  },
  {
    name: "graph validator accepts valid references",
    run: () => assert.deepEqual(validateCircuitGraph(validGraph, componentDefinitions), [])
  },
  {
    name: "graph validator detects duplicate component ids",
    run: () => {
      const graph = {
        ...validGraph,
        components: [
          validGraph.components[0],
          { ...validGraph.components[1], id: validGraph.components[0].id }
        ]
      };

      assert.ok(validateCircuitGraph(graph, componentDefinitions).some((issue) => issue.code === "duplicate_component_id"));
    }
  },
  {
    name: "graph validator detects unknown component definitions",
    run: () => {
      const graph = {
        ...validGraph,
        components: [{ ...validGraph.components[0], definitionId: "missing.definition" }]
      };

      assert.ok(validateCircuitGraph(graph, componentDefinitions).some((issue) => issue.code === "unknown_component_definition"));
    }
  },
  {
    name: "graph validator detects missing required ports",
    run: () => {
      const graph = {
        ...validGraph,
        components: [{ ...validGraph.components[1], ports: ["p"] }]
      };

      assert.ok(validateCircuitGraph(graph, componentDefinitions).some((issue) => issue.code === "missing_required_port"));
    }
  },
  {
    name: "graph validator detects dangling nodes",
    run: () => {
      const graph = {
        ...validGraph,
        nodes: [...validGraph.nodes, { id: "n_dangling", kind: "electrical" as const }]
      };

      assert.ok(validateCircuitGraph(graph, componentDefinitions).some((issue) => issue.code === "dangling_node"));
    }
  },
  {
    name: "graph validator detects invalid parameter units",
    run: () => {
      const graph = {
        ...validGraph,
        components: [
          {
            ...validGraph.components[1],
            parameters: { resistance: { value: 1000, unit: "volt" as const } }
          }
        ]
      };

      assert.ok(validateCircuitGraph(graph, componentDefinitions).some((issue) => issue.code === "invalid_parameter_unit"));
    }
  },
  {
    name: "graph validator detects invalid parameter values",
    run: () => {
      const graph = {
        ...validGraph,
        components: [
          {
            ...validGraph.components[1],
            parameters: { resistance: { value: -1, unit: "ohm" as const } }
          }
        ]
      };

      assert.ok(validateCircuitGraph(graph, componentDefinitions).some((issue) => issue.code === "parameter_below_minimum"));
    }
  }
];

let failed = 0;

for (const test of tests) {
  try {
    test.run();
    console.log(`✓ ${test.name}`);
  } catch (error) {
    failed += 1;
    console.error(`✗ ${test.name}`);
    console.error(error);
  }
}

if (failed > 0) {
  console.error(`${failed}/${tests.length} tests failed.`);
  process.exit(1);
}

console.log(`${tests.length} tests passed.`);
