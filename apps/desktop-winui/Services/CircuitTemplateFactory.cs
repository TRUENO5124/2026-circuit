using System;
using System.Collections.Generic;
using CircuitAssistant.Models;

namespace CircuitAssistant.Services;

public record CircuitTemplateData(
    string ProjectName,
    string ProjectFileName,
    List<CircuitElement> Elements,
    List<CircuitWire> Wires
);

public static class CircuitTemplateFactory
{
    public static CircuitTemplateData CreateCommonEmitterTemplate(
        double vcc = 12.0, double rb1 = 51.0, double rb2 = 15.0, double rc = 3.0,
        double re = 1.0, double rl = 10.0, double c1 = 10.0, double c2 = 10.0,
        double ce = 47.0, double beta = 100.0, double vs = 10.0)
    {
        var elements = new List<CircuitElement>
        {
            new CircuitElement
            {
                Id = "vs",
                Name = "交流信号源 vs",
                DefinitionId = "source.v_ac_small_signal",
                Category = "激励电源",
                Type = ElementType.AcVoltageSource,
                DisplayNumber = vs,
                UnitSymbol = "mV",
                X = 80,
                Y = 320,
                Width = 60,
                Height = 60,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "C1",
                Name = "输入耦合电容 C1",
                DefinitionId = "passive.capacitor",
                Category = "无源器件",
                Type = ElementType.Capacitor,
                DisplayNumber = c1,
                UnitSymbol = "μF",
                X = 160,
                Y = 260,
                Width = 80,
                Height = 50,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "Rb1",
                Name = "基极偏置电阻 Rb1",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rb1,
                UnitSymbol = "kΩ",
                X = 240,
                Y = 160,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "Rb2",
                Name = "基极偏置电阻 Rb2",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rb2,
                UnitSymbol = "kΩ",
                X = 240,
                Y = 360,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "Q1",
                Name = "NPN 晶体管 Q1",
                DefinitionId = "bjt.npn",
                Category = "半导体管",
                Type = ElementType.BjtNpn,
                DisplayNumber = beta,
                UnitSymbol = "β",
                X = 320,
                Y = 260,
                Width = 80,
                Height = 80,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "Rc",
                Name = "集电极电阻 Rc",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rc,
                UnitSymbol = "kΩ",
                X = 340,
                Y = 140,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "Re",
                Name = "发射极电阻 Re",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = re,
                UnitSymbol = "kΩ",
                X = 320,
                Y = 400,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "Ce",
                Name = "发射极旁路电容 Ce",
                DefinitionId = "passive.capacitor",
                Category = "无源器件",
                Type = ElementType.Capacitor,
                DisplayNumber = ce,
                UnitSymbol = "μF",
                X = 420,
                Y = 400,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "C2",
                Name = "输出耦合电容 C2",
                DefinitionId = "passive.capacitor",
                Category = "无源器件",
                Type = ElementType.Capacitor,
                DisplayNumber = c2,
                UnitSymbol = "μF",
                X = 440,
                Y = 200,
                Width = 80,
                Height = 50,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "RL",
                Name = "负载电阻 RL",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rl,
                UnitSymbol = "kΩ",
                X = 540,
                Y = 280,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "VCC",
                Name = "直流电源 VCC",
                DefinitionId = "source.v_dc",
                Category = "激励电源",
                Type = ElementType.DcVoltageSource,
                DisplayNumber = vcc,
                UnitSymbol = "V",
                X = 620,
                Y = 280,
                Width = 60,
                Height = 60,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "GND",
                Name = "参考地 GND",
                DefinitionId = "reference.ground",
                Category = "参考地",
                Type = ElementType.Ground,
                DisplayNumber = 0,
                UnitSymbol = "V",
                X = 340,
                Y = 502,
                Width = 40,
                Height = 35,
                RotationDeg = 0,
                Orientation = "Horizontal"
            }
        };

        foreach (var el in elements)
        {
            if (el.Pins.Count == 0) el.InitializeDefaultPins();
        }

        var wires = new List<CircuitWire>
        {
            new CircuitWire { Id = "W_VS_C1", StartElementId = "vs", StartPinName = "pos", EndElementId = "C1", EndPinName = "1", StartX = 80, StartY = 285, EndX = 125, EndY = 260, FlipCorner = true, NetId = 1 },
            new CircuitWire { Id = "W_BASE_LINE", StartElementId = "C1", StartPinName = "2", EndElementId = "Q1", EndPinName = "B", StartX = 195, StartY = 260, EndX = 285, EndY = 260, FlipCorner = false, NetId = 2 },
            new CircuitWire { Id = "W_RB1_BASE", StartElementId = "Rb1", StartPinName = "2", EndElementId = "W_BASE_LINE", EndPinName = "WireTap", StartX = 240, StartY = 200, EndX = 240, EndY = 260, FlipCorner = false, NetId = 2 },
            new CircuitWire { Id = "W_RB2_BASE", StartElementId = "Rb2", StartPinName = "1", EndElementId = "W_BASE_LINE", EndPinName = "WireTap", StartX = 240, StartY = 320, EndX = 240, EndY = 260, FlipCorner = false, NetId = 2 },
            new CircuitWire { Id = "W_VCC_RAIL", StartElementId = "", StartPinName = "None", EndElementId = "", EndPinName = "None", StartX = 240, StartY = 80, EndX = 620, EndY = 80, FlipCorner = false, NetId = 3 },
            new CircuitWire { Id = "W_RB1_VCC", StartElementId = "Rb1", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 240, StartY = 120, EndX = 240, EndY = 80, FlipCorner = false, NetId = 3 },
            new CircuitWire { Id = "W_RC_VCC", StartElementId = "Rc", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 340, StartY = 100, EndX = 340, EndY = 80, FlipCorner = false, NetId = 3 },
            new CircuitWire { Id = "W_VCC_FEED", StartElementId = "VCC", StartPinName = "pos", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 245, EndX = 620, EndY = 80, FlipCorner = false, NetId = 3 },
            new CircuitWire { Id = "W_COLL_LINE", StartElementId = "Rc", StartPinName = "2", EndElementId = "Q1", EndPinName = "C", StartX = 340, StartY = 180, EndX = 340, EndY = 225, FlipCorner = false, NetId = 4 },
            new CircuitWire { Id = "W_COLL_C2", StartElementId = "W_COLL_LINE", StartPinName = "WireTap", EndElementId = "C2", EndPinName = "1", StartX = 340, StartY = 200, EndX = 405, EndY = 200, FlipCorner = false, NetId = 4 },
            new CircuitWire { Id = "W_EMIT_TRUNK", StartElementId = "Q1", StartPinName = "E", EndElementId = "W_EMIT_BUS", EndPinName = "WireTap", StartX = 340, StartY = 295, EndX = 340, EndY = 340, FlipCorner = false, NetId = 5 },
            new CircuitWire { Id = "W_EMIT_BUS", StartElementId = "", StartPinName = "None", EndElementId = "", EndPinName = "None", StartX = 320, StartY = 340, EndX = 420, EndY = 340, FlipCorner = false, NetId = 5 },
            new CircuitWire { Id = "W_RE_DROP", StartElementId = "Re", StartPinName = "1", EndElementId = "W_EMIT_BUS", EndPinName = "WireTap", StartX = 320, StartY = 360, EndX = 320, EndY = 340, FlipCorner = false, NetId = 5 },
            new CircuitWire { Id = "W_CE_DROP", StartElementId = "Ce", StartPinName = "1", EndElementId = "W_EMIT_BUS", EndPinName = "WireTap", StartX = 420, StartY = 365, EndX = 420, EndY = 340, FlipCorner = false, NetId = 5 },
            new CircuitWire { Id = "W_C2_OUT", StartElementId = "C2", StartPinName = "2", EndElementId = "W_RL_DROP", EndPinName = "WireTap", StartX = 475, StartY = 200, EndX = 540, EndY = 200, FlipCorner = false, NetId = 6 },
            new CircuitWire { Id = "W_RL_DROP", StartElementId = "RL", StartPinName = "1", EndElementId = "W_C2_OUT", EndPinName = "WireTap", StartX = 540, StartY = 240, EndX = 540, EndY = 200, FlipCorner = false, NetId = 6 },
            new CircuitWire { Id = "W_GND_RAIL", StartElementId = "", StartPinName = "None", EndElementId = "", EndPinName = "None", StartX = 80, StartY = 480, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_VS_GND", StartElementId = "vs", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 80, StartY = 355, EndX = 80, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_RB2_GND", StartElementId = "Rb2", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 240, StartY = 400, EndX = 240, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_RE_GND", StartElementId = "Re", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 320, StartY = 440, EndX = 320, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_CE_GND", StartElementId = "Ce", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 420, StartY = 435, EndX = 420, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_RL_GND", StartElementId = "RL", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 540, StartY = 320, EndX = 540, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_VCC_GND", StartElementId = "VCC", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 315, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 }
        };

        return new CircuitTemplateData("分压式偏置共射放大电路", "CommonEmitter_Amp.sch", elements, wires);
    }

