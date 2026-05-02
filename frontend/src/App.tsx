import "./App.css";
import { AuthProvider, useAuth } from "./auth/AuthContext";
import { Login } from "./components/Login";
import { Sidebar } from "./components/Sidebar";
import { Import } from "./components/Import";
import { Workspace } from "./components/Workspace";
import { UiProvider, useUi } from "./ui/UiContext";

function AuthenticatedShell() {
  const { page, sidebarOpen, toggleSidebar, goHome } = useUi();

  return (
    <div className="app-shell">
      <Sidebar />
      <div className="app-main-area">
        <header className="app-header">
          <div className="app-header-left">
            <button
              type="button"
              className="hamburger"
              onClick={toggleSidebar}
              aria-label={sidebarOpen ? "Hide menu" : "Show menu"}
              aria-expanded={sidebarOpen}
            >
              ≡
            </button>
            <button
              type="button"
              className="app-brand"
              onClick={goHome}
              title="Go to home"
            >
              Valyze
            </button>
          </div>
          <span className="muted">Personal mode</span>
        </header>

        <main className="app-main app-main-workspace">
          {/* Both pages stay mounted so chat history and dashboard caches
              survive navigation. Only the active page is shown. */}
          <div style={{ display: page === "home" ? "contents" : "none" }}>
            <Workspace />
          </div>
          <div
            className="page-import"
            style={{ display: page === "import" ? "block" : "none" }}
          >
            <Import />
          </div>
        </main>
      </div>
    </div>
  );
}

function Shell() {
  const { token, loading } = useAuth();
  if (loading) {
    return (
      <div className="app">
        <main className="app-main">
          <p className="muted">Loading…</p>
        </main>
      </div>
    );
  }
  if (!token) {
    return (
      <div className="app">
        <header className="app-header">
          <h1>Valyze</h1>
          <span className="muted">Personal mode</span>
        </header>
        <main className="app-main">
          <Login />
        </main>
      </div>
    );
  }
  return (
    <UiProvider>
      <AuthenticatedShell />
    </UiProvider>
  );
}

export function App() {
  return (
    <AuthProvider>
      <Shell />
    </AuthProvider>
  );
}
