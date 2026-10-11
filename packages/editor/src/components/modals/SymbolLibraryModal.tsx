/**
 * Interactive Vector Electrical Symbol Library Modal.
 *
 * Sourced & vectorized from ElectricalSymbolLibrary (CC0 1.0 Universal) and schemalib.
 * Provides live previews, IEC/ANSI comparisons, instant placement onto canvas,
 * and SVG code copying for academic papers/Inkscape/TikZ.
 */

import React, { useState, useMemo } from "react";
import { useEditorStore } from "../../store/editorStore";
import {
  Sparkles,
  X,
  Search,
  Check,
  Copy,
  PlusCircle,
  ExternalLink,
  Layers,
  BookOpen,
  Filter,
} from "lucide-react";

import {
  ResistorIec,
  ResistorAnsi,
  ResistorPotentiometer,
  CapacitorIec,
  CapacitorPolarized,
  InductorIec,
  InductorAnsi,
  Transformer,
  GroundEarth,
  GroundSignal,
  VdcSource,
  IdcSource,
  VacSource,
  IacSource,
  DiodeStandard,
  DiodeZener,
  DiodeLed,
  NpnBjt,
  PnpBjt,
  NmosEnhancement,
  PmosEnhancement,
  NmosDepletion,
  PmosDepletion,
  GenericMosfet,
  Jfet,
  OpAmp,
  Vcvs,
  Vccs,
  Ccvs,
  Cccs,
  Voltmeter,
  Ammeter,
} from "../nodes/symbols";

export interface SymbolCatalogItem {
  id: string;
  name: string;
  nameEn: string;
  definitionId: string;
  modelVariant?: string;
  standard: "iec" | "ansi" | "both";
  standardCode: string;
  category: "passives" | "sources" | "semiconductors" | "controlled_meter";
  categoryLabel: string;
  portsCount: number;
  description: string;
  component: React.FC;
  svgXml: string;
}

