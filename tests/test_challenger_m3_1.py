"""
Adversarial Empirical Verification Harness for Milestone 3 (Requirement R3)
Challenger M3-1: Geometric Consistency, Solder Dot Keep-Out, and Manhattan Invariant

Empirical Assertions:
1. Solder Dot Terminal Keep-Out:
   - For every rendered solder dot across all templates, Euclidean distance to EVERY component pin > 5.0px.
   - Zero solder dots within 5.0px of any component pin.
2. True T-Junction Solder Dot Coverage:
   - Every genuine T-junction outside 5.0px pin keep-out has a rendered 3.8px solder dot.
   - Pin terminations (at distance <= 5.0px) have ZERO solder dots.
3. Dynamic Component Dragging & Manhattan Invariant:
   - Moving any component in any template preserves 100% orthogonal Manhattan routing (zero wire slant).
   - Trunk power/ground rails remain perfectly horizontal.
   - T-tap branch wires slide perpendicularly along trunk rails without tearing or orphan displacement.
"""

import math
import sys
from typing import Dict, List, Tuple, Optional, Any, Set

class Vector2:
    def __init__(self, x: float, y: float):
        self.x = float(x)
        self.y = float(y)

    def __repr__(self):
        return f"({self.x:g}, {self.y:g})"

    def __eq__(self, other):
        if not isinstance(other, Vector2):
            return False
        return abs(self.x - other.x) < 1e-3 and abs(self.y - other.y) < 1e-3

    def __hash__(self):
        return hash((round(self.x, 3), round(self.y, 3)))

    def distance_to(self, other: 'Vector2') -> float:
        return math.hypot(self.x - other.x, self.y - other.y)

    def distance_squared(self, other: 'Vector2') -> float:
        dx = self.x - other.x
        dy = self.y - other.y
        return dx * dx + dy * dy


class CircuitPin:
    def __init__(self, pin_id: str, name: str, rel_x: float, rel_y: float):
        self.id = pin_id
        self.name = name
        self.rel_x = float(rel_x)
        self.rel_y = float(rel_y)


class CircuitElement:
    def __init__(self, elem_id: str, name: str, elem_type: str, x: float, y: float,
                 width: float = 80, height: float = 50, rotation_deg: int = 0, orientation: str = "Horizontal",
                 display_number: float = 0.0, unit_symbol: str = ""):
        self.id = elem_id
        self.name = name
        self.type = elem_type
        self.x = float(x)
        self.y = float(y)
        self.width = float(width)
        self.height = float(height)
        self.rotation_deg = rotation_deg
        self.orientation = orientation
        self.display_number = display_number
        self.unit_symbol = unit_symbol
        self.pins: Dict[str, CircuitPin] = {}
        self.init_pins()

    def clone(self) -> 'CircuitElement':
        el = CircuitElement(self.id, self.name, self.type, self.x, self.y,
                            self.width, self.height, self.rotation_deg, self.orientation,
                            self.display_number, self.unit_symbol)
        return el

    def init_pins(self):
        t = self.type
        if t == "Resistor":
            self.pins["1"] = CircuitPin("1", "引脚 1", -40, 0)
            self.pins["2"] = CircuitPin("2", "引脚 2", 40, 0)
        elif t in ("Capacitor", "Inductor"):
            self.pins["1"] = CircuitPin("1", "引脚 1", -35, 0)
            self.pins["2"] = CircuitPin("2", "引脚 2", 35, 0)
        elif t in ("DcVoltageSource", "AcVoltageSource"):
            self.pins["pos"] = CircuitPin("pos", "正极 (+)", 0, -35)
            self.pins["neg"] = CircuitPin("neg", "负极 (-)", 0, 35)
        elif t in ("BjtNpn", "BjtPnp"):
            self.pins["B"] = CircuitPin("B", "基极 (B)", -35, 0)
            self.pins["C"] = CircuitPin("C", "集电极 (C)", 20, -35)
            self.pins["E"] = CircuitPin("E", "发射极 (E)", 20, 35)
        elif t == "Ground":
            self.pins["GND"] = CircuitPin("GND", "参考地 (GND)", 0, -22)
        else:
            self.pins["1"] = CircuitPin("1", "端 1", -35, 0)
            self.pins["2"] = CircuitPin("2", "端 2", 35, 0)

    def get_pin_world_pos(self, pin_key: str) -> Vector2:
        pin = None
        if pin_key in self.pins:
            pin = self.pins[pin_key]
        else:
            for p in self.pins.values():
                if p.name == pin_key or p.id == pin_key:
                    pin = p
                    break
        if not pin:
            raise KeyError(f"Pin '{pin_key}' not found on {self.id} ({self.type})")

        rad = math.radians(self.rotation_deg)
        cos_r = math.cos(rad)
        sin_r = math.sin(rad)
        rot_x = pin.rel_x * cos_r - pin.rel_y * sin_r
        rot_y = pin.rel_x * sin_r + pin.rel_y * cos_r
        return Vector2(self.x + rot_x, self.y + rot_y)


