import type { BenchmarkCase } from "@circuit/schema";
import type { BenchmarkItem } from "../types/editor";

import bjtJson from "../../../../benchmarks/examples/bjt-common-emitter-bias.json";
import diodeJson from "../../../../benchmarks/examples/diode-limiter.json";
import mosJson from "../../../../benchmarks/examples/mos-common-source.json";

export const BUILTIN_BENCHMARKS: BenchmarkItem[] = [
  {
    id: bjtJson.id,
    title: "BJT 分压偏置共射放大电路 (DC 静态工作点)",
    category: "双极型三极管 (BJT)",
    difficulty: "典型",
    problemText:
      "针对由集电极电阻 RC、基极电阻 RB、直流偏置电源 VCC 构成的 NPN 共射极偏置电路，在发射极接地条件下推导并求解直流静态工作点 (IB, IC, VCE)，并检验是否工作在放大区。",
    teachingTags: ["BJT", "共射放大", "直流偏置", "静态工作点", "放大区判定"],
    circuitGraph: bjtJson.circuitGraph as unknown as BenchmarkCase["circuitGraph"],
    equationsCount: bjtJson.groundTruth.equations.length,
    groundTruthSummary: "求得 IB = (VCC - VBE)/RB, IC = β·IB, VCE = VCC - IC·RC，并验证 VCE > 0.3V。",
  },
  {
    id: diodeJson.id,
    title: "二极管正向限幅电路 (非线性传输特性)",
    category: "二极管电路 (Diode)",
    difficulty: "基础",
    problemText:
      "已知输入激励信号 Vin、限流电阻 R1、并联限幅二极管 D1 和负载电阻 RL。分析二极管导通与截止临界状态，求解输出电压传输特性与正向限幅方程。",
    teachingTags: ["二极管", "正向限幅", "非线性分析", "理想二极管模型", "分压"],
    circuitGraph: diodeJson.circuitGraph as unknown as BenchmarkCase["circuitGraph"],
    equationsCount: diodeJson.groundTruth.equations.length,
    groundTruthSummary: "当 Vin > 0.7V 时二极管导通限幅在 0.7V；当 Vin ≤ 0.7V 时二极管截止，输出为电阻分压。",
  },
  {
    id: mosJson.id,
    title: "MOSFET 共源极交流小信号放大电路",
    category: "场效应管 (FET/MOS)",
    difficulty: "提高",
    problemText:
      "针对由漏极负载电阻 RD、小信号输入 vin、源极接地的 NMOS 共源极放大电路，利用微变等效模型推导中频频段电压放大倍数 Av、输入阻抗 Rin 及输出阻抗 Rout。",
    teachingTags: ["MOSFET", "共源放大", "微变等效法", "跨导 gm", "电压增益 Av"],
    circuitGraph: mosJson.circuitGraph as unknown as BenchmarkCase["circuitGraph"],
    equationsCount: mosJson.groundTruth.equations.length,
    groundTruthSummary: "求得中频电压放大倍数 Av = -gm·RD，输入电阻 Rin → ∞，输出电阻 Rout = RD。",
  },
];
