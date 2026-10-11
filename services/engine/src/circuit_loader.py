"""Load CircuitGraph and BenchmarkCase from JSON files.

Maps JSON keys (camelCase, as produced by the TypeScript layer) to Python
dataclass field names (snake_case).
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from .types import (
    BenchmarkCase,
    BenchmarkSource,
    CircuitGraph,
    ComponentInstance,
    Connection,
    Crop,
    Equation,
    ExpectedResult,
    GraphPort,
    GraphPortRole,
    GroundTruth,
    ImageKind,
    ImageRef,
    Node,
    NodeKind,
    ParamSource,
    ParameterValue,
    Position,
    Severity,
    SourceKind,
    Unit,
    ValidationIssue,
)


# ── JSON → Python deserializers ─────────────────────────────────────


def _parse_position(raw: dict[str, Any] | None) -> Position | None:
    if raw is None:
        return None
    return Position(x=float(raw["x"]), y=float(raw["y"]))


def _parse_param_value(raw: dict[str, Any]) -> ParameterValue:
    return ParameterValue(
        value=raw["value"],
        unit=Unit(raw["unit"]) if raw.get("unit") else None,
        source=ParamSource(raw["source"]) if raw.get("source") else None,
    )


def _parse_node(raw: dict[str, Any]) -> Node:
    return Node(
        id=raw["id"],
        label=raw.get("label"),
        kind=NodeKind(raw.get("kind", "electrical")),
        position=_parse_position(raw.get("position")),
    )


def _parse_component(raw: dict[str, Any]) -> ComponentInstance:
    return ComponentInstance(
        id=raw["id"],
        definition_id=raw["definitionId"],
        label=raw["label"],
        ports=[str(p) for p in raw["ports"]],
        parameters={k: _parse_param_value(v) for k, v in raw.get("parameters", {}).items()},
        model_variant=raw.get("modelVariant", ""),
        position=_parse_position(raw.get("position")),
    )


def _parse_connection(raw: dict[str, Any]) -> Connection:
    return Connection(
        id=raw["id"],
        node_id=raw["nodeId"],
        component_id=raw["componentId"],
        port_id=raw["portId"],
    )


def _parse_graph_port(raw: dict[str, Any]) -> GraphPort:
    return GraphPort(
        id=raw["id"],
        label=raw["label"],
        node_id=raw["nodeId"],
        role=GraphPortRole(raw.get("role", "probe")),
    )


def _parse_validation_issue(raw: dict[str, Any]) -> ValidationIssue:
    return ValidationIssue(
        severity=Severity(raw["severity"]),
        code=raw["code"],
        message=raw["message"],
        path=[item for item in raw.get("path", [])],
    )


def parse_circuit_graph(raw: dict[str, Any]) -> CircuitGraph:
    """Parse a CircuitGraph from its JSON dict representation."""
    return CircuitGraph(
        id=raw["id"],
        version=raw["version"],
        title=raw.get("title"),
        nodes=[_parse_node(n) for n in raw["nodes"]],
        components=[_parse_component(c) for c in raw["components"]],
        ports=[_parse_graph_port(p) for p in raw.get("ports", [])],
        connections=[_parse_connection(c) for c in raw["connections"]],
        metadata=raw.get("metadata", {}),
        validation_issues=[_parse_validation_issue(v) for v in raw.get("validationIssues", [])],
    )


def parse_benchmark_case(raw: dict[str, Any]) -> BenchmarkCase:
    """Parse a BenchmarkCase from its JSON dict representation."""
    gt = raw.get("groundTruth", {})
    return BenchmarkCase(
        id=raw["id"],
        title=raw["title"],
        source=BenchmarkSource(
            kind=SourceKind(raw["source"]["kind"]),
            title=raw["source"].get("title"),
            page=raw["source"].get("page"),
            note=raw["source"].get("note"),
        ),
        problem_text=raw["problemText"],
        circuit_graph=parse_circuit_graph(raw["circuitGraph"]),
        ground_truth=_parse_ground_truth(gt),
        image_refs=[_parse_image_ref(ir) for ir in raw.get("imageRefs", [])],
        difficulty_tags=[str(t) for t in raw.get("difficultyTags", [])],
    )


def _parse_crop(raw: dict[str, Any] | None) -> Crop | None:
    if raw is None:
        return None
    return Crop(x=float(raw["x"]), y=float(raw["y"]), width=float(raw["width"]), height=float(raw["height"]))


def _parse_image_ref(raw: dict[str, Any]) -> ImageRef:
    return ImageRef(
        id=raw["id"],
        path=raw["path"],
        kind=ImageKind(raw.get("kind", "original")),
        crop=_parse_crop(raw.get("crop")),
    )


def _parse_equation(raw: dict[str, Any]) -> Equation:
    return Equation(
        id=raw["id"],
        latex=raw["latex"],
        variables=[str(v) for v in raw.get("variables", [])],
    )


def _parse_expected_result(raw: dict[str, Any]) -> ExpectedResult:
    return ExpectedResult(
        id=raw["id"],
        quantity=raw["quantity"],
        expression=raw["expression"],
        unit=Unit(raw["unit"]) if raw.get("unit") else None,
        tolerance=raw.get("tolerance"),
    )


def _parse_ground_truth(raw: dict[str, Any]) -> GroundTruth:
    return GroundTruth(
        expected_component_ids=[str(c) for c in raw.get("expectedComponentIds", [])],
        expected_node_ids=[str(n) for n in raw.get("expectedNodeIds", [])],
        expected_connection_count=int(raw.get("expectedConnectionCount", 0)),
        equations=[_parse_equation(e) for e in raw.get("equations", [])],
        expected_results=[_parse_expected_result(r) for r in raw.get("expectedResults", [])],
        accepted_equivalent_expressions=[str(e) for e in raw.get("acceptedEquivalentExpressions", [])],
        teaching_tags=[str(t) for t in raw.get("teachingTags", [])],
    )


# ── File I/O ────────────────────────────────────────────────────────


def load_circuit_graph(path: str | Path) -> CircuitGraph:
    """Load a CircuitGraph JSON file."""
    with open(path, encoding="utf-8") as f:
        return parse_circuit_graph(json.load(f))


def load_benchmark_case(path: str | Path) -> BenchmarkCase:
    """Load a BenchmarkCase JSON file."""
    with open(path, encoding="utf-8") as f:
        return parse_benchmark_case(json.load(f))