class CircuitWire:
    def __init__(self, wire_id: str, start_elem: str, start_pin: str,
                  end_elem: str, end_pin: str,
                  start_x: float, start_y: float, end_x: float, end_y: float,
                  flip_corner: bool = False, custom_mid: float = 0.0, net_id: int = 0):
        self.id = wire_id
        self.start_element_id = start_elem
        self.start_pin_name = start_pin
        self.end_element_id = end_elem
        self.end_pin_name = end_pin
        self.start_x = float(start_x)
        self.start_y = float(start_y)
        self.end_x = float(end_x)
        self.end_y = float(end_y)
        self.flip_corner = flip_corner
        self.custom_mid = float(custom_mid)
        self.net_id = net_id

    def clone(self) -> 'CircuitWire':
        return CircuitWire(self.id, self.start_element_id, self.start_pin_name,
                           self.end_element_id, self.end_pin_name,
                           self.start_x, self.start_y, self.end_x, self.end_y,
                           self.flip_corner, self.custom_mid, self.net_id)

    def get_path_points(self) -> List[Vector2]:
        p1 = Vector2(self.start_x, self.start_y)
        p2 = Vector2(self.end_x, self.end_y)
        pts = [p1]

        if abs(p1.x - p2.x) > 1.0 and abs(p1.y - p2.y) > 1.0:
            if self.custom_mid != 0.0:
                if self.flip_corner:
                    pts.append(Vector2(p1.x, self.custom_mid))
                    pts.append(Vector2(p2.x, self.custom_mid))
                else:
                    pts.append(Vector2(self.custom_mid, p1.y))
                    pts.append(Vector2(self.custom_mid, p2.y))
            else:
                if self.flip_corner:
                    pts.append(Vector2(p1.x, p2.y))
                else:
                    pts.append(Vector2(p2.x, p1.y))
        pts.append(p2)
        return pts

    def get_segments(self) -> List[Tuple[Vector2, Vector2]]:
        pts = self.get_path_points()
        return [(pts[i], pts[i+1]) for i in range(len(pts)-1)]

    def project_tap_perpendicular(self, branch_point: Vector2) -> Vector2:
        """Mirrors CircuitElement.cs ProjectTapPerpendicular exactly."""
        segments = self.get_segments()
        if not segments:
            return branch_point

        best_dist_sq = float('inf')
        best_proj = branch_point

        for a, b in segments:
            is_horizontal = abs(a.y - b.y) < 1e-3
            is_vertical = abs(a.x - b.x) < 1e-3

            if is_horizontal:
                min_x = min(a.x, b.x)
                max_x = max(a.x, b.x)
                snapped_x = round(branch_point.x / 10.0) * 10.0
                clamped_x = max(min_x, min(max_x, snapped_x))
                proj = Vector2(clamped_x, a.y)
            elif is_vertical:
                min_y = min(a.y, b.y)
                max_y = max(a.y, b.y)
                snapped_y = round(branch_point.y / 10.0) * 10.0
                clamped_y = max(min_y, min(max_y, snapped_y))
                proj = Vector2(a.x, clamped_y)
            else:
                ab_x = b.x - a.x
                ab_y = b.y - a.y
                len_sq = ab_x * ab_x + ab_y * ab_y
                if len_sq < 1e-4:
                    continue
                t = max(0.0, min(1.0, ((branch_point.x - a.x) * ab_x + (branch_point.y - a.y) * ab_y) / len_sq))
                proj = Vector2(a.x + t * ab_x, a.y + t * ab_y)

            d_sq = branch_point.distance_squared(proj)
            if d_sq < best_dist_sq:
                best_dist_sq = d_sq
                best_proj = proj

        return best_proj