const CATALOG_ITEMS: SymbolCatalogItem[] = [
  // ── 无源元件 (Passives) ──
  {
    id: "resistor-iec",
    name: "电阻器 (IEC / 国标)",
    nameEn: "Resistor (IEC)",
    definitionId: "passive.resistor",
    standard: "iec",
    standardCode: "GB/T 4728.4 · IEC 60617-4",
    category: "passives",
    categoryLabel: "基础无源元件",
    portsCount: 2,
    description: "标准 1:2.5 矩形阻值符号，国内高校教材与 IEC 国际标准默认规范",
    component: ResistorIec,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H28 M72 50 H100" /><rect x="28" y="36" width="44" height="28" rx="1.5" /></g></svg>`,
  },
  {
    id: "resistor-ansi",
    name: "电阻器 (ANSI / 美标)",
    nameEn: "Resistor (ANSI)",
    definitionId: "passive.resistor",
    standard: "ansi",
    standardCode: "ANSI Y32.2 · IEEE 315",
    category: "passives",
    categoryLabel: "基础无源元件",
    portsCount: 2,
    description: "经典 3 峰 2 谷折线锯齿符号，广泛见于美系仿真工具与国际期刊",
    component: ResistorAnsi,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H22 L29 34 L43 66 L57 34 L71 66 L78 50 H100" /></g></svg>`,
  },
  {
    id: "potentiometer",
    name: "电位器 / 可调电阻",
    nameEn: "Potentiometer",
    definitionId: "passive.resistor",
    standard: "both",
    standardCode: "GB/T 4728.4 · IEC 60617-4",
    category: "passives",
    categoryLabel: "基础无源元件",
    portsCount: 2,
    description: "具备抽头滑动调节箭头指示的可调分压电阻器",
    component: ResistorPotentiometer,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H28 M72 50 H100" /><rect x="28" y="36" width="44" height="28" rx="1.5" /><path d="M50 82 V66" /><path d="M50 66 L44 74 H56 Z" fill="currentColor" stroke="none" /></g></svg>`,
  },
  {
    id: "capacitor-iec",
    name: "电容器 (无极性)",
    nameEn: "Capacitor",
    definitionId: "passive.capacitor",
    standard: "both",
    standardCode: "GB/T 4728.4 · IEC 60617-4",
    category: "passives",
    categoryLabel: "基础无源元件",
    portsCount: 2,
    description: "双平板介质间隙对称结构，标准无极性滤波/耦合电容",
    component: CapacitorIec,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H44 M56 50 H100" /><path d="M44 24 V76 M56 24 V76" /></g></svg>`,
  },
  {
    id: "capacitor-polarized",
    name: "极性电解电容器",
    nameEn: "Polarized Capacitor",
    definitionId: "passive.capacitor",
    modelVariant: "polarized",
    standard: "both",
    standardCode: "GB/T 4728.4 · ANSI Y32.2",
    category: "passives",
    categoryLabel: "基础无源元件",
    portsCount: 2,
    description: "正极平直极板带 '+' 号标注，负极圆弧弯曲极板",
    component: CapacitorPolarized,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H44 M56 50 H100" /><path d="M44 24 V76" /><path d="M56 24 C62 40 62 60 56 76" /><path d="M30 30 H38 M34 26 V34" stroke-width="2.2" /></g></svg>`,
  },
  {
    id: "inductor-iec",
    name: "电感器 (IEC / 国标)",
    nameEn: "Inductor (IEC)",
    definitionId: "passive.inductor",
    standard: "iec",
    standardCode: "GB/T 4728.4 · IEC 60617-4",
    category: "passives",
    categoryLabel: "基础无源元件",
    portsCount: 2,
    description: "四节连续平滑半圆弧，严格符合国标 GB 与 IEC 标准图示",
    component: InductorIec,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H20 M80 50 H100" /><path d="M20 50 A 7.5 7.5 0 0 1 35 50 A 7.5 7.5 0 0 1 50 50 A 7.5 7.5 0 0 1 65 50 A 7.5 7.5 0 0 1 80 50" /></g></svg>`,
  },
  {
    id: "inductor-ansi",
    name: "电感器 (ANSI / 美标)",
    nameEn: "Inductor (ANSI)",
    definitionId: "passive.inductor",
    standard: "ansi",
    standardCode: "ANSI Y32.2 · IEEE 315",
    category: "passives",
    categoryLabel: "基础无源元件",
    portsCount: 2,
    description: "多圈螺旋重叠环状线圈，美标电气图与 SPICE 风格",
    component: InductorAnsi,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H18 M82 50 H100" /><path d="M18 50 C21 32 31 32 34 50 C29 55 26 55 29 50 C32 32 42 32 45 50 C40 55 37 55 40 50 C43 32 53 32 56 50 C51 55 48 55 51 50 C54 32 64 32 67 50 C62 55 59 55 62 50 C65 32 75 32 82 50" /></g></svg>`,
  },
  {
    id: "transformer",
    name: "理想变压器",
    nameEn: "Transformer",
    definitionId: "passive.transformer",
    standard: "both",
    standardCode: "GB/T 4728.4 · IEC 60617-4",
    category: "passives",
    categoryLabel: "基础无源元件",
    portsCount: 4,
    description: "原副边双绕组、双磁芯隔离线及同名端同极性圆点指示",
    component: Transformer,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 30 H36 M0 70 H36" /><path d="M36 30 A6.67 6.67 0 0 0 36 43.33 A6.67 6.67 0 0 0 36 56.67 A6.67 6.67 0 0 0 36 70" /><path d="M47 20 V80 M53 20 V80" stroke-width="2.4" /><path d="M64 30 A6.67 6.67 0 0 1 64 43.33 A6.67 6.67 0 0 1 64 56.67 A6.67 6.67 0 0 1 64 70" /><path d="M64 30 H100 M64 70 H100" /><circle cx="18" cy="22" r="3.2" fill="currentColor" stroke="none" /><circle cx="82" cy="22" r="3.2" fill="currentColor" stroke="none" /></g></svg>`,
  },

  // ── 激励源与基准 (Sources & Ground) ──
  {
    id: "ground-earth",
    name: "大地参考点 (Earth Ground)",
    nameEn: "Earth Ground",
    definitionId: "reference.ground",
    standard: "both",
    standardCode: "GB/T 4728.3 · IEC 60617-2",
    category: "sources",
    categoryLabel: "激励源与接地",
    portsCount: 1,
    description: "标准三层等距递减水平横线，电路节点电位 0V 绝对参考点",
    component: GroundEarth,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M50 0 V38" /><path d="M22 38 H78" /><path d="M32 50 H68" /><path d="M42 62 H58" /></g></svg>`,
  },
  {
    id: "ground-signal",
    name: "信号地 / 机壳地 (Signal Ground)",
    nameEn: "Signal Ground",
    definitionId: "reference.ground",
    modelVariant: "signal_ground",
    standard: "both",
    standardCode: "GB/T 4728.3 · ANSI Y32.2",
    category: "sources",
    categoryLabel: "激励源与接地",
    portsCount: 1,
    description: "45° 倾斜梳状接地，通常用于弱电信号回路与仪器机壳屏蔽",
    component: GroundSignal,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M50 0 V38" /><path d="M24 38 H76" /><path d="M34 38 L22 54 M50 38 L38 54 M66 38 L54 54" /></g></svg>`,
  },
  {
    id: "source-vdc",
    name: "直流独立电压源",
    nameEn: "DC Voltage Source",
    definitionId: "source.v_dc",
    standard: "both",
    standardCode: "GB/T 4728.6 · IEC 60617-6",
    category: "sources",
    categoryLabel: "激励源与接地",
    portsCount: 2,
    description: "圆形带明确高精度 '+' 与 '-' 极性标号的理想恒压源",
    component: VdcSource,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M50 0 V27 M50 73 V100" /><circle cx="50" cy="50" r="23" /><path d="M42 38 H58 M50 30 V46" stroke-width="2.4" /><path d="M42 63 H58" stroke-width="2.4" /></g></svg>`,
  },
  {
    id: "source-idc",
    name: "直流独立电流源",
    nameEn: "DC Current Source",
    definitionId: "source.i_dc",
    standard: "both",
    standardCode: "GB/T 4728.6 · IEC 60617-6",
    category: "sources",
    categoryLabel: "激励源与接地",
    portsCount: 2,
    description: "圆形带正向实心电流指向箭头的理想恒流源",
    component: IdcSource,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M50 0 V27 M50 73 V100" /><circle cx="50" cy="50" r="23" /><path d="M50 63 V38" stroke-width="2.4" /><path d="M50 30 L42 42 H58 Z" fill="currentColor" stroke="none" /></g></svg>`,
  },
  {
    id: "source-vac",
    name: "交流小信号电压源",
    nameEn: "AC Voltage Source",
    definitionId: "source.v_ac_small_signal",
    standard: "both",
    standardCode: "GB/T 4728.6 · IEC 60617-6",
    category: "sources",
    categoryLabel: "激励源与接地",
    portsCount: 2,
    description: "正弦信号波纹源，用于稳态交流分析与频域相量计算",
    component: VacSource,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M50 0 V27 M50 73 V100" /><circle cx="50" cy="50" r="23" /><path d="M35 50 C39 36 45 36 50 50 C55 64 61 64 65 50" stroke-width="2.6" /></g></svg>`,
  },
  {
    id: "source-iac",
    name: "交流小信号电流源",
    nameEn: "AC Current Source",
    definitionId: "source.i_ac_small_signal",
    standard: "both",
    standardCode: "GB/T 4728.6 · IEC 60617-6",
    category: "sources",
    categoryLabel: "激励源与接地",
    portsCount: 2,
    description: "复合正弦信号与流向箭头的交流激励电流源",
    component: IacSource,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M50 0 V27 M50 73 V100" /><circle cx="50" cy="50" r="23" /><path d="M50 64 C58 58 42 52 50 46" stroke-width="2.4" /><path d="M50 28 L42 42 H58 Z" fill="currentColor" stroke="none" /></g></svg>`,
  },

  // ── 半导体分立器件 (Semiconductors) ──
  {
    id: "diode-standard",
    name: "晶体整流二极管",
    nameEn: "Rectifier Diode",
    definitionId: "diode.standard",
    standard: "both",
    standardCode: "GB/T 4728.7 · IEC 60617-7",
    category: "semiconductors",
    categoryLabel: "半导体器件",
    portsCount: 2,
    description: "正向导通三角实体与阴极截止竖条，单向导电开关元件",
    component: DiodeStandard,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H36 M64 50 H100" /><path d="M36 30 L64 50 L36 70 Z" /><path d="M64 28 V72" /></g></svg>`,
  },
  {
    id: "diode-zener",
    name: "齐纳稳压二极管",
    nameEn: "Zener Diode",
    definitionId: "diode.standard",
    modelVariant: "zener",
    standard: "both",
    standardCode: "GB/T 4728.7 · IEC 60617-7",
    category: "semiconductors",
    categoryLabel: "半导体器件",
    portsCount: 2,
    description: "阴极两端带特征折角的稳压管，利用反向击穿特性稳定直流电压",
    component: DiodeZener,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H36 M64 50 H100" /><path d="M36 30 L64 50 L36 70 Z" /><path d="M72 28 H64 V72 H56" /></g></svg>`,
  },
  {
    id: "diode-led",
    name: "发光二极管 (LED)",
    nameEn: "Light Emitting Diode",
    definitionId: "diode.standard",
    modelVariant: "led",
    standard: "both",
    standardCode: "GB/T 4728.7 · IEC 60617-7",
    category: "semiconductors",
    categoryLabel: "半导体器件",
    portsCount: 2,
    description: "向外辐射的双箭头指示光子释放的发光二极管",
    component: DiodeLed,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H36 M64 50 H100" /><path d="M36 30 L64 50 L36 70 Z" /><path d="M64 28 V72" /><path d="M54 22 L68 8 M65 8 L68 8 L68 11" stroke-width="2.2" /><path d="M64 28 L78 14 M75 14 L78 14 L78 17" stroke-width="2.2" /></g></svg>`,
  },
  {
    id: "bjt-npn",
    name: "NPN 双极型晶体管",
    nameEn: "NPN BJT",
    definitionId: "bjt.npn",
    standard: "both",
    standardCode: "GB/T 4728.7 · IEEE 315",
    category: "semiconductors",
    categoryLabel: "半导体器件",
    portsCount: 3,
    description: "发射极向外箭头，经典的电流控制型电流放大器件",
    component: NpnBjt,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H36" /><path d="M36 26 V74" stroke-width="3.4" /><path d="M36 40 L60 22 V0" /><path d="M36 60 L60 78 V100" /><path d="M42 65 L54 69 L42 73 Z" transform="rotate(36.87 48 69)" fill="currentColor" stroke="none" /></g></svg>`,
  },
  {
    id: "bjt-pnp",
    name: "PNP 双极型晶体管",
    nameEn: "PNP BJT",
    definitionId: "bjt.pnp",
    standard: "both",
    standardCode: "GB/T 4728.7 · IEEE 315",
    category: "semiconductors",
    categoryLabel: "半导体器件",
    portsCount: 3,
    description: "发射极朝向基极箭头，互补推挽放大电路基础",
    component: PnpBjt,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H36" /><path d="M36 26 V74" stroke-width="3.4" /><path d="M36 40 L60 22 V0" /><path d="M36 60 L60 78 V100" /><path d="M54 65 L42 69 L54 73 Z" transform="rotate(36.87 48 69)" fill="currentColor" stroke="none" /></g></svg>`,
  },
  {
    id: "mosfet-nmos-enh",
    name: "NMOS 增强型场效应管",
    nameEn: "N-MOSFET (Enhancement)",
    definitionId: "fet.mosfet",
    modelVariant: "n_enhancement",
    standard: "both",
    standardCode: "GB/T 4728.7 · IEEE 315",
    category: "semiconductors",
    categoryLabel: "半导体器件",
    portsCount: 4,
    description: "三段断续沟道条表示增强型，衬底箭头向外 (P-substrate)",
    component: NmosEnhancement,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H28" /><path d="M28 28 V72" stroke-width="3.2" /><path d="M44 28 V41 M44 45 V55 M44 59 V72" stroke-width="3" /><path d="M44 28 H62 V0" /><path d="M44 72 H62 V100" /><path d="M44 50 H100" /><path d="M74 44 V56 L86 50 Z" fill="currentColor" stroke="none" /></g></svg>`,
  },
  {
    id: "mosfet-pmos-enh",
    name: "PMOS 增强型场效应管",
    nameEn: "P-MOSFET (Enhancement)",
    definitionId: "fet.mosfet",
    modelVariant: "p_enhancement",
    standard: "both",
    standardCode: "GB/T 4728.7 · IEEE 315",
    category: "semiconductors",
    categoryLabel: "半导体器件",
    portsCount: 4,
    description: "三段断续沟道条，衬底箭头向内 (N-well)",
    component: PmosEnhancement,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H28" /><path d="M28 28 V72" stroke-width="3.2" /><path d="M44 28 V41 M44 45 V55 M44 59 V72" stroke-width="3" /><path d="M44 28 H62 V0" /><path d="M44 72 H62 V100" /><path d="M44 50 H100" /><path d="M86 44 V56 L74 50 Z" fill="currentColor" stroke="none" /></g></svg>`,
  },
  {
    id: "mosfet-depletion",
    name: "MOSFET 耗尽型",
    nameEn: "MOSFET (Depletion)",
    definitionId: "fet.mosfet",
    modelVariant: "n_depletion",
    standard: "both",
    standardCode: "GB/T 4728.7 · IEEE 315",
    category: "semiconductors",
    categoryLabel: "半导体器件",
    portsCount: 4,
    description: "连续连续实线厚沟道条，无需栅极偏压即预置导电沟道",
    component: NmosDepletion,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H28" /><path d="M28 28 V72" stroke-width="3.2" /><path d="M44 28 V72" stroke-width="3" /><path d="M44 28 H62 V0" /><path d="M44 72 H62 V100" /><path d="M44 50 H100" /><path d="M74 44 V56 L86 50 Z" fill="currentColor" stroke="none" /></g></svg>`,
  },
  {
    id: "fet-jfet",
    name: "结型场效应管 (JFET)",
    nameEn: "JFET",
    definitionId: "fet.jfet",
    standard: "both",
    standardCode: "GB/T 4728.7 · IEEE 315",
    category: "semiconductors",
    categoryLabel: "半导体器件",
    portsCount: 3,
    description: "PN结反偏耗尽层调控沟道宽度的电压控制型器件",
    component: Jfet,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 50 H38" /><path d="M38 30 V70" stroke-width="3.2" /><path d="M62 0 V100" /><path d="M46 44 V56 L58 50 Z" fill="currentColor" stroke="none" /></g></svg>`,
  },

  // ── 模拟集成、受控源与仪表 (Controlled, Amplifiers & Meters) ──
  {
    id: "amplifier-opamp",
    name: "通用运算放大器 (Op-Amp)",
    nameEn: "Operational Amplifier",
    definitionId: "amplifier.opamp",
    standard: "both",
    standardCode: "GB/T 4728.5 · IEC 60617-5",
    category: "controlled_meter",
    categoryLabel: "集成运放与仪表",
    portsCount: 3,
    description: "正三角高增益放大器，同相端 '+'、反相端 '-' 与单端输出引脚",
    component: OpAmp,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M0 32 H28 M0 68 H28" /><path d="M28 14 V86 L86 50 Z" /><path d="M86 50 H100" /><path d="M34 32 H42" stroke-width="2.4" /><path d="M34 68 H42 M38 64 V72" stroke-width="2.4" /></g></svg>`,
  },
  {
    id: "controlled-vcvs",
    name: "压控电压源 (VCVS)",
    nameEn: "VCVS",
    definitionId: "controlled.vcvs",
    standard: "both",
    standardCode: "线性受控源经典模型",
    category: "controlled_meter",
    categoryLabel: "集成运放与仪表",
    portsCount: 4,
    description: "四端菱形受控源，输入为高阻抗开路控制电压端，输出受控电压",
    component: Vcvs,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M56 0 V18 M56 82 V100" /><path d="M56 18 L86 50 L56 82 L26 50 Z" /><path d="M0 34 H38 M0 66 H38" /><path d="M50 36 H62 M56 30 V42" stroke-width="2.4" /><path d="M50 64 H62" stroke-width="2.4" /><path d="M14 26 H26 M20 20 V32" stroke-width="2.2" /><path d="M14 74 H26" stroke-width="2.2" /></g></svg>`,
  },
  {
    id: "controlled-vccs",
    name: "压控电流源 (VCCS)",
    nameEn: "VCCS",
    definitionId: "controlled.vccs",
    standard: "both",
    standardCode: "线性受控源经典模型",
    category: "controlled_meter",
    categoryLabel: "集成运放与仪表",
    portsCount: 4,
    description: "四端菱形受控源，输出受控电流大小正比于输入控制电压 (跨导 gm)",
    component: Vccs,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M56 0 V18 M56 82 V100" /><path d="M56 18 L86 50 L56 82 L26 50 Z" /><path d="M0 34 H38 M0 66 H38" /><path d="M56 40 V58" stroke-width="2.4" /><path d="M56 68 L49 56 H63 Z" fill="currentColor" stroke="none" /><path d="M14 26 H26 M20 20 V32" stroke-width="2.2" /><path d="M14 74 H26" stroke-width="2.2" /></g></svg>`,
  },
  {
    id: "controlled-ccvs",
    name: "流控电压源 (CCVS)",
    nameEn: "CCVS",
    definitionId: "controlled.ccvs",
    standard: "both",
    standardCode: "线性受控源经典模型",
    category: "controlled_meter",
    categoryLabel: "集成运放与仪表",
    portsCount: 4,
    description: "输入检测支路短路环流，输出受控电压 (互阻 r)",
    component: Ccvs,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M56 0 V18 M56 82 V100" /><path d="M56 18 L86 50 L56 82 L26 50 Z" /><path d="M0 34 H38 M0 66 H38" /><path d="M50 36 H62 M56 30 V42" stroke-width="2.4" /><path d="M50 64 H62" stroke-width="2.4" /><path d="M38 34 L28 28 V40 Z" fill="currentColor" stroke="none" /></g></svg>`,
  },
  {
    id: "controlled-cccs",
    name: "流控电流源 (CCCS)",
    nameEn: "CCCS",
    definitionId: "controlled.cccs",
    standard: "both",
    standardCode: "线性受控源经典模型",
    category: "controlled_meter",
    categoryLabel: "集成运放与仪表",
    portsCount: 4,
    description: "输入支路电流放大为输出受控电流 (电流增益 β)",
    component: Cccs,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M56 0 V18 M56 82 V100" /><path d="M56 18 L86 50 L56 82 L26 50 Z" /><path d="M0 34 H38 M0 66 H38" /><path d="M56 40 V58" stroke-width="2.4" /><path d="M56 68 L49 56 H63 Z" fill="currentColor" stroke="none" /><path d="M38 34 L28 28 V40 Z" fill="currentColor" stroke="none" /></g></svg>`,
  },
  {
    id: "meter-voltmeter",
    name: "模拟电压表",
    nameEn: "Voltmeter",
    definitionId: "meter.voltmeter",
    standard: "both",
    standardCode: "GB/T 4728.8 · IEC 60617-8",
    category: "controlled_meter",
    categoryLabel: "集成运放与仪表",
    portsCount: 2,
    description: "圆形标注 'V'，理想无限大内阻测量并联支路电压",
    component: Voltmeter,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M50 0 V27 M50 73 V100" /><circle cx="50" cy="50" r="23" /><path d="M42 40 L50 60 L58 40" stroke-width="3" /></g></svg>`,
  },
  {
    id: "meter-ammeter",
    name: "模拟电流表",
    nameEn: "Ammeter",
    definitionId: "meter.ammeter",
    standard: "both",
    standardCode: "GB/T 4728.8 · IEC 60617-8",
    category: "controlled_meter",
    categoryLabel: "集成运放与仪表",
    portsCount: 2,
    description: "圆形标注 'A'，理想零内阻串联测量支路电流",
    component: Ammeter,
    svgXml: `<svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg"><g fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="M50 0 V27 M50 73 V100" /><circle cx="50" cy="50" r="23" /><path d="M42 60 L50 40 L58 60 M44 54 H56" stroke-width="3" /></g></svg>`,
  },
];

