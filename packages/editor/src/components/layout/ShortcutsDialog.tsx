/** In-app keyboard shortcut reference dialog. */

export interface ShortcutItem {
  keys: string[];
  description: string;
}

export interface ShortcutGroup {
  title: string;
  items: ShortcutItem[];
}

/** Single source of truth for the shortcut list shown in the dialog. */
export const SHORTCUT_GROUPS: ShortcutGroup[] = [
  {
    title: "文件",
    items: [
      { keys: ["Ctrl/⌘", "O"], description: "打开电路 JSON" },
      { keys: ["Ctrl/⌘", "S"], description: "导出电路 JSON" },
    ],
  },
  {
    title: "编辑",
    items: [
      { keys: ["Ctrl/⌘", "Z"], description: "撤销" },
      { keys: ["Ctrl/⌘", "Shift", "Z"], description: "重做" },
      { keys: ["Ctrl/⌘", "Y"], description: "重做" },
      { keys: ["Ctrl/⌘", "D"], description: "复制选中元件" },
      { keys: ["R"], description: "选中元件顺时针旋转 90°" },
      { keys: ["Delete"], description: "删除选中元件或导线" },
      { keys: ["Backspace"], description: "删除选中元件或导线" },
      { keys: ["Esc"], description: "取消选择 / 关闭弹窗" },
    ],
  },
  {
    title: "移动元件",
    items: [
      { keys: ["←", "↑", "↓", "→"], description: "移动 1 px" },
      { keys: ["Shift", "方向键"], description: "移动 10 px（一格）" },
    ],
  },
  {
    title: "帮助",
    items: [
      { keys: ["F1"], description: "显示 / 隐藏快捷键帮助" },
      { keys: ["?"], description: "显示 / 隐藏快捷键帮助" },
    ],
  },
];

export default function ShortcutsDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  if (!open) return null;
  return (
    <div className="shortcut-overlay" onClick={onClose} role="presentation">
      <div
        className="shortcut-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="快捷键帮助"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="shortcut-dialog__header">
          <div>
            <span className="eyebrow">KEYBOARD</span>
            <h2>快捷键</h2>
          </div>
          <button className="inspector__close" onClick={onClose} aria-label="关闭快捷键帮助">×</button>
        </header>
        <div className="shortcut-dialog__body">
          {SHORTCUT_GROUPS.map((group) => (
            <section key={group.title} className="shortcut-dialog__group">
              <h3>{group.title}</h3>
              {group.items.map((item) => (
                <div className="shortcut-dialog__row" key={`${group.title}:${item.keys.join("+")}`}>
                  <span className="shortcut-dialog__keys">
                    {item.keys.map((key, index) => (
                      <span key={key} className="shortcut-dialog__keys">
                        {index > 0 && <span className="shortcut-dialog__plus">+</span>}
                        <kbd>{key}</kbd>
                      </span>
                    ))}
                  </span>
                  <span>{item.description}</span>
                </div>
              ))}
            </section>
          ))}
        </div>
        <footer className="shortcut-dialog__footer">
          按 <kbd>F1</kbd> 或 <kbd>?</kbd> 打开本帮助，<kbd>Esc</kbd> 关闭。输入框内输入时快捷键不生效。
        </footer>
      </div>
    </div>
  );
}
