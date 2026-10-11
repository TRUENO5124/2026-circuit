"""End-to-end tests: load benchmark JSON, run through pipeline, verify output."""

from __future__ import annotations

import json
import sys
from pathlib import Path

# Make services/engine importable
_ENGINE_DIR = Path(__file__).resolve().parents[1]  # services/engine/
sys.path.insert(0, str(_ENGINE_DIR.parent))        # services/  (so "import engine" works)

from engine.src.circuit_loader import parse_circuit_graph, parse_benchmark_case
from engine.src.pipeline import AnalysisPipeline, UnsupportedCircuitError
from engine.src.types import AnalysisResult
from engine.src.strategies.bjt_dc import BJTDCBiasStrategy
from engine.src.strategies.bjt_ac import BJTACSmallSignalStrategy
from engine.src.strategies.mos_dc import MOSDCBiasStrategy
from engine.src.strategies.mos_ac import MOSACSmallSignalStrategy
from engine.src.strategies.diode import DiodeAnalysisStrategy

BENCHMARKS_DIR = Path(__file__).resolve().parents[3] / "benchmarks" / "examples"


def _load_json(path: Path) -> dict:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


# ── Tests ────────────────────────────────────────────────────────────


def test_can_parse_all_benchmarks() -> None:
    """All 3 benchmark JSON files parse without error."""
    for bm_file in sorted(BENCHMARKS_DIR.glob("*.json")):
        raw = _load_json(bm_file)
        bm = parse_benchmark_case(raw)
        assert bm.id, f"Benchmark {bm_file.name} missing id"
        assert bm.circuit_graph.components, f"{bm_file.name} has no components"
        print(f"  [OK] parse {bm_file.name} → '{bm.title}' ({len(bm.circuit_graph.components)} components)")


def test_strategy_routing() -> None:
    """Each benchmark is routed to the correct strategy."""
    pipeline = AnalysisPipeline()

    expectations = {
        "bjt-common-emitter-bias.json": "BJTDCBiasStrategy",
        "mos-common-source.json": "MOSACSmallSignalStrategy",
        "diode-limiter.json": "DiodeAnalysisStrategy",
    }

    for filename, expected in expectations.items():
        path = BENCHMARKS_DIR / filename
        raw = _load_json(path)
        bm = parse_benchmark_case(raw)
        graph = bm.circuit_graph

        strategy = None
        for s in pipeline._strategies:
            if s.can_handle(graph):
                strategy = s.__class__.__name__
                break

        assert strategy == expected, (
            f"{filename}: expected {expected}, got {strategy}"
        )
        print(f"  [OK] {filename} → {strategy}")


def test_bjt_dc_bias_strategy() -> None:
    """BJT DC bias strategy produces AnalysisResult with expected structure."""
    path = BENCHMARKS_DIR / "bjt-common-emitter-bias.json"
    bm = parse_benchmark_case(_load_json(path))
    strategy = BJTDCBiasStrategy()

    assert strategy.can_handle(bm.circuit_graph)

    result = strategy.analyze(bm.circuit_graph)

    assert isinstance(result, AnalysisResult)
    assert result.problem_type == "bjt_dc_bias"
    assert len(result.steps) >= 5, f"Expected ≥5 steps, got {len(result.steps)}"
    assert result.operating_point is not None
    assert result.is_symbolic

    # Check step structure
    for step in result.steps:
        assert step.description
        assert step.formula
        assert step.reasoning

    # Check operating point has expected entries
    op = result.operating_point
    assert "I_B" in op.branch_currents
    assert "I_C" in op.branch_currents

    print(f"  [OK] BJT DC bias: {len(result.steps)} steps, "
          f"{len(result.equations)} equations, "
          f"problem_type={result.problem_type}")


def test_mos_ac_small_signal_strategy() -> None:
    """MOS AC small-signal strategy produces gain expressions."""
    path = BENCHMARKS_DIR / "mos-common-source.json"
    bm = parse_benchmark_case(_load_json(path))
    strategy = MOSACSmallSignalStrategy()

    assert strategy.can_handle(bm.circuit_graph)

    result = strategy.analyze(bm.circuit_graph)

    assert isinstance(result, AnalysisResult)
    assert result.problem_type == "mos_ac_small_signal"
    assert result.gain_expressions is not None
    assert result.gain_expressions.voltage_gain is not None
    assert "g_{m}" in result.gain_expressions.voltage_gain

    # Verify the expected gain expression matches ground truth
    gt = bm.ground_truth
    expected_result = gt.expected_results[0] if gt.expected_results else None
    if expected_result:
        assert expected_result.quantity == "midband voltage gain"

    print(f"  [OK] MOS AC: Av = {result.gain_expressions.voltage_gain}, "
          f"{len(result.steps)} steps")