export default function SymbolLibraryModal() {
  const {
    symbolLibraryOpen,
    setSymbolLibraryOpen,
    symbolStandard,
    setSymbolStandard,
    addDefaultComponent,
    addToast,
  } = useEditorStore();

  const [query, setQuery] = useState("");
  const [activeCategory, setActiveCategory] = useState<string>("all");
  const [copiedId, setCopiedId] = useState<string | null>(null);

  if (!symbolLibraryOpen) return null;

  const filteredItems = CATALOG_ITEMS.filter((item) => {
    // category filter
    if (activeCategory !== "all" && item.category !== activeCategory) {
      return false;
    }
    // search filter
    if (query.trim()) {
      const q = query.toLowerCase();
      const matchName = item.name.toLowerCase().includes(q);
      const matchEn = item.nameEn.toLowerCase().includes(q);
      const matchDesc = item.description.toLowerCase().includes(q);
      const matchCode = item.standardCode.toLowerCase().includes(q);
      return matchName || matchEn || matchDesc || matchCode;
    }
    return true;
  });

  const handleCopySvg = (item: SymbolCatalogItem) => {
    navigator.clipboard.writeText(item.svgXml).then(
      () => {
        setCopiedId(item.id);
        addToast({
          type: "success",
          title: "SVG 源码已复制到剪贴板",
          message: `${item.name} (${item.standardCode})`,
          duration: 2500,
        });
        setTimeout(() => setCopiedId(null), 2000);
      },
      () => {
        addToast({
          type: "error",
          title: "复制失败",
          message: "无法访问系统剪贴板",
        });
      },
    );
  };

  const handleAddComponent = (item: SymbolCatalogItem) => {
    addDefaultComponent(item.definitionId);
    addToast({
      type: "success",
      title: "已放置到原理图画布",
      message: `${item.name} (端口数: ${item.portsCount})`,
      duration: 2200,
    });
  };

  return (
    <div className="modal-backdrop" onClick={() => setSymbolLibraryOpen(false)}>
      <div
        className="modal-dialog symbol-library-modal"
        onClick={(e) => e.stopPropagation()}
        style={{ width: "min(1080px, 95vw)", maxHeight: "90vh" }}
      >
        {/* Modal Header */}
        <div className="modal-header">
          <div className="modal-title-group">
            <div className="modal-icon-badge" style={{ backgroundColor: "#e0f2fe", color: "#0284c7" }}>
              <Sparkles size={20} />
            </div>
            <div>
              <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                <h2 className="modal-title">矢量电路符号库 (Electrical Symbol Library)</h2>
                <span
                  style={{
                    fontSize: "11px",
                    fontWeight: 600,
                    padding: "2px 8px",
                    borderRadius: "12px",
                    backgroundColor: "#ecfdf5",
                    color: "#059669",
                    border: "1px solid #a7f3d0",
                  }}
                >
                  CC0 1.0 Universal
                </span>
              </div>
              <p className="modal-subtitle">
                基于开源标准高质量矢量电气符号库，支持 IEC 60617 (国标 GB/T 4728) 与 ANSI/IEEE 规范实时切换
              </p>
            </div>
          </div>
          <button className="modal-close-btn" onClick={() => setSymbolLibraryOpen(false)} title="关闭">
            <X size={18} />
          </button>
        </div>

        {/* Filter & Standard Switch Bar */}
        <div
          style={{
            padding: "12px 24px",
            borderBottom: "1px solid var(--border-light)",
            backgroundColor: "#f8fafc",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: "12px",
          }}
        >
          {/* Category Tabs */}
          <div style={{ display: "flex", gap: "6px", flexWrap: "wrap" }}>
            {[
              { id: "all", label: "全部符号", count: CATALOG_ITEMS.length },
              { id: "passives", label: "无源元件", count: 8 },
              { id: "sources", label: "激励源与接地", count: 6 },
              { id: "semiconductors", label: "半导体器件", count: 9 },
              { id: "controlled_meter", label: "集成运放与仪表", count: 7 },
            ].map((cat) => (
              <button
                key={cat.id}
                onClick={() => setActiveCategory(cat.id)}
                style={{
                  padding: "5px 12px",
                  fontSize: "12.5px",
                  fontWeight: activeCategory === cat.id ? 600 : 500,
                  borderRadius: "6px",
                  border: activeCategory === cat.id ? "1px solid #bae6fd" : "1px solid #e2e8f0",
                  backgroundColor: activeCategory === cat.id ? "#ffffff" : "transparent",
                  color: activeCategory === cat.id ? "#0284c7" : "#475569",
                  boxShadow: activeCategory === cat.id ? "0 1px 3px rgba(0,0,0,0.05)" : "none",
                  transition: "all 0.15s",
                }}
              >
                {cat.label} ({cat.count})
              </button>
            ))}
          </div>

          {/* Right Controls: Search & Global Standard Switch */}
          <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
            {/* Search Box */}
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "6px",
                padding: "4px 10px",
                borderRadius: "6px",
                border: "1px solid #cbd5e1",
                backgroundColor: "#ffffff",
                width: "210px",
              }}
            >
              <Search size={14} style={{ color: "#94a3b8" }} />
              <input
                type="text"
                placeholder="搜索符号名称或标准..."
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                style={{
                  border: "none",
                  outline: "none",
                  fontSize: "12px",
                  width: "100%",
                  color: "#1e293b",
                }}
              />
              {query && (
                <button onClick={() => setQuery("")} style={{ color: "#94a3b8" }}>
                  <X size={12} />
                </button>
              )}
            </div>

            {/* Global Standard Switch */}
            <div
              style={{
                display: "flex",
                alignItems: "center",
                backgroundColor: "#e2e8f0",
                padding: "2px",
                borderRadius: "6px",
                gap: "2px",
              }}
            >
              <button
                onClick={() => setSymbolStandard("iec")}
                style={{
                  padding: "4px 10px",
                  fontSize: "12px",
                  fontWeight: symbolStandard === "iec" ? 600 : 500,
                  borderRadius: "5px",
                  border: "none",
                  backgroundColor: symbolStandard === "iec" ? "#ffffff" : "transparent",
                  color: symbolStandard === "iec" ? "#0284c7" : "#64748b",
                  boxShadow: symbolStandard === "iec" ? "0 1px 2px rgba(0,0,0,0.06)" : "none",
                  transition: "all 0.15s",
                }}
                title="设置默认使用 IEC (国标 GB/T 4728) 矩形电阻与弧形电感"
              >
                IEC (国标)
              </button>
              <button
                onClick={() => setSymbolStandard("ansi")}
                style={{
                  padding: "4px 10px",
                  fontSize: "12px",
                  fontWeight: symbolStandard === "ansi" ? 600 : 500,
                  borderRadius: "5px",
                  border: "none",
                  backgroundColor: symbolStandard === "ansi" ? "#ffffff" : "transparent",
                  color: symbolStandard === "ansi" ? "#0284c7" : "#64748b",
                  boxShadow: symbolStandard === "ansi" ? "0 1px 2px rgba(0,0,0,0.06)" : "none",
                  transition: "all 0.15s",
                }}
                title="设置默认使用 ANSI (美标 IEEE 315) 锯齿电阻与线圈电感"
              >
                ANSI (美标)
              </button>
            </div>
          </div>
        </div>

        {/* Modal Body: Cards Grid */}
        <div
          className="modal-body"
          style={{
            padding: "20px 24px",
            overflowY: "auto",
            flex: 1,
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(235px, 1fr))",
            gap: "16px",
            backgroundColor: "#f8fafc",
          }}
        >
          {filteredItems.length === 0 ? (
            <div
              style={{
                gridColumn: "1 / -1",
                padding: "48px 0",
                textAlign: "center",
                color: "#64748b",
              }}
            >
              <Filter size={32} style={{ margin: "0 auto 12px", opacity: 0.5 }} />
              <div style={{ fontSize: "14px", fontWeight: 500 }}>未找到匹配的电气符号</div>
              <div style={{ fontSize: "12px", color: "#94a3b8", marginTop: "4px" }}>
                请尝试更换搜索关键词或选择“全部符号”
              </div>
            </div>
          ) : (
            filteredItems.map((item) => {
              const SymbolComp = item.component;
              const isIec = item.standard === "iec";
              const isAnsi = item.standard === "ansi";

              return (
                <div
                  key={item.id}
                  style={{
                    backgroundColor: "#ffffff",
                    borderRadius: "10px",
                    border: "1px solid #e2e8f0",
                    display: "flex",
                    flexDirection: "column",
                    overflow: "hidden",
                    transition: "transform 0.15s, box-shadow 0.15s, border-color 0.15s",
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.borderColor = "#bae6fd";
                    e.currentTarget.style.boxShadow = "0 4px 12px rgba(14, 165, 233, 0.08)";
                    e.currentTarget.style.transform = "translateY(-1px)";
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.borderColor = "#e2e8f0";
                    e.currentTarget.style.boxShadow = "none";
                    e.currentTarget.style.transform = "translateY(0)";
                  }}
                >
                  {/* Symbol Vector Preview Box */}
                  <div
                    style={{
                      height: "110px",
                      backgroundColor: "#f1f5f9",
                      backgroundImage:
                        "radial-gradient(#cbd5e1 0.75px, transparent 0.75px)",
                      backgroundSize: "12px 12px",
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "center",
                      position: "relative",
                      borderBottom: "1px solid #e2e8f0",
                    }}
                  >
                    <div style={{ width: "70px", height: "70px", color: "#1e293b" }}>
                      <SymbolComp />
                    </div>

                    {/* Standard Tag Badge */}
                    <span
                      style={{
                        position: "absolute",
                        top: "8px",
                        right: "8px",
                        fontSize: "10px",
                        fontWeight: 600,
                        padding: "2px 6px",
                        borderRadius: "4px",
                        backgroundColor: isIec
                          ? "#dbeafe"
                          : isAnsi
                          ? "#fef3c7"
                          : "#f1f5f9",
                        color: isIec ? "#1e40af" : isAnsi ? "#92400e" : "#475569",
                        border: `1px solid ${
                          isIec ? "#bfdbfe" : isAnsi ? "#fde68a" : "#e2e8f0"
                        }`,
                      }}
                    >
                      {isIec ? "IEC 国标" : isAnsi ? "ANSI 美标" : "通用规范"}
                    </span>

                    {/* Port count badge */}
                    <span
                      style={{
                        position: "absolute",
                        bottom: "6px",
                        left: "8px",
                        fontSize: "10px",
                        color: "#64748b",
                        fontWeight: 500,
                      }}
                    >
                      {item.portsCount} 端子引脚
                    </span>
                  </div>

                  {/* Info details */}
                  <div style={{ padding: "12px 14px", flex: 1, display: "flex", flexDirection: "column" }}>
                    <div style={{ fontWeight: 600, fontSize: "13px", color: "#0f172a" }}>
                      {item.name}
                    </div>
                    <div
                      style={{
                        fontSize: "11px",
                        fontFamily: "var(--font-mono)",
                        color: "#0284c7",
                        marginTop: "2px",
                      }}
                    >
                      {item.standardCode}
                    </div>
                    <p
                      style={{
                        fontSize: "11.5px",
                        color: "#64748b",
                        lineHeight: "1.45",
                        marginTop: "6px",
                        flex: 1,
                      }}
                    >
                      {item.description}
                    </p>

                    {/* Action Buttons */}
                    <div
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: "8px",
                        marginTop: "12px",
                        paddingTop: "10px",
                        borderTop: "1px solid #f1f5f9",
                      }}
                    >
                      <button
                        onClick={() => handleAddComponent(item)}
                        style={{
                          flex: 1,
                          display: "inline-flex",
                          alignItems: "center",
                          justifyContent: "center",
                          gap: "5px",
                          padding: "5px 10px",
                          borderRadius: "6px",
                          backgroundColor: "#0ea5e9",
                          color: "#ffffff",
                          fontSize: "11.5px",
                          fontWeight: 500,
                          transition: "background-color 0.15s",
                        }}
                        onMouseEnter={(e) => (e.currentTarget.style.backgroundColor = "#0284c7")}
                        onMouseLeave={(e) => (e.currentTarget.style.backgroundColor = "#0ea5e9")}
                        title="放置到原理图画布中央"
                      >
                        <PlusCircle size={13} /> 放置到电路
                      </button>

                      <button
                        onClick={() => handleCopySvg(item)}
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          justifyContent: "center",
                          gap: "4px",
                          padding: "5px 8px",
                          borderRadius: "6px",
                          backgroundColor: copiedId === item.id ? "#dcfce7" : "#f1f5f9",
                          color: copiedId === item.id ? "#15803d" : "#475569",
                          border: "1px solid #cbd5e1",
                          fontSize: "11.5px",
                          fontWeight: 500,
                          transition: "all 0.15s",
                        }}
                        title="复制标准化 100x100 SVG 代码，用于论文或绘图"
                      >
                        {copiedId === item.id ? <Check size={13} /> : <Copy size={13} />}
                        {copiedId === item.id ? "已复制" : "SVG"}
                      </button>
                    </div>
                  </div>
                </div>
              );
            })
          )}
        </div>

        {/* Modal Footer: Attribution & Specifications */}
        <div
          className="modal-footer"
          style={{
            padding: "14px 24px",
            borderTop: "1px solid var(--border-light)",
            backgroundColor: "#ffffff",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "12px", color: "#64748b" }}>
            <BookOpen size={15} style={{ color: "#0ea5e9" }} />
            <span>
              符号库致谢：<strong>basverdoes/ElectricalSymbolLibrary</strong> (CC0 1.0) 及 <strong>pacuserra/schemalib</strong>。
              引脚端子以 (0,50), (100,50), (50,0), (50,100) 统一绝对坐标锚定，旋转与导线连接零漂移。
            </span>
          </div>

          <button
            onClick={() => setSymbolLibraryOpen(false)}
            style={{
              padding: "6px 16px",
              borderRadius: "6px",
              backgroundColor: "#f1f5f9",
              border: "1px solid #cbd5e1",
              fontSize: "12.5px",
              fontWeight: 500,
              color: "#334155",
            }}
          >
            完成并关闭
          </button>
        </div>
      </div>
    </div>
  );
}
