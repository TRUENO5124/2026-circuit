"""Component definitions mirroring packages/components/src/definitions.ts.

Each entry describes the ports, parameters, model variants, and teaching/analysis
tags for one component type. The analysis engine uses these definitions to:
- Validate that component instances in a CircuitGraph are well-formed
- Map parameter names to their types and units
- Select the appropriate transistor model for a given analysis
"""

from .types import (
    ComponentDefinition,
    ComponentFamily,
    ParameterDefinition,
    PortDefinition,
    PortRole,
    Quantity,
    Unit,
)


def _num_param(
    pid: str,
    label: str,
    quantity: Quantity,
    unit: Unit | None = None,
    required: bool = True,
    minimum: float | None = None,
) -> ParameterDefinition:
    return ParameterDefinition(
        id=pid,
        label=label,
        quantity=quantity,
        unit=unit,
        value_type="number",
        required=required,
        min=minimum,
    )


COMPONENT_DEFINITIONS: list[ComponentDefinition] = [
    ComponentDefinition(
        id="reference.ground",
        display_name="Ground",
        family=ComponentFamily.REFERENCE,
        ports=[PortDefinition(id="ref", label="Reference", role=PortRole.REFERENCE, required=True)],
        parameters=[],
        allowed_model_variants=["ideal_ground"],
        default_model_variant="ideal_ground",
        analysis_tags=["reference_node"],
        teaching_tags=["ground", "reference"],
    ),
    ComponentDefinition(
        id="passive.resistor",
        display_name="Resistor",
        family=ComponentFamily.PASSIVE,
        ports=[
            PortDefinition(id="p", label="Positive", role=PortRole.PASSIVE, required=True),
            PortDefinition(id="n", label="Negative", role=PortRole.PASSIVE, required=True),
        ],
        parameters=[_num_param("resistance", "Resistance", Quantity.RESISTANCE, Unit.OHM, minimum=0)],
        allowed_model_variants=["ideal_resistor"],
        default_model_variant="ideal_resistor",
        analysis_tags=["ohm_law", "linear"],
        teaching_tags=["resistance", "voltage_divider"],
    ),
    ComponentDefinition(
        id="passive.capacitor",
        display_name="Capacitor",
        family=ComponentFamily.PASSIVE,
        ports=[
            PortDefinition(id="p", label="Positive", role=PortRole.PASSIVE, required=True),
            PortDefinition(id="n", label="Negative", role=PortRole.PASSIVE, required=True),
        ],
        parameters=[_num_param("capacitance", "Capacitance", Quantity.CAPACITANCE, Unit.FARAD, minimum=0)],
        allowed_model_variants=["ideal_capacitor"],
        default_model_variant="ideal_capacitor",
        analysis_tags=["reactive", "small_signal"],
        teaching_tags=["capacitance", "coupling", "bypass"],
    ),
    ComponentDefinition(
        id="passive.inductor",
        display_name="Inductor",
        family=ComponentFamily.PASSIVE,
        ports=[
            PortDefinition(id="p", label="Positive", role=PortRole.PASSIVE, required=True),
            PortDefinition(id="n", label="Negative", role=PortRole.PASSIVE, required=True),
        ],
        parameters=[_num_param("inductance", "Inductance", Quantity.INDUCTANCE, Unit.HENRY, minimum=0)],
        allowed_model_variants=["ideal_inductor"],
        default_model_variant="ideal_inductor",
        analysis_tags=["reactive", "small_signal"],
        teaching_tags=["inductance", "energy_storage"],
    ),
    ComponentDefinition(
        id="passive.transformer",
        display_name="Transformer",
        family=ComponentFamily.PASSIVE,
        ports=[
            PortDefinition(id="p1", label="Primary +", role=PortRole.PRIMARY, required=True),
            PortDefinition(id="n1", label="Primary -", role=PortRole.PRIMARY, required=True),
            PortDefinition(id="p2", label="Secondary +", role=PortRole.SECONDARY, required=True),
            PortDefinition(id="n2", label="Secondary -", role=PortRole.SECONDARY, required=True),
        ],
        parameters=[_num_param("turns_ratio", "Turns Ratio", Quantity.TURNS_RATIO, Unit.RATIO, minimum=0)],
        allowed_model_variants=["ideal_transformer"],
        default_model_variant="ideal_transformer",
        analysis_tags=["coupled", "ac"],
        teaching_tags=["transformer", "impedance_reflection"],
    ),
    ComponentDefinition(
        id="source.v_dc",
        display_name="DC Voltage Source",
        family=ComponentFamily.INDEPENDENT_SOURCE,
        ports=[
            PortDefinition(id="p", label="Positive", role=PortRole.POSITIVE, required=True),
            PortDefinition(id="n", label="Negative", role=PortRole.NEGATIVE, required=True),
        ],
        parameters=[_num_param("voltage", "Voltage", Quantity.VOLTAGE, Unit.VOLT)],
        allowed_model_variants=["ideal_dc_voltage_source"],
        default_model_variant="ideal_dc_voltage_source",
        analysis_tags=["dc_bias", "source"],
        teaching_tags=["voltage_source", "bias"],
    ),
    ComponentDefinition(
        id="source.i_dc",
        display_name="DC Current Source",
        family=ComponentFamily.INDEPENDENT_SOURCE,
        ports=[
            PortDefinition(id="p", label="Positive", role=PortRole.POSITIVE, required=True),
            PortDefinition(id="n", label="Negative", role=PortRole.NEGATIVE, required=True),
        ],
        parameters=[_num_param("current", "Current", Quantity.CURRENT, Unit.AMPERE)],
        allowed_model_variants=["ideal_dc_current_source"],
        default_model_variant="ideal_dc_current_source",
        analysis_tags=["dc_bias", "source"],
        teaching_tags=["current_source", "bias"],
    ),
    ComponentDefinition(
        id="source.v_ac_small_signal",
        display_name="AC Small-Signal Voltage Source",
        family=ComponentFamily.INDEPENDENT_SOURCE,
        ports=[
            PortDefinition(id="p", label="Positive", role=PortRole.POSITIVE, required=True),
            PortDefinition(id="n", label="Negative", role=PortRole.NEGATIVE, required=True),
        ],
        parameters=[
            _num_param("amplitude", "Amplitude", Quantity.VOLTAGE, Unit.VOLT, minimum=0),
            _num_param("frequency", "Frequency", Quantity.FREQUENCY, Unit.HERTZ, required=False, minimum=0),
        ],
        allowed_model_variants=["ideal_ac_small_signal_voltage_source"],
        default_model_variant="ideal_ac_small_signal_voltage_source",
        analysis_tags=["small_signal", "source"],
        teaching_tags=["small_signal_source", "ac_analysis"],
    ),
    ComponentDefinition(
        id="source.i_ac_small_signal",
        display_name="AC Small-Signal Current Source",
        family=ComponentFamily.INDEPENDENT_SOURCE,
        ports=[
            PortDefinition(id="p", label="Positive", role=PortRole.POSITIVE, required=True),
            PortDefinition(id="n", label="Negative", role=PortRole.NEGATIVE, required=True),
        ],
        parameters=[
            _num_param("amplitude", "Amplitude", Quantity.CURRENT, Unit.AMPERE, minimum=0),
            _num_param("frequency", "Frequency", Quantity.FREQUENCY, Unit.HERTZ, required=False, minimum=0),
        ],
        allowed_model_variants=["ideal_ac_small_signal_current_source"],
        default_model_variant="ideal_ac_small_signal_current_source",
        analysis_tags=["small_signal", "source"],
        teaching_tags=["small_signal_source", "ac_analysis"],
    ),
    ComponentDefinition(
        id="controlled.vcvs",
        display_name="Voltage-Controlled Voltage Source",
        family=ComponentFamily.CONTROLLED_SOURCE,
        ports=[
            PortDefinition(id="p", label="Output +", role=PortRole.POSITIVE, required=True),
            PortDefinition(id="n", label="Output -", role=PortRole.NEGATIVE, required=True),
            PortDefinition(id="cp", label="Control +", role=PortRole.CONTROL_POSITIVE, required=True),
            PortDefinition(id="cn", label="Control -", role=PortRole.CONTROL_NEGATIVE, required=True),
        ],
        parameters=[_num_param("gain", "Voltage Gain", Quantity.GAIN, Unit.UNITLESS)],
        allowed_model_variants=["ideal_vcvs"],
        default_model_variant="ideal_vcvs",
        analysis_tags=["dependent_source", "linear"],
        teaching_tags=["controlled_source", "vcvs"],
    ),
    ComponentDefinition(
        id="controlled.vccs",
        display_name="Voltage-Controlled Current Source",
        family=ComponentFamily.CONTROLLED_SOURCE,
        ports=[
            PortDefinition(id="p", label="Output +", role=PortRole.POSITIVE, required=True),
            PortDefinition(id="n", label="Output -", role=PortRole.NEGATIVE, required=True),
            PortDefinition(id="cp", label="Control +", role=PortRole.CONTROL_POSITIVE, required=True),
            PortDefinition(id="cn", label="Control -", role=PortRole.CONTROL_NEGATIVE, required=True),
        ],
        parameters=[_num_param("transconductance", "Transconductance", Quantity.TRANSCONDUCTANCE, Unit.SIEMENS)],
        allowed_model_variants=["ideal_vccs"],
        default_model_variant="ideal_vccs",
        analysis_tags=["dependent_source", "linear"],
        teaching_tags=["controlled_source", "vccs"],
    ),
    ComponentDefinition(
        id="controlled.ccvs",
        display_name="Current-Controlled Voltage Source",
        family=ComponentFamily.CONTROLLED_SOURCE,
        ports=[
            PortDefinition(id="p", label="Output +", role=PortRole.POSITIVE, required=True),
            PortDefinition(id="n", label="Output -", role=PortRole.NEGATIVE, required=True),
            PortDefinition(id="sp", label="Sense +", role=PortRole.SENSE_POSITIVE, required=True),
            PortDefinition(id="sn", label="Sense -", role=PortRole.SENSE_NEGATIVE, required=True),
        ],
        parameters=[_num_param("transresistance", "Transresistance", Quantity.TRANSRESISTANCE, Unit.VOLT_PER_AMPERE)],
        allowed_model_variants=["ideal_ccvs"],
        default_model_variant="ideal_ccvs",
        analysis_tags=["dependent_source", "linear"],
        teaching_tags=["controlled_source", "ccvs"],
    ),
    ComponentDefinition(
        id="controlled.cccs",
        display_name="Current-Controlled Current Source",
        family=ComponentFamily.CONTROLLED_SOURCE,
        ports=[
            PortDefinition(id="p", label="Output +", role=PortRole.POSITIVE, required=True),
            PortDefinition(id="n", label="Output -", role=PortRole.NEGATIVE, required=True),
            PortDefinition(id="sp", label="Sense +", role=PortRole.SENSE_POSITIVE, required=True),
            PortDefinition(id="sn", label="Sense -", role=PortRole.SENSE_NEGATIVE, required=True),
        ],
        parameters=[_num_param("gain", "Current Gain", Quantity.GAIN, Unit.UNITLESS)],
        allowed_model_variants=["ideal_cccs"],
        default_model_variant="ideal_cccs",
        analysis_tags=["dependent_source", "linear"],
        teaching_tags=["controlled_source", "cccs"],
    ),
    ComponentDefinition(
        id="diode.standard",
        display_name="Diode",
        family=ComponentFamily.DIODE,
        ports=[
            PortDefinition(id="anode", label="Anode", role=PortRole.ANODE, required=True),
            PortDefinition(id="cathode", label="Cathode", role=PortRole.CATHODE, required=True),
        ],
        parameters=[
            _num_param("forward_voltage", "Forward Voltage", Quantity.VOLTAGE, Unit.VOLT, required=False, minimum=0),
            _num_param("saturation_current", "Saturation Current", Quantity.SATURATION_CURRENT, Unit.AMPERE, required=False, minimum=0),
        ],
        allowed_model_variants=["ideal_diode", "constant_voltage_drop", "shockley"],
        default_model_variant="constant_voltage_drop",
        analysis_tags=["nonlinear", "piecewise_linear"],
        teaching_tags=["diode", "rectifier", "limiter", "clamp"],
    ),
    ComponentDefinition(
        id="bjt.npn",
        display_name="NPN BJT",
        family=ComponentFamily.BJT,
        ports=[
            PortDefinition(id="collector", label="Collector", role=PortRole.COLLECTOR, required=True),
            PortDefinition(id="base", label="Base", role=PortRole.BASE, required=True),
            PortDefinition(id="emitter", label="Emitter", role=PortRole.EMITTER, required=True),
        ],
        parameters=[
            _num_param("beta", "Current Gain", Quantity.BETA, Unit.UNITLESS, required=False, minimum=0),
            _num_param("vbe_on", "VBE On", Quantity.VOLTAGE, Unit.VOLT, required=False, minimum=0),
        ],
        allowed_model_variants=["npn_large_signal", "npn_hybrid_pi"],
        default_model_variant="npn_large_signal",
        analysis_tags=["transistor", "dc_bias", "small_signal"],
        teaching_tags=["bjt", "common_emitter", "operating_point"],
    ),
    ComponentDefinition(
        id="bjt.pnp",
        display_name="PNP BJT",
        family=ComponentFamily.BJT,
        ports=[
            PortDefinition(id="collector", label="Collector", role=PortRole.COLLECTOR, required=True),
            PortDefinition(id="base", label="Base", role=PortRole.BASE, required=True),
            PortDefinition(id="emitter", label="Emitter", role=PortRole.EMITTER, required=True),
        ],
        parameters=[
            _num_param("beta", "Current Gain", Quantity.BETA, Unit.UNITLESS, required=False, minimum=0),
            _num_param("vbe_on", "VBE On", Quantity.VOLTAGE, Unit.VOLT, required=False, minimum=0),
        ],
        allowed_model_variants=["pnp_large_signal", "pnp_hybrid_pi"],
        default_model_variant="pnp_large_signal",
        analysis_tags=["transistor", "dc_bias", "small_signal"],
        teaching_tags=["bjt", "operating_point"],
    ),
    ComponentDefinition(
        id="fet.mosfet",
        display_name="MOSFET",
        family=ComponentFamily.FET,
        ports=[
            PortDefinition(id="drain", label="Drain", role=PortRole.DRAIN, required=True),
            PortDefinition(id="gate", label="Gate", role=PortRole.GATE, required=True),
            PortDefinition(id="source", label="Source", role=PortRole.SOURCE, required=True),
            PortDefinition(id="bulk", label="Bulk", role=PortRole.BULK, required=True),
        ],
        parameters=[
            _num_param("threshold_voltage", "Threshold Voltage", Quantity.THRESHOLD_VOLTAGE, Unit.VOLT, required=False),
            _num_param("transconductance", "Transconductance", Quantity.TRANSCONDUCTANCE, Unit.SIEMENS, required=False, minimum=0),
        ],
        allowed_model_variants=["n_enhancement", "p_enhancement", "n_depletion", "p_depletion", "small_signal"],
        default_model_variant="n_enhancement",
        analysis_tags=["fet", "mosfet", "dc_bias", "small_signal"],
        teaching_tags=["mosfet", "common_source", "enhancement", "depletion"],
    ),
    ComponentDefinition(
        id="fet.jfet",
        display_name="JFET",
        family=ComponentFamily.FET,
        ports=[
            PortDefinition(id="drain", label="Drain", role=PortRole.DRAIN, required=True),
            PortDefinition(id="gate", label="Gate", role=PortRole.GATE, required=True),
            PortDefinition(id="source", label="Source", role=PortRole.SOURCE, required=True),
        ],
        parameters=[
            _num_param("pinch_off_voltage", "Pinch-Off Voltage", Quantity.THRESHOLD_VOLTAGE, Unit.VOLT, required=False),
            _num_param("transconductance", "Transconductance", Quantity.TRANSCONDUCTANCE, Unit.SIEMENS, required=False, minimum=0),
        ],
        allowed_model_variants=["n_channel", "p_channel", "small_signal"],
        default_model_variant="n_channel",
        analysis_tags=["fet", "jfet", "dc_bias", "small_signal"],
        teaching_tags=["jfet", "field_effect_transistor"],
    ),
]

DEFINITIONS_BY_ID: dict[str, ComponentDefinition] = {d.id: d for d in COMPONENT_DEFINITIONS}
