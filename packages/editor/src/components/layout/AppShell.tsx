/**
 * App shell: overall layout and view mode switching.
 */

import React from "react";
import { useEditorStore } from "../../store/editorStore";
import Toolbar from "./Toolbar";
import ComponentPalette from "./ComponentPalette";
import CircuitCanvas from "../editor/CircuitCanvas";
import ParameterPanel from "./ParameterPanel";
import BottomTabs from "./BottomTabs";
import AnalysisPanel from "./AnalysisPanel";
import ReportView from "./ReportView";

import BenchmarkGalleryModal from "../modals/BenchmarkGalleryModal";
import MobileCaptureModal from "../modals/MobileCaptureModal";
import ExportStudioModal from "../modals/ExportStudioModal";
import ShortcutsModal from "../modals/ShortcutsModal";
import SymbolLibraryModal from "../modals/SymbolLibraryModal";
import ToastContainer from "../common/ToastContainer";

export default function AppShell() {
  const { viewMode } = useEditorStore();

  return (
    <div className="app-shell-root">
      {/* Top command navigation */}
      <Toolbar />

      {/* Main Workspace based on View Mode */}
      {viewMode === "editor" && (
        <div className="workspace-editor-layout">
          <ComponentPalette />
          <div className="workspace-center-canvas-column">
            <CircuitCanvas />
            <BottomTabs />
          </div>
          <ParameterPanel />
        </div>
      )}

      {viewMode === "analysis" && (
        <div className="workspace-full-studio-layout">
          <AnalysisPanel />
        </div>
      )}

      {viewMode === "report" && (
        <div className="workspace-full-report-layout">
          <ReportView />
        </div>
      )}

      {/* Global Modals & Notifications */}
      <BenchmarkGalleryModal />
      <MobileCaptureModal />
      <ExportStudioModal />
      <ShortcutsModal />
      <SymbolLibraryModal />
      <ToastContainer />
    </div>
  );
}