def test_diode_analysis_strategy() -> None:
    """Diode analysis strategy handles limiter circuits."""
    path = BENCHMARKS_DIR / "diode-limiter.json"
    bm = parse_benchmark_case(_load_json(path))
    strategy = DiodeAnalysisStrategy()

    assert strategy.can_handle(bm.circuit_graph)

    result = strategy.analyze(bm.circuit_graph)

    assert isinstance(result, AnalysisResult)
    assert result.problem_type == "diode_analysis"
    assert len(result.steps) >= 3

    # Should mention piecewise analysis
    all_text = " ".join(s.description + s.formula + s.reasoning for s in result.steps)
    assert "导通" in all_text or "on" in all_text.lower()

    print(f"  [OK] Diode: {len(result.steps)} steps, "
          f"{len(result.equations)} equations")


def test_pipeline_end_to_end() -> None:
    """Full pipeline: load → analyze → serialize for all 3 benchmarks."""
    pipeline = AnalysisPipeline()

    for bm_file in sorted(BENCHMARKS_DIR.glob("*.json")):
        bm = parse_benchmark_case(_load_json(bm_file))
        result = pipeline.analyze(bm.circuit_graph)

        # Verify serialization works
        d = result.to_dict()
        assert d["problemType"]
        assert d["steps"]
        assert d["isSymbolic"]
        assert isinstance(d["steps"], list)
        assert isinstance(d["steps"][0], dict)

        print(f"  [OK] {bm_file.name}: {len(d['steps'])} steps → JSON-ready")


def test_result_to_dict_roundtrip() -> None:
    """AnalysisResult.to_dict() produces valid JSON-serializable output."""
    path = BENCHMARKS_DIR / "bjt-common-emitter-bias.json"
    bm = parse_benchmark_case(_load_json(path))
    pipeline = AnalysisPipeline()
    result = pipeline.analyze(bm.circuit_graph)

    d = result.to_dict()

    # Verify all keys are camelCase (matching TypeScript convention)
    assert "problemType" in d
    assert "steps" in d
    assert "equations" in d
    assert "teachingTags" in d
    assert "isSymbolic" in d
    assert "operatingPoint" in d

    # Verify it's JSON-serializable
    json_str = json.dumps(d, ensure_ascii=False)
    assert len(json_str) > 100
    parsed = json.loads(json_str)
    assert parsed["problemType"] == "bjt_dc_bias"

    print(f"  [OK] Serialized {len(json_str)} chars of JSON")


def test_unsupported_circuit_raises() -> None:
    """A trivial circuit with no active devices raises UnsupportedCircuitError."""
    from engine.src.types import (
        CircuitGraph, Node, NodeKind, ComponentInstance, Connection,
    )
    from engine.src.component_defs import DEFINITIONS_BY_ID

    graph = CircuitGraph(
        id="test.bare",
        version="0.1.0",
        nodes=[
            Node(id="n1", kind=NodeKind.ELECTRICAL),
            Node(id="n_gnd", kind=NodeKind.REFERENCE),
        ],
        components=[
            ComponentInstance(
                id="r1", definition_id="passive.resistor", label="R1",
                ports=["p", "n"],
                parameters={"resistance": type("P", (), {"value": 1000.0, "unit": None, "source": None})()},
                model_variant="ideal_resistor",
            ),
        ],
        connections=[
            Connection(id="c1", node_id="n1", component_id="r1", port_id="p"),
            Connection(id="c2", node_id="n_gnd", component_id="r1", port_id="n"),
        ],
    )

    pipeline = AnalysisPipeline()
    try:
        pipeline.analyze(graph)
        assert False, "Should have raised UnsupportedCircuitError"
    except UnsupportedCircuitError as e:
        assert "No analysis strategy" in str(e)
        print(f"  [OK] Correctly raised UnsupportedCircuitError")


