"""
Adversarial Verification Suite for Milestone 1 (Educational Templates & Model Modernization - R2)
Challenger M1-2 Empirical Stress-Test

Test Scope:
1. Template Switching Integrity:
   - Verify that switching between common_emitter, rc_transient, ac_coupled, and blank
     leaves zero residual elements, zero orphan wires, and exactly the expected element/wire counts.
   - Random and permutative stress switching (100+ state transitions).
2. Graph Connectivity via Union-Find:
   - Disjoint-set union on element pins and circuit wires.
   - Verify Ground Rail net connectivity (GND, vs.neg, VCC.neg, bypass caps, resistors).
   - Verify VCC Rail net connectivity (VCC.pos, bias resistors, pull-ups).
   - Verify Signal path connectivity (Input -> Base -> Collector -> Output).
   - Verify absence of unwanted short circuits (VCC != GND, Input != Output).
   - Verify absence of orphan wires (every wire must be part of a connected net with valid components).
   - Verify absence of dangling references (every referenced element and pin must exist).
3. Geometric & Coordinate Exactness:
   - Every wire endpoint claiming to connect to a pin must exactly match the pin's world position.
   - Every WireTap must lie on the segment of the tapped parent wire.
4. Bus/Trunk Routing Compliance:
   - Every pin must have at most 1 primary wire lead. Parallel branches must tap via T-junctions.
   - Check if trunk wires (rails) erroneously bind to component pins, causing rail distortion upon component movement.
"""

import math
import random
import sys
from collections import defaultdict
from typing import Dict, List, Set, Tuple, Optional, Any

class Vector2:
    def __init__(self, x: float, y: float):
        self.x = round(float(x), 4)
        self.y = round(float(y), 4)

    def __repr__(self):
        return f"({self.x:g}, {self.y:g})"

    def __eq__(self, other):
        if not isinstance(other, Vector2):
            return False
        return abs(self.x - other.x) < 1e-3 and abs(self.y - other.y) < 1e-3

    def __hash__(self):
        return hash((round(self.x, 2), round(self.y, 2)))

    def distance_to(self, other: 'Vector2') -> float:
        return math.hypot(self.x - other.x, self.y - other.y)

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
                if p.name == pin_key:
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
                 flip_corner: bool = False, net_id: int = 0):
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
        self.net_id = net_id

    def get_path_points(self) -> List[Vector2]:
        p1 = Vector2(self.start_x, self.start_y)
        p2 = Vector2(self.end_x, self.end_y)
        pts = [p1]
        if abs(p1.x - p2.x) > 6 and abs(p1.y - p2.y) > 6:
            if self.flip_corner:
                pts.append(Vector2(p1.x, p2.y))
            else:
                pts.append(Vector2(p2.x, p1.y))
        pts.append(p2)
        return pts

    def get_segments(self) -> List[Tuple[Vector2, Vector2]]:
        pts = self.get_path_points()
        return [(pts[i], pts[i+1]) for i in range(len(pts)-1)]

    def point_on_wire(self, pt: Vector2, tolerance: float = 1.0) -> bool:
        for sa, sb in self.get_segments():
            # Orthogonal segment check
            if abs(sa.x - sb.x) < 1e-3: # vertical
                if abs(pt.x - sa.x) <= tolerance and (min(sa.y, sb.y) - tolerance <= pt.y <= max(sa.y, sb.y) + tolerance):
                    return True
            elif abs(sa.y - sb.y) < 1e-3: # horizontal
                if abs(pt.y - sa.y) <= tolerance and (min(sa.x, sb.x) - tolerance <= pt.x <= max(sa.x, sb.x) + tolerance):
                    return True
            else:
                # General distance
                dx = sb.x - sa.x
                dy = sb.y - sa.y
                l2 = dx*dx + dy*dy
                if l2 > 0:
                    t = max(0.0, min(1.0, ((pt.x - sa.x)*dx + (pt.y - sa.y)*dy) / l2))
                    proj = Vector2(sa.x + t*dx, sa.y + t*dy)
                    if pt.distance_to(proj) <= tolerance:
                        return True
        return False


