"""Abstract base class for analysis strategies.

Every analysis strategy must implement can_handle() and analyze().
The pipeline calls can_handle() in registration order; the first match wins.
"""

from __future__ import annotations

from abc import ABC, abstractmethod

from ..types import AnalysisResult, CircuitGraph


class AnalysisStrategy(ABC):
    """Plug-in analysis strategy for one circuit problem type."""

    @abstractmethod
    def can_handle(self, graph: CircuitGraph) -> bool:
        """Return True if this strategy can analyze the given circuit."""
        ...

    @abstractmethod
    def analyze(self, graph: CircuitGraph) -> AnalysisResult:
        """Run the analysis and return a structured result."""
        ...

    @abstractmethod
    def get_problem_type(self) -> str:
        """Human-readable problem type label, e.g. 'bjt_dc_bias'."""
        ...

    def get_teaching_tags(self) -> list[str]:
        """Teaching tags contributed by this strategy."""
        return []
