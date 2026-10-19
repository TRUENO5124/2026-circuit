// ============================================================================
// File: apps/desktop-winui/Engine/Nonlinear/MosfetModel.cs
// Namespace: CircuitAssistant.Engine.Nonlinear
// Description: Level-1 square-law MOSFET device model used by the native
//              nonlinear analysis path.
//
//              Parameter mapping (see Models/CircuitElement.cs and
//              Models/CircuitProjectData.cs):
//                * DisplayNumber = threshold voltage Vth (NMOS positive,
//                  PMOS stored negative, e.g. -1.5 V);
//                * MosfetK       = K = 0.5 * mu * Cox * (W/L) in A/V^2
//                  (default 1 mA/V^2, persisted in the project file);
//                * MosfetLambda  = channel-length modulation lambda in 1/V
//                  (0 => ro -> infinity).
//
//              Polarity convention for the model API: all current/magnitude
//              methods operate on NON-NEGATIVE mirror quantities
//                  vov = |Vov| (overdrive magnitude, on-positive)
//                  vdsMag = |VDS| (or VSD for PMOS)
//              so the same square-law equations serve NMOS and PMOS; the
//              strategy layer maps terminal voltages to these magnitudes and
//              applies the sign conventions in the pedagogical steps.
//
//              The model accepts enhancement and depletion MOSFETs (the same
//              equations with a signed Vth; a negative threshold describes an
//              N-channel depletion device). JFETs use explicit IDSS/VGS(off)
//              parameters to obtain K=IDSS/VGS(off)^2; their reverse gate bias
//              is additionally checked by the teaching strategy.
//
// OWNER: engine-mos (implemented).
// ============================================================================

using System;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine.Nonlinear;

/// <summary>MOSFET operating region in the square-law model.</summary>
public enum MosfetRegion
{
    Cutoff,
    Triode,
    Saturation,
    /// <summary>Non-finite input or a reversed drain/source magnitude; no physical region inferred.</summary>
    Invalid
}

/// <summary>
/// Level-1 square-law MOSFET model (NMOS/PMOS, enhancement and depletion
/// variants share the same equations with a signed threshold voltage).
/// </summary>
public sealed class MosfetModel
{
    /// <summary>Teaching default for the square-law parameter K (A/V^2).</summary>
    private const double DefaultK = 1e-3;

    public bool IsPChannel { get; }
    public bool IsEnhancement { get; }
    public bool IsJfet { get; private init; }
    public double Idss { get; private init; }

    /// <summary>Signed threshold voltage Vth (V).</summary>
    public double ThresholdVoltage { get; }

    /// <summary>Square-law parameter K in A/V^2 (positive).</summary>
    public double K { get; }

    /// <summary>Channel-length modulation lambda in 1/V (0 = ignored).</summary>
    public double Lambda { get; }

    private MosfetModel(bool isPChannel, bool isEnhancement, double thresholdVoltage, double k, double lambda)
    {
        IsPChannel = isPChannel;
        IsEnhancement = isEnhancement;
        ThresholdVoltage = thresholdVoltage;
        K = k;
        Lambda = lambda;
    }

    /// <summary>Builds the model from the circuit element and analysis parameters.</summary>
    public static MosfetModel FromElement(CircuitElement element, AnalysisParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!element.IsMosfet && !element.IsJfet)
        {
            throw new ArgumentException(
                $"器件 {element.Id}（{element.Type}）不是 MOSFET，无法用平方律模型描述。",
                nameof(element));
        }

        // Signed threshold voltage: NMOS stores a positive Vth, PMOS a negative
        // Vth (e.g. -1.5 V); a negative NMOS Vth describes a depletion device.
        double thresholdVoltage = double.IsFinite(element.DisplayNumber) ? element.DisplayNumber : 0.0;
        bool isPChannel = element.Type is ElementType.MosfetPEnh or ElementType.MosfetPDep or ElementType.JfetP;
        double normalizedThreshold = isPChannel ? -thresholdVoltage : thresholdVoltage;
        if (!double.IsFinite(element.DisplayNumber) ||
            (element.IsEnhancementMosfet ? normalizedThreshold <= 0 : normalizedThreshold >= 0))
            throw new ArgumentException($"器件 {element.Id} 的阈值/夹断电压符号无效：增强型 N/P 应为正/负，耗尽型及 JFET N/P 应为负/正，且不能为零。");

