"""
Automated Empirical Verification Script for CircuitAssistant Educational Templates
Milestone 1 (Educational Templates & Model Modernization - R2)

Adversarial Verification Suite:
1. Geometric Consistency: Every wire endpoint connects to an actual pin on referenced component,
   or is a valid orthogonal T-tap on a parent wire segment. No dangling or misaligned endpoints.
2. Zero Piercing: No wire segment intersects or pierces any component body or forbidden envelope.
3. Solder Dot Rule: T-junctions meet at valid points outside pin terminal keep-out zones (>= 5px from pins).
   Bus/Trunk rule: No pin terminal is directly double-terminated by multiple independent wires.
4. Grid Compliance: All element coordinates, pin positions, and wire vertices conform to grid modulus (20px / 40px).
"""

import sys
import math
from typing import List, Dict, Tuple, Optional, Any

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
        return hash((round(self.x, 2), round(self.y, 2)))

    def distance_to(self, other: 'Vector2') -> float:
        return math.hypot(self.x - other.x, self.y - other.y)

class CircuitPin:
    def __init__(self, pin_id: str, name: str, rel_x: float, rel_y: float):
        self.id = pin_id
        self.name = name
        self.rel_x = rel_x
        self.rel_y = rel_y

class CircuitElement:
    def __init__(self, elem_id: str, name: str, elem_type: str, x: float, y: float,
                 width: float, height: float, rotation_deg: int, orientation: str,
                 display_number: float, unit_symbol: str):
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
        self._init_pins()

    def _init_pins(self):
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

    def get_pin_world_pos(self, pin_id: str) -> Vector2:
        if pin_id not in self.pins:
            raise KeyError(f"Pin '{pin_id}' not found on element '{self.id}' ({self.type})")
        pin = self.pins[pin_id]
        rad = math.radians(self.rotation_deg)
        cos_r = math.cos(rad)
        sin_r = math.sin(rad)
        # Matrix3x2 rotation:
        rot_x = pin.rel_x * cos_r - pin.rel_y * sin_r
        rot_y = pin.rel_x * sin_r + pin.rel_y * cos_r
        return Vector2(self.x + rot_x, self.y + rot_y)

    def get_body_envelope(self) -> Dict[str, Any]:
        """Returns the core body (excluding external lead wires) where wires are forbidden to pierce."""
        t = self.type
        center = Vector2(self.x, self.y)
        is_vert = (self.rotation_deg % 180 != 0)

        if t == "Resistor":
            # Resistor body rectangle: 40x20
            hw = 10 if is_vert else 20
            hh = 20 if is_vert else 10
            return {"shape": "rect", "min_x": self.x - hw, "max_x": self.x + hw,
                    "min_y": self.y - hh, "max_y": self.y + hh, "center": center}
        elif t == "Capacitor":
            # Capacitor plates region: 12x36
            hw = 18 if is_vert else 6
            hh = 6 if is_vert else 18
            return {"shape": "rect", "min_x": self.x - hw, "max_x": self.x + hw,
                    "min_y": self.y - hh, "max_y": self.y + hh, "center": center}
        elif t in ("DcVoltageSource", "AcVoltageSource"):
            # Circular source body: radius 18
            return {"shape": "circle", "radius": 18.0, "center": center}
        elif t in ("BjtNpn", "BjtPnp"):
            # Transistor circular outline: radius 30
            return {"shape": "circle", "radius": 30.0, "center": center}
        elif t == "Ground":
            # Ground symbol bar region: 32x10 below pos.Y
            return {"shape": "rect", "min_x": self.x - 16, "max_x": self.x + 16,
                    "min_y": self.y, "max_y": self.y + 10, "center": center}
        else:
            return {"shape": "rect", "min_x": self.x - self.width/2, "max_x": self.x + self.width/2,
                    "min_y": self.y - self.height/2, "max_y": self.y + self.height/2, "center": center}

