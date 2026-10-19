// ============================================================================
// File: apps/desktop-winui/Engine/Nonlinear/DiodeModel.cs
// Namespace: CircuitAssistant.Engine.Nonlinear
// Description: Two-terminal diode device model used by the native nonlinear
//              analysis path. Two complementary models are provided:
//                * piecewise-linear constant-voltage-drop model (large-signal /
//                  waveform-oriented teaching analysis: silicon 0.7 V, germanium
//                  0.3 V forward drop, ideal reverse blocking);
//                * Shockley exponential model (iD = Is*(exp(vD/(n*Vt))-1)) with
//                  numerically clamped exponent, used by the general DC operating
//                  point solver (Newton-Raphson).
//
// OWNER: engine-diode.
// ============================================================================

using System;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine.Nonlinear;

/// <summary>Conduction state of a two-terminal diode in the piecewise-linear model.</summary>
public enum DiodeRegion
{
    /// <summary>Reverse biased: negligible current (piecewise model: exactly 0).</summary>
    Reverse,
    /// <summary>Forward biased below the constant drop: blocked in the piecewise model.</summary>
    Off,
    /// <summary>Forward conducting: vD >= VF (piecewise) or significant Shockley current.</summary>
    On,
    /// <summary>Non-finite terminal voltage; this is not a physical off state.</summary>
    Invalid
}

/// <summary>
/// Diode device model shared by the diode teaching strategies and the general
/// DC operating-point solver. Supports both the diode's material-derived forward
/// drop (silicon 0.7 V / germanium 0.3 V, exposed by
/// <see cref="CircuitElement.ForwardVoltageDrop"/>) and a Shockley exponential
/// description with silicon-like defaults.
///
/// Numeric contract: the Shockley exponential is evaluable for any finite
/// voltage (the exponent argument is clamped), the reverse current saturates at
/// -Is, and <see cref="Conductance"/> is the analytic derivative of
/// <see cref="Current"/> (it returns exactly 0 outside the clamped interval where
/// the current is deliberately held constant).
/// </summary>
public sealed class DiodeModel
{
    /// <summary>Default Shockley saturation current Is (A) for teaching diodes.</summary>
    public const double DefaultSaturationCurrent = 1e-12;

    /// <summary>Default Shockley ideality factor n (dimensionless).</summary>
    public const double DefaultIdealityFactor = 1.0;

    /// <summary>Default thermal voltage Vt (V) at room temperature.</summary>
    public const double DefaultThermalVoltage = 0.026;

    /// <summary>
    /// Upper clamping bound for the diode voltage (V). At and above this value
    /// the Shockley current is held constant so a Newton iteration can never
    /// overflow (e^38 ≈ 3.4e16, current ≈ 34 kA for Is = 1 pA).
    /// </summary>
    public const double MaximumForwardVoltage = 0.99;

    /// <summary>Lower clamping bound for the diode voltage (V); the current saturates at -Is.</summary>
    public const double MaximumReverseVoltage = -0.99;

    /// <summary>Piecewise-linear forward drop in volts (silicon 0.7 V, germanium 0.3 V).</summary>
    public double ForwardVoltageDrop { get; }

    /// <summary>Shockley saturation current Is in amperes (default 1e-12 A).</summary>
    public double SaturationCurrent { get; }

    /// <summary>Shockley ideality factor n (default 1).</summary>
    public double IdealityFactor { get; }

    /// <summary>Thermal voltage Vt in volts (default 0.026 V).</summary>
    public double ThermalVoltage { get; }

    /// <summary>Exponential scale n·Vt in volts; always finite and positive.</summary>
    public double ThermalVoltageScale => IdealityFactor * ThermalVoltage;

    /// <summary>Diode current at the forward clamping bound (finite upper bound for Newton solves).</summary>
    public double ForwardClampCurrent => Current(MaximumForwardVoltage);

    private DiodeModel(
        double forwardVoltageDrop,
        double saturationCurrent,
        double idealityFactor,
        double thermalVoltage)
    {
        ForwardVoltageDrop = forwardVoltageDrop;
        SaturationCurrent = saturationCurrent;
        IdealityFactor = idealityFactor;
        ThermalVoltage = thermalVoltage;
    }

    /// <summary>
    /// Creates the model from a circuit element (material => VF) and the current
    /// analysis parameters (thermal voltage). Is/n use silicon-like teaching
    /// defaults unless the implementation later supports per-element overrides.
    /// </summary>
    public static DiodeModel FromElement(CircuitElement element, AnalysisParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(parameters);

        double vf = element.ForwardVoltageDrop;
        if (!double.IsFinite(vf) || vf <= 0) vf = 0.7;

        double vt = parameters.ThermalVoltageUt;
        if (!double.IsFinite(vt) || vt <= 0) vt = DefaultThermalVoltage;

        return new DiodeModel(vf, DefaultSaturationCurrent, DefaultIdealityFactor, vt);
    }

