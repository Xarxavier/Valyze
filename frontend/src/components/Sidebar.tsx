import { useAuth } from "../auth/AuthContext";
import { useUi, type AppPage } from "../ui/UiContext";

interface NavItem {
  id: AppPage;
  label: string;
  icon: string;
}

const NAV: NavItem[] = [
  { id: "home", label: "Home", icon: "■" },
  { id: "import", label: "Import data", icon: "↧" },
];

export function Sidebar() {
  const { sidebarOpen, page, setPage, hideAmounts, toggleHideAmounts } = useUi();
  const { clearToken } = useAuth();

  return (
    <aside className={`sidebar ${sidebarOpen ? "sidebar-open" : "sidebar-closed"}`} aria-hidden={!sidebarOpen}>
      <div className="sidebar-inner">
        <nav className="sidebar-nav" aria-label="Primary">
          {NAV.map((item) => (
            <button
              key={item.id}
              type="button"
              className={`sidebar-link ${page === item.id ? "is-active" : ""}`}
              onClick={() => setPage(item.id)}
            >
              <span className="sidebar-icon" aria-hidden="true">
                {item.icon}
              </span>
              <span className="sidebar-label">{item.label}</span>
            </button>
          ))}
        </nav>

        <div className="sidebar-spacer" />

        <div className="sidebar-section">
          <button
            type="button"
            className={`sidebar-toggle ${hideAmounts ? "is-on" : ""}`}
            onClick={toggleHideAmounts}
            aria-pressed={hideAmounts}
            title={hideAmounts ? "Show amounts" : "Hide absolute amounts"}
          >
            <span className="sidebar-icon" aria-hidden="true">
              {hideAmounts ? "•" : "€"}
            </span>
            <span className="sidebar-label">
              {hideAmounts ? "Amounts hidden" : "Hide amounts"}
            </span>
          </button>

          <button type="button" className="sidebar-link sidebar-signout" onClick={clearToken}>
            <span className="sidebar-icon" aria-hidden="true">
              ⎋
            </span>
            <span className="sidebar-label">Sign out</span>
          </button>
        </div>
      </div>
    </aside>
  );
}