        // K is a persisted teaching parameter; older/edited files may carry 0.
        double k = element.MosfetK;
        if (!double.IsFinite(k) || k <= 0.0) k = DefaultK;
        if (element.IsJfet)
        {
            if (!double.IsFinite(element.JfetIdss) || element.JfetIdss <= 0)
                throw new ArgumentException($"器件 {element.Id}（JFET）缺少有效的 IDSS：请填写有限正数，单位 A，不能用 MOSFET 的 K 默认值替代。");
            // Shockley: ID=IDSS(1-VGS/VGS(off))² = K·Vov².
            k = element.JfetIdss / (thresholdVoltage * thresholdVoltage);
            if (!double.IsFinite(k) || k <= 0)
                throw new ArgumentException($"器件 {element.Id}（JFET）的 IDSS/VGS(off)² 超出数值范围。");
        }

        // lambda = 0 means channel-length modulation is ignored (ro -> infinity).
        double lambda = element.MosfetLambda;
        if (!double.IsFinite(lambda) || lambda < 0.0) lambda = 0.0;

        return new MosfetModel(
            isPChannel: isPChannel,
            isEnhancement: element.IsEnhancementMosfet,
            thresholdVoltage: thresholdVoltage,
            k: k,
            lambda: lambda) { IsJfet = element.IsJfet, Idss = element.JfetIdss };
    }

    /// <summary>
    /// Overdrive voltage for the given gate-source voltage, on-positive:
    /// VGS - Vth for NMOS, Vth - VGS for PMOS (>= 0 when the device is on;
    /// a non-positive value means the device is cut off).
    /// </summary>
    public double OverdriveMagnitude(double gateSourceVoltage) =>
        IsPChannel
            ? ThresholdVoltage - gateSourceVoltage
            : gateSourceVoltage - ThresholdVoltage;

    /// <summary>
    /// Drain current magnitude in the square-law model, dispatched by region:
    /// saturation K*vov^2*(1 + lambda*vdsMag), triode
    /// K*(2*vov*vdsMag - vdsMag^2)*(1 + lambda*vdsMag); 0 in cutoff.
    /// </summary>
    public double DrainCurrentMagnitude(double overdrive, double drainSourceMagnitude)
    {
        if (Region(overdrive, drainSourceMagnitude) == MosfetRegion.Invalid) return double.NaN;
        if (!(overdrive > 0.0) || !(drainSourceMagnitude > 0.0)) return 0.0;

        double channelLengthFactor = 1.0 + Lambda * drainSourceMagnitude;
        if (drainSourceMagnitude < overdrive)
        {
            // Variable-resistance (triode) region.
            return K * (2.0 * overdrive * drainSourceMagnitude - drainSourceMagnitude * drainSourceMagnitude)
                     * channelLengthFactor;
        }

        // Saturation region.
        return K * overdrive * overdrive * channelLengthFactor;
    }

    /// <summary>Saturation derivative dID/dVGS magnitude, including channel-length modulation.</summary>
    public double Transconductance(double overdrive, double drainSourceMagnitude = 0.0) =>
        overdrive > 0.0 ? 2.0 * K * overdrive * (1.0 + Lambda * drainSourceMagnitude) : 0.0;

    /// <summary>Small-signal output conductance gds = lambda*K*vov^2 (0 when lambda = 0).</summary>
    public double OutputConductance(double overdrive) =>
        overdrive > 0.0 ? Lambda * K * overdrive * overdrive : 0.0;

    /// <summary>Region for the given non-negative magnitude quantities.</summary>
    public MosfetRegion Region(double overdrive, double drainSourceMagnitude)
    {
        if (!double.IsFinite(overdrive) || !double.IsFinite(drainSourceMagnitude) || drainSourceMagnitude < 0)
            return MosfetRegion.Invalid;
        if (!(overdrive > 0.0)) return MosfetRegion.Cutoff;
        if (drainSourceMagnitude < overdrive) return MosfetRegion.Triode;
        return MosfetRegion.Saturation;
    }
}
