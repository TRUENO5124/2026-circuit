import type { ComponentDefinition } from "../../schema/src/index.js";

const numericParameter = (
  id: string,
  label: string,
  quantity: ComponentDefinition["parameters"][number]["quantity"],
  unit: ComponentDefinition["parameters"][number]["unit"],
  required = true,
  min?: number
): ComponentDefinition["parameters"][number] => ({
  id,
  label,
  quantity,
  unit,
  valueType: "number",
  required,
  ...(min !== undefined ? { min } : {})
});

export const componentDefinitions: ComponentDefinition[] = [
  {
    id: "reference.ground",
    displayName: "Ground",
    family: "reference",
    ports: [{ id: "ref", label: "Reference", role: "reference", required: true }],
    parameters: [],
    allowedModelVariants: ["ideal_ground"],
    defaultModelVariant: "ideal_ground",
    analysisTags: ["reference_node"],
    teachingTags: ["ground", "reference"]
  },
  {
    id: "passive.resistor",
    displayName: "Resistor",
    family: "passive",
    ports: [
      { id: "p", label: "Positive", role: "passive", required: true },
      { id: "n", label: "Negative", role: "passive", required: true }
    ],
    parameters: [numericParameter("resistance", "Resistance", "resistance", "ohm", true, 0)],
    allowedModelVariants: ["ideal_resistor"],
    defaultModelVariant: "ideal_resistor",
    analysisTags: ["ohm_law", "linear"],
    teachingTags: ["resistance", "voltage_divider"]
  },
  {
    id: "passive.capacitor",
    displayName: "Capacitor",
    family: "passive",
    ports: [
      { id: "p", label: "Positive", role: "passive", required: true },
      { id: "n", label: "Negative", role: "passive", required: true }
    ],
    parameters: [numericParameter("capacitance", "Capacitance", "capacitance", "farad", true, 0)],
    allowedModelVariants: ["ideal_capacitor"],
    defaultModelVariant: "ideal_capacitor",
    analysisTags: ["reactive", "small_signal"],
    teachingTags: ["capacitance", "coupling", "bypass"]
  },
  {
    id: "passive.inductor",
    displayName: "Inductor",
    family: "passive",
    ports: [
      { id: "p", label: "Positive", role: "passive", required: true },
      { id: "n", label: "Negative", role: "passive", required: true }
    ],
    parameters: [numericParameter("inductance", "Inductance", "inductance", "henry", true, 0)],
    allowedModelVariants: ["ideal_inductor"],
    defaultModelVariant: "ideal_inductor",
    analysisTags: ["reactive", "small_signal"],
    teachingTags: ["inductance", "energy_storage"]
  },
  {
    id: "passive.transformer",
    displayName: "Transformer",
    family: "passive",
    ports: [
      { id: "p1", label: "Primary +", role: "primary", required: true },
      { id: "n1", label: "Primary -", role: "primary", required: true },
      { id: "p2", label: "Secondary +", role: "secondary", required: true },
      { id: "n2", label: "Secondary -", role: "secondary", required: true }
    ],
    parameters: [numericParameter("turns_ratio", "Turns Ratio", "turns_ratio", "ratio", true, 0)],
    allowedModelVariants: ["ideal_transformer"],
    defaultModelVariant: "ideal_transformer",
    analysisTags: ["coupled", "ac"],
    teachingTags: ["transformer", "impedance_reflection"]
  },
  {
    id: "source.v_dc",
    displayName: "DC Voltage Source",
    family: "independent_source",
    ports: [
      { id: "p", label: "Positive", role: "positive", required: true },
      { id: "n", label: "Negative", role: "negative", required: true }
    ],
    parameters: [numericParameter("voltage", "Voltage", "voltage", "volt")],
    allowedModelVariants: ["ideal_dc_voltage_source"],
    defaultModelVariant: "ideal_dc_voltage_source",
    analysisTags: ["dc_bias", "source"],
    teachingTags: ["voltage_source", "bias"]
  },
  {
    id: "source.i_dc",
    displayName: "DC Current Source",
    family: "independent_source",
    ports: [
      { id: "p", label: "Positive", role: "positive", required: true },
      { id: "n", label: "Negative", role: "negative", required: true }
    ],
    parameters: [numericParameter("current", "Current", "current", "ampere")],
    allowedModelVariants: ["ideal_dc_current_source"],
    defaultModelVariant: "ideal_dc_current_source",
    analysisTags: ["dc_bias", "source"],
    teachingTags: ["current_source", "bias"]
  },
  {
    id: "source.v_ac_small_signal",
    displayName: "AC Small-Signal Voltage Source",
    family: "independent_source",
    ports: [
      { id: "p", label: "Positive", role: "positive", required: true },
      { id: "n", label: "Negative", role: "negative", required: true }
    ],
    parameters: [
      numericParameter("amplitude", "Amplitude", "voltage", "volt", true, 0),
      numericParameter("frequency", "Frequency", "frequency", "hertz", false, 0)
    ],
    allowedModelVariants: ["ideal_ac_small_signal_voltage_source"],
    defaultModelVariant: "ideal_ac_small_signal_voltage_source",
    analysisTags: ["small_signal", "source"],
    teachingTags: ["small_signal_source", "ac_analysis"]
  },
  {
    id: "source.i_ac_small_signal",
    displayName: "AC Small-Signal Current Source",
    family: "independent_source",
    ports: [
      { id: "p", label: "Positive", role: "positive", required: true },
      { id: "n", label: "Negative", role: "negative", required: true }
    ],
    parameters: [
      numericParameter("amplitude", "Amplitude", "current", "ampere", true, 0),
      numericParameter("frequency", "Frequency", "frequency", "hertz", false, 0)
    ],
    allowedModelVariants: ["ideal_ac_small_signal_current_source"],
    defaultModelVariant: "ideal_ac_small_signal_current_source",
    analysisTags: ["small_signal", "source"],
    teachingTags: ["small_signal_source", "ac_analysis"]
  },
  {
    id: "controlled.vcvs",
    displayName: "Voltage-Controlled Voltage Source",
    family: "controlled_source",
    ports: [
      { id: "p", label: "Output +", role: "positive", required: true },
      { id: "n", label: "Output -", role: "negative", required: true },
      { id: "cp", label: "Control +", role: "control_positive", required: true },
      { id: "cn", label: "Control -", role: "control_negative", required: true }
    ],
    parameters: [numericParameter("gain", "Voltage Gain", "gain", "unitless")],
    allowedModelVariants: ["ideal_vcvs"],
    defaultModelVariant: "ideal_vcvs",
    analysisTags: ["dependent_source", "linear"],
    teachingTags: ["controlled_source", "vcvs"]
  },
  {
    id: "controlled.vccs",
    displayName: "Voltage-Controlled Current Source",
    family: "controlled_source",
    ports: [
      { id: "p", label: "Output +", role: "positive", required: true },
      { id: "n", label: "Output -", role: "negative", required: true },
      { id: "cp", label: "Control +", role: "control_positive", required: true },
      { id: "cn", label: "Control -", role: "control_negative", required: true }
    ],
    parameters: [numericParameter("transconductance", "Transconductance", "transconductance", "siemens")],
    allowedModelVariants: ["ideal_vccs"],
    defaultModelVariant: "ideal_vccs",
    analysisTags: ["dependent_source", "linear"],
    teachingTags: ["controlled_source", "vccs"]
  },
  {
    id: "controlled.ccvs",
    displayName: "Current-Controlled Voltage Source",
    family: "controlled_source",
    ports: [
      { id: "p", label: "Output +", role: "positive", required: true },
      { id: "n", label: "Output -", role: "negative", required: true },
      { id: "sp", label: "Sense +", role: "sense_positive", required: true },
      { id: "sn", label: "Sense -", role: "sense_negative", required: true }
    ],
    parameters: [numericParameter("transresistance", "Transresistance", "transresistance", "volt_per_ampere")],
    allowedModelVariants: ["ideal_ccvs"],
    defaultModelVariant: "ideal_ccvs",
    analysisTags: ["dependent_source", "linear"],
    teachingTags: ["controlled_source", "ccvs"]
  },
  {
    id: "controlled.cccs",
    displayName: "Current-Controlled Current Source",
    family: "controlled_source",
    ports: [
      { id: "p", label: "Output +", role: "positive", required: true },
      { id: "n", label: "Output -", role: "negative", required: true },
      { id: "sp", label: "Sense +", role: "sense_positive", required: true },
      { id: "sn", label: "Sense -", role: "sense_negative", required: true }
    ],
    parameters: [numericParameter("gain", "Current Gain", "gain", "unitless")],
    allowedModelVariants: ["ideal_cccs"],
    defaultModelVariant: "ideal_cccs",
    analysisTags: ["dependent_source", "linear"],
    teachingTags: ["controlled_source", "cccs"]
  },
  {
    id: "diode.standard",
    displayName: "Diode",
    family: "diode",
    ports: [
      { id: "anode", label: "Anode", role: "anode", required: true },
      { id: "cathode", label: "Cathode", role: "cathode", required: true }
    ],
    parameters: [
      numericParameter("forward_voltage", "Forward Voltage", "voltage", "volt", false, 0),
      numericParameter("saturation_current", "Saturation Current", "saturation_current", "ampere", false, 0)
    ],
    allowedModelVariants: ["ideal_diode", "constant_voltage_drop", "shockley"],
    defaultModelVariant: "constant_voltage_drop",
    analysisTags: ["nonlinear", "piecewise_linear"],
    teachingTags: ["diode", "rectifier", "limiter", "clamp"]
  },
  {
    id: "bjt.npn",
    displayName: "NPN BJT",
    family: "bjt",
    ports: [
      { id: "collector", label: "Collector", role: "collector", required: true },
      { id: "base", label: "Base", role: "base", required: true },
      { id: "emitter", label: "Emitter", role: "emitter", required: true }
    ],
    parameters: [
      numericParameter("beta", "Current Gain", "beta", "unitless", false, 0),
      numericParameter("vbe_on", "VBE On", "voltage", "volt", false, 0)
    ],
    allowedModelVariants: ["npn_large_signal", "npn_hybrid_pi"],
    defaultModelVariant: "npn_large_signal",
    analysisTags: ["transistor", "dc_bias", "small_signal"],
    teachingTags: ["bjt", "common_emitter", "operating_point"]
  },
  {
    id: "bjt.pnp",
    displayName: "PNP BJT",
    family: "bjt",
    ports: [
      { id: "collector", label: "Collector", role: "collector", required: true },
      { id: "base", label: "Base", role: "base", required: true },
      { id: "emitter", label: "Emitter", role: "emitter", required: true }
    ],
    parameters: [
      numericParameter("beta", "Current Gain", "beta", "unitless", false, 0),
      numericParameter("vbe_on", "VBE On", "voltage", "volt", false, 0)
    ],
    allowedModelVariants: ["pnp_large_signal", "pnp_hybrid_pi"],
    defaultModelVariant: "pnp_large_signal",
    analysisTags: ["transistor", "dc_bias", "small_signal"],
    teachingTags: ["bjt", "operating_point"]
  },
  {
    id: "fet.mosfet",
    displayName: "MOSFET",
    family: "fet",
    ports: [
      { id: "drain", label: "Drain", role: "drain", required: true },
      { id: "gate", label: "Gate", role: "gate", required: true },
      { id: "source", label: "Source", role: "source", required: true },
      { id: "bulk", label: "Bulk", role: "bulk", required: true }
    ],
    parameters: [
      numericParameter("threshold_voltage", "Threshold Voltage", "threshold_voltage", "volt", false),
      numericParameter("transconductance", "Transconductance", "transconductance", "siemens", false, 0)
    ],
    allowedModelVariants: ["n_enhancement", "p_enhancement", "n_depletion", "p_depletion", "small_signal"],
    defaultModelVariant: "n_enhancement",
    analysisTags: ["fet", "mosfet", "dc_bias", "small_signal"],
    teachingTags: ["mosfet", "common_source", "enhancement", "depletion"]
  },
  {
    id: "fet.jfet",
    displayName: "JFET",
    family: "fet",
    ports: [
      { id: "drain", label: "Drain", role: "drain", required: true },
      { id: "gate", label: "Gate", role: "gate", required: true },
      { id: "source", label: "Source", role: "source", required: true }
    ],
    parameters: [
      numericParameter("pinch_off_voltage", "Pinch-Off Voltage", "threshold_voltage", "volt", false),
      numericParameter("transconductance", "Transconductance", "transconductance", "siemens", false, 0)
    ],
    allowedModelVariants: ["n_channel", "p_channel", "small_signal"],
    defaultModelVariant: "n_channel",
    analysisTags: ["fet", "jfet", "dc_bias", "small_signal"],
    teachingTags: ["jfet", "field_effect_transistor"]
  }
];
