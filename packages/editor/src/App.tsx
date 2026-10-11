/**
 * Root application component.
 *
 * The Zustand store is used directly by child components (no Provider needed).
 * AppShell renders the full editor layout.
 */

import AppShell from "./components/layout/AppShell";

export default function App() {
  return <AppShell />;
}