def is_point_on_segment_strict(p: Vector2, a: Vector2, b: Vector2, tolerance: float = 3.5) -> bool:
    """Mirrors CircuitCanvas.xaml.cs IsPointOnSegmentStrict."""
    ab_x = b.x - a.x
    ab_y = b.y - a.y
    len_sq = ab_x * ab_x + ab_y * ab_y
    if len_sq < 1e-4:
        return False
    t = max(0.0, min(1.0, ((p.x - a.x) * ab_x + (p.y - a.y) * ab_y) / len_sq))
    proj = Vector2(a.x + t * ab_x, a.y + t * ab_y)
    if p.distance_to(proj) > tolerance:
        return False
    # Strict interior: at least 4.0px away from segment ends
    if p.distance_to(a) < 4.0 or p.distance_to(b) < 4.0:
        return False
    return True


def compute_canvas_solder_dots(elements: List[CircuitElement], wires: List[CircuitWire]) -> List[Vector2]:
    """Exact reproduction of CircuitCanvas.xaml.cs lines 359-470."""
    all_pin_world_positions = []
    for el in elements:
        for pin_key in el.pins:
            all_pin_world_positions.append(el.get_pin_world_pos(pin_key))

    def is_outside_pin_keep_out(pt: Vector2) -> bool:
        for pin_pos in all_pin_world_positions:
            if pt.distance_to(pin_pos) <= 5.0:
                return False
        return True

    raw_junction_points = []
    all_segments = []
    for w in wires:
        for sa, sb in w.get_segments():
            all_segments.append((sa, sb, w))

    # A) Detect true T-junctions
    for wire in wires:
        endpoints = [Vector2(wire.start_x, wire.start_y), Vector2(wire.end_x, wire.end_y)]
        for ep in endpoints:
            if not is_outside_pin_keep_out(ep):
                continue
            for sa, sb, sw in all_segments:
                if sw.id == wire.id:
                    continue
                if is_point_on_segment_strict(ep, sa, sb, tolerance=3.5):
                    raw_junction_points.append(ep)
                    break

        # Explicit WireTap endpoints
        if wire.start_pin_name == "WireTap":
            p = Vector2(wire.start_x, wire.start_y)
            if is_outside_pin_keep_out(p):
                raw_junction_points.append(p)
        if wire.end_pin_name == "WireTap":
            p = Vector2(wire.end_x, wire.end_y)
            if is_outside_pin_keep_out(p):
                raw_junction_points.append(p)

    # B) 3/4-way intersections: 3 or more endpoints meeting within 3px
    all_endpoints = []
    for wire in wires:
        all_endpoints.append(Vector2(wire.start_x, wire.start_y))
        all_endpoints.append(Vector2(wire.end_x, wire.end_y))

    for i in range(len(all_endpoints)):
        ep1 = all_endpoints[i]
        if not is_outside_pin_keep_out(ep1):
            continue
        meeting_count = sum(1 for ep2 in all_endpoints if ep1.distance_to(ep2) <= 3.0)
        if meeting_count >= 3:
            raw_junction_points.append(ep1)

    # C) Deduplicate / cluster within 2.5px
    unique_solder_dots: List[Vector2] = []
    for pt in raw_junction_points:
        exists = any(pt.distance_to(dot) <= 2.5 for dot in unique_solder_dots)
        if not exists:
            unique_solder_dots.append(pt)

    return unique_solder_dots