def get_template_data(template_name: str) -> Tuple[List[CircuitElement], List[CircuitWire]]:
    """Mirrors CircuitTemplateFactory.cs exactly."""
    if template_name == "common_emitter":
        elements = [
            CircuitElement("vs", "交流信号源 vs", "AcVoltageSource", 80, 320, 60, 60, 0, "Horizontal", 10.0, "mV"),
            CircuitElement("C1", "输入耦合电容 C1", "Capacitor", 160, 260, 80, 50, 0, "Horizontal", 10.0, "μF"),
            CircuitElement("Rb1", "基极偏置电阻 Rb1", "Resistor", 240, 160, 80, 50, 90, "Vertical", 51.0, "kΩ"),
            CircuitElement("Rb2", "基极偏置电阻 Rb2", "Resistor", 240, 360, 80, 50, 90, "Vertical", 15.0, "kΩ"),
            CircuitElement("Q1", "NPN 晶体管 Q1", "BjtNpn", 320, 260, 80, 80, 0, "Horizontal", 100.0, "β"),
            CircuitElement("Rc", "集电极电阻 Rc", "Resistor", 340, 140, 80, 50, 90, "Vertical", 3.0, "kΩ"),
            CircuitElement("Re", "发射极电阻 Re", "Resistor", 320, 400, 80, 50, 90, "Vertical", 1.0, "kΩ"),
            CircuitElement("Ce", "发射极旁路电容 Ce", "Capacitor", 420, 400, 80, 50, 90, "Vertical", 47.0, "μF"),
            CircuitElement("C2", "输出耦合电容 C2", "Capacitor", 440, 200, 80, 50, 0, "Horizontal", 10.0, "μF"),
            CircuitElement("RL", "负载电阻 RL", "Resistor", 540, 280, 80, 50, 90, "Vertical", 10.0, "kΩ"),
            CircuitElement("VCC", "直流电源 VCC", "DcVoltageSource", 620, 280, 60, 60, 0, "Horizontal", 12.0, "V"),
            CircuitElement("GND", "参考地 GND", "Ground", 340, 502, 40, 35, 0, "Horizontal", 0.0, "V")
        ]
        wires = [
            CircuitWire("W_VS_C1", "vs", "pos", "C1", "1", 80, 285, 125, 260, True, 1),
            CircuitWire("W_BASE_LINE", "C1", "2", "Q1", "B", 195, 260, 285, 260, False, 2),
            CircuitWire("W_RB1_BASE", "Rb1", "2", "W_BASE_LINE", "WireTap", 240, 200, 240, 260, False, 2),
            CircuitWire("W_RB2_BASE", "Rb2", "1", "W_BASE_LINE", "WireTap", 240, 320, 240, 260, False, 2),
            CircuitWire("W_VCC_RAIL", "W_RB1_VCC", "WireTap", "W_VCC_FEED", "WireTap", 240, 80, 620, 80, False, 3),
            CircuitWire("W_RB1_VCC", "Rb1", "1", "W_VCC_RAIL", "WireTap", 240, 120, 240, 80, False, 3),
            CircuitWire("W_RC_VCC", "Rc", "1", "W_VCC_RAIL", "WireTap", 340, 100, 340, 80, False, 3),
            CircuitWire("W_VCC_FEED", "VCC", "pos", "W_VCC_RAIL", "WireTap", 620, 245, 620, 80, False, 3),
            CircuitWire("W_COLL_LINE", "Rc", "2", "Q1", "C", 340, 180, 340, 225, False, 4),
            CircuitWire("W_COLL_C2", "W_COLL_LINE", "WireTap", "C2", "1", 340, 200, 405, 200, False, 4),
            CircuitWire("W_EMIT_TRUNK", "Q1", "E", "W_EMIT_BUS", "WireTap", 340, 295, 340, 340, False, 5),
            CircuitWire("W_EMIT_BUS", "W_RE_DROP", "WireTap", "W_CE_DROP", "WireTap", 320, 340, 420, 340, False, 5),
            CircuitWire("W_RE_DROP", "Re", "1", "W_EMIT_BUS", "WireTap", 320, 360, 320, 340, False, 5),
            CircuitWire("W_CE_DROP", "Ce", "1", "W_EMIT_BUS", "WireTap", 420, 365, 420, 340, False, 5),
            CircuitWire("W_C2_OUT", "C2", "2", "W_RL_DROP", "WireTap", 475, 200, 540, 200, False, 6),
            CircuitWire("W_RL_DROP", "RL", "1", "W_C2_OUT", "WireTap", 540, 240, 540, 200, False, 6),
            CircuitWire("W_GND_RAIL", "W_VS_GND", "WireTap", "W_VCC_GND", "WireTap", 80, 480, 620, 480, False, 0),
            CircuitWire("W_VS_GND", "vs", "neg", "W_GND_RAIL", "WireTap", 80, 355, 80, 480, False, 0),
            CircuitWire("W_RB2_GND", "Rb2", "2", "W_GND_RAIL", "WireTap", 240, 400, 240, 480, False, 0),
            CircuitWire("W_RE_GND", "Re", "2", "W_GND_RAIL", "WireTap", 320, 440, 320, 480, False, 0),
            CircuitWire("W_CE_GND", "Ce", "2", "W_GND_RAIL", "WireTap", 420, 435, 420, 480, False, 0),
            CircuitWire("W_RL_GND", "RL", "2", "W_GND_RAIL", "WireTap", 540, 320, 540, 480, False, 0),
            CircuitWire("W_VCC_GND", "VCC", "neg", "W_GND_RAIL", "WireTap", 620, 315, 620, 480, False, 0)
        ]
        return elements, wires

    elif template_name == "rc_transient":
        elements = [
            CircuitElement("VS", "直流阶跃电源 VS", "DcVoltageSource", 120, 280, 60, 60, 0, "Horizontal", 10.0, "V"),
            CircuitElement("R1", "充电电阻 R1", "Resistor", 240, 200, 80, 50, 0, "Horizontal", 1.0, "kΩ"),
            CircuitElement("C1", "积分电容 C1", "Capacitor", 360, 280, 80, 50, 90, "Vertical", 100.0, "μF"),
            CircuitElement("GND", "参考地 GND", "Ground", 240, 382, 40, 35, 0, "Horizontal", 0.0, "V")
        ]
        wires = [
            CircuitWire("W_VS_R1", "VS", "pos", "R1", "1", 120, 245, 200, 200, True, 1),
            CircuitWire("W_R1_C1", "R1", "2", "C1", "1", 280, 200, 360, 245, False, 2),
            CircuitWire("W_GND_RAIL", "W_VS_GND", "WireTap", "W_C1_GND", "WireTap", 120, 360, 360, 360, False, 0),
            CircuitWire("W_VS_GND", "VS", "neg", "W_GND_RAIL", "WireTap", 120, 315, 120, 360, False, 0),
            CircuitWire("W_C1_GND", "C1", "2", "W_GND_RAIL", "WireTap", 360, 315, 360, 360, False, 0)
        ]
        return elements, wires

    elif template_name == "ac_coupled":
        elements = [
            CircuitElement("vs", "交流信号源 vs", "AcVoltageSource", 80, 320, 60, 60, 0, "Horizontal", 10.0, "mV"),
            CircuitElement("C1", "输入耦合电容 C1", "Capacitor", 160, 260, 80, 50, 0, "Horizontal", 10.0, "μF"),
            CircuitElement("Rb", "基极固定偏置电阻 Rb", "Resistor", 240, 160, 80, 50, 90, "Vertical", 300.0, "kΩ"),
            CircuitElement("Q1", "NPN 晶体管 Q1", "BjtNpn", 320, 260, 80, 80, 0, "Horizontal", 100.0, "β"),
            CircuitElement("Rc", "集电极电阻 Rc", "Resistor", 340, 140, 80, 50, 90, "Vertical", 3.0, "kΩ"),
            CircuitElement("C2", "输出耦合电容 C2", "Capacitor", 440, 200, 80, 50, 0, "Horizontal", 10.0, "μF"),
            CircuitElement("RL", "负载电阻 RL", "Resistor", 540, 280, 80, 50, 90, "Vertical", 10.0, "kΩ"),
            CircuitElement("VCC", "直流电源 VCC", "DcVoltageSource", 620, 280, 60, 60, 0, "Horizontal", 12.0, "V"),
            CircuitElement("GND", "参考地 GND", "Ground", 340, 502, 40, 35, 0, "Horizontal", 0.0, "V")
        ]
        wires = [
            CircuitWire("W_VS_C1", "vs", "pos", "C1", "1", 80, 285, 125, 260, True, 1),
            CircuitWire("W_BASE_LINE", "C1", "2", "Q1", "B", 195, 260, 285, 260, False, 2),
            CircuitWire("W_RB_BASE", "Rb", "2", "W_BASE_LINE", "WireTap", 240, 200, 240, 260, False, 2),
            CircuitWire("W_VCC_RAIL", "W_RB_VCC", "WireTap", "W_VCC_FEED", "WireTap", 240, 80, 620, 80, False, 3),
            CircuitWire("W_RB_VCC", "Rb", "1", "W_VCC_RAIL", "WireTap", 240, 120, 240, 80, False, 3),
            CircuitWire("W_RC_VCC", "Rc", "1", "W_VCC_RAIL", "WireTap", 340, 100, 340, 80, False, 3),
            CircuitWire("W_VCC_FEED", "VCC", "pos", "W_VCC_RAIL", "WireTap", 620, 245, 620, 80, False, 3),
            CircuitWire("W_COLL_LINE", "Rc", "2", "Q1", "C", 340, 180, 340, 225, False, 4),
            CircuitWire("W_COLL_C2", "W_COLL_LINE", "WireTap", "C2", "1", 340, 200, 405, 200, False, 4),
            CircuitWire("W_EMIT_GND", "Q1", "E", "W_GND_RAIL", "WireTap", 340, 295, 360, 480, False, 0),
            CircuitWire("W_C2_OUT", "C2", "2", "W_RL_DROP", "WireTap", 475, 200, 540, 200, False, 5),
            CircuitWire("W_RL_DROP", "RL", "1", "W_C2_OUT", "WireTap", 540, 240, 540, 200, False, 5),
            CircuitWire("W_GND_RAIL", "W_VS_GND", "WireTap", "W_VCC_GND", "WireTap", 80, 480, 620, 480, False, 0),
            CircuitWire("W_VS_GND", "vs", "neg", "W_GND_RAIL", "WireTap", 80, 355, 80, 480, False, 0),
            CircuitWire("W_RL_GND", "RL", "2", "W_GND_RAIL", "WireTap", 540, 320, 540, 480, False, 0),
            CircuitWire("W_VCC_GND", "VCC", "neg", "W_GND_RAIL", "WireTap", 620, 315, 620, 480, False, 0)
        ]
        return elements, wires

    elif template_name == "blank":
        return [], []
    else:
        raise ValueError(f"Unknown template: {template_name}")


