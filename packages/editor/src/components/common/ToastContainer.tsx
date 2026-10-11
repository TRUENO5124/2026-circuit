import React from "react";
import { useEditorStore } from "../../store/editorStore";
import { CheckCircle2, AlertTriangle, AlertCircle, Info, X } from "lucide-react";

export default function ToastContainer() {
  const { toasts, removeToast } = useEditorStore();

  if (toasts.length === 0) return null;

  return (
    <div className="toast-container" aria-live="polite">
      {toasts.map((toast) => {
        const icon = (() => {
          switch (toast.type) {
            case "success":
              return <CheckCircle2 className="toast-icon toast-icon--success" size={18} />;
            case "warning":
              return <AlertTriangle className="toast-icon toast-icon--warning" size={18} />;
            case "error":
              return <AlertCircle className="toast-icon toast-icon--error" size={18} />;
            case "info":
            default:
              return <Info className="toast-icon toast-icon--info" size={18} />;
          }
        })();

        return (
          <div key={toast.id} className={`toast-card toast-card--${toast.type}`}>
            <div className="toast-content">
              {icon}
              <div className="toast-text">
                <div className="toast-title">{toast.title}</div>
                {toast.message && <div className="toast-message">{toast.message}</div>}
              </div>
            </div>
            <button
              className="toast-close"
              onClick={() => removeToast(toast.id)}
              aria-label="关闭通知"
            >
              <X size={14} />
            </button>
          </div>
        );
      })}
    </div>
  );
}
