/**
 * Publication-quality vector electrical symbols (GB/T 4728 · IEC 60617 & ANSI/IEEE style).
 *
 * Sourced & vectorized from ElectricalSymbolLibrary (CC0 1.0 Universal).
 *
 * Every symbol is drawn on a standardized 100×100 viewBox so artwork fills the
 * square symbol box in CircuitNode cleanly and rotates symmetrically.
 *
 * Supports dynamic switching between IEC (国标/国际标准) and ANSI (美标/IEEE)
 * representations (e.g. rectangular vs zig-zag resistors, curved vs looped inductors).
 */

import type { FC, ReactNode } from "react";

const LINE = {
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 2.8,
  strokeLinecap: "round",
  strokeLinejoin: "round",
} as const;

const SOLID = {
  fill: "currentColor",
  stroke: "none",
} as const;

export function SymbolSvg({ children, label }: { children: ReactNode; label: string }) {
  return (
    <svg viewBox="0 0 100 100" role="img" aria-label={label} className="circuit-symbol-svg">
      <g {...LINE}>{children}</g>
    </svg>
  );
}

// ── Passives ─────────────────────────────────────────────────────────────

/** IEC 60617 / GB/T 4728 standard rectangular resistor */
export const ResistorIec: FC = () => (
  <SymbolSvg label="电阻器 (IEC / 国标)">
    <path d="M0 50 H28 M72 50 H100" />
    <rect x="28" y="36" width="44" height="28" rx="1.5" />
  </SymbolSvg>
);

/** ANSI / IEEE standard zig-zag resistor */
export const ResistorAnsi: FC = () => (
  <SymbolSvg label="电阻器 (ANSI / 美标)">
    <path d="M0 50 H22 L29 34 L43 66 L57 34 L71 66 L78 50 H100" />
  </SymbolSvg>
);

/** Potentiometer */
export const ResistorPotentiometer: FC = () => (
  <SymbolSvg label="电位器">
    <path d="M0 50 H28 M72 50 H100" />
    <rect x="28" y="36" width="44" height="28" rx="1.5" />
    <path d="M50 82 V66" />
    <path d="M50 66 L44 74 H56 Z" {...SOLID} />
  </SymbolSvg>
);

/** IEC parallel-plate capacitor */
export const CapacitorIec: FC = () => (
  <SymbolSvg label="电容器 (IEC)">
    <path d="M0 50 H44 M56 50 H100" />
    <path d="M44 24 V76 M56 24 V76" />
  </SymbolSvg>
);

/** Polarized electrolytic capacitor */
export const CapacitorPolarized: FC = () => (
  <SymbolSvg label="极性电解电容器">
    <path d="M0 50 H44 M56 50 H100" />
    <path d="M44 24 V76" />
    <path d="M56 24 C62 40 62 60 56 76" />
    <path d="M30 30 H38 M34 26 V34" strokeWidth={2.2} />
  </SymbolSvg>
);

/** IEC arc-loop inductor */
export const InductorIec: FC = () => (
  <SymbolSvg label="电感器 (IEC)">
    <path d="M0 50 H20 M80 50 H100" />
    <path d="M20 50 A 7.5 7.5 0 0 1 35 50 A 7.5 7.5 0 0 1 50 50 A 7.5 7.5 0 0 1 65 50 A 7.5 7.5 0 0 1 80 50" />
  </SymbolSvg>
);

/** ANSI looped inductor */
export const InductorAnsi: FC = () => (
  <SymbolSvg label="电感器 (ANSI)">
    <path d="M0 50 H18 M82 50 H100" />
    <path d="M18 50 C21 32 31 32 34 50 C29 55 26 55 29 50 C32 32 42 32 45 50 C40 55 37 55 40 50 C43 32 53 32 56 50 C51 55 48 55 51 50 C54 32 64 32 67 50 C62 55 59 55 62 50 C65 32 75 32 82 50" />
  </SymbolSvg>
);

