import { z } from "zod";

export const idSchema = z
  .string()
  .min(1)
  .regex(/^[a-zA-Z][a-zA-Z0-9_.:-]*$/, "IDs must start with a letter and contain only letters, numbers, _, ., :, or -");

export const positionSchema = z
  .object({
    x: z.number(),
    y: z.number()
  })
  .strict();

export const portRoleSchema = z.enum([
  "positive",
  "negative",
  "reference",
  "passive",
  "anode",
  "cathode",
  "collector",
  "base",
  "emitter",
  "drain",
  "gate",
  "source",
  "bulk",
  "primary",
  "secondary",
  "control_positive",
  "control_negative",
  "sense_positive",
  "sense_negative"
]);

export const quantitySchema = z.enum([
  "resistance",
  "capacitance",
  "inductance",
  "voltage",
  "current",
  "frequency",
  "gain",
  "transconductance",
  "transresistance",
  "turns_ratio",
  "beta",
  "threshold_voltage",
  "emission_coefficient",
  "saturation_current",
  "unitless"
]);

export const unitSchema = z.enum([
  "ohm",
  "farad",
  "henry",
  "volt",
  "ampere",
  "hertz",
  "siemens",
  "volt_per_ampere",
  "ampere_per_volt",
  "ratio",
  "unitless"
]);

export const componentFamilySchema = z.enum([
  "passive",
  "independent_source",
  "controlled_source",
  "diode",
  "bjt",
  "fet",
  "reference"
]);

export const parameterValueSchema = z
  .object({
    value: z.union([z.number(), z.string(), z.boolean()]),
    unit: unitSchema.optional(),
    source: z.enum(["user", "vision", "benchmark", "default"]).optional()
  })
  .strict();

export const parameterDefinitionSchema = z
  .object({
    id: idSchema,
    label: z.string().min(1),
    quantity: quantitySchema,
    unit: unitSchema.optional(),
    valueType: z.enum(["number", "string", "boolean"]).default("number"),
    required: z.boolean().default(true),
    defaultValue: parameterValueSchema.optional(),
    min: z.number().optional(),
    max: z.number().optional(),
    allowedValues: z.array(z.union([z.string(), z.number(), z.boolean()])).optional(),
    description: z.string().optional()
  })
  .strict();

export const portDefinitionSchema = z
  .object({
    id: idSchema,
    label: z.string().min(1),
    role: portRoleSchema,
    required: z.boolean().default(true),
    description: z.string().optional()
  })
  .strict();

export const componentDefinitionSchema = z
  .object({
    id: idSchema,
    displayName: z.string().min(1),
    family: componentFamilySchema,
    ports: z.array(portDefinitionSchema).min(1),
    parameters: z.array(parameterDefinitionSchema).default([]),
    allowedModelVariants: z.array(idSchema).min(1),
    defaultModelVariant: idSchema,
    analysisTags: z.array(z.string().min(1)).default([]),
    teachingTags: z.array(z.string().min(1)).default([])
  })
  .strict();

export const nodeSchema = z
  .object({
    id: idSchema,
    label: z.string().optional(),
    kind: z.enum(["electrical", "reference"]).default("electrical"),
    position: positionSchema.optional()
  })
  .strict();

export const graphPortSchema = z
  .object({
    id: idSchema,
    label: z.string().min(1),
    nodeId: idSchema,
    role: z.enum(["input", "output", "reference", "probe"]).default("probe")
  })
  .strict();

export const componentInstanceSchema = z
  .object({
    id: idSchema,
    definitionId: idSchema,
    label: z.string().min(1),
    ports: z.array(idSchema).min(1),
    parameters: z.record(parameterValueSchema).default({}),
    modelVariant: idSchema,
    position: positionSchema.optional(),
    /** Schematic rotation in degrees (clockwise), preserved across save/load. */
    rotation: z.number().optional()
  })
  .strict();

export const connectionSchema = z
  .object({
    id: idSchema,
    nodeId: idSchema,
    componentId: idSchema,
    portId: idSchema
  })
  .strict();

export const validationIssueSchema = z
  .object({
    severity: z.enum(["error", "warning", "info"]),
    code: z.string().min(1),
    message: z.string().min(1),
    path: z.array(z.union([z.string(), z.number()])).default([])
  })
  .strict();

export const circuitGraphSchema = z
  .object({
    id: idSchema,
    version: z.string().min(1),
    title: z.string().optional(),
    nodes: z.array(nodeSchema).min(1),
    components: z.array(componentInstanceSchema).min(1),
    ports: z.array(graphPortSchema).default([]),
    connections: z.array(connectionSchema).min(1),
    metadata: z.record(z.union([z.string(), z.number(), z.boolean(), z.array(z.string())])).default({}),
    validationIssues: z.array(validationIssueSchema).default([])
  })
  .strict();

export const benchmarkSourceSchema = z
  .object({
    kind: z.enum(["textbook", "exercise_book", "synthetic", "unknown"]),
    title: z.string().optional(),
    page: z.string().optional(),
    note: z.string().optional()
  })
  .strict();

export const imageRefSchema = z
  .object({
    id: idSchema,
    path: z.string().min(1),
    kind: z.enum(["original", "crop", "processed"]).default("original"),
    crop: z
      .object({
        x: z.number().nonnegative(),
        y: z.number().nonnegative(),
        width: z.number().positive(),
        height: z.number().positive()
      })
      .strict()
      .optional()
  })
  .strict();

export const expectedResultSchema = z
  .object({
    id: idSchema,
    quantity: z.string().min(1),
    expression: z.string().min(1),
    unit: unitSchema.optional(),
    tolerance: z.number().nonnegative().optional()
  })
  .strict();

export const groundTruthSchema = z
  .object({
    expectedComponentIds: z.array(idSchema),
    expectedNodeIds: z.array(idSchema),
    expectedConnectionCount: z.number().int().nonnegative(),
    equations: z
      .array(
        z
          .object({
            id: idSchema,
            latex: z.string().min(1),
            variables: z.array(z.string()).default([])
          })
          .strict()
      )
      .default([]),
    expectedResults: z.array(expectedResultSchema).default([]),
    acceptedEquivalentExpressions: z.array(z.string()).default([]),
    teachingTags: z.array(z.string()).default([])
  })
  .strict();

export const benchmarkCaseSchema = z
  .object({
    id: idSchema,
    title: z.string().min(1),
    source: benchmarkSourceSchema,
    problemText: z.string().min(1),
    imageRefs: z.array(imageRefSchema).default([]),
    circuitGraph: circuitGraphSchema,
    groundTruth: groundTruthSchema,
    difficultyTags: z.array(z.string().min(1)).default([])
  })
  .strict();

export type Connection = z.infer<typeof connectionSchema>;
export type ParameterValue = z.infer<typeof parameterValueSchema>;
export type ParameterDefinition = z.infer<typeof parameterDefinitionSchema>;
export type PortDefinition = z.infer<typeof portDefinitionSchema>;
export type ComponentDefinition = z.infer<typeof componentDefinitionSchema>;
export type ComponentInstance = z.infer<typeof componentInstanceSchema>;
export type CircuitGraph = z.infer<typeof circuitGraphSchema>;
export type BenchmarkCase = z.infer<typeof benchmarkCaseSchema>;
export type ValidationIssue = z.infer<typeof validationIssueSchema>;
