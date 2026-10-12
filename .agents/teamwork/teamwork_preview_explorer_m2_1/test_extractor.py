import math
from typing import Dict, List, Tuple, Set, Optional

class Vector2:
    def __init__(self, x: float, y: float):
        self.x = round(float(x), 4)
        self.y = round(float(y), 4)

    def __repr__(self):
        return f"({self.x:g}, {self.y:g})"

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
                 rotation_deg: int, display_number: float, unit_symbol: str):
        self.id = elem_id
        self.name = name
        self.type = elem_type
        self.x = float(x)
        self.y = float(y)
        self.rotation_deg = rotation_deg
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

    def get_pin_world_pos(self, pin_id: str) -> Vector2:
        pin = self.pins[pin_id]
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
        self.custom_mid = custom_mid
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

def distance_point_to_segment(p: Vector2, a: Vector2, b: Vector2) -> float:
    ab_x = b.x - a.x
    ab_y = b.y - a.y
    len_sq = ab_x * ab_x + ab_y * ab_y
    if len_sq < 1e-6:
        return p.distance_to(a)
    ap_x = p.x - a.x
    ap_y = p.y - a.y
    t = (ap_x * ab_x + ap_y * ab_y) / len_sq
    t = max(0.0, min(1.0, t))
    proj = Vector2(a.x + t * ab_x, a.y + t * ab_y)
    return p.distance_to(proj)

class DisjointSet:
    def __init__(self):
        self.parent: Dict[str, str] = {}
        self.rank: Dict[str, int] = {}

    def find(self, x: str) -> str:
        if x not in self.parent:
            self.parent[x] = x
            self.rank[x] = 0
            return x
        if self.parent[x] != x:
            self.parent[x] = self.find(self.parent[x])
        return self.parent[x]

    def union(self, x: str, y: str):
        root_x = self.find(x)
        root_y = self.find(y)
        if root_x != root_y:
            if self.rank[root_x] < self.rank[root_y]:
                self.parent[root_x] = root_y
            elif self.rank[root_x] > self.rank[root_y]:
                self.parent[root_y] = root_x
            else:
                self.parent[root_y] = root_x
                self.rank[root_x] += 1

    def get_components(self) -> Dict[str, List[str]]:
        groups = {}
        for k in list(self.parent.keys()):
            r = self.find(k)
            groups.setdefault(r, []).append(k)
        return groups