/** Ideal transformer with dual coils, magnetic core and polarity dots */
export const Transformer: FC = () => (
  <SymbolSvg label="理想变压器">
    <path d="M0 30 H36 M0 70 H36" />
    <path d="M36 30 A6.67 6.67 0 0 0 36 43.33 A6.67 6.67 0 0 0 36 56.67 A6.67 6.67 0 0 0 36 70" />
    <path d="M47 20 V80 M53 20 V80" strokeWidth={2.4} />
    <path d="M64 30 A6.67 6.67 0 0 1 64 43.33 A6.67 6.67 0 0 1 64 56.67 A6.67 6.67 0 0 1 64 70" />
    <path d="M64 30 H100 M64 70 H100" />
    <circle cx="18" cy="22" r="3.2" {...SOLID} />
    <circle cx="82" cy="22" r="3.2" {...SOLID} />
  </SymbolSvg>
);

// ── References & Grounds ──────────────────────────────────────────────────

/** Earth Ground (IEC / GB standard 3 decreasing horizontal bars) */
export const GroundEarth: FC = () => (
  <SymbolSvg label="大地参考地">
    <path d="M50 0 V38" />
    <path d="M22 38 H78" />
    <path d="M32 50 H68" />
    <path d="M42 62 H58" />
  </SymbolSvg>
);

/** Signal / Chassis Ground */
export const GroundSignal: FC = () => (
  <SymbolSvg label="信号地">
    <path d="M50 0 V38" />
    <path d="M24 38 H76" />
    <path d="M34 38 L22 54 M50 38 L38 54 M66 38 L54 54" />
  </SymbolSvg>
);

// ── Independent Sources ──────────────────────────────────────────────────

function SourceVerticalLeads() {
  return <path d="M50 0 V27 M50 73 V100" />;
}

/** DC Voltage Source with +/- polarities */
export const VdcSource: FC = () => (
  <SymbolSvg label="直流电压源">
    <SourceVerticalLeads />
    <circle cx="50" cy="50" r="23" />
    <path d="M42 38 H58 M50 30 V46" strokeWidth={2.4} />
    <path d="M42 63 H58" strokeWidth={2.4} />
  </SymbolSvg>
);

/** DC Current Source with directional flow arrow */
export const IdcSource: FC = () => (
  <SymbolSvg label="直流电流源">
    <SourceVerticalLeads />
    <circle cx="50" cy="50" r="23" />
    <path d="M50 63 V38" strokeWidth={2.4} />
    <path d="M50 30 L42 42 H58 Z" {...SOLID} />
  </SymbolSvg>
);

/** AC Small-Signal Voltage Source with sine curve */
export const VacSource: FC = () => (
  <SymbolSvg label="交流小信号电压源">
    <SourceVerticalLeads />
    <circle cx="50" cy="50" r="23" />
    <path d="M35 50 C39 36 45 36 50 50 C55 64 61 64 65 50" strokeWidth={2.6} />
  </SymbolSvg>
);

/** AC Small-Signal Current Source */
export const IacSource: FC = () => (
  <SymbolSvg label="交流小信号电流源">
    <SourceVerticalLeads />
    <circle cx="50" cy="50" r="23" />
    <path d="M50 64 C58 58 42 52 50 46" strokeWidth={2.4} />
    <path d="M50 28 L42 42 H58 Z" {...SOLID} />
  </SymbolSvg>
);

// ── Semiconductors & Diodes ──────────────────────────────────────────────

/** Standard Rectifier Diode */
export const DiodeStandard: FC = () => (
  <SymbolSvg label="二极管">
    <path d="M0 50 H36 M64 50 H100" />
    <path d="M36 30 L64 50 L36 70 Z" />
    <path d="M64 28 V72" />
  </SymbolSvg>
);

/** Zener Diode with bent cathode bar */
export const DiodeZener: FC = () => (
  <SymbolSvg label="稳压二极管 (齐纳)">
    <path d="M0 50 H36 M64 50 H100" />
    <path d="M36 30 L64 50 L36 70 Z" />
    <path d="M72 28 H64 V72 H56" />
  </SymbolSvg>
);

/** Light Emitting Diode (LED) */
export const DiodeLed: FC = () => (
  <SymbolSvg label="发光二极管 (LED)">
    <path d="M0 50 H36 M64 50 H100" />
    <path d="M36 30 L64 50 L36 70 Z" />
    <path d="M64 28 V72" />
    <path d="M54 22 L68 8 M65 8 L68 8 L68 11" strokeWidth={2.2} />
    <path d="M64 28 L78 14 M75 14 L78 14 L78 17" strokeWidth={2.2} />
  </SymbolSvg>
);