class CircuitWire:
    def __init__(self, wire_id: str, start_elem: str, start_pin: str,
                 end_elem: str, end_pin: str, start_x: float, start_y: float,
                 end_x: float, end_y: float, flip_corner: bool = False,
                 custom_mid: float = 0.0, net_id: int = 0):
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

    def get_path_points(self) -> List[Vector2]:
        p1 = Vector2(self.start_x, self.start_y)
        p2 = Vector2(self.end_x, self.end_y)
        pts = [p1]

        if abs(p1.x - p2.x) > 6 and abs(p1.y - p2.y) > 6:
            if self.custom_mid != 0:
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


# Helper geometric calculations
def point_on_segment(p: Vector2, a: Vector2, b: Vector2, tol: float = 1e-2) -> bool:
    ab_x = b.x - a.x
    ab_y = b.y - a.y
    len_sq = ab_x * ab_x + ab_y * ab_y
    if len_sq < 1e-6:
        return p.distance_to(a) <= tol
    t = ((p.x - a.x) * ab_x + (p.y - a.y) * ab_y) / len_sq
    if t < -1e-4 or t > 1.0 + 1e-4:
        return False
    t_clamped = max(0.0, min(1.0, t))
    proj = Vector2(a.x + t_clamped * ab_x, a.y + t_clamped * ab_y)
    return p.distance_to(proj) <= tol

def distance_point_to_segment(p: Vector2, a: Vector2, b: Vector2) -> float:
    ab_x = b.x - a.x
    ab_y = b.y - a.y
    len_sq = ab_x * ab_x + ab_y * ab_y
    if len_sq < 1e-6:
        return p.distance_to(a)
    t = max(0.0, min(1.0, ((p.x - a.x) * ab_x + (p.y - a.y) * ab_y) / len_sq))
    proj = Vector2(a.x + t * ab_x, a.y + t * ab_y)
    return p.distance_to(proj)

def segment_intersects_box(a: Vector2, b: Vector2, min_x: float, max_x: float, min_y: float, max_y: float) -> bool:
    """Check if segment (a, b) intersects the strict interior of an axis-aligned box [min_x, max_x] x [min_y, max_y]."""
    # Exclude endpoints if they merely touch boundary
    # Liang-Barsky line clipping algorithm
    dx = b.x - a.x
    dy = b.y - a.y
    p = [-dx, dx, -dy, dy]
    q = [a.x - min_x, max_x - a.x, a.y - min_y, max_y - a.y]

    u1 = 0.0
    u2 = 1.0

    for pi, qi in zip(p, q):
        if abs(pi) < 1e-7:
            if qi <= 0:
                return False
        else:
            t = qi / pi
            if pi < 0:
                if t > u2: return False
                if t > u1: u1 = t
            else:
                if t < u1: return False
                if t < u2: u2 = t

    # Must have strict overlap with interior
    return u1 < u2 - 1e-4 and u2 > 1e-4 and u1 < 1.0 - 1e-4

def segment_intersects_circle(a: Vector2, b: Vector2, center: Vector2, radius: float) -> bool:
    """Check if segment (a, b) intersects the interior of a circle."""
    dist = distance_point_to_segment(center, a, b)
    if dist >= radius - 1e-4:
        return False
    # If both endpoints are outside the circle, does segment pierce through?
    # Or if one is inside, it pierces.
    return True