    public static CircuitTemplateData CreateRcTransientTemplate(
        double vs = 10.0, double r1 = 1.0, double c1 = 100.0)
    {
        var elements = new List<CircuitElement>
        {
            new CircuitElement
            {
                Id = "VS",
                Name = "直流阶跃电源 VS",
                DefinitionId = "source.v_dc",
                Category = "激励电源",
                Type = ElementType.DcVoltageSource,
                DisplayNumber = vs,
                UnitSymbol = "V",
                X = 120,
                Y = 280,
                Width = 60,
                Height = 60,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "R1",
                Name = "充电电阻 R1",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = r1,
                UnitSymbol = "kΩ",
                X = 240,
                Y = 200,
                Width = 80,
                Height = 50,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "C1",
                Name = "积分电容 C1",
                DefinitionId = "passive.capacitor",
                Category = "无源器件",
                Type = ElementType.Capacitor,
                DisplayNumber = c1,
                UnitSymbol = "μF",
                X = 360,
                Y = 280,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "GND",
                Name = "参考地 GND",
                DefinitionId = "reference.ground",
                Category = "参考地",
                Type = ElementType.Ground,
                DisplayNumber = 0,
                UnitSymbol = "V",
                X = 240,
                Y = 382,
                Width = 40,
                Height = 35,
                RotationDeg = 0,
                Orientation = "Horizontal"
            }
        };

        foreach (var el in elements)
        {
            if (el.Pins.Count == 0) el.InitializeDefaultPins();
        }

        var wires = new List<CircuitWire>
        {
            new CircuitWire { Id = "W_VS_R1", StartElementId = "VS", StartPinName = "pos", EndElementId = "R1", EndPinName = "1", StartX = 120, StartY = 245, EndX = 200, EndY = 200, FlipCorner = true, NetId = 1 },
            new CircuitWire { Id = "W_R1_C1", StartElementId = "R1", StartPinName = "2", EndElementId = "C1", EndPinName = "1", StartX = 280, StartY = 200, EndX = 360, EndY = 245, FlipCorner = false, NetId = 2 },
            new CircuitWire { Id = "W_GND_RAIL", StartElementId = "", StartPinName = "None", EndElementId = "", EndPinName = "None", StartX = 120, StartY = 360, EndX = 360, EndY = 360, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_VS_GND", StartElementId = "VS", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 120, StartY = 315, EndX = 120, EndY = 360, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_C1_GND", StartElementId = "C1", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 360, StartY = 315, EndX = 360, EndY = 360, FlipCorner = false, NetId = 0 }
        };

        return new CircuitTemplateData("一阶 RC 暂态响应电路", "RC_Transient.sch", elements, wires);
    }