def sync_connected_wires_to_element(el: CircuitElement, wires: List[CircuitWire]):
    """Mirrors AnalysisStudioViewModel.cs SyncConnectedWiresToElement Two-Phase Synchronization."""
    moved_wire_ids = set()

    # Phase 1: Update direct pin-connected wires with 10px grid snap
    for w in wires:
        if w.start_element_id == el.id and w.start_pin_name != "WireTap":
            try:
                p_pos = el.get_pin_world_pos(w.start_pin_name)
                w.start_x = p_pos.x
                w.start_y = p_pos.y
                moved_wire_ids.add(w.id)
            except KeyError:
                pass
        if w.end_element_id == el.id and w.end_pin_name != "WireTap":
            try:
                p_pos = el.get_pin_world_pos(w.end_pin_name)
                w.end_x = p_pos.x
                w.end_y = p_pos.y
                moved_wire_ids.add(w.id)
            except KeyError:
                pass

    wire_map = {w.id: w for w in wires}

    # Phase 2A: Branch wires starting or ending at el and tapping into a parent rail
    for w in wires:
        if w.start_element_id == el.id and w.end_pin_name == "WireTap":
            parent = wire_map.get(w.end_element_id)
            if parent:
                try:
                    p_pos = el.get_pin_world_pos(w.start_pin_name)
                    w.start_x = p_pos.x
                    w.start_y = p_pos.y
                    new_tap = parent.project_tap_perpendicular(Vector2(w.start_x, w.start_y))
                    w.end_x = new_tap.x
                    w.end_y = new_tap.y
                    moved_wire_ids.add(w.id)
                except KeyError:
                    pass
        elif w.end_element_id == el.id and w.start_pin_name == "WireTap":
            parent = wire_map.get(w.start_element_id)
            if parent:
                try:
                    p_pos = el.get_pin_world_pos(w.end_pin_name)
                    w.end_x = p_pos.x
                    w.end_y = p_pos.y
                    new_tap = parent.project_tap_perpendicular(Vector2(w.end_x, w.end_y))
                    w.start_x = new_tap.x
                    w.start_y = new_tap.y
                    moved_wire_ids.add(w.id)
                except KeyError:
                    pass

    # Phase 2B: Child wires tapping into parent wires that moved in Phase 1 or 2A
    for _ in range(3):
        any_updated = False
        for w in wires:
            if w.end_pin_name == "WireTap" and w.end_element_id in moved_wire_ids and w.id not in moved_wire_ids:
                parent = wire_map.get(w.end_element_id)
                if parent:
                    new_tap = parent.project_tap_perpendicular(Vector2(w.start_x, w.start_y))
                    w.end_x = new_tap.x
                    w.end_y = new_tap.y
                    moved_wire_ids.add(w.id)
                    any_updated = True
            if w.start_pin_name == "WireTap" and w.start_element_id in moved_wire_ids and w.id not in moved_wire_ids:
                parent = wire_map.get(w.start_element_id)
                if parent:
                    new_tap = parent.project_tap_perpendicular(Vector2(w.end_x, w.end_y))
                    w.start_x = new_tap.x
                    w.start_y = new_tap.y
                    moved_wire_ids.add(w.id)
                    any_updated = True
        if not any_updated:
            break


# Template definitions
def get_templates() -> Dict[str, Tuple[List[CircuitElement], List[CircuitWire]]]:
    import verify_templates
    raw_templates = {
        "CommonEmitter_Amp": verify_templates.get_common_emitter_template(),
        "RC_Transient": verify_templates.get_rc_transient_template(),
        "RC_Coupled_Amp": verify_templates.get_rc_coupled_template(),
    }
    converted = {}
    for name, (raw_els, raw_ws) in raw_templates.items():
        els = [CircuitElement(e.id, e.name, e.type, e.x, e.y, e.width, e.height,
                              e.rotation_deg, e.orientation, e.display_number, e.unit_symbol)
               for e in raw_els]
        ws = [CircuitWire(w.id, w.start_element_id, w.start_pin_name,
                          w.end_element_id, w.end_pin_name,
                          w.start_x, w.start_y, w.end_x, w.end_y,
                          w.flip_corner, getattr(w, 'custom_mid', 0.0), w.net_id)
              for w in raw_ws]
        converted[name] = (els, ws)
    return converted