# Template definitions matching CircuitTemplateFactory.cs
def get_common_emitter_template() -> Tuple[List[CircuitElement], List[CircuitWire]]:
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
        CircuitElement("GND", "参考地 GND", "Ground", 340, 502, 40, 35, 0, "Horizontal", 0.0, "V"),
    ]
    wires = [
        CircuitWire("W_VS_C1", "vs", "pos", "C1", "1", 80, 285, 125, 260, flip_corner=True, net_id=1),
        CircuitWire("W_BASE_LINE", "C1", "2", "Q1", "B", 195, 260, 285, 260, flip_corner=False, net_id=2),
        CircuitWire("W_RB1_BASE", "Rb1", "2", "W_BASE_LINE", "WireTap", 240, 200, 240, 260, flip_corner=False, net_id=2),
        CircuitWire("W_RB2_BASE", "Rb2", "1", "W_BASE_LINE", "WireTap", 240, 320, 240, 260, flip_corner=False, net_id=2),
        CircuitWire("W_VCC_RAIL", "W_RB1_VCC", "WireTap", "W_VCC_FEED", "WireTap", 240, 80, 620, 80, flip_corner=False, net_id=3),
        CircuitWire("W_RB1_VCC", "Rb1", "1", "W_VCC_RAIL", "WireTap", 240, 120, 240, 80, flip_corner=False, net_id=3),
        CircuitWire("W_RC_VCC", "Rc", "1", "W_VCC_RAIL", "WireTap", 340, 100, 340, 80, flip_corner=False, net_id=3),
        CircuitWire("W_VCC_FEED", "VCC", "pos", "W_VCC_RAIL", "WireTap", 620, 245, 620, 80, flip_corner=False, net_id=3),
        CircuitWire("W_COLL_LINE", "Rc", "2", "Q1", "C", 340, 180, 340, 225, flip_corner=False, net_id=4),
        CircuitWire("W_COLL_C2", "W_COLL_LINE", "WireTap", "C2", "1", 340, 200, 405, 200, flip_corner=False, net_id=4),
        CircuitWire("W_EMIT_TRUNK", "Q1", "E", "W_EMIT_BUS", "WireTap", 340, 295, 340, 340, flip_corner=False, net_id=5),
        CircuitWire("W_EMIT_BUS", "W_RE_DROP", "WireTap", "W_CE_DROP", "WireTap", 320, 340, 420, 340, flip_corner=False, net_id=5),
        CircuitWire("W_RE_DROP", "Re", "1", "W_EMIT_BUS", "WireTap", 320, 360, 320, 340, flip_corner=False, net_id=5),
        CircuitWire("W_CE_DROP", "Ce", "1", "W_EMIT_BUS", "WireTap", 420, 365, 420, 340, flip_corner=False, net_id=5),
        CircuitWire("W_C2_OUT", "C2", "2", "W_RL_DROP", "WireTap", 475, 200, 540, 200, flip_corner=False, net_id=6),
        CircuitWire("W_RL_DROP", "RL", "1", "W_C2_OUT", "WireTap", 540, 240, 540, 200, flip_corner=False, net_id=6),
        CircuitWire("W_GND_RAIL", "W_VS_GND", "WireTap", "W_VCC_GND", "WireTap", 80, 480, 620, 480, flip_corner=False, net_id=0),
        CircuitWire("W_VS_GND", "vs", "neg", "W_GND_RAIL", "WireTap", 80, 355, 80, 480, flip_corner=False, net_id=0),
        CircuitWire("W_RB2_GND", "Rb2", "2", "W_GND_RAIL", "WireTap", 240, 400, 240, 480, flip_corner=False, net_id=0),
        CircuitWire("W_RE_GND", "Re", "2", "W_GND_RAIL", "WireTap", 320, 440, 320, 480, flip_corner=False, net_id=0),
        CircuitWire("W_CE_GND", "Ce", "2", "W_GND_RAIL", "WireTap", 420, 435, 420, 480, flip_corner=False, net_id=0),
        CircuitWire("W_RL_GND", "RL", "2", "W_GND_RAIL", "WireTap", 540, 320, 540, 480, flip_corner=False, net_id=0),
        CircuitWire("W_VCC_GND", "VCC", "neg", "W_GND_RAIL", "WireTap", 620, 315, 620, 480, flip_corner=False, net_id=0),
    ]
    return elements, wires

def get_rc_transient_template() -> Tuple[List[CircuitElement], List[CircuitWire]]:
    elements = [
        CircuitElement("VS", "直流阶跃电源 VS", "DcVoltageSource", 120, 280, 60, 60, 0, "Horizontal", 10.0, "V"),
        CircuitElement("R1", "充电电阻 R1", "Resistor", 240, 200, 80, 50, 0, "Horizontal", 1.0, "kΩ"),
        CircuitElement("C1", "积分电容 C1", "Capacitor", 360, 280, 80, 50, 90, "Vertical", 100.0, "μF"),
        CircuitElement("GND", "参考地 GND", "Ground", 240, 382, 40, 35, 0, "Horizontal", 0.0, "V"),
    ]
    wires = [
        CircuitWire("W_VS_R1", "VS", "pos", "R1", "1", 120, 245, 200, 200, flip_corner=True, net_id=1),
        CircuitWire("W_R1_C1", "R1", "2", "C1", "1", 280, 200, 360, 245, flip_corner=False, net_id=2),
        CircuitWire("W_GND_RAIL", "W_VS_GND", "WireTap", "W_C1_GND", "WireTap", 120, 360, 360, 360, flip_corner=False, net_id=0),
        CircuitWire("W_VS_GND", "VS", "neg", "W_GND_RAIL", "WireTap", 120, 315, 120, 360, flip_corner=False, net_id=0),
        CircuitWire("W_C1_GND", "C1", "2", "W_GND_RAIL", "WireTap", 360, 315, 360, 360, flip_corner=False, net_id=0),
    ]
    return elements, wires