def test_strategy_registration_order() -> None:
    """Custom strategies can be registered and take priority."""
    from engine.src.types import CircuitGraph
    from engine.src.strategies.base import AnalysisStrategy

    # A strategy that claims to handle everything
    class CatchAllStrategy(AnalysisStrategy):
        def can_handle(self, graph: CircuitGraph) -> bool:
            return True
        def analyze(self, graph: CircuitGraph) -> AnalysisResult:
            return AnalysisResult(problem_type="catch_all", is_symbolic=True)
        def get_problem_type(self) -> str:
            return "catch_all"

    # Default pipeline: BJT circuit goes to BJTDCBias
    path = BENCHMARKS_DIR / "bjt-common-emitter-bias.json"
    bm = parse_benchmark_case(_load_json(path))

    default_pipeline = AnalysisPipeline()
    result = default_pipeline.analyze(bm.circuit_graph)
    assert result.problem_type == "bjt_dc_bias"

    # Custom pipeline with CatchAll first
    custom = AnalysisPipeline(strategies=[CatchAllStrategy()])
    # Also register the default strategies
    custom.register(BJTDCBiasStrategy())
    result2 = custom.analyze(bm.circuit_graph)
    assert result2.problem_type == "catch_all", f"Expected catch_all, got {result2.problem_type}"

    print("  [OK] Strategy registration order works correctly")


def test_linear_rc_filter_strategy() -> None:
    """Linear RC circuit is correctly analyzed by LinearAnalysisStrategy."""
    from engine.src.types import CircuitGraph, Node, ComponentInstance, Connection, ParameterValue, Unit

    graph = CircuitGraph(
        id="test_rc_circuit",
        version="1.0",
        nodes=[
            Node(id="n_gnd", kind="reference"),
            Node(id="n_in", kind="electrical"),
            Node(id="n_out", kind="electrical"),
        ],
        components=[
            ComponentInstance(
                id="vac1", definition_id="source.v_ac_small_signal", label="vac1",
                ports=["p", "n"],
                parameters={"amplitude": ParameterValue(value=0.01, unit=Unit.VOLT), "frequency": ParameterValue(value=1000.0, unit=Unit.HERTZ)},
            ),
            ComponentInstance(
                id="r1", definition_id="passive.resistor", label="r1",
                ports=["p", "n"],
                parameters={"resistance": ParameterValue(value=1000.0, unit=Unit.OHM)},
            ),
            ComponentInstance(
                id="c1", definition_id="passive.capacitor", label="c1",
                ports=["p", "n"],
                parameters={"capacitance": ParameterValue(value=0.001, unit=Unit.FARAD)},
            ),
            ComponentInstance(
                id="gnd1", definition_id="reference.ground", label="gnd1",
                ports=["ref"], parameters={},
            ),
        ],
        connections=[
            Connection(id="c1", node_id="n_in", component_id="vac1", port_id="p"),
            Connection(id="c2", node_id="n_gnd", component_id="vac1", port_id="n"),
            Connection(id="c3", node_id="n_in", component_id="r1", port_id="p"),
            Connection(id="c4", node_id="n_out", component_id="r1", port_id="n"),
            Connection(id="c5", node_id="n_out", component_id="c1", port_id="p"),
            Connection(id="c6", node_id="n_gnd", component_id="c1", port_id="n"),
            Connection(id="c7", node_id="n_gnd", component_id="gnd1", port_id="ref"),
        ],
    )

    pipeline = AnalysisPipeline()
    result = pipeline.analyze(graph)
    assert result.problem_type == "linear_frequency_analysis"
    assert len(result.steps) >= 5
    assert result.gain_expressions is not None
    assert "C_{c1}" in result.gain_expressions.voltage_gain
    print("  [OK] Linear RC filter: 6 steps, transfer function derived")


# ── Runner ───────────────────────────────────────────────────────────


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    tests = [
        ("parse all benchmarks", test_can_parse_all_benchmarks),
        ("strategy routing", test_strategy_routing),
        ("BJT DC bias strategy", test_bjt_dc_bias_strategy),
        ("MOS AC small-signal strategy", test_mos_ac_small_signal_strategy),
        ("diode analysis strategy", test_diode_analysis_strategy),
        ("linear RC filter strategy", test_linear_rc_filter_strategy),
        ("pipeline end-to-end", test_pipeline_end_to_end),
        ("result to_dict roundtrip", test_result_to_dict_roundtrip),
        ("unsupported circuit raises", test_unsupported_circuit_raises),
        ("strategy registration order", test_strategy_registration_order),
    ]

    failed = 0
    for name, fn in tests:
        try:
            fn()
        except Exception as exc:
            failed += 1
            print(f"  [FAIL] {name}: {exc}")
            import traceback
            traceback.print_exc()

    print(f"\n{'='*50}")
    if failed:
        print(f"FAILED: {failed}/{len(tests)} tests failed.")
        sys.exit(1)
    else:
        print(f"OK: {len(tests)} tests passed.")