// ── Transistors (BJT / FET) ──────────────────────────────────────────────

function BjtSymbol({ pnp, label }: { pnp: boolean; label: string }) {
  const arrow = pnp ? "M54 65 L42 69 L54 73 Z" : "M42 65 L54 69 L42 73 Z";
  return (
    <SymbolSvg label={label}>
      <path d="M0 50 H36" />
      <path d="M36 26 V74" strokeWidth={3.4} />
      <path d="M36 40 L60 22 V0" />
      <path d="M36 60 L60 78 V100" />
      <path d={arrow} transform="rotate(36.87 48 69)" {...SOLID} />
    </SymbolSvg>
  );
}

export const NpnBjt: FC = () => <BjtSymbol pnp={false} label="NPN 三极管" />;
export const PnpBjt: FC = () => <BjtSymbol pnp label="PNP 三极管" />;

function MosfetSymbol({ channel, bulkArrow, label }: {
  channel: "enhancement" | "depletion";
  bulkArrow: "out" | "in" | "none";
  label: string;
}) {
  const channelPath = channel === "enhancement"
    ? "M44 28 V41 M44 45 V55 M44 59 V72"
    : "M44 28 V72";
  const arrowPath = bulkArrow === "out"
    ? "M74 44 V56 L86 50 Z"
    : "M86 44 V56 L74 50 Z";
  return (
    <SymbolSvg label={label}>
      <path d="M0 50 H28" />
      <path d="M28 28 V72" strokeWidth={3.2} />
      <path d={channelPath} strokeWidth={3} />
      <path d="M44 28 H62 V0" />
      <path d="M44 72 H62 V100" />
      <path d="M44 50 H100" />
      {bulkArrow !== "none" && <path d={arrowPath} {...SOLID} />}
    </SymbolSvg>
  );
}

export const NmosEnhancement: FC = () => <MosfetSymbol channel="enhancement" bulkArrow="out" label="N 沟道增强型 MOSFET" />;
export const PmosEnhancement: FC = () => <MosfetSymbol channel="enhancement" bulkArrow="in" label="P 沟道增强型 MOSFET" />;
export const NmosDepletion: FC = () => <MosfetSymbol channel="depletion" bulkArrow="out" label="N 沟道耗尽型 MOSFET" />;
export const PmosDepletion: FC = () => <MosfetSymbol channel="depletion" bulkArrow="in" label="P 沟道耗尽型 MOSFET" />;
export const GenericMosfet: FC = () => <MosfetSymbol channel="enhancement" bulkArrow="none" label="MOSFET" />;

export const Jfet: FC = () => (
  <SymbolSvg label="N 沟道 JFET">
    <path d="M0 50 H38" />
    <path d="M38 30 V70" strokeWidth={3.2} />
    <path d="M62 0 V100" />
    <path d="M46 44 V56 L58 50 Z" {...SOLID} />
  </SymbolSvg>
);

// ── Controlled Sources ────────────────────────────────────────────────────

function ControlledSourceSymbol({ output, control, label }: {
  output: "voltage" | "current";
  control: "voltage" | "current";
  label: string;
}) {
  return (
    <SymbolSvg label={label}>
      <path d="M56 0 V18 M56 82 V100" />
      <path d="M56 18 L86 50 L56 82 L26 50 Z" />
      <path d="M0 34 H38" />
      <path d="M0 66 H38" />
      {output === "voltage" ? (
        <>
          <path d="M50 36 H62 M56 30 V42" strokeWidth={2.4} />
          <path d="M50 64 H62" strokeWidth={2.4} />
        </>
      ) : (
        <>
          <path d="M56 40 V58" strokeWidth={2.4} />
          <path d="M56 68 L49 56 H63 Z" {...SOLID} />
        </>
      )}
      {control === "voltage" ? (
        <>
          <path d="M14 26 H26 M20 20 V32" strokeWidth={2.2} />
          <path d="M14 74 H26" strokeWidth={2.2} />
        </>
      ) : (
        <path d="M38 34 L28 28 V40 Z" {...SOLID} />
      )}
    </SymbolSvg>
  );
}