    /// <summary>
    /// Creates a model with explicit device parameters. Kept public so unit checks
    /// can exercise Is/n/thermal-voltage overrides without touching the element.
    /// </summary>
    public static DiodeModel Create(
        double forwardVoltageDrop = 0.7,
        double saturationCurrent = DefaultSaturationCurrent,
        double idealityFactor = DefaultIdealityFactor,
        double thermalVoltage = DefaultThermalVoltage)
    {
        double vf = double.IsFinite(forwardVoltageDrop) && forwardVoltageDrop > 0 ? forwardVoltageDrop : 0.7;
        double isat = double.IsFinite(saturationCurrent) && saturationCurrent > 0 ? saturationCurrent : DefaultSaturationCurrent;
        double n = double.IsFinite(idealityFactor) && idealityFactor > 0 ? idealityFactor : DefaultIdealityFactor;
        double vt = double.IsFinite(thermalVoltage) && thermalVoltage > 0 ? thermalVoltage : DefaultThermalVoltage;
        return new DiodeModel(vf, isat, n, vt);
    }

    /// <summary>
    /// Clamps a diode voltage into the numerically safe interval
    /// [<see cref="MaximumReverseVoltage"/>, <see cref="MaximumForwardVoltage"/>].
    /// </summary>
    public double ClampVoltage(double voltage)
    {
        if (double.IsNaN(voltage)) return double.NaN;
        return Math.Clamp(voltage, MaximumReverseVoltage, MaximumForwardVoltage);
    }

    /// <summary>Shockley diode current in amperes at the given anode-cathode voltage.</summary>
    public double Current(double voltage)
    {
        if (double.IsNaN(voltage)) return double.NaN;

        double v = ClampVoltage(voltage);
        double x = v / ThermalVoltageScale;

        // Defensive guard: an absurdly small Vt could otherwise overflow exp().
        if (x > 700.0) return SaturationCurrent * (Math.Exp(700.0) - 1.0);
        if (x < -700.0) return -SaturationCurrent;
        return SaturationCurrent * (Math.Exp(x) - 1.0);
    }

    /// <summary>
    /// Small-signal conductance dI/dV in siemens at the given voltage. This is the
    /// exact analytic derivative of <see cref="Current"/>, including the clamped
    /// regions where the current is held constant (derivative 0).
    /// </summary>
    public double Conductance(double voltage)
    {
        if (double.IsNaN(voltage)) return double.NaN;
        if (voltage >= MaximumForwardVoltage || voltage <= MaximumReverseVoltage)
            return 0.0;

        double v = ClampVoltage(voltage);
        double x = v / ThermalVoltageScale;
        if (x > 700.0) return 0.0;
        if (x < -700.0) return 0.0;
        return (SaturationCurrent / ThermalVoltageScale) * Math.Exp(x);
    }

    /// <summary>
    /// Piecewise-linear region classification at the given voltage:
    /// v &lt; 0 reverse, 0 ≤ v &lt; VF blocked, v ≥ VF conducting.
    /// </summary>
    public DiodeRegion Region(double voltage)
    {
        if (!double.IsFinite(voltage)) return DiodeRegion.Invalid;
        if (voltage < 0) return DiodeRegion.Reverse;
        return voltage >= ForwardVoltageDrop ? DiodeRegion.On : DiodeRegion.Off;
    }

    /// <summary>True when the piecewise-linear model considers the diode conducting.</summary>
    public bool IsForwardConducting(double voltage) => Region(voltage) == DiodeRegion.On;

    /// <summary>Piecewise-linear current: 0 when blocked/reverse, otherwise the branch current is set by the circuit.</summary>
    public double PiecewiseLinearCurrent(double voltage, double branchResistance)
    {
        if (!double.IsFinite(voltage)) return double.NaN;
        if (double.IsPositiveInfinity(branchResistance)) return 0.0; // Explicit open circuit.
        if (!double.IsFinite(branchResistance) || branchResistance <= 0) return double.NaN;
        return Region(voltage) == DiodeRegion.On
            ? Math.Max(0.0, (voltage - ForwardVoltageDrop) / branchResistance)
            : 0.0;
    }

    /// <summary>Inverse Shockley relation: diode voltage needed for a given forward current (A).</summary>
    public double VoltageForCurrent(double current)
    {
        if (double.IsNaN(current)) return double.NaN;
        double ratio = current / SaturationCurrent + 1.0;
        if (ratio <= 0) return MaximumReverseVoltage;
        return ThermalVoltageScale * Math.Log(ratio);
    }
}