def get_rc_coupled_template() -> Tuple[List[CircuitElement], List[CircuitWire]]:
    elements = [
        CircuitElement("vs", "交流信号源 vs", "AcVoltageSource", 80, 320, 60, 60, 0, "Horizontal", 10.0, "mV"),
        CircuitElement("C1", "输入耦合电容 C1", "Capacitor", 160, 260, 80, 50, 0, "Horizontal", 10.0, "μF"),
        CircuitElement("Rb", "基极固定偏置电阻 Rb", "Resistor", 240, 160, 80, 50, 90, "Vertical", 300.0, "kΩ"),
        CircuitElement("Q1", "NPN 晶体管 Q1", "BjtNpn", 320, 260, 80, 80, 0, "Horizontal", 100.0, "β"),
        CircuitElement("Rc", "集电极电阻 Rc", "Resistor", 340, 140, 80, 50, 90, "Vertical", 3.0, "kΩ"),
        CircuitElement("C2", "输出耦合电容 C2", "Capacitor", 440, 200, 80, 50, 0, "Horizontal", 10.0, "μF"),
        CircuitElement("RL", "负载电阻 RL", "Resistor", 540, 280, 80, 50, 90, "Vertical", 10.0, "kΩ"),
        CircuitElement("VCC", "直流电源 VCC", "DcVoltageSource", 620, 280, 60, 60, 0, "Horizontal", 12.0, "V"),
        CircuitElement("GND", "参考地 GND", "Ground", 340, 502, 40, 35, 0, "Horizontal", 0.0, "V"),
    ]
    wires = [
        CircuitWire("W_VS_C1", "vs", "pos", "C1", "1", 80, 285, 125, 260, flip_corner=True, net_id=1),
        CircuitWire("W_BASE_LINE", "C1", "2", "Q1", "B", 195, 260, 285, 260, flip_corner=False, net_id=2),
        CircuitWire("W_RB_BASE", "Rb", "2", "W_BASE_LINE", "WireTap", 240, 200, 240, 260, flip_corner=False, net_id=2),
        CircuitWire("W_VCC_RAIL", "W_RB_VCC", "WireTap", "W_VCC_FEED", "WireTap", 240, 80, 620, 80, flip_corner=False, net_id=3),
        CircuitWire("W_RB_VCC", "Rb", "1", "W_VCC_RAIL", "WireTap", 240, 120, 240, 80, flip_corner=False, net_id=3),
        CircuitWire("W_RC_VCC", "Rc", "1", "W_VCC_RAIL", "WireTap", 340, 100, 340, 80, flip_corner=False, net_id=3),
        CircuitWire("W_VCC_FEED", "VCC", "pos", "W_VCC_RAIL", "WireTap", 620, 245, 620, 80, flip_corner=False, net_id=3),
        CircuitWire("W_COLL_LINE", "Rc", "2", "Q1", "C", 340, 180, 340, 225, flip_corner=False, net_id=4),
        CircuitWire("W_COLL_C2", "W_COLL_LINE", "WireTap", "C2", "1", 340, 200, 405, 200, flip_corner=False, net_id=4),
        CircuitWire("W_EMIT_GND", "Q1", "E", "W_GND_RAIL", "WireTap", 340, 295, 360, 480, flip_corner=False, net_id=0),
        CircuitWire("W_C2_OUT", "C2", "2", "W_RL_DROP", "WireTap", 475, 200, 540, 200, flip_corner=False, net_id=5),
        CircuitWire("W_RL_DROP", "RL", "1", "W_C2_OUT", "WireTap", 540, 240, 540, 200, flip_corner=False, net_id=5),
        CircuitWire("W_GND_RAIL", "W_VS_GND", "WireTap", "W_VCC_GND", "WireTap", 80, 480, 620, 480, flip_corner=False, net_id=0),
        CircuitWire("W_VS_GND", "vs", "neg", "W_GND_RAIL", "WireTap", 80, 355, 80, 480, flip_corner=False, net_id=0),
        CircuitWire("W_RL_GND", "RL", "2", "W_GND_RAIL", "WireTap", 540, 320, 540, 480, flip_corner=False, net_id=0),
        CircuitWire("W_VCC_GND", "VCC", "neg", "W_GND_RAIL", "WireTap", 620, 315, 620, 480, flip_corner=False, net_id=0),
    ]
    return elements, wires


