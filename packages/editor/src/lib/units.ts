/**
 * International Engineering Units System & Multiplier Utilities.
 *
 * Provides standardized SI prefix multipliers, automatic prefix decomposition,
 * formatted display on canvas, and interactive unit switching in the inspector.
 */

export interface UnitPrefixOption {
  symbol: string;      // e.g. "kΩ", "μF", "mA"
  multiplier: number;  // e.g. 1e3, 1e-6, 1e-3
  label: string;       // e.g. "千欧 (kΩ)"
  isDefault?: boolean;
}

export const ENGINEERING_UNITS: Record<string, UnitPrefixOption[]> = {
  ohm: [
    { symbol: "mΩ", multiplier: 1e-3, label: "毫欧 (mΩ)" },
    { symbol: "Ω", multiplier: 1, label: "欧姆 (Ω)" },
    { symbol: "kΩ", multiplier: 1e3, label: "千欧 (kΩ)", isDefault: true },
    { symbol: "MΩ", multiplier: 1e6, label: "兆欧 (MΩ)" },
    { symbol: "GΩ", multiplier: 1e9, label: "吉欧 (GΩ)" },
  ],
  farad: [
    { symbol: "pF", multiplier: 1e-12, label: "皮法 (pF)" },
    { symbol: "nF", multiplier: 1e-9, label: "纳法 (nF)" },
    { symbol: "μF", multiplier: 1e-6, label: "微法 (μF)", isDefault: true },
    { symbol: "mF", multiplier: 1e-3, label: "毫法 (mF)" },
    { symbol: "F", multiplier: 1, label: "法拉 (F)" },
  ],
  henry: [
    { symbol: "nH", multiplier: 1e-9, label: "纳亨 (nH)" },
    { symbol: "μH", multiplier: 1e-6, label: "微亨 (μH)" },
    { symbol: "mH", multiplier: 1e-3, label: "毫亨 (mH)", isDefault: true },
    { symbol: "H", multiplier: 1, label: "亨利 (H)" },
  ],
  volt: [
    { symbol: "μV", multiplier: 1e-6, label: "微伏 (μV)" },
    { symbol: "mV", multiplier: 1e-3, label: "毫伏 (mV)" },
    { symbol: "V", multiplier: 1, label: "伏特 (V)", isDefault: true },
    { symbol: "kV", multiplier: 1e3, label: "千伏 (kV)" },
  ],
  ampere: [
    { symbol: "pA", multiplier: 1e-12, label: "皮安 (pA)" },
    { symbol: "nA", multiplier: 1e-9, label: "纳安 (nA)" },
    { symbol: "μA", multiplier: 1e-6, label: "微安 (μA)" },
    { symbol: "mA", multiplier: 1e-3, label: "毫安 (mA)", isDefault: true },
    { symbol: "A", multiplier: 1, label: "安培 (A)" },
  ],
  hertz: [
    { symbol: "Hz", multiplier: 1, label: "赫兹 (Hz)" },
    { symbol: "kHz", multiplier: 1e3, label: "千赫 (kHz)", isDefault: true },
    { symbol: "MHz", multiplier: 1e6, label: "兆赫 (MHz)" },
    { symbol: "GHz", multiplier: 1e9, label: "吉赫 (GHz)" },
  ],
  siemens: [
    { symbol: "μS", multiplier: 1e-6, label: "微西 (μS)" },
    { symbol: "mS", multiplier: 1e-3, label: "毫西 (mS)", isDefault: true },
    { symbol: "S", multiplier: 1, label: "西门子 (S)" },
  ],
};

export const FALLBACK_UNITS_BY_DEFINITION: Record<string, string> = {
  "passive.resistor": "ohm",
  "passive.capacitor": "farad",
  "passive.inductor": "henry",
  "source.v_dc": "volt",
  "source.i_dc": "ampere",
  "source.v_ac_small_signal": "volt",
  "source.i_ac_small_signal": "ampere",
  "meter.voltmeter": "volt",
  "meter.ammeter": "ampere",
};

/**
 * Given a base SI unit (e.g. "ohm"), return available prefix options.
 */
export function getUnitOptions(baseUnit: string | undefined): UnitPrefixOption[] | null {
  if (!baseUnit) return null;
  const canonical = baseUnit.toLowerCase();
  return ENGINEERING_UNITS[canonical] ?? null;
}

/**
 * Automatically decompose a base SI number (e.g. 1000) into a user-friendly
 * display number (e.g. 1) and matching prefix option (e.g. "kΩ").
 */
export function decomposeBaseValue(
  baseValue: number,
  baseUnit: string | undefined,
): { displayValue: number; selectedOption: UnitPrefixOption } | null {
  const options = getUnitOptions(baseUnit);
  if (!options || options.length === 0) return null;

  if (baseValue === 0) {
    const defaultOpt = options.find((o) => o.isDefault) ?? options[0];
    return { displayValue: 0, selectedOption: defaultOpt };
  }

  const absVal = Math.abs(baseValue);

  // Find option where 0.999 <= absVal / multiplier < 1000
  let best = options[0];
  let bestDist = Infinity;

  for (const opt of options) {
    const scaled = absVal / opt.multiplier;
    if (scaled >= 0.999 && scaled < 1000) {
      best = opt;
      break;
    }
    const dist = Math.abs(Math.log10(scaled));
    if (dist < bestDist) {
      bestDist = dist;
      best = opt;
    }
  }

  const scaledVal = baseValue / best.multiplier;
  // Clean IEEE 754 float precision (e.g. 9.999999999999999 -> 10)
  const cleanDisplay = Math.round(scaledVal * 1e8) / 1e8;

  return {
    displayValue: cleanDisplay,
    selectedOption: best,
  };
}

/**
 * Format a base SI number for compact display on schematic nodes (e.g. "1kΩ", "10μF").
 */
export function formatCanvasParam(
  baseValue: any,
  baseUnit: string | undefined,
  definitionId?: string,
): string {
  if (typeof baseValue !== "number") {
    return baseUnit ? `${baseValue} ${baseUnit}` : String(baseValue);
  }

  const effectiveUnit = baseUnit || (definitionId ? FALLBACK_UNITS_BY_DEFINITION[definitionId] : undefined);
  const decomposed = decomposeBaseValue(baseValue, effectiveUnit);
  if (decomposed) {
    return `${decomposed.displayValue}${decomposed.selectedOption.symbol}`;
  }

  return effectiveUnit ? `${baseValue}${effectiveUnit}` : `${baseValue}`;
}
