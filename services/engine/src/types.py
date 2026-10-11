"""Python type definitions mirroring the Zod schema in packages/schema/src/schemas.ts.

These dataclasses represent the CircuitGraph JSON that the TypeScript layer
validates and passes to the Python analysis engine.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Optional, Union

# ── Primitive aliases ──────────────────────────────────────────────

ParamValue = Union[float, str, bool]


# ── Enums ───────────────────────────────────────────────────────────

class PortRole(str, Enum):
    POSITIVE = "positive"
    NEGATIVE = "negative"
    REFERENCE = "reference"
    PASSIVE = "passive"
    ANODE = "anode"
    CATHODE = "cathode"
    COLLECTOR = "collector"
    BASE = "base"
    EMITTER = "emitter"
    DRAIN = "drain"
    GATE = "gate"
    SOURCE = "source"
    BULK = "bulk"
    PRIMARY = "primary"
    SECONDARY = "secondary"
    CONTROL_POSITIVE = "control_positive"
    CONTROL_NEGATIVE = "control_negative"
    SENSE_POSITIVE = "sense_positive"
    SENSE_NEGATIVE = "sense_negative"


class ComponentFamily(str, Enum):
    PASSIVE = "passive"
    INDEPENDENT_SOURCE = "independent_source"
    CONTROLLED_SOURCE = "controlled_source"
    DIODE = "diode"
    BJT = "bjt"
    FET = "fet"
    REFERENCE = "reference"


class Quantity(str, Enum):
    RESISTANCE = "resistance"
    CAPACITANCE = "capacitance"
    INDUCTANCE = "inductance"
    VOLTAGE = "voltage"
    CURRENT = "current"
    FREQUENCY = "frequency"
    GAIN = "gain"
    TRANSCONDUCTANCE = "transconductance"
    TRANSRESISTANCE = "transresistance"
    TURNS_RATIO = "turns_ratio"
    BETA = "beta"
    THRESHOLD_VOLTAGE = "threshold_voltage"
    EMISSION_COEFFICIENT = "emission_coefficient"
    SATURATION_CURRENT = "saturation_current"
    UNITLESS = "unitless"


class Unit(str, Enum):
    OHM = "ohm"
    FARAD = "farad"
    HENRY = "henry"
    VOLT = "volt"
    AMPERE = "ampere"
    HERTZ = "hertz"
    SIEMENS = "siemens"
    VOLT_PER_AMPERE = "volt_per_ampere"
    AMPERE_PER_VOLT = "ampere_per_volt"
    RATIO = "ratio"
    UNITLESS = "unitless"


class NodeKind(str, Enum):
    ELECTRICAL = "electrical"
    REFERENCE = "reference"


class GraphPortRole(str, Enum):
    INPUT = "input"
    OUTPUT = "output"
    REFERENCE = "reference"
    PROBE = "probe"


class Severity(str, Enum):
    ERROR = "error"
    WARNING = "warning"
    INFO = "info"


class SourceKind(str, Enum):
    TEXTBOOK = "textbook"
    EXERCISE_BOOK = "exercise_book"
    SYNTHETIC = "synthetic"
    UNKNOWN = "unknown"


class ImageKind(str, Enum):
    ORIGINAL = "original"
    CROP = "crop"
    PROCESSED = "processed"


class ParamSource(str, Enum):
    USER = "user"
    VISION = "vision"
    BENCHMARK = "benchmark"
    DEFAULT = "default"


# ── Dataclasses ─────────────────────────────────────────────────────


@dataclass
class Position:
    x: float
    y: float


@dataclass
class ParameterValue:
    value: ParamValue
    unit: Optional[Unit] = None
    source: Optional[ParamSource] = None


@dataclass
class ParameterDefinition:
    id: str
    label: str
    quantity: Quantity
    unit: Optional[Unit] = None
    value_type: str = "number"  # "number" | "string" | "boolean"
    required: bool = True
    default_value: Optional[ParameterValue] = None
    min: Optional[float] = None
    max: Optional[float] = None
    allowed_values: Optional[list[ParamValue]] = None
    description: Optional[str] = None


@dataclass
class PortDefinition:
    id: str
    label: str
    role: PortRole
    required: bool = True
    description: Optional[str] = None


@dataclass
class ComponentDefinition:
    id: str
    display_name: str
    family: ComponentFamily
    ports: list[PortDefinition]
    parameters: list[ParameterDefinition] = field(default_factory=list)
    allowed_model_variants: list[str] = field(default_factory=list)
    default_model_variant: str = ""
    analysis_tags: list[str] = field(default_factory=list)
    teaching_tags: list[str] = field(default_factory=list)


@dataclass
class Node:
    id: str
    label: Optional[str] = None
    kind: NodeKind = NodeKind.ELECTRICAL
    position: Optional[Position] = None


@dataclass
class GraphPort:
    id: str
    label: str
    node_id: str
    role: GraphPortRole = GraphPortRole.PROBE


@dataclass
class ComponentInstance:
    id: str
    definition_id: str
    label: str
    ports: list[str]
    parameters: dict[str, ParameterValue] = field(default_factory=dict)
    model_variant: str = ""
    position: Optional[Position] = None


@dataclass
class Connection:
    id: str
    node_id: str
    component_id: str
    port_id: str


@dataclass
class ValidationIssue:
    severity: Severity
    code: str
    message: str
    path: list[Union[str, int]] = field(default_factory=list)


@dataclass
class CircuitGraph:
    id: str
    version: str
    nodes: list[Node]
    components: list[ComponentInstance]
    ports: list[GraphPort] = field(default_factory=list)
    connections: list[Connection] = field(default_factory=list)
    metadata: dict[str, Any] = field(default_factory=dict)
    validation_issues: list[ValidationIssue] = field(default_factory=list)
    title: Optional[str] = None


@dataclass
class Crop:
    x: float
    y: float
    width: float
    height: float


@dataclass
class ImageRef:
    id: str
    path: str
    kind: ImageKind = ImageKind.ORIGINAL
    crop: Optional[Crop] = None


@dataclass
class BenchmarkSource:
    kind: SourceKind
    title: Optional[str] = None
    page: Optional[str] = None
    note: Optional[str] = None


@dataclass
class ExpectedResult:
    id: str
    quantity: str
    expression: str
    unit: Optional[Unit] = None
    tolerance: Optional[float] = None


@dataclass
class Equation:
    id: str
    latex: str
    variables: list[str] = field(default_factory=list)


@dataclass
class GroundTruth:
    expected_component_ids: list[str]
    expected_node_ids: list[str]
    expected_connection_count: int
    equations: list[Equation] = field(default_factory=list)
    expected_results: list[ExpectedResult] = field(default_factory=list)
    accepted_equivalent_expressions: list[str] = field(default_factory=list)
    teaching_tags: list[str] = field(default_factory=list)


@dataclass
class BenchmarkCase:
    id: str
    title: str
    source: BenchmarkSource
    problem_text: str
    circuit_graph: CircuitGraph
    ground_truth: GroundTruth
    image_refs: list[ImageRef] = field(default_factory=list)
    difficulty_tags: list[str] = field(default_factory=list)


# ── Analysis engine output types ────────────────────────────────────


@dataclass
class Step:
    """One step in the solution process."""
    description: str
    formula: str  # LaTeX
    reasoning: str


@dataclass
class OperatingPoint:
    """DC operating point results."""
    node_voltages: dict[str, str] = field(default_factory=dict)    # node_id → LaTeX expr
    branch_currents: dict[str, str] = field(default_factory=dict)  # branch_id → LaTeX expr
    regions: dict[str, str] = field(default_factory=dict)          # device_id → region label


@dataclass
class GainExpressions:
    """Small-signal gain expressions."""
    voltage_gain: Optional[str] = None     # LaTeX
    current_gain: Optional[str] = None     # LaTeX
    transconductance: Optional[str] = None # LaTeX


@dataclass
class ImpedanceExpressions:
    """Small-signal impedance expressions."""
    input_resistance: Optional[str] = None   # LaTeX
    output_resistance: Optional[str] = None  # LaTeX


@dataclass
class AnalysisResult:
    """Structured output from the analysis engine.

    All formula/expression fields contain LaTeX strings suitable for
    rendering by KaTeX in the presentation layer.
    """
    problem_type: str
    steps: list[Step] = field(default_factory=list)
    equations: list[Equation] = field(default_factory=list)
    operating_point: Optional[OperatingPoint] = None
    gain_expressions: Optional[GainExpressions] = None
    impedance_expressions: Optional[ImpedanceExpressions] = None
    teaching_tags: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    is_symbolic: bool = True

    def to_dict(self) -> dict[str, Any]:
        """Serialize to a JSON-compatible dict for the presentation layer."""
        d: dict[str, Any] = {
            "problemType": self.problem_type,
            "steps": [
                {"description": s.description, "formula": s.formula, "reasoning": s.reasoning}
                for s in self.steps
            ],
            "equations": [
                {"id": e.id, "latex": e.latex, "variables": e.variables}
                for e in self.equations
            ],
            "teachingTags": self.teaching_tags,
            "warnings": self.warnings,
            "isSymbolic": self.is_symbolic,
        }
        if self.operating_point:
            d["operatingPoint"] = {
                "nodeVoltages": self.operating_point.node_voltages,
                "branchCurrents": self.operating_point.branch_currents,
                "regions": self.operating_point.regions,
            }
        if self.gain_expressions:
            d["gainExpressions"] = {
                k: v for k, v in {
                    "voltageGain": self.gain_expressions.voltage_gain,
                    "currentGain": self.gain_expressions.current_gain,
                    "transconductance": self.gain_expressions.transconductance,
                }.items() if v is not None
            }
        if self.impedance_expressions:
            d["impedanceExpressions"] = {
                k: v for k, v in {
                    "inputResistance": self.impedance_expressions.input_resistance,
                    "outputResistance": self.impedance_expressions.output_resistance,
                }.items() if v is not None
            }
        return d
