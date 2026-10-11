import React from "react";
import { useEditorStore } from "../../store/editorStore";
import { Keyboard, X } from "lucide-react";

export default function ShortcutsModal() {
  const { shortcutsOpen, setShortcutsOpen } = useEditorStore();

  if (!shortcutsOpen) return null;

  return (
    <div className="modal-backdrop" onClick={() => setShortcutsOpen(false)}>
      <div className="modal-dialog modal-dialog--medium" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div className="modal-title-group">
            <div className="modal-icon-badge">
              <Keyboard size={20} className="text-cyan" />
            </div>
            <div>
              <h2 className="modal-title">快捷键速查指南</h2>
              <p className="modal-subtitle">支持与常用电路仿真软件 (Multisim / Altium) 一致的专业高频快捷操作</p>
            </div>
          </div>
          <button className="modal-close-btn" onClick={() => setShortcutsOpen(false)}>
            <X size={18} />
          </button>
        </div>

        <div className="modal-body">
          <div className="shortcuts-grid">
            <div className="shortcut-group">
              <h4 className="shortcut-group-title">元件与画布操作</h4>
              <div className="shortcut-item">
                <span className="shortcut-desc">顺时针旋转 90°</span>
                <span className="shortcut-keys"><kbd>R</kbd></span>
              </div>
              <div className="shortcut-item">
                <span className="shortcut-desc">微调移动元件</span>
                <span className="shortcut-keys"><kbd>←</kbd><kbd>↑</kbd><kbd>↓</kbd><kbd>→</kbd></span>
              </div>
              <div className="shortcut-item">
                <span className="shortcut-desc">快速位移 (10 步长)</span>
                <span className="shortcut-keys"><kbd>Shift</kbd> + 方向键</span>
              </div>
              <div className="shortcut-item">
                <span className="shortcut-desc">克隆复制元件</span>
                <span className="shortcut-keys"><kbd>Ctrl</kbd> + <kbd>D</kbd></span>
              </div>
              <div className="shortcut-item">
                <span className="shortcut-desc">删除选中元件 / 导线</span>
                <span className="shortcut-keys"><kbd>Delete</kbd> / <kbd>Backspace</kbd></span>
              </div>
              <div className="shortcut-item">
                <span className="shortcut-desc">取消选择</span>
                <span className="shortcut-keys"><kbd>Esc</kbd></span>
              </div>
            </div>

            <div className="shortcut-group">
              <h4 className="shortcut-group-title">编辑历史与文件</h4>
              <div className="shortcut-item">
                <span className="shortcut-desc">撤销上一步操作</span>
                <span className="shortcut-keys"><kbd>Ctrl</kbd> + <kbd>Z</kbd></span>
              </div>
              <div className="shortcut-item">
                <span className="shortcut-desc">重做下一步操作</span>
                <span className="shortcut-keys"><kbd>Ctrl</kbd> + <kbd>Y</kbd></span>
              </div>
              <div className="shortcut-item">
                <span className="shortcut-desc">导出电路数据 JSON</span>
                <span className="shortcut-keys"><kbd>Ctrl</kbd> + <kbd>S</kbd></span>
              </div>
              <div className="shortcut-item">
                <span className="shortcut-desc">打开本地电路 JSON</span>
                <span className="shortcut-keys"><kbd>Ctrl</kbd> + <kbd>O</kbd></span>
              </div>
              <div className="shortcut-item">
                <span className="shortcut-desc">打开快捷键面板</span>
                <span className="shortcut-keys"><kbd>F1</kbd> / <kbd>?</kbd></span>
              </div>
            </div>
          </div>
        </div>

        <div className="modal-footer">
          <div className="modal-footer-hint">
            <span>在输入框内打字时快捷键会自动避让</span>
          </div>
          <button className="btn btn-outline" onClick={() => setShortcutsOpen(false)}>
            我知道了
          </button>
        </div>
      </div>
    </div>
  );
}