def extract_netlist(elements: List[CircuitElement], wires: List[CircuitWire]):
    SNAP_TOLERANCE = 8.0
    SEGMENT_TAP_TOLERANCE = 5.0

    elem_map = {e.id: e for e in elements}
    wire_map = {w.id: w for w in wires}

    ds = DisjointSet()

    # Pre-register all pins and wires
    all_pins: List[Tuple[str, str, Vector2]] = []
    for el in elements:
        for pin_id, pin in el.pins.items():
            pos = el.get_pin_world_pos(pin_id)
            pin_key = f"PIN:{el.id}.{pin_id}"
            ds.find(pin_key)
            all_pins.append((el.id, pin_id, pos))

    for w in wires:
        w_key = f"WIRE:{w.id}"
        ds.find(w_key)

    # 1. Wire-to-Wire Connections:
    for w in wires:
        w_key = f"WIRE:{w.id}"
        # A) Explicit WireTap
        if w.start_pin_name == "WireTap" and w.start_element_id in wire_map:
            ds.union(w_key, f"WIRE:{w.start_element_id}")
        if w.end_pin_name == "WireTap" and w.end_element_id in wire_map:
            ds.union(w_key, f"WIRE:{w.end_element_id}")

        # B) Geometric T-tap onto other wires
        w_start = Vector2(w.start_x, w.start_y)
        w_end = Vector2(w.end_x, w.end_y)

        for other_w in wires:
            if other_w.id == w.id:
                continue
            other_key = f"WIRE:{other_w.id}"
            for seg in other_w.get_segments():
                if distance_point_to_segment(w_start, seg[0], seg[1]) <= SEGMENT_TAP_TOLERANCE:
                    ds.union(w_key, other_key)
                if distance_point_to_segment(w_end, seg[0], seg[1]) <= SEGMENT_TAP_TOLERANCE:
                    ds.union(w_key, other_key)

    # 2. Pin-to-Wire Connections:
    for el_id, pin_id, p_pos in all_pins:
        pin_key = f"PIN:{el_id}.{pin_id}"
        for w in wires:
            w_key = f"WIRE:{w.id}"
            w_start = Vector2(w.start_x, w.start_y)
            w_end = Vector2(w.end_x, w.end_y)

            # A) Snapping to wire endpoints
            if p_pos.distance_to(w_start) <= SNAP_TOLERANCE or p_pos.distance_to(w_end) <= SNAP_TOLERANCE:
                ds.union(pin_key, w_key)
                continue

            # B) Pin resting directly on wire segment
            for seg in w.get_segments():
                if distance_point_to_segment(p_pos, seg[0], seg[1]) <= SEGMENT_TAP_TOLERANCE:
                    ds.union(pin_key, w_key)
                    break

            # C) Explicit topological link fallback
            if w.start_pin_name != "WireTap" and w.start_element_id == el_id and w.start_pin_name == pin_id:
                if p_pos.distance_to(w_start) <= 15.0:
                    ds.union(pin_key, w_key)
            if w.end_pin_name != "WireTap" and w.end_element_id == el_id and w.end_pin_name == pin_id:
                if p_pos.distance_to(w_end) <= 15.0:
                    ds.union(pin_key, w_key)

    # 2b. Direct Pin-to-Pin Snapping:
    for i in range(len(all_pins)):
        el1, pin1, p1 = all_pins[i]
        for j in range(i + 1, len(all_pins)):
            el2, pin2, p2 = all_pins[j]
            if el1 != el2 and p1.distance_to(p2) <= SNAP_TOLERANCE:
                ds.union(f"PIN:{el1}.{pin1}", f"PIN:{el2}.{pin2}")

    # 3. Ground Merging:
    # Any net containing an ElementType == Ground must be merged
    gnd_roots = []
    for el in elements:
        if el.type == "Ground":
            pin_key = f"PIN:{el.id}.GND"
            gnd_roots.append(ds.find(pin_key))

    primary_gnd_root = gnd_roots[0] if gnd_roots else None
    for r in gnd_roots[1:]:
        ds.union(primary_gnd_root, r)

    if primary_gnd_root:
        primary_gnd_root = ds.find(primary_gnd_root)

    # 4. Extract Net Groups
    raw_components = ds.get_components()

    # Filter into electrical nets with pins
    nets = []
    for root, items in raw_components.items():
        pins = [x[4:] for x in items if x.startswith("PIN:")] # e.g. "Rb1.1"
        wire_ids = [x[5:] for x in items if x.startswith("WIRE:")]
        if not pins:
            continue
        is_gnd = (root == primary_gnd_root) or any(p.startswith("GND.") for p in pins)
        nets.append({
            "root": root,
            "is_gnd": is_gnd,
            "pins": sorted(pins),
            "wires": sorted(wire_ids)
        })

    # Put Ground first as Net 0
    ground_net = None
    other_nets = []
    for n in nets:
        if n["is_gnd"]:
            ground_net = n
        else:
            other_nets.append(n)

    if ground_net is None and other_nets:
        # Fallback: check VCC.neg or vs.neg
        for n in other_nets:
            if any(p in ("VCC.neg", "VS.neg", "vs.neg") for p in n["pins"]):
                ground_net = n
                other_nets.remove(n)
                ground_net["is_gnd"] = True
                break

    sorted_nets = []
    if ground_net:
        ground_net["id"] = 0
        ground_net["name"] = "GND"
        sorted_nets.append(ground_net)

    for idx, n in enumerate(other_nets, start=1):
        n["id"] = idx
        # Assign semantic name
        pins = n["pins"]
        if any(p == "VCC.pos" for p in pins):
            n["name"] = "VCC"
        elif any(p in ("vs.pos", "VS.pos") for p in pins):
            n["name"] = "Vin"
        elif any(p.endswith(".B") for p in pins):
            n["name"] = "Vb"
        elif any(p.endswith(".C") for p in pins):
            n["name"] = "Vc"
        elif any(p.endswith(".E") for p in pins):
            n["name"] = "Ve"
        elif any(p == "C1.1" for p in pins) and any(p == "R1.2" for p in pins):
            n["name"] = "Vc" # In RC transient
        elif any(p == "RL.1" for p in pins):
            n["name"] = "Vout"
        else:
            n["name"] = f"N{idx}"
        sorted_nets.append(n)

    return sorted_nets

# Import templates from verify_templates
import sys
sys.path.insert(0, "d:/BJTU/2026年大创/tests")
from verify_templates import get_rc_transient_template, get_common_emitter_template, get_rc_coupled_template

print("=== TESTING RC_Transient ===")
elems, wires = get_rc_transient_template()
nets = extract_netlist(elems, wires)
print(f"Discovered {len(nets)} electrical nets:")
for n in nets:
    print(f"  Net {n['id']} ({n['name']}): Pins = {n['pins']}, Wires = {n['wires']}")

