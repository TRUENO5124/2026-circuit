/**
 * Component palette sidebar with instant fuzzy search, category filtering,
 * SVG symbols, click-to-add, and drag-and-drop.
 */

import React, { useMemo, useState } from "react";
import { componentDefinitions } from "@circuit/components/definitions";
import { useEditorStore } from "../../store/editorStore";
import { SYMBOLS, symbolFor } from "../nodes/symbols/index";
import { Search, Plus, Layers, Sparkles, Filter, ChevronRight, ChevronDown } from "lucide-react";

type CategoryFilter = "all" | "passive" | "source" | "semiconductor" | "controlled" | "reference";

interface CategoryMeta {
  id: CategoryFilter;
  label: string;
  icon?: string;
  families: string[];
}

const CATEGORIES: CategoryMeta[] = [
  { id: "all", label: "全部器件", families: [] },
  { id: "passive", label: "无源器件", families: ["passive"] },
  { id: "source", label: "激励电源", families: ["independent_source"] },
  { id: "semiconductor", label: "半导体管", families: ["bjt", "fet", "diode"] },
  { id: "controlled", label: "受控源", families: ["controlled_source"] },
  { id: "reference", label: "参考地", families: ["reference"] },
];

const CHINESE_NAMES: Record<string, { zh: string; spec: string; desc: string }> = {
  "reference.ground": { zh: "参考地 (GND)", spec: "0V", desc: "基准零电位参考节点" },
  "passive.resistor": { zh: "电阻器 (Resistor)", spec: "1kΩ", desc: "欧姆定律无源线性电阻" },
  "passive.capacitor": { zh: "电容器 (Capacitor)", spec: "10μF", desc: "隔直通交阻容耦合元件" },
  "passive.inductor": { zh: "电感器 (Inductor)", spec: "10mH", desc: "通直流阻交流电感元件" },
  "passive.transformer": { zh: "理想变压器", spec: "1:1", desc: "双端口阻抗变换器" },
  "source.v_dc": { zh: "直流电压源 (VDC)", spec: "12V", desc: "恒定偏置直流电压源" },
  "source.i_dc": { zh: "直流电流源 (IDC)", spec: "2mA", desc: "恒定偏置直流电流源" },
  "source.v_ac_small_signal": { zh: "交流小信号源", spec: "10mV", desc: "正弦交流微变等效激励" },
  "source.i_ac_small_signal": { zh: "交流电流源", spec: "1mA", desc: "交流小信号电流激励" },
  "diode.standard": { zh: "二极管 (Diode)", spec: "0.7V", desc: "单向导电限幅整流器件" },
  "bjt.npn": { zh: "NPN 三极管", spec: "β=100", desc: "双极型三极管 NPN 管" },
  "bjt.pnp": { zh: "PNP 三极管", spec: "β=100", desc: "双极型三极管 PNP 管" },
  "fet.mosfet": { zh: "NMOS 增强型", spec: "Vth=1.5V", desc: "四端绝缘栅场效应管" },
  "fet.jfet": { zh: "JFET 结型管", spec: "Vp=-2V", desc: "耗尽型结型场效应管" },
  "controlled.vcvs": { zh: "压控电压源 (VCVS)", spec: "μ=10", desc: "电压控制的电压源" },
  "controlled.vccs": { zh: "压控电流源 (VCCS)", spec: "gm=5mS", desc: "跨导受控电流源" },
  "controlled.ccvs": { zh: "流控电压源 (CCVS)", spec: "rm=100Ω", desc: "互阻受控电压源" },
  "controlled.cccs": { zh: "流控电流源 (CCCS)", spec: "β=50", desc: "电流受控电流源" },
};