# ==============================================================================
# Union-Find Data Structure
# ==============================================================================
class UnionFind:
    def __init__(self):
        self.parent = {}
        self.rank = {}

    def find(self, item):
        if item not in self.parent:
            self.parent[item] = item
            self.rank[item] = 0
            return item
        if self.parent[item] != item:
            self.parent[item] = self.find(self.parent[item])
        return self.parent[item]

    def union(self, a, b):
        root_a = self.find(a)
        root_b = self.find(b)
        if root_a != root_b:
            if self.rank[root_a] < self.rank[root_b]:
                self.parent[root_a] = root_b
            elif self.rank[root_a] > self.rank[root_b]:
                self.parent[root_b] = root_a
            else:
                self.parent[root_b] = root_a
                self.rank[root_a] += 1

    def get_components(self) -> Dict[Any, Set[Any]]:
        comps = defaultdict(set)
        for item in self.parent:
            root = self.find(item)
            comps[root].add(item)
        return comps


def run_adversarial_suite():
    failures = []
    total_tests = 0
    passed_tests = 0

    print("=" * 80)
    print("CHALLENGER M1-2: EMPIRICAL ADVERSARIAL STRESS-TEST SUITE")
    print("=" * 80)

    # --------------------------------------------------------------------------
    # SUITE 1: Template Switching & Lifecycle State Machine Stress Test
    # --------------------------------------------------------------------------
    print("\n>>> [SECTION 1] TEMPLATE SWITCHING & LIFECYCLE INTEGRITY")
    template_types = ["common_emitter", "rc_transient", "ac_coupled", "blank"]
    expected_counts = {
        "common_emitter": (12, 23),
        "rc_transient": (4, 5),
        "ac_coupled": (9, 16),
        "blank": (0, 0)
    }

    # Simulate ViewModel state
    vm_elements: List[CircuitElement] = []
    vm_wires: List[CircuitWire] = []
    current_template = None

    def switch_template(t_name: str):
        nonlocal vm_elements, vm_wires, current_template
        # In AnalysisStudioViewModel.cs CreateNewProject:
        vm_elements.clear()
        vm_wires.clear()
        els, ws = get_template_data(t_name)
        vm_elements.extend(els)
        vm_wires.extend(ws)
        current_template = t_name

    # 1. Deterministic transitions
    sequence = [
        "common_emitter", "rc_transient", "ac_coupled", "blank",
        "common_emitter", "ac_coupled", "rc_transient", "blank",
        "rc_transient", "common_emitter", "blank", "ac_coupled",
        "blank", "blank", "common_emitter", "common_emitter"
    ]

    for step_idx, target_template in enumerate(sequence):
        prev_template = current_template
        switch_template(target_template)

        total_tests += 1
        exp_elem_cnt, exp_wire_cnt = expected_counts[target_template]

        # Check exact count
        if len(vm_elements) != exp_elem_cnt or len(vm_wires) != exp_wire_cnt:
            failures.append(f"Switch step {step_idx} ({prev_template} -> {target_template}): count mismatch! Expected ({exp_elem_cnt} els, {exp_wire_cnt} wires), got ({len(vm_elements)}, {len(vm_wires)})")
        else:
            passed_tests += 1

        # Check unique IDs
        elem_ids = [e.id for e in vm_elements]
        wire_ids = [w.id for w in vm_wires]
        total_tests += 1
        if len(elem_ids) != len(set(elem_ids)):
            failures.append(f"Switch step {step_idx}: Duplicate element IDs in {target_template}: {elem_ids}")
        elif len(wire_ids) != len(set(wire_ids)):
            failures.append(f"Switch step {step_idx}: Duplicate wire IDs in {target_template}: {wire_ids}")
        else:
            passed_tests += 1

        # Check no residual elements from previous template
        if prev_template and prev_template != target_template:
            prev_els, prev_ws = get_template_data(prev_template)
            prev_elem_ids = {e.id for e in prev_els}
            curr_elem_ids = set(elem_ids)
            prev_only_ids = prev_elem_ids - {e.id for e in get_template_data(target_template)[0]}
            residual_ids = prev_only_ids.intersection(curr_elem_ids)
            total_tests += 1
            if residual_ids:
                failures.append(f"Switch step {step_idx} ({prev_template} -> {target_template}): Residual elements left over: {residual_ids}")
            else:
                passed_tests += 1

    # 2. Random Monte Carlo stress transitions (100 switches)
    print("  Testing 100 random Monte Carlo switching transitions...")
    random.seed(42)
    for i in range(100):
        t_name = random.choice(template_types)
        switch_template(t_name)
        exp_elem_cnt, exp_wire_cnt = expected_counts[t_name]
        total_tests += 1
        if len(vm_elements) != exp_elem_cnt or len(vm_wires) != exp_wire_cnt:
            failures.append(f"Monte Carlo step {i} ({t_name}): Count mismatch!")
            break
        else:
            passed_tests += 1

    print(f"  [Section 1 Complete] Switching integrity passed with {passed_tests} valid steps.")


    # --------------------------------------------------------------------------
    # SUITE 2: Graph Connectivity via Union-Find (Topological & Electrical Nets)
    # --------------------------------------------------------------------------
    print("\n>>> [SECTION 2] GRAPH CONNECTIVITY (UNION-FIND ANALYSIS)")

    for t_name in ["common_emitter", "rc_transient", "ac_coupled"]:
        elements, wires = get_template_data(t_name)
        elem_map = {e.id: e for e in elements}
        wire_map = {w.id: w for w in wires}

        uf = UnionFind()

        # Connect wire endpoints to pins or tapped wires
        for w in wires:
            w_node = f"WIRE:{w.id}"
            uf.find(w_node)

            # Start endpoint
            if w.start_pin_name == "WireTap":
                parent_wire_node = f"WIRE:{w.start_element_id}"
                uf.union(w_node, parent_wire_node)
            else:
                pin_node = f"PIN:{w.start_element_id}.{w.start_pin_name}"
                uf.union(w_node, pin_node)

            # End endpoint
            if w.end_pin_name == "WireTap":
                parent_wire_node = f"WIRE:{w.end_element_id}"
                uf.union(w_node, parent_wire_node)
            else:
                pin_node = f"PIN:{w.end_element_id}.{w.end_pin_name}"
                uf.union(w_node, pin_node)

        # Rule B from NetlistExtractor.cs: pins resting on wire segments
        for elem in elements:
            for pin_id in elem.pins:
                p_pos = elem.get_pin_world_pos(pin_id)
                for w in wires:
                    if w.point_on_wire(p_pos, tolerance=5.0):
                        uf.union(f"PIN:{elem.id}.{pin_id}", f"WIRE:{w.id}")

        components = uf.get_components()

        # Helper to find which net a pin or wire belongs to
        def find_net(item: str) -> Optional[Set[str]]:
            root = uf.find(item)
            return components.get(root)

        print(f"\n  --- Analyzing Template: {t_name} ---")
        print(f"  Found {len(components)} distinct electrical nets.")

        # Test: No Orphan Wires
        for w in wires:
            total_tests += 1
            net = find_net(f"WIRE:{w.id}")
            has_pins = any(x.startswith("PIN:") for x in net)
            if not has_pins:
                failures.append(f"{t_name}: Orphan wire detected! Wire '{w.id}' has no connected component pins!")
            else:
                passed_tests += 1

        # Test: Ground Rail Net
        total_tests += 1
        gnd_rail_net = find_net("WIRE:W_GND_RAIL")
        if not gnd_rail_net:
            failures.append(f"{t_name}: Ground rail wire W_GND_RAIL missing from netlist!")
        else:
            passed_tests += 1

            if t_name == "common_emitter":
                expected_gnd_pins = {
                    "PIN:vs.neg", "PIN:Rb2.2", "PIN:Re.2", "PIN:Ce.2", "PIN:RL.2", "PIN:VCC.neg"
                }
                for ep in expected_gnd_pins:
                    total_tests += 1
                    if ep not in gnd_rail_net:
                        failures.append(f"{t_name}: Expected GND pin '{ep}' is NOT connected to Ground Rail net! (Net contains: {gnd_rail_net})")
                    else:
                        passed_tests += 1

                # Check GND reference element connection
                total_tests += 1
                if "PIN:GND.GND" not in gnd_rail_net:
                    print(f"  [CRITICAL OBSERVATION] {t_name}: Reference Ground element 'GND.GND' is NOT topologically connected to W_GND_RAIL by any wire!")
                    failures.append(f"{t_name}: Reference symbol 'GND' has pin 'GND' at (340, 480) resting on rail, but NO wire connects GND element to netlist!")
                else:
                    passed_tests += 1

            elif t_name == "rc_transient":
                expected_gnd_pins = {"PIN:VS.neg", "PIN:C1.2"}
                for ep in expected_gnd_pins:
                    total_tests += 1
                    if ep not in gnd_rail_net:
                        failures.append(f"{t_name}: Expected GND pin '{ep}' is NOT connected to Ground Rail net!")
                    else:
                        passed_tests += 1

                total_tests += 1
                if "PIN:GND.GND" not in gnd_rail_net:
                    print(f"  [CRITICAL OBSERVATION] {t_name}: Reference Ground element 'GND.GND' is NOT topologically connected to W_GND_RAIL by any wire!")
                    failures.append(f"{t_name}: Reference symbol 'GND' has pin 'GND' at (240, 360) resting on rail, but NO wire connects GND element to netlist!")
                else:
                    passed_tests += 1

            elif t_name == "ac_coupled":
                expected_gnd_pins = {"PIN:vs.neg", "PIN:Q1.E", "PIN:RL.2", "PIN:VCC.neg"}
                for ep in expected_gnd_pins:
                    total_tests += 1
                    if ep not in gnd_rail_net:
                        failures.append(f"{t_name}: Expected GND pin '{ep}' is NOT connected to Ground Rail net!")
                    else:
                        passed_tests += 1

                total_tests += 1
                if "PIN:GND.GND" not in gnd_rail_net:
                    print(f"  [CRITICAL OBSERVATION] {t_name}: Reference Ground element 'GND.GND' is NOT topologically connected to W_GND_RAIL by any wire!")
                    failures.append(f"{t_name}: Reference symbol 'GND' has pin 'GND' at (340, 480) resting on rail, but NO wire connects GND element to netlist!")
                else:
                    passed_tests += 1

        # Test: VCC Rail Net
        if t_name in ("common_emitter", "ac_coupled"):
            total_tests += 1
            vcc_rail_net = find_net("WIRE:W_VCC_RAIL")
            if not vcc_rail_net:
                failures.append(f"{t_name}: VCC rail wire W_VCC_RAIL missing from netlist!")
            else:
                passed_tests += 1
                rb_pin = "PIN:Rb1.1" if t_name == "common_emitter" else "PIN:Rb.1"
                expected_vcc_pins = {"PIN:VCC.pos", rb_pin, "PIN:Rc.1"}
                for ep in expected_vcc_pins:
                    total_tests += 1
                    if ep not in vcc_rail_net:
                        failures.append(f"{t_name}: Expected VCC pin '{ep}' is NOT connected to VCC Rail net!")
                    else:
                        passed_tests += 1

        # Test: Signal Path Nets (Common Emitter)
        if t_name == "common_emitter":
            # Input net: vs.pos <-> C1.1
            total_tests += 1
            net_in = find_net("PIN:vs.pos")
            if "PIN:C1.1" not in net_in:
                failures.append(f"{t_name}: Input signal path broken: vs.pos and C1.1 not in same net!")
            else:
                passed_tests += 1

            # Base net: C1.2 <-> Rb1.2 <-> Rb2.1 <-> Q1.B
            total_tests += 1
            net_base = find_net("PIN:Q1.B")
            base_expected = {"PIN:C1.2", "PIN:Rb1.2", "PIN:Rb2.1", "PIN:Q1.B"}
            if not base_expected.issubset(net_base):
                failures.append(f"{t_name}: Base bias net incomplete! Missing: {base_expected - net_base}")
            else:
                passed_tests += 1

            # Emitter net: Q1.E <-> Re.1 <-> Ce.1
            total_tests += 1
            net_emit = find_net("PIN:Q1.E")
            emit_expected = {"PIN:Q1.E", "PIN:Re.1", "PIN:Ce.1"}
            if not emit_expected.issubset(net_emit):
                failures.append(f"{t_name}: Emitter bypass net incomplete! Missing: {emit_expected - net_emit}")
            else:
                passed_tests += 1

            # Collector net: Rc.2 <-> Q1.C <-> C2.1
            total_tests += 1
            net_coll = find_net("PIN:Q1.C")
            coll_expected = {"PIN:Rc.2", "PIN:Q1.C", "PIN:C2.1"}
            if not coll_expected.issubset(net_coll):
                failures.append(f"{t_name}: Collector signal net incomplete! Missing: {coll_expected - net_coll}")
            else:
                passed_tests += 1

            # Output net: C2.2 <-> RL.1
            total_tests += 1
            net_out = find_net("PIN:RL.1")
            out_expected = {"PIN:C2.2", "PIN:RL.1"}
            if not out_expected.issubset(net_out):
                failures.append(f"{t_name}: Output signal net incomplete! Missing: {out_expected - net_out}")
            else:
                passed_tests += 1

            # Short Circuit Tests: VCC != GND, Input != Output, Base != Collector
            total_tests += 1
            if uf.find("WIRE:W_VCC_RAIL") == uf.find("WIRE:W_GND_RAIL"):
                failures.append(f"{t_name}: CRITICAL SHORT CIRCUIT: VCC Rail is shorted to GND Rail!")
            else:
                passed_tests += 1

            total_tests += 1
            if uf.find("PIN:vs.pos") == uf.find("PIN:RL.1"):
                failures.append(f"{t_name}: CRITICAL SHORT CIRCUIT: Input vs.pos is shorted to Output RL.1!")
            else:
                passed_tests += 1

    # --------------------------------------------------------------------------
    # SUITE 3: Bus/Trunk Routing & Endpoint Double-Termination Flaws
    # --------------------------------------------------------------------------
    print("\n>>> [SECTION 3] BUS/TRUNK ROUTING & PIN DOUBLE-TERMINATION FLAWS")

    for t_name in ["common_emitter", "rc_transient", "ac_coupled"]:
        elements, wires = get_template_data(t_name)
        elem_map = {e.id: e for e in elements}

        # Check: Every wire claiming to connect to a pin must match that pin's exact world position
        for w in wires:
            # Check Start
            if w.start_pin_name != "WireTap":
                total_tests += 1
                elem = elem_map.get(w.start_element_id)
                if not elem:
                    failures.append(f"{t_name} / {w.id}: StartElementId '{w.start_element_id}' does not exist!")
                else:
                    try:
                        p_pos = elem.get_pin_world_pos(w.start_pin_name)
                        w_start = Vector2(w.start_x, w.start_y)
                        if w_start.distance_to(p_pos) > 1.0:
                            failures.append(f"{t_name} / {w.id}: Start coordinate {w_start} does NOT match pin '{w.start_pin_name}' pos {p_pos} on '{elem.id}' (err={w_start.distance_to(p_pos):.1f}px)!")
                        else:
                            passed_tests += 1
                    except KeyError as e:
                        failures.append(f"{t_name} / {w.id}: {e}")

            # Check End
            if w.end_pin_name != "WireTap":
                total_tests += 1
                elem = elem_map.get(w.end_element_id)
                if not elem:
                    failures.append(f"{t_name} / {w.id}: EndElementId '{w.end_element_id}' does not exist!")
                else:
                    try:
                        p_pos = elem.get_pin_world_pos(w.end_pin_name)
                        w_end = Vector2(w.end_x, w.end_y)
                        if w_end.distance_to(p_pos) > 1.0:
                            failures.append(f"{t_name} / {w.id}: End coordinate {w_end} does NOT match pin '{w.end_pin_name}' pos {p_pos} on '{elem.id}' (err={w_end.distance_to(p_pos):.1f}px)!")
                        else:
                            passed_tests += 1
                    except KeyError as e:
                        failures.append(f"{t_name} / {w.id}: {e}")

        # Check: Double-terminated pins (violates Bus/Trunk architecture)
        pin_leads = defaultdict(list)
        for w in wires:
            if w.start_pin_name != "WireTap":
                pin_leads[(w.start_element_id, w.start_pin_name)].append(w.id)
            if w.end_pin_name != "WireTap":
                pin_leads[(w.end_element_id, w.end_pin_name)].append(w.id)

        for (elem_id, pin_id), attached_wires in pin_leads.items():
            total_tests += 1
            if len(attached_wires) > 1:
                failures.append(f"{t_name}: Pin '{elem_id}.{pin_id}' has MULTIPLE direct wire connections {attached_wires}! (Violates single trunk EDA lead standard)")
            else:
                passed_tests += 1

    # --------------------------------------------------------------------------
    # SUITE 4: Rubber-Banding Rail Distortion Vulnerability
    # --------------------------------------------------------------------------
    print("\n>>> [SECTION 4] RUBBER-BANDING RAIL DISTORTION STRESS-TEST")

    # In AnalysisStudioViewModel.cs lines 858-883:
    # SyncConnectedWiresToElement updates wire start/end coords whenever an element moves.
    # If a bus rail wire (like W_VCC_RAIL or W_GND_RAIL) sets StartElementId or EndElementId
    # to a component, moving that component will distort the rail!
    for t_name in ["common_emitter", "ac_coupled"]:
        elements, wires = get_template_data(t_name)
        elem_map = {e.id: e for e in elements}
        wire_map = {w.id: w for w in wires}

        vcc_rail = wire_map["W_VCC_RAIL"]
        gnd_rail = wire_map["W_GND_RAIL"]

        total_tests += 1
        # Check: Does W_VCC_RAIL reference Rb1 as StartElementId?
        if vcc_rail.start_element_id in elem_map:
            failures.append(f"{t_name}: W_VCC_RAIL erroneously binds StartElementId to component '{vcc_rail.start_element_id}' instead of trunk bus! Moving '{vcc_rail.start_element_id}' will pull W_VCC_RAIL off Y=80!")
        else:
            passed_tests += 1

        total_tests += 1
        # Check: Does W_GND_RAIL reference vs or VCC as Start/End?
        if gnd_rail.start_element_id in elem_map or gnd_rail.end_element_id in elem_map:
            failures.append(f"{t_name}: W_GND_RAIL erroneously binds to components ('{gnd_rail.start_element_id}', '{gnd_rail.end_element_id}')! Moving them will distort the horizontal GND rail!")
        else:
            passed_tests += 1


    # --------------------------------------------------------------------------
    # SUMMARY REPORT
    # --------------------------------------------------------------------------
    print("\n" + "=" * 80)
    print("CHALLENGER M1-2 EMPIRICAL VERDICT SUMMARY")
    print(f"Total Invariants Tested: {total_tests}")
    print(f"Passed Invariants:       {passed_tests}")
    print(f"Failed Invariants:       {len(failures)}")
    print("=" * 80)

    if failures:
        print("\n[!] DETECTED DEFECTS AND VULNERABILITIES:")
        for idx, f in enumerate(failures, 1):
            print(f"  {idx}. {f}")
        print("\nOVERALL EMPIRICAL VERDICT: REJECT")
        return 1
    else:
        print("\nALL EMPIRICAL TESTS PASSED.")
        print("\nOVERALL EMPIRICAL VERDICT: APPROVE")
        return 0

if __name__ == "__main__":
    sys.exit(run_adversarial_suite())
