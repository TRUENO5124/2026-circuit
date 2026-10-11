"""Analysis pipeline: strategy registry and dispatch.

Usage:
    pipeline = AnalysisPipeline()
    result = pipeline.analyze(circuit_graph)
    print(result.to_dict())
"""

from __future__ import annotations

from .types import AnalysisResult, CircuitGraph
from .strategies.base import AnalysisStrategy
from .strategies.bjt_dc import BJTDCBiasStrategy
from .strategies.bjt_ac import BJTACSmallSignalStrategy
from .strategies.mos_dc import MOSDCBiasStrategy
from .strategies.mos_ac import MOSACSmallSignalStrategy
from .strategies.diode import DiodeAnalysisStrategy
from .strategies.linear import LinearAnalysisStrategy


class UnsupportedCircuitError(ValueError):
    """Raised when no registered strategy can handle a circuit."""


class AnalysisPipeline:
    """Strategy-based analysis pipeline.

    Strategies are checked in registration order; the first that returns
    True from can_handle() is used.

    >>> pipeline = AnalysisPipeline()
    >>> result = pipeline.analyze(graph)
    """

    def __init__(self, strategies: list[AnalysisStrategy] | None = None):
        if strategies is not None:
            self._strategies = list(strategies)
        else:
            self._strategies = [
                DiodeAnalysisStrategy(),
                BJTACSmallSignalStrategy(),
                BJTDCBiasStrategy(),
                MOSACSmallSignalStrategy(),
                MOSDCBiasStrategy(),
                LinearAnalysisStrategy(),
            ]

    def register(self, strategy: AnalysisStrategy) -> None:
        """Register a strategy (appended to the end of the list)."""
        self._strategies.append(strategy)

    def register_first(self, strategy: AnalysisStrategy) -> None:
        """Register a strategy at the front of the list (highest priority)."""
        self._strategies.insert(0, strategy)

    def analyze(self, graph: CircuitGraph) -> AnalysisResult:
        """Find the first matching strategy and run it.

        Raises UnsupportedCircuitError if no strategy can handle the circuit.
        """
        for strategy in self._strategies:
            if strategy.can_handle(graph):
                return strategy.analyze(graph)
        raise UnsupportedCircuitError(
            f"No analysis strategy can handle circuit '{graph.id}'. "
            f"Components: {[c.definition_id for c in graph.components]}"
        )

    @property
    def registered_strategies(self) -> list[str]:
        """Return class names of all registered strategies."""
        return [s.__class__.__name__ for s in self._strategies]
