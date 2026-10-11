import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { benchmarkCaseSchema, validateCircuitGraph } from "../packages/schema/src/index.js";
import { componentDefinitions } from "../packages/components/src/index.js";

const examplesDir = join(process.cwd(), "benchmarks", "examples");
const files = (await readdir(examplesDir)).filter((file) => file.endsWith(".json"));

let hasFailure = false;

for (const file of files) {
  const fullPath = join(examplesDir, file);
  const raw = await readFile(fullPath, "utf8");
  const parsedJson = JSON.parse(raw) as unknown;
  const parsedBenchmark = benchmarkCaseSchema.safeParse(parsedJson);

  if (!parsedBenchmark.success) {
    hasFailure = true;
    console.error(`✗ ${file}: schema validation failed`);
    console.error(parsedBenchmark.error.format());
    continue;
  }

  const graphIssues = validateCircuitGraph(parsedBenchmark.data.circuitGraph, componentDefinitions);
  const graphErrors = graphIssues.filter((issue) => issue.severity === "error");

  if (graphErrors.length > 0) {
    hasFailure = true;
    console.error(`✗ ${file}: graph validation failed`);
    graphErrors.forEach((issue) => {
      console.error(`  - ${issue.code}: ${issue.message}`);
    });
    continue;
  }

  console.log(`✓ ${file}`);
}

if (hasFailure) {
  process.exit(1);
}

console.log(`Validated ${files.length} benchmark example(s).`);