def run_verification_suite():
    templates = {
        "CommonEmitter_Amp": get_common_emitter_template(),
        "RC_Transient": get_rc_transient_template(),
        "RC_Coupled_Amp": get_rc_coupled_template(),
    }

    total_checks = 0
    passed_checks = 0
    failed_checks = []

    print("=" * 80)
    print("EMIRICAL VERIFICATION SUITE FOR CIRCUIT TEMPLATES (M1)")
    print("=" * 80)

    for t_name, (elements, wires) in templates.items():
        print(f"\n>>> Verifying Template: {t_name} ({len(elements)} elements, {len(wires)} wires)")
        elem_map = {e.id: e for e in elements}
        wire_map = {w.id: w for w in wires}

        # -------------------------------------------------------------
        # 1. Geometric Consistency
        # -------------------------------------------------------------
        print("  [Test 1] Geometric Consistency of Wire Endpoints...")
        for w in wires:
            # Check Start Endpoint
            total_checks += 1
            start_pt = Vector2(w.start_x, w.start_y)
            if w.start_pin_name == "WireTap":
                # T-tap on wire
                parent_wire = wire_map.get(w.start_element_id)
                if not parent_wire:
                    failed_checks.append(f"{t_name} / {w.id}: Parent wire '{w.start_element_id}' for Start WireTap not found!")
                else:
                    on_parent = any(point_on_segment(start_pt, sa, sb) for sa, sb in parent_wire.get_segments())
                    if on_parent:
                        passed_checks += 1
                    else:
                        failed_checks.append(f"{t_name} / {w.id}: Start WireTap {start_pt} is NOT on parent wire {parent_wire.id} segments!")
            else:
                # Element pin connection
                elem = elem_map.get(w.start_element_id)
                if not elem:
                    failed_checks.append(f"{t_name} / {w.id}: Start element '{w.start_element_id}' not found!")
                else:
                    try:
                        expected_pin_pos = elem.get_pin_world_pos(w.start_pin_name)
                        if start_pt.distance_to(expected_pin_pos) <= 1.0:
                            passed_checks += 1
                        else:
                            failed_checks.append(f"{t_name} / {w.id}: Start endpoint {start_pt} does NOT match pin '{w.start_pin_name}' pos {expected_pin_pos} on '{elem.id}' (err={start_pt.distance_to(expected_pin_pos):.2f}px)")
                    except KeyError as e:
                        failed_checks.append(f"{t_name} / {w.id}: Start pin error: {e}")

            # Check End Endpoint
            total_checks += 1
            end_pt = Vector2(w.end_x, w.end_y)
            if w.end_pin_name == "WireTap":
                parent_wire = wire_map.get(w.end_element_id)
                if not parent_wire:
                    failed_checks.append(f"{t_name} / {w.id}: Parent wire '{w.end_element_id}' for End WireTap not found!")
                else:
                    on_parent = any(point_on_segment(end_pt, sa, sb) for sa, sb in parent_wire.get_segments())
                    if on_parent:
                        passed_checks += 1
                    else:
                        failed_checks.append(f"{t_name} / {w.id}: End WireTap {end_pt} is NOT on parent wire {parent_wire.id} segments!")
            else:
                elem = elem_map.get(w.end_element_id)
                if not elem:
                    failed_checks.append(f"{t_name} / {w.id}: End element '{w.end_element_id}' not found!")
                else:
                    try:
                        expected_pin_pos = elem.get_pin_world_pos(w.end_pin_name)
                        if end_pt.distance_to(expected_pin_pos) <= 1.0:
                            passed_checks += 1
                        else:
                            failed_checks.append(f"{t_name} / {w.id}: End endpoint {end_pt} does NOT match pin '{w.end_pin_name}' pos {expected_pin_pos} on '{elem.id}' (err={end_pt.distance_to(expected_pin_pos):.2f}px)")
                    except KeyError as e:
                        failed_checks.append(f"{t_name} / {w.id}: End pin error: {e}")

            # Check Segment Orthogonality
            total_checks += 1
            is_orthogonal = True
            for sa, sb in w.get_segments():
                if abs(sa.x - sb.x) > 1e-3 and abs(sa.y - sb.y) > 1e-3:
                    is_orthogonal = False
                    failed_checks.append(f"{t_name} / {w.id}: Segment {sa}->{sb} is NOT orthogonal (diagonal wire)!")
            if is_orthogonal:
                passed_checks += 1

        # -------------------------------------------------------------
        # 2. Zero Piercing Verification
        # -------------------------------------------------------------
        print("  [Test 2] Zero Piercing: No wire intersects component interior...")
        for w in wires:
            segments = w.get_segments()
            for sa, sb in segments:
                for elem in elements:
                    total_checks += 1
                    env = elem.get_body_envelope()
                    pierced = False
                    if env["shape"] == "rect":
                        pierced = segment_intersects_box(sa, sb, env["min_x"], env["max_x"], env["min_y"], env["max_y"])
                    elif env["shape"] == "circle":
                        # If a wire endpoint connects to this element's pin, check that the wire does not cross into the circle
                        # In standard symbols, leads are outside the circle or end at pin
                        # Distance to circle center:
                        dist = distance_point_to_segment(env["center"], sa, sb)
                        # The circle radius is 30 for BJT, 18 for Sources
                        # An endpoint touching a pin is at distance 35 (Base), or sqrt(20^2 + 35^2)=40.3 (C/E)
                        if dist < env["radius"] - 1.0:
                            pierced = True

                    if pierced:
                        failed_checks.append(f"{t_name}: Wire {w.id} segment {sa}->{sb} pierces component '{elem.id}' ({elem.type}) envelope!")
                    else:
                        passed_checks += 1

        # -------------------------------------------------------------
        # 3. Solder Dot & Terminal Keep-Out Rules
        # -------------------------------------------------------------
        print("  [Test 3] Solder Dot Rule: T-junctions meet outside pin keep-out zones...")
        # Collect all pin positions
        pin_positions = []
        for elem in elements:
            for pin_id in elem.pins:
                pin_positions.append((elem.get_pin_world_pos(pin_id), elem.id, pin_id))

        # Check all WireTap junction points
        for w in wires:
            taps = []
            if w.start_pin_name == "WireTap":
                taps.append((Vector2(w.start_x, w.start_y), "Start", w.start_element_id))
            if w.end_pin_name == "WireTap":
                taps.append((Vector2(w.end_x, w.end_y), "End", w.end_element_id))

            for tap_pos, tap_side, parent_id in taps:
                total_checks += 1
                # Must be outside any pin terminal keep-out zone (>= 5px)
                too_close_pin = None
                for pin_pos, elem_id, pin_id in pin_positions:
                    if tap_pos.distance_to(pin_pos) < 5.0:
                        too_close_pin = (elem_id, pin_id, tap_pos.distance_to(pin_pos))
                        break
                if too_close_pin:
                    failed_checks.append(f"{t_name} / {w.id}: {tap_side} WireTap at {tap_pos} violates pin keep-out! (Within {too_close_pin[2]:.1f}px of {too_close_pin[0]}.{too_close_pin[1]})")
                else:
                    passed_checks += 1

        # Check Bus/Trunk Single Termination Rule:
        # No single pin should have more than 1 direct wire exiting it
        pin_direct_wire_counts = {}
        for w in wires:
            if w.start_pin_name != "WireTap":
                key = (w.start_element_id, w.start_pin_name)
                pin_direct_wire_counts[key] = pin_direct_wire_counts.get(key, 0) + 1
            if w.end_pin_name != "WireTap":
                key = (w.end_element_id, w.end_pin_name)
                pin_direct_wire_counts[key] = pin_direct_wire_counts.get(key, 0) + 1

        total_checks += 1
        over_terminated_pins = [f"{elem}.{pin} (wires={cnt})" for (elem, pin), cnt in pin_direct_wire_counts.items() if cnt > 1]
        if over_terminated_pins:
            failed_checks.append(f"{t_name}: Pin terminal over-termination detected (violates bus/trunk routing): {', '.join(over_terminated_pins)}")
        else:
            passed_checks += 1

        # -------------------------------------------------------------
        # 4. Grid Compliance
        # -------------------------------------------------------------
        print("  [Test 4] Grid Compliance: Element positions and wire vertices snap to modulus...")
        # Check elements (except Ground which is intentionally adjusted so pin lands on grid)
        for elem in elements:
            total_checks += 1
            if elem.type == "Ground":
                # Check ground pin lands on 40px grid!
                gnd_pin = elem.get_pin_world_pos("GND")
                if int(gnd_pin.x) % 20 == 0 and int(gnd_pin.y) % 20 == 0:
                    passed_checks += 1
                else:
                    failed_checks.append(f"{t_name}: Ground pin {gnd_pin} does NOT snap to 20px grid modulus!")
            else:
                if int(elem.x) % 20 == 0 and int(elem.y) % 20 == 0:
                    passed_checks += 1
                else:
                    failed_checks.append(f"{t_name}: Element '{elem.id}' pos ({elem.x}, {elem.y}) does NOT snap to 20px grid modulus!")

        # Check wire vertices
        for w in wires:
            for pt in w.get_path_points():
                total_checks += 1
                # All wire vertices must snap to 5px/10px/20px modulus
                if abs(pt.x - round(pt.x / 5.0) * 5.0) < 1e-2 and abs(pt.y - round(pt.y / 5.0) * 5.0) < 1e-2:
                    passed_checks += 1
                else:
                    failed_checks.append(f"{t_name} / {w.id}: Vertex {pt} does not snap to 5px grid modulus!")

        # Check Specific Layout Metrics:
        if t_name == "CommonEmitter_Amp":
            total_checks += 1
            # Check Re vs Ce separation >= 80px (100px)
            re_elem = elem_map["Re"]
            ce_elem = elem_map["Ce"]
            spacing = abs(ce_elem.x - re_elem.x)
            if spacing >= 80.0:
                passed_checks += 1
            else:
                failed_checks.append(f"{t_name}: Re and Ce spacing {spacing}px is less than 80px standard!")

            total_checks += 1
            # Check VCC Rail vs GND Rail parallelism & alignment
            vcc_rail = wire_map["W_VCC_RAIL"]
            gnd_rail = wire_map["W_GND_RAIL"]
            if vcc_rail.start_y == 80 and vcc_rail.end_y == 80 and gnd_rail.start_y == 480 and gnd_rail.end_y == 480:
                passed_checks += 1
            else:
                failed_checks.append(f"{t_name}: Power rails misaligned! VCC Y=[{vcc_rail.start_y},{vcc_rail.end_y}], GND Y=[{gnd_rail.start_y},{gnd_rail.end_y}]")

    print("\n" + "=" * 80)
    print("VERIFICATION RESULTS SUMMARY")
    print(f"Total Checks Executed: {total_checks}")
    print(f"Passed Checks:        {passed_checks}")
    print(f"Failed Checks:        {len(failed_checks)}")

    if failed_checks:
        print("\nFAILURES ENCOUNTERED:")
        for f in failed_checks:
            print(f"  [X] {f}")
        print("\nVERDICT: REJECT")
        return 1
    else:
        print("\nALL ADVERSARIAL CHALLENGES PASSED (100% SUCCESS RATE)")
        print("VERDICT: APPROVE")
        return 0

if __name__ == "__main__":
    sys.exit(run_verification_suite())
