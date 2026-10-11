/**
 * Unique ID generation for circuit elements.
 *
 * Uses simple counters per definitionId prefix so IDs are readable
 * (e.g. "r1", "r2", "c1", "q1"). Junction and edge IDs use random suffixes.
 */

const counters = new Map<string, number>();

const PREFIX_MAP: Record<string, string> = {
  "passive.resistor": "r",
  "passive.capacitor": "c",
  "passive.inductor": "l",
  "passive.transformer": "t",
  "source.v_dc": "vdc",
  "source.i_dc": "idc",
  "source.v_ac_small_signal": "vac",
  "source.i_ac_small_signal": "iac",
  "controlled.vcvs": "vcvs",
  "controlled.vccs": "vccs",
  "controlled.ccvs": "ccvs",
  "controlled.cccs": "cccs",
  "diode.standard": "d",
  "bjt.npn": "q",
  "bjt.pnp": "q",
  "fet.mosfet": "m",
  "fet.jfet": "j",
  "reference.ground": "gnd",
};

function shortId(): string {
  return Math.random().toString(36).slice(2, 10);
}

export function nextComponentId(definitionId: string): string {
  const prefix = PREFIX_MAP[definitionId] ?? "x";
  const key = `comp:${prefix}`;
  const next = (counters.get(key) ?? 0) + 1;
  counters.set(key, next);
  return `${prefix}${next}`;
}

export function nextJunctionId(): string {
  return `jn_${shortId()}`;
}

export function nextEdgeId(): string {
  return `e_${shortId()}`;
}

export function nextNodeId(): string {
  return `n_${shortId()}`;
}

export function resetCounters(): void {
  counters.clear();
}
