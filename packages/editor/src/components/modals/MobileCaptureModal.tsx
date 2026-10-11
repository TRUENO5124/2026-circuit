import React, { useEffect, useState } from "react";
import { useEditorStore } from "../../store/editorStore";
import {
  createSession,
  subscribeSessionEvents,
  type SessionCreateResponse,
  type UploadedImage,
} from "../../api/client";
import { QRCodeSVG } from "qrcode.react";
import { QrCode, Smartphone, CheckCircle, Clock, X, Image as ImageIcon, RefreshCw } from "lucide-react";

export default function MobileCaptureModal() {
  const { mobileCaptureOpen, setMobileCaptureOpen, addToast } = useEditorStore();

  const [session, setSession] = useState<SessionCreateResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [uploadedImages, setUploadedImages] = useState<UploadedImage[]>([]);
  const [sessionStatus, setSessionStatus] = useState<string>("pending");
  const [timeLeft, setTimeLeft] = useState<number>(300);

  const initSession = async () => {
    setLoading(true);
    setError(null);
    setUploadedImages([]);
    setSessionStatus("pending");
    try {
      const sess = await createSession();
      setSession(sess);
      setTimeLeft(sess.expires_in_seconds || 300);
    } catch (err) {
      setError(`创建会话失败: ${String(err)}。请确认后端 FastAPI 服务正在运行 (:3000)`);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!mobileCaptureOpen) return;
    initSession();
  }, [mobileCaptureOpen]);

  // Countdown timer
  useEffect(() => {
    if (!session || timeLeft <= 0) return;
    const interval = setInterval(() => {
      setTimeLeft((t) => Math.max(0, t - 1));
    }, 1000);
    return () => clearInterval(interval);
  }, [session, timeLeft]);

  // SSE subscription
  useEffect(() => {
    if (!session) return;

    const unsubscribe = subscribeSessionEvents(session.id, {
      onStatus: (st) => {
        setSessionStatus(st);
      },
      onImageUploaded: (img) => {
        setUploadedImages((prev) => [...prev, img]);
        setSessionStatus("completed");
        addToast({
          type: "success",
          title: "收到手机上传图片",
          message: img.filename,
        });
      },
      onError: () => {
        // SSE network or polling
      },
    });

    return () => {
      unsubscribe();
    };
  }, [session, addToast]);

  if (!mobileCaptureOpen) return null;

  const formatTime = (secs: number) => {
    const m = Math.floor(secs / 60);
    const s = secs % 60;
    return `${m}:${s < 10 ? "0" : ""}${s}`;
  };

  return (
    <div className="modal-backdrop" onClick={() => setMobileCaptureOpen(false)}>
      <div className="modal-dialog modal-dialog--medium" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div className="modal-title-group">
            <div className="modal-icon-badge">
              <Smartphone size={20} className="text-cyan" />
            </div>
            <div>
              <h2 className="modal-title">手机扫码拍照直传</h2>
              <p className="modal-subtitle">在同一局域网下，用手机扫描二维码即可直接拍照并将电路图同步至电脑</p>
            </div>
          </div>
          <button className="modal-close-btn" onClick={() => setMobileCaptureOpen(false)}>
            <X size={18} />
          </button>
        </div>

        <div className="modal-body modal-body--center">
          {loading && (
            <div className="qr-loading-box">
              <RefreshCw className="animate-spin text-cyan" size={32} />
              <p>正在生成安全局域网会话与二维码...</p>
            </div>
          )}

          {error && (
            <div className="qr-error-box">
              <p>{error}</p>
              <button className="btn btn-outline" onClick={initSession}>
                重试
              </button>
            </div>
          )}

          {!loading && !error && session && (
            <div className="qr-flow-container">
              <div className="qr-code-wrapper">
                <QRCodeSVG
                  value={session.qr_url}
                  size={200}
                  level="M"
                  includeMargin={true}
                  className="qr-code-svg"
                />
                <div className="qr-timer">
                  <Clock size={13} /> 有效期剩余：{formatTime(timeLeft)}
                </div>
              </div>

              <div className="qr-status-indicator">
                {sessionStatus === "pending" && (
                  <div className="qr-status-pill qr-status-pill--pending">
                    <span className="pulsing-dot" /> 等待手机扫描...
                  </div>
                )}
                {sessionStatus === "linked" && (
                  <div className="qr-status-pill qr-status-pill--linked">
                    <Smartphone size={14} /> 手机已打开拍照页面
                  </div>
                )}
                {sessionStatus === "completed" && (
                  <div className="qr-status-pill qr-status-pill--success">
                    <CheckCircle size={14} /> 图片已接收！
                  </div>
                )}

                <div className="qr-url-text">
                  <span>局域网访问地址：</span>
                  <code>{session.qr_url}</code>
                </div>
              </div>

              {uploadedImages.length > 0 && (
                <div className="uploaded-gallery">
                  <h4>已上传图片 ({uploadedImages.length})</h4>
                  <div className="uploaded-grid">
                    {uploadedImages.map((img) => (
                      <div key={img.id} className="uploaded-thumb-card">
                        <ImageIcon size={24} className="text-muted" />
                        <span className="uploaded-name">{img.filename}</span>
                        <span className="uploaded-time">{img.uploaded_at}</span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>

        <div className="modal-footer">
          <div className="modal-footer-hint">
            <span>确保手机和电脑连接至同一 Wi-Fi 或局域网络</span>
          </div>
          <button className="btn btn-outline" onClick={() => setMobileCaptureOpen(false)}>
            完成
          </button>
        </div>
      </div>
    </div>
  );
}