export default function ComponentPalette() {
  const [search, setSearch] = useState("");
  const [activeCategory, setActiveCategory] = useState<CategoryFilter>("all");
  const { addDefaultComponent, symbolStandard } = useEditorStore();

  const filteredComponents = useMemo(() => {
    return componentDefinitions.filter((comp) => {
      // Category filter
      if (activeCategory !== "all") {
        const cat = CATEGORIES.find((c) => c.id === activeCategory);
        if (cat && !cat.families.includes(comp.family)) {
          return false;
        }
      }

      // Search keyword filter
      if (search.trim()) {
        const kw = search.trim().toLowerCase();
        const zh = CHINESE_NAMES[comp.id]?.zh.toLowerCase() ?? "";
        const desc = CHINESE_NAMES[comp.id]?.desc.toLowerCase() ?? "";
        const tags = (comp.teachingTags ?? []).join(" ").toLowerCase();
        return (
          comp.id.toLowerCase().includes(kw) ||
          comp.displayName.toLowerCase().includes(kw) ||
          zh.includes(kw) ||
          desc.includes(kw) ||
          tags.includes(kw)
        );
      }

      return true;
    });
  }, [search, activeCategory]);

  const onDragStart = (e: React.DragEvent, definitionId: string, modelVariant?: string) => {
    e.dataTransfer.setData("application/definitionId", definitionId);
    if (modelVariant) {
      e.dataTransfer.setData("application/modelVariant", modelVariant);
    }
    e.dataTransfer.effectAllowed = "move";
  };

  return (
    <aside className="app-palette-sidebar">
      {/* Palette Header */}
      <div className="palette-header">
        <div className="palette-header-top">
          <div className="palette-title-group">
            <Layers size={18} className="text-cyan" />
            <h2 className="palette-heading">标准器件库</h2>
          </div>
          <span className="palette-badge">{filteredComponents.length}</span>
        </div>

        {/* Search Input */}
        <div className="palette-search-wrapper">
          <Search size={14} className="search-icon" />
          <input
            type="text"
            placeholder="搜索器件名称、符号或参数..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="palette-search-input"
          />
          {search && (
            <button className="search-clear-btn" onClick={() => setSearch("")}>
              ✕
            </button>
          )}
        </div>

        {/* Category Pills */}
        <div className="palette-category-pills">
          {CATEGORIES.map((cat) => (
            <button
              key={cat.id}
              className={`category-pill ${activeCategory === cat.id ? "category-pill--active" : ""}`}
              onClick={() => setActiveCategory(cat.id)}
            >
              {cat.label}
            </button>
          ))}
        </div>
      </div>

      {/* Component Cards List */}
      <div className="palette-scroll-content">
        {filteredComponents.length === 0 ? (
          <div className="palette-empty-state">
            <p>未找到匹配器件</p>
            <span>尝试切换分类或搜索其他关键词</span>
          </div>
        ) : (
          <div className="palette-grid">
            {filteredComponents.map((comp) => {
              const meta = CHINESE_NAMES[comp.id] ?? {
                zh: comp.displayName,
                spec: "",
                desc: comp.id,
              };
              const SymbolComp = symbolFor(comp.id, comp.defaultModelVariant, symbolStandard) ?? SYMBOLS[comp.id];

              return (
                <div
                  key={comp.id}
                  className="palette-component-card"
                  draggable
                  onDragStart={(e) => onDragStart(e, comp.id, comp.defaultModelVariant)}
                  onClick={() => addDefaultComponent(comp.id)}
                  title="点击直接放置在画布中央，或拖拽至指定位置"
                >
                  <div className="card-symbol-frame">
                    {SymbolComp ? <SymbolComp /> : <div className="fallback-symbol">{comp.id}</div>}
                  </div>

                  <div className="card-info">
                    <div className="card-name-row">
                      <span className="card-name-zh">{meta.zh}</span>
                      {meta.spec && <span className="card-spec-pill">{meta.spec}</span>}
                    </div>
                    <span className="card-desc">{meta.desc}</span>
                  </div>

                  <button
                    className="card-quick-add-btn"
                    onClick={(e) => {
                      e.stopPropagation();
                      addDefaultComponent(comp.id);
                    }}
                    title="添加到画布"
                  >
                    <Plus size={14} />
                  </button>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Palette Footer Tip */}
      <div className="palette-footer-hint">
        <span>💡 支持单击添加或拖拽放置</span>
      </div>
    </aside>
  );
}