export const Vcvs: FC = () => <ControlledSourceSymbol output="voltage" control="voltage" label="压控电压源 (VCVS)" />;
export const Vccs: FC = () => <ControlledSourceSymbol output="current" control="voltage" label="压控电流源 (VCCS)" />;
export const Ccvs: FC = () => <ControlledSourceSymbol output="voltage" control="current" label="流控电压源 (CCVS)" />;
export const Cccs: FC = () => <ControlledSourceSymbol output="current" control="current" label="流控电流源 (CCCS)" />;

// ── Amplifiers & Instruments ──────────────────────────────────────────────

export const OpAmp: FC = () => (
  <SymbolSvg label="运算放大器 (Op-Amp)">
    <path d="M0 32 H28 M0 68 H28" />
    <path d="M28 14 V86 L86 50 Z" />
    <path d="M86 50 H100" />
    <path d="M34 32 H42" strokeWidth={2.4} />
    <path d="M34 68 H42 M38 64 V72" strokeWidth={2.4} />
  </SymbolSvg>
);

export const Voltmeter: FC = () => (
  <SymbolSvg label="电压表">
    <SourceVerticalLeads />
    <circle cx="50" cy="50" r="23" />
    <path d="M42 40 L50 60 L58 40" strokeWidth={3} />
  </SymbolSvg>
);

export const Ammeter: FC = () => (
  <SymbolSvg label="电流表">
    <SourceVerticalLeads />
    <circle cx="50" cy="50" r="23" />
    <path d="M42 60 L50 40 L58 60 M44 54 H56" strokeWidth={3} />
  </SymbolSvg>
);

// ── Variant & Lookup Tables ──────────────────────────────────────────────

export const MOSFET_SYMBOLS: Record<string, FC> = {
  n_enhancement: NmosEnhancement,
  p_enhancement: PmosEnhancement,
  n_depletion: NmosDepletion,
  p_depletion: PmosDepletion,
  small_signal: GenericMosfet,
};

export const SYMBOLS: Record<string, FC> = {
  "reference.ground": GroundEarth,
  "passive.resistor": ResistorIec,
  "passive.capacitor": CapacitorIec,
  "passive.inductor": InductorIec,
  "passive.transformer": Transformer,
  "source.v_dc": VdcSource,
  "source.i_dc": IdcSource,
  "source.v_ac_small_signal": VacSource,
  "source.i_ac_small_signal": IacSource,
  "diode.standard": DiodeStandard,
  "bjt.npn": NpnBjt,
  "bjt.pnp": PnpBjt,
  "fet.mosfet": MOSFET_SYMBOLS.n_enhancement,
  "fet.mosfet.nmos": MOSFET_SYMBOLS.n_enhancement,
  "fet.mosfet.pmos": MOSFET_SYMBOLS.p_enhancement,
  "fet.mosfet.generic": MOSFET_SYMBOLS.small_signal,
  "fet.jfet": Jfet,
  "controlled.vcvs": Vcvs,
  "controlled.vccs": Vccs,
  "controlled.ccvs": Ccvs,
  "controlled.cccs": Cccs,
  "amplifier.opamp": OpAmp,
  "meter.voltmeter": Voltmeter,
  "meter.ammeter": Ammeter,
};

export function symbolFor(
  definitionId: string,
  modelVariant?: string,
  standard: "iec" | "ansi" = "iec",
): FC | undefined {
  if (definitionId === "passive.resistor") {
    return standard === "ansi" ? ResistorAnsi : ResistorIec;
  }
  if (definitionId === "passive.inductor") {
    return standard === "ansi" ? InductorAnsi : InductorIec;
  }
  if (definitionId === "passive.capacitor") {
    if (modelVariant === "polarized") return CapacitorPolarized;
    return CapacitorIec;
  }
  if (definitionId === "reference.ground") {
    if (modelVariant === "signal_ground") return GroundSignal;
    return GroundEarth;
  }
  if (definitionId === "diode.standard") {
    if (modelVariant === "zener") return DiodeZener;
    if (modelVariant === "led") return DiodeLed;
    return DiodeStandard;
  }
  if (definitionId === "fet.mosfet" && modelVariant) {
    return MOSFET_SYMBOLS[modelVariant] ?? SYMBOLS[definitionId];
  }
  return SYMBOLS[definitionId];
}
