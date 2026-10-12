import sys
sys.path.insert(0, "tests")
from verify_templates import (
    CircuitElement, CircuitWire, Vector2,
    point_on_segment, distance_point_to_segment
)

def get_my_common_emitter():
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

def get_my_rc_transient():
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

def get_my_rc_coupled():
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

# Test adversarial suite with Rule B
import adversarial_template_tests
def my_get_template_data(template_name):
    if template_name == "common_emitter":
        return get_my_common_emitter()
    elif template_name == "rc_transient":
        return get_my_rc_transient()
    elif template_name == "ac_coupled":
        return get_my_rc_coupled()
    elif template_name == "blank":
        return [], []
    raise ValueError(template_name)

adversarial_template_tests.get_template_data = my_get_template_data

# Run modified suite simulation
failures = []
total_tests = 0
passed_tests = 0

for t_name in ["common_emitter", "rc_transient", "ac_coupled"]:
    elements, wires = my_get_template_data(t_name)
    elem_map = {e.id: e for e in elements}
    wire_map = {w.id: w for w in wires}

    uf = adversarial_template_tests.UnionFind()

    for w in wires:
        w_node = f"WIRE:{w.id}"
        uf.find(w_node)
        if w.start_pin_name == "WireTap":
            uf.union(w_node, f"WIRE:{w.start_element_id}")
        else:
            uf.union(w_node, f"PIN:{w.start_element_id}.{w.start_pin_name}")

        if w.end_pin_name == "WireTap":
            uf.union(w_node, f"WIRE:{w.end_element_id}")
        else:
            uf.union(w_node, f"PIN:{w.end_element_id}.{w.end_pin_name}")

    # Rule B from NetlistExtractor.cs: pins resting on wire segments
    for elem in elements:
        for pin_id in elem.pins:
            p_pos = elem.get_pin_world_pos(pin_id)
            for w in wires:
                pts = w.get_path_points()
                for k in range(len(pts) - 1):
                    if point_on_segment(p_pos, pts[k], pts[k+1], tol=5.0):
                        uf.union(f"PIN:{elem.id}.{pin_id}", f"WIRE:{w.id}")
                        break

    components = uf.get_components()
    def find_net(item):
        root = uf.find(item)
        return components.get(root)

    gnd_rail_net = find_net("WIRE:W_GND_RAIL")
    print(f"{t_name} GND.GND in gnd_rail_net:", "PIN:GND.GND" in gnd_rail_net)

