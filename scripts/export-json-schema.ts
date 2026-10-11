import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { zodToJsonSchema } from "zod-to-json-schema";
import {
  benchmarkCaseSchema,
  circuitGraphSchema,
  componentDefinitionSchema
} from "../packages/schema/src/index.js";

const outDir = join(process.cwd(), "generated", "schema");
await mkdir(outDir, { recursive: true });

const schemas = [
  ["circuit-graph.schema.json", circuitGraphSchema, "CircuitGraph"],
  ["component-definition.schema.json", componentDefinitionSchema, "ComponentDefinition"],
  ["benchmark-case.schema.json", benchmarkCaseSchema, "BenchmarkCase"]
] as const;

for (const [fileName, schema, name] of schemas) {
  const jsonSchema = zodToJsonSchema(schema, name);
  await writeFile(join(outDir, fileName), `${JSON.stringify(jsonSchema, null, 2)}\n`, "utf8");
  console.log(`Wrote generated/schema/${fileName}`);
}