print("\n=== TESTING CommonEmitter_Amp ===")
elems, wires = get_common_emitter_template()
nets = extract_netlist(elems, wires)
print(f"Discovered {len(nets)} electrical nets:")
for n in nets:
    print(f"  Net {n['id']} ({n['name']}): Pins = {n['pins']}, Wires = {n['wires']}")

print("\n=== TESTING EDGE CASE 1: Floating Pins ===")
elems_ec1, wires_ec1 = get_rc_transient_template()
elems_ec1.append(CircuitElement("R_float", "悬空电阻", "Resistor", 500, 500, 0, 10.0, "kΩ"))
nets_ec1 = extract_netlist(elems_ec1, wires_ec1)
print(f"Floating element test: R_float pins connected? {[n['pins'] for n in nets_ec1 if any(p.startswith('R_float') for p in n['pins'])]}")

print("\n=== TESTING EDGE CASE 2: Shorted Resistor ===")
elems_ec2, wires_ec2 = get_rc_transient_template()
# Add a wire shorting R1.1 to R1.2 directly
wires_ec2.append(CircuitWire("W_SHORT", "R1", "1", "R1", "2", 200, 200, 280, 200, False, 0.0, 1))
nets_ec2 = extract_netlist(elems_ec2, wires_ec2)
for n in nets_ec2:
    if "R1.1" in n["pins"] and "R1.2" in n["pins"]:
        print(f"  [SHORT DETECTED] R1.1 and R1.2 collapsed into same Net {n['id']} ({n['name']})")

print("\n=== TESTING EDGE CASE 3: Direct Pin-to-Pin (Zero-Wire) Snapping ===")
elems_ec3 = [
    CircuitElement("R1", "R1", "Resistor", 100, 100, 0, 1.0, "kΩ"), # Pin 2 at (140, 100)
    CircuitElement("R2", "R2", "Resistor", 180, 100, 0, 2.0, "kΩ"), # Pin 1 at (180-40, 100) = (140, 100)
    CircuitElement("GND", "GND", "Ground", 60, 122, 0, 0.0, "V")   # Pin GND at (60, 100) -> touches R1.1 at (60, 100)
]
wires_ec3 = []
nets_ec3 = extract_netlist(elems_ec3, wires_ec3)
print(f"Direct pin-to-pin without wires: {len(nets_ec3)} nets:")
for n in nets_ec3:
    print(f"  Net {n['id']} ({n['name']}): {n['pins']}")

print("\n=== TESTING EDGE CASE 4: Missing Ground Symbol (Automatic Fallback) ===")
elems_ec4 = [
    CircuitElement("VCC", "VCC", "DcVoltageSource", 100, 100, 0, 12.0, "V"), # pos at (100, 65), neg at (100, 135)
    CircuitElement("R1", "R1", "Resistor", 200, 100, 90, 1.0, "kΩ")        # 1 at (200, 60), 2 at (200, 140)
]
wires_ec4 = [
    CircuitWire("W_TOP", "VCC", "pos", "R1", "1", 100, 65, 200, 60, False, 0.0, 1),
    CircuitWire("W_BOT", "VCC", "neg", "R1", "2", 100, 135, 200, 140, False, 0.0, 2)
]
nets_ec4 = extract_netlist(elems_ec4, wires_ec4)
print(f"Missing Ground test: {len(nets_ec4)} nets:")
for n in nets_ec4:
    print(f"  Net {n['id']} ({n['name']}, IsGround={n['is_gnd']}): {n['pins']}")

print("\n=== TESTING EDGE CASE 5: Multiple Ground Symbols (Auto-Merge) ===")
elems_ec5 = [
    CircuitElement("GND1", "GND1", "Ground", 100, 200, 0, 0.0, "V"),
    CircuitElement("GND2", "GND2", "Ground", 300, 200, 0, 0.0, "V"),
    CircuitElement("R1", "R1", "Resistor", 200, 100, 0, 1.0, "kΩ")
]
wires_ec5 = [
    CircuitWire("W_G1", "GND1", "GND", "R1", "1", 100, 178, 160, 100, False, 0.0, 0),
    CircuitWire("W_G2", "GND2", "GND", "R1", "2", 300, 178, 240, 100, False, 0.0, 0)
]
nets_ec5 = extract_netlist(elems_ec5, wires_ec5)
print(f"Multiple Ground test: {len(nets_ec5)} nets:")
for n in nets_ec5:
    print(f"  Net {n['id']} ({n['name']}, IsGround={n['is_gnd']}): {n['pins']}")

