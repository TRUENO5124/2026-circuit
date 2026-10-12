namespace CircuitAssistant.Models;

public enum UnitPrefix
{
    Pico,
    Nano,
    Micro,
    Milli,
    None,
    Kilo,
    Mega,
    Giga
}

public class UnitOption
{
    public string Symbol { get; set; } = string.Empty;
    public double Multiplier { get; set; } = 1.0;
    public string DisplayName { get; set; } = string.Empty;

    public UnitOption() { }

    public UnitOption(string symbol, double multiplier, string displayName)
    {
        Symbol = symbol;
        Multiplier = multiplier;
        DisplayName = displayName;
    }
}

public static class EngineeringUnits
{
    public static readonly UnitOption[] ResistanceUnits =
    [
        new("Ω", 1.0, "Ω (欧姆)"),
        new("kΩ", 1e3, "kΩ (千欧)"),
        new("MΩ", 1e6, "MΩ (兆欧)")
    ];

    public static readonly UnitOption[] CapacitanceUnits =
    [
        new("pF", 1e-12, "pF (皮法)"),
        new("nF", 1e-9, "nF (纳法)"),
        new("μF", 1e-6, "μF (微法)"),
        new("mF", 1e-3, "mF (毫法)"),
        new("F", 1.0, "F (法拉)")
    ];

    public static readonly UnitOption[] InductanceUnits =
    [
        new("μH", 1e-6, "μH (微亨)"),
        new("mH", 1e-3, "mH (毫亨)"),
        new("H", 1.0, "H (亨利)")
    ];

    public static readonly UnitOption[] VoltageUnits =
    [
        new("mV", 1e-3, "mV (毫伏)"),
        new("V", 1.0, "V (伏特)"),
        new("kV", 1e3, "kV (千伏)")
    ];

    public static readonly UnitOption[] CurrentUnits =
    [
        new("μA", 1e-6, "μA (微安)"),
        new("mA", 1e-3, "mA (毫安)"),
        new("A", 1.0, "A (安培)")
    ];

    public static readonly UnitOption[] FrequencyUnits =
    [
        new("Hz", 1.0, "Hz (赫兹)"),
        new("kHz", 1e3, "kHz (千赫)"),
        new("MHz", 1e6, "MHz (兆赫)"),
        new("GHz", 1e9, "GHz (吉赫)")
    ];
}