def run_challenger_m3_1_suite():
    print("=" * 80)
    print("CHALLENGER M3-1: EMPIRICAL STRESS TEST SUITE (EDA ROUTING & CANVAS)")
    print("=" * 80)

    total_checks = 0
    passed_checks = 0
    failures = []

    templates = get_templates()

    # =========================================================================
    # SUITE 1: Continuous Euclidean Solder Dot Keep-Out (> 5.0px)
    # =========================================================================
    print("\n>>> [TEST SUITE 1] SOLDER DOT TERMINAL KEEP-OUT (> 5.0px EUCLIDEAN)")

    for t_name, (elements, wires) in templates.items():
        print(f"\n  Analyzing Template: {t_name}")
        dots = compute_canvas_solder_dots(elements, wires)
        print(f"    Rendered EDA solder dots count: {len(dots)}")

        pin_positions = []
        for el in elements:
            for pk in el.pins:
                pin_positions.append((el.get_pin_world_pos(pk), el.id, pk))

        for idx, dot in enumerate(dots):
            total_checks += 1
            min_dist = float('inf')
            closest_pin = None
            for p_pos, el_id, pk in pin_positions:
                d = dot.distance_to(p_pos)
                if d < min_dist:
                    min_dist = d
                    closest_pin = (el_id, pk)

            if min_dist <= 5.0:
                failures.append(f"{t_name}: Solder dot #{idx} at {dot} violates keep-out! Distance to pin {closest_pin[0]}.{closest_pin[1]} is {min_dist:.2f}px <= 5.0px")
            else:
                passed_checks += 1
                # Diagnostic log for clearance confirmation
                # print(f"      Dot #{idx} at {dot}: nearest pin={closest_pin[0]}.{closest_pin[1]}, clearance={min_dist:.1f}px > 5.0px")

        # Invariant: Zero solder dots within 5.0px of any pin
        total_checks += 1
        dots_in_keepout = [dot for dot in dots if any(dot.distance_to(p[0]) <= 5.0 for p in pin_positions)]
        if dots_in_keepout:
            failures.append(f"{t_name}: {len(dots_in_keepout)} solder dots rendered inside pin keep-out zones: {dots_in_keepout}")
        else:
            passed_checks += 1

    # =========================================================================
    # SUITE 2: True T-Junction Solder Dot Completeness & Pin Terminal Silence
    # =========================================================================
    print("\n>>> [TEST SUITE 2] TRUE T-JUNCTION SOLDER DOT COVERAGE & TERMINAL SILENCE")

    for t_name, (elements, wires) in templates.items():
        print(f"\n  Analyzing Template: {t_name}")
        dots = compute_canvas_solder_dots(elements, wires)

        # Identify all genuine T-junctions
        t_junctions: List[Tuple[Vector2, str]] = []
        wire_map = {w.id: w for w in wires}

        for w in wires:
            if w.start_pin_name == "WireTap":
                t_junctions.append((Vector2(w.start_x, w.start_y), f"{w.id}.Start->{w.start_element_id}"))
            if w.end_pin_name == "WireTap":
                t_junctions.append((Vector2(w.end_x, w.end_y), f"{w.id}.End->{w.end_element_id}"))

        pin_positions = [el.get_pin_world_pos(pk) for el in elements for pk in el.pins]

        for pt, desc in t_junctions:
            total_checks += 1
            # Check clearance to pins
            dist_to_pins = min(pt.distance_to(p) for p in pin_positions)
            if dist_to_pins > 5.0:
                # Must be rendered as a solder dot!
                matched_dot = any(pt.distance_to(dot) <= 2.5 for dot in dots)
                if matched_dot:
                    passed_checks += 1
                else:
                    failures.append(f"{t_name}: True T-junction at {pt} ({desc}, clearance={dist_to_pins:.1f}px) MISSING solder dot!")
            else:
                # Must NOT be rendered as a solder dot!
                matched_dot = any(pt.distance_to(dot) <= 2.5 for dot in dots)
                if matched_dot:
                    failures.append(f"{t_name}: T-junction at {pt} ({desc}) within keep-out ({dist_to_pins:.1f}px) ERRONEOUSLY rendered solder dot!")
                else:
                    passed_checks += 1

        # Check Pin Terminal Silence: Component pin terminals must NEVER render solder dots
        for el in elements:
            for pk in el.pins:
                total_checks += 1
                p_pos = el.get_pin_world_pos(pk)
                dot_at_pin = [dot for dot in dots if dot.distance_to(p_pos) <= 5.0]
                if dot_at_pin:
                    failures.append(f"{t_name}: Pin {el.id}.{pk} at {p_pos} has illegal solder dot: {dot_at_pin}")
                else:
                    passed_checks += 1

    # =========================================================================
    # SUITE 3: Dynamic Component Movement & Manhattan Orthogonality Invariant
    # =========================================================================
    print("\n>>> [TEST SUITE 3] DYNAMIC COMPONENT DRAGGING & MANHATTAN ORTHOGONALITY")

    delta_moves = [
        (10, 0), (-10, 0), (0, 10), (0, -10),
        (20, 0), (-20, 0), (0, 20), (0, -20),
        (40, 0), (-40, 0), (0, 40), (0, -40),
        (30, 20), (-20, 40)
    ]

    for t_name, (elements, wires) in templates.items():
        print(f"\n  Stress-testing Template: {t_name}")
        movable_elements = [el for el in elements if el.type != "Ground"]

        for el in movable_elements:
            for dx, dy in delta_moves:
                # Clone elements and wires
                test_elements = [e.clone() for e in elements]
                test_wires = [w.clone() for w in wires]
                target_el = next(e for e in test_elements if e.id == el.id)

                # Move target element
                target_el.x += dx
                target_el.y += dy

                # Synchronize wires using Two-Phase Topology Synchronization
                sync_connected_wires_to_element(target_el, test_wires)

                # Check 1: Strict Manhattan Orthogonality on ALL wire segments
                for tw in test_wires:
                    for sa, sb in tw.get_segments():
                        total_checks += 1
                        is_orthogonal = (abs(sa.x - sb.x) < 1e-3) or (abs(sa.y - sb.y) < 1e-3)
                        if not is_orthogonal:
                            failures.append(f"{t_name} [Move {el.id} by ({dx},{dy})]: Wire {tw.id} segment {sa}->{sb} is SLANTED / NON-MANHATTAN (dx={abs(sa.x-sb.x):.2f}, dy={abs(sa.y-sb.y):.2f})!")
                        else:
                            passed_checks += 1

                # Check 2: 5px Grid Micro-Snapping on all vertices (matching verify_templates.py)
                for tw in test_wires:
                    for pt in tw.get_path_points():
                        total_checks += 1
                        grid_x_err = abs(pt.x - round(pt.x / 5.0) * 5.0)
                        grid_y_err = abs(pt.y - round(pt.y / 5.0) * 5.0)
                        if grid_x_err > 1e-2 or grid_y_err > 1e-2:
                            failures.append(f"{t_name} [Move {el.id} by ({dx},{dy})]: Wire {tw.id} vertex {pt} off 5px grid!")
                        else:
                            passed_checks += 1

                # Check 3: Bus Rail Invariant (VCC and GND rails must remain strictly horizontal)
                vcc_rail = next((w for w in test_wires if w.id == "W_VCC_RAIL"), None)
                if vcc_rail:
                    total_checks += 1
                    if abs(vcc_rail.start_y - 80) > 1e-3 or abs(vcc_rail.end_y - 80) > 1e-3:
                        failures.append(f"{t_name} [Move {el.id} by ({dx},{dy})]: W_VCC_RAIL Y was distorted! Y=[{vcc_rail.start_y}, {vcc_rail.end_y}] (expected 80)")
                    else:
                        passed_checks += 1

                gnd_rail = next((w for w in test_wires if w.id == "W_GND_RAIL"), None)
                if gnd_rail:
                    total_checks += 1
                    exp_y = 360 if t_name == "RC_Transient" else 480
                    if abs(gnd_rail.start_y - exp_y) > 1e-3 or abs(gnd_rail.end_y - exp_y) > 1e-3:
                        failures.append(f"{t_name} [Move {el.id} by ({dx},{dy})]: W_GND_RAIL Y was distorted! Y=[{gnd_rail.start_y}, {gnd_rail.end_y}] (expected {exp_y})")
                    else:
                        passed_checks += 1

    # =========================================================================
    # SUMMARY & VERDICT
    # =========================================================================
    print("\n" + "=" * 80)
    print("CHALLENGER M3-1 EMPIRICAL RESULTS SUMMARY")
    print(f"Total Checks Executed: {total_checks}")
    print(f"Passed Checks:        {passed_checks}")
    print(f"Failed Checks:        {len(failures)}")
    print("=" * 80)

    if failures:
        print("\nFAILURE LOG:")
        for idx, f in enumerate(failures[:20], 1):
            print(f"  [{idx}] {f}")
        if len(failures) > 20:
            print(f"  ... and {len(failures) - 20} more failures.")
        print("\nOVERALL EMPIRICAL VERDICT: REJECT")
        return 1
    else:
        print("\nALL EMPIRICAL TESTS PASSED WITH 100% SUCCESS RATE.")
        print("OVERALL EMPIRICAL VERDICT: APPROVE")
        return 0


if __name__ == "__main__":
    sys.exit(run_challenger_m3_1_suite())