    public static CircuitTemplateData CreateRcCoupledTemplate(
        double vcc = 12.0, double rb = 300.0, double rc = 3.0, double rl = 10.0,
        double c1 = 10.0, double c2 = 10.0, double beta = 100.0, double vs = 10.0)
    {
        var elements = new List<CircuitElement>
        {
            new CircuitElement
            {
                Id = "vs",
                Name = "交流信号源 vs",
                DefinitionId = "source.v_ac_small_signal",
                Category = "激励电源",
                Type = ElementType.AcVoltageSource,
                DisplayNumber = vs,
                UnitSymbol = "mV",
                X = 80,
                Y = 320,
                Width = 60,
                Height = 60,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "C1",
                Name = "输入耦合电容 C1",
                DefinitionId = "passive.capacitor",
                Category = "无源器件",
                Type = ElementType.Capacitor,
                DisplayNumber = c1,
                UnitSymbol = "μF",
                X = 160,
                Y = 260,
                Width = 80,
                Height = 50,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "Rb",
                Name = "基极固定偏置电阻 Rb",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rb,
                UnitSymbol = "kΩ",
                X = 240,
                Y = 160,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "Q1",
                Name = "NPN 晶体管 Q1",
                DefinitionId = "bjt.npn",
                Category = "半导体管",
                Type = ElementType.BjtNpn,
                DisplayNumber = beta,
                UnitSymbol = "β",
                X = 320,
                Y = 260,
                Width = 80,
                Height = 80,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "Rc",
                Name = "集电极电阻 Rc",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rc,
                UnitSymbol = "kΩ",
                X = 340,
                Y = 140,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "C2",
                Name = "输出耦合电容 C2",
                DefinitionId = "passive.capacitor",
                Category = "无源器件",
                Type = ElementType.Capacitor,
                DisplayNumber = c2,
                UnitSymbol = "μF",
                X = 440,
                Y = 200,
                Width = 80,
                Height = 50,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "RL",
                Name = "负载电阻 RL",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rl,
                UnitSymbol = "kΩ",
                X = 540,
                Y = 280,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "VCC",
                Name = "直流电源 VCC",
                DefinitionId = "source.v_dc",
                Category = "激励电源",
                Type = ElementType.DcVoltageSource,
                DisplayNumber = vcc,
                UnitSymbol = "V",
                X = 620,
                Y = 280,
                Width = 60,
                Height = 60,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "GND",
                Name = "参考地 GND",
                DefinitionId = "reference.ground",
                Category = "参考地",
                Type = ElementType.Ground,
                DisplayNumber = 0,
                UnitSymbol = "V",
                X = 340,
                Y = 502,
                Width = 40,
                Height = 35,
                RotationDeg = 0,
                Orientation = "Horizontal"
            }
        };

        foreach (var el in elements)
        {
            if (el.Pins.Count == 0) el.InitializeDefaultPins();
        }

        var wires = new List<CircuitWire>
        {
            new CircuitWire { Id = "W_VS_C1", StartElementId = "vs", StartPinName = "pos", EndElementId = "C1", EndPinName = "1", StartX = 80, StartY = 285, EndX = 125, EndY = 260, FlipCorner = true, NetId = 1 },
            new CircuitWire { Id = "W_BASE_LINE", StartElementId = "C1", StartPinName = "2", EndElementId = "Q1", EndPinName = "B", StartX = 195, StartY = 260, EndX = 285, EndY = 260, FlipCorner = false, NetId = 2 },
            new CircuitWire { Id = "W_RB_BASE", StartElementId = "Rb", StartPinName = "2", EndElementId = "W_BASE_LINE", EndPinName = "WireTap", StartX = 240, StartY = 200, EndX = 240, EndY = 260, FlipCorner = false, NetId = 2 },
            new CircuitWire { Id = "W_VCC_RAIL", StartElementId = "", StartPinName = "None", EndElementId = "", EndPinName = "None", StartX = 240, StartY = 80, EndX = 620, EndY = 80, FlipCorner = false, NetId = 3 },
            new CircuitWire { Id = "W_RB_VCC", StartElementId = "Rb", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 240, StartY = 120, EndX = 240, EndY = 80, FlipCorner = false, NetId = 3 },
            new CircuitWire { Id = "W_RC_VCC", StartElementId = "Rc", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 340, StartY = 100, EndX = 340, EndY = 80, FlipCorner = false, NetId = 3 },
            new CircuitWire { Id = "W_VCC_FEED", StartElementId = "VCC", StartPinName = "pos", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 245, EndX = 620, EndY = 80, FlipCorner = false, NetId = 3 },
            new CircuitWire { Id = "W_COLL_LINE", StartElementId = "Rc", StartPinName = "2", EndElementId = "Q1", EndPinName = "C", StartX = 340, StartY = 180, EndX = 340, EndY = 225, FlipCorner = false, NetId = 4 },
            new CircuitWire { Id = "W_COLL_C2", StartElementId = "W_COLL_LINE", StartPinName = "WireTap", EndElementId = "C2", EndPinName = "1", StartX = 340, StartY = 200, EndX = 405, EndY = 200, FlipCorner = false, NetId = 4 },
            new CircuitWire { Id = "W_EMIT_GND", StartElementId = "Q1", StartPinName = "E", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 340, StartY = 295, EndX = 360, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_C2_OUT", StartElementId = "C2", StartPinName = "2", EndElementId = "W_RL_DROP", EndPinName = "WireTap", StartX = 475, StartY = 200, EndX = 540, EndY = 200, FlipCorner = false, NetId = 5 },
            new CircuitWire { Id = "W_RL_DROP", StartElementId = "RL", StartPinName = "1", EndElementId = "W_C2_OUT", EndPinName = "WireTap", StartX = 540, StartY = 240, EndX = 540, EndY = 200, FlipCorner = false, NetId = 5 },
            new CircuitWire { Id = "W_GND_RAIL", StartElementId = "", StartPinName = "None", EndElementId = "", EndPinName = "None", StartX = 80, StartY = 480, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_VS_GND", StartElementId = "vs", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 80, StartY = 355, EndX = 80, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_RL_GND", StartElementId = "RL", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 540, StartY = 320, EndX = 540, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_VCC_GND", StartElementId = "VCC", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 315, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 }
        };

        return new CircuitTemplateData("阻容耦合单管放大电路", "RC_Coupled_Amp.sch", elements, wires);
    }

    public static CircuitTemplateData CreateCommonBaseTemplate(
        double vcc = 12.0, double rb1 = 51.0, double rb2 = 15.0, double rc = 3.0,
        double re = 1.5, double rl = 3.0, double c1 = 10.0, double c2 = 10.0,
        double cb = 47.0, double beta = 80.0, double vs = 10.0)
    {
        var elements = new List<CircuitElement>
        {
            new CircuitElement
            {
                Id = "vs",
                Name = "交流信号源 vs",
                DefinitionId = "source.v_ac_small_signal",
                Category = "激励电源",
                Type = ElementType.AcVoltageSource,
                DisplayNumber = vs,
                UnitSymbol = "mV",
                X = 80,
                Y = 320,
                Width = 60,
                Height = 60,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "C1",
                Name = "输入耦合电容 C1",
                DefinitionId = "passive.capacitor",
                Category = "无源器件",
                Type = ElementType.Capacitor,
                DisplayNumber = c1,
                UnitSymbol = "μF",
                X = 160,
                Y = 320,
                Width = 80,
                Height = 50,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "Re",
                Name = "发射极电阻 Re",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = re,
                UnitSymbol = "kΩ",
                X = 240,
                Y = 380,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "Q1",
                Name = "NPN 晶体管 Q1",
                DefinitionId = "bjt.npn",
                Category = "半导体管",
                Type = ElementType.BjtNpn,
                DisplayNumber = beta,
                UnitSymbol = "β",
                X = 320,
                Y = 260,
                Width = 80,
                Height = 80,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "Rb1",
                Name = "基极偏置电阻 Rb1",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rb1,
                UnitSymbol = "kΩ",
                X = 260,
                Y = 160,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "Rb2",
                Name = "基极偏置电阻 Rb2",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rb2,
                UnitSymbol = "kΩ",
                X = 260,
                Y = 260,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "Cb",
                Name = "基极旁路电容 Cb",
                DefinitionId = "passive.capacitor_polarized",
                Category = "无源器件",
                Type = ElementType.CapacitorPolarized,
                DisplayNumber = cb,
                UnitSymbol = "μF",
                X = 200,
                Y = 240,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "Rc",
                Name = "集电极电阻 Rc",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rc,
                UnitSymbol = "kΩ",
                X = 360,
                Y = 140,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "C2",
                Name = "输出耦合电容 C2",
                DefinitionId = "passive.capacitor",
                Category = "无源器件",
                Type = ElementType.Capacitor,
                DisplayNumber = c2,
                UnitSymbol = "μF",
                X = 460,
                Y = 200,
                Width = 80,
                Height = 50,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "RL",
                Name = "负载电阻 RL",
                DefinitionId = "passive.resistor",
                Category = "无源器件",
                Type = ElementType.Resistor,
                DisplayNumber = rl,
                UnitSymbol = "kΩ",
                X = 540,
                Y = 280,
                Width = 80,
                Height = 50,
                RotationDeg = 90,
                Orientation = "Vertical"
            },
            new CircuitElement
            {
                Id = "VCC",
                Name = "直流电源 VCC",
                DefinitionId = "source.v_dc",
                Category = "激励电源",
                Type = ElementType.DcVoltageSource,
                DisplayNumber = vcc,
                UnitSymbol = "V",
                X = 620,
                Y = 280,
                Width = 60,
                Height = 60,
                RotationDeg = 0,
                Orientation = "Horizontal"
            },
            new CircuitElement
            {
                Id = "GND",
                Name = "参考地 GND",
                DefinitionId = "reference.ground",
                Category = "参考地",
                Type = ElementType.Ground,
                DisplayNumber = 0,
                UnitSymbol = "V",
                X = 340,
                Y = 502,
                Width = 40,
                Height = 35,
                RotationDeg = 0,
                Orientation = "Horizontal"
            }
        };

        foreach (var el in elements)
        {
            if (el.Pins.Count == 0) el.InitializeDefaultPins();
        }

        var wires = new List<CircuitWire>
        {
            // Input branch to emitter
            new CircuitWire { Id = "W_VS_C1", StartElementId = "vs", StartPinName = "pos", EndElementId = "C1", EndPinName = "1", StartX = 80, StartY = 285, EndX = 125, EndY = 320, FlipCorner = true, NetId = 1 },
            new CircuitWire { Id = "W_EMIT_IN", StartElementId = "C1", StartPinName = "2", EndElementId = "Q1", EndPinName = "E", StartX = 195, StartY = 320, EndX = 340, EndY = 295, FlipCorner = true, NetId = 2 },
            new CircuitWire { Id = "W_RE_EMIT", StartElementId = "Re", StartPinName = "1", EndElementId = "W_EMIT_IN", EndPinName = "WireTap", StartX = 240, StartY = 340, EndX = 240, EndY = 320, FlipCorner = false, NetId = 2 },

            // Base node (Q1.B, Rb1.2, Rb2.1, Cb.pos)
            new CircuitWire { Id = "W_BASE_BUS", StartElementId = "Q1", StartPinName = "B", EndElementId = "Cb", EndPinName = "+", StartX = 285, StartY = 260, EndX = 200, EndY = 215, FlipCorner = true, NetId = 3 },
            new CircuitWire { Id = "W_RB1_BASE", StartElementId = "Rb1", StartPinName = "2", EndElementId = "W_BASE_BUS", EndPinName = "WireTap", StartX = 260, StartY = 200, EndX = 260, EndY = 260, FlipCorner = false, NetId = 3 },
            new CircuitWire { Id = "W_RB2_BASE", StartElementId = "Rb2", StartPinName = "1", EndElementId = "W_BASE_BUS", EndPinName = "WireTap", StartX = 260, StartY = 220, EndX = 260, EndY = 260, FlipCorner = false, NetId = 3 },

            // VCC rail
            new CircuitWire { Id = "W_VCC_RAIL", StartElementId = "", StartPinName = "None", EndElementId = "", EndPinName = "None", StartX = 260, StartY = 80, EndX = 620, EndY = 80, FlipCorner = false, NetId = 4 },
            new CircuitWire { Id = "W_RB1_VCC", StartElementId = "Rb1", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 260, StartY = 120, EndX = 260, EndY = 80, FlipCorner = false, NetId = 4 },
            new CircuitWire { Id = "W_RC_VCC", StartElementId = "Rc", StartPinName = "1", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 360, StartY = 100, EndX = 360, EndY = 80, FlipCorner = false, NetId = 4 },
            new CircuitWire { Id = "W_VCC_FEED", StartElementId = "VCC", StartPinName = "pos", EndElementId = "W_VCC_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 245, EndX = 620, EndY = 80, FlipCorner = false, NetId = 4 },

            // Collector output
            new CircuitWire { Id = "W_COLL_LINE", StartElementId = "Rc", StartPinName = "2", EndElementId = "Q1", EndPinName = "C", StartX = 360, StartY = 180, EndX = 340, EndY = 225, FlipCorner = true, NetId = 5 },
            new CircuitWire { Id = "W_COLL_C2", StartElementId = "W_COLL_LINE", StartPinName = "WireTap", EndElementId = "C2", EndPinName = "1", StartX = 360, StartY = 200, EndX = 425, EndY = 200, FlipCorner = false, NetId = 5 },
            new CircuitWire { Id = "W_C2_RL", StartElementId = "C2", StartPinName = "2", EndElementId = "W_RL_DROP", EndPinName = "WireTap", StartX = 495, StartY = 200, EndX = 540, EndY = 200, FlipCorner = false, NetId = 6 },
            new CircuitWire { Id = "W_RL_DROP", StartElementId = "RL", StartPinName = "1", EndElementId = "W_C2_RL", EndPinName = "WireTap", StartX = 540, StartY = 240, EndX = 540, EndY = 200, FlipCorner = false, NetId = 6 },

            // GND rail
            new CircuitWire { Id = "W_GND_RAIL", StartElementId = "", StartPinName = "None", EndElementId = "", EndPinName = "None", StartX = 80, StartY = 480, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_VS_GND", StartElementId = "vs", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 80, StartY = 355, EndX = 80, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_CB_GND", StartElementId = "Cb", StartPinName = "-", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 200, StartY = 265, EndX = 200, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_RE_GND", StartElementId = "Re", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 240, StartY = 420, EndX = 240, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_RB2_GND", StartElementId = "Rb2", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 260, StartY = 300, EndX = 260, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_RL_GND", StartElementId = "RL", StartPinName = "2", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 540, StartY = 320, EndX = 540, EndY = 480, FlipCorner = false, NetId = 0 },
            new CircuitWire { Id = "W_VCC_GND", StartElementId = "VCC", StartPinName = "neg", EndElementId = "W_GND_RAIL", EndPinName = "WireTap", StartX = 620, StartY = 315, EndX = 620, EndY = 480, FlipCorner = false, NetId = 0 }
        };

        return new CircuitTemplateData("共基极放大电路 (教学题13标准模板)", "CommonBase_Amp.sch", elements, wires);
    }

    public static CircuitTemplateData CreateBlankTemplate()
    {
        return new CircuitTemplateData("空白电路工程", "Blank_Circuit.sch", new List<CircuitElement>(), new List<CircuitWire>());
    }
}

