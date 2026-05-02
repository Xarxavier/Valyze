import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";

export type AppPage = "home" | "import";

interface UiState {
  page: AppPage;
  setPage: (p: AppPage) => void;
  goHome: () => void;

  sidebarOpen: boolean;
  toggleSidebar: () => void;
  setSidebarOpen: (open: boolean) => void;

  /** When true, monetary amounts are masked across the UI. Percentages stay. */
  hideAmounts: boolean;
  toggleHideAmounts: () => void;

  /**
   * Counter bumped whenever the dashboard should refetch (e.g. after an
   * import succeeds). Components subscribe via `useEffect` deps.
   */
  portfolioReloadKey: number;
  bumpPortfolioReload: () => void;
}

const UiContext = createContext<UiState | null>(null);

const HIDE_AMOUNTS_KEY = "valyze:hideAmounts";

export function UiProvider({ children }: { children: React.ReactNode }) {
  const [page, setPage] = useState<AppPage>("home");
  const [sidebarOpen, setSidebarOpen] = useState<boolean>(true);
  const [portfolioReloadKey, setPortfolioReloadKey] = useState(0);
  const [hideAmounts, setHideAmounts] = useState<boolean>(() => {
    if (typeof window === "undefined") return false;
    try {
      return window.localStorage.getItem(HIDE_AMOUNTS_KEY) === "1";
    } catch {
      return false;
    }
  });

  // Persist privacy preference — survives reloads but never leaves the device.
  useEffect(() => {
    try {
      window.localStorage.setItem(HIDE_AMOUNTS_KEY, hideAmounts ? "1" : "0");
    } catch {
      /* localStorage may be unavailable in some Tauri contexts; ignore */
    }
  }, [hideAmounts]);

  const toggleSidebar = useCallback(() => setSidebarOpen((open) => !open), []);
  const toggleHideAmounts = useCallback(() => setHideAmounts((on) => !on), []);
  const goHome = useCallback(() => setPage("home"), []);
  const bumpPortfolioReload = useCallback(() => setPortfolioReloadKey((n) => n + 1), []);

  const value = useMemo<UiState>(
    () => ({
      page,
      setPage,
      goHome,
      sidebarOpen,
      toggleSidebar,
      setSidebarOpen,
      hideAmounts,
      toggleHideAmounts,
      portfolioReloadKey,
      bumpPortfolioReload,
    }),
    [
      page,
      goHome,
      sidebarOpen,
      toggleSidebar,
      hideAmounts,
      toggleHideAmounts,
      portfolioReloadKey,
      bumpPortfolioReload,
    ],
  );

  return <UiContext.Provider value={value}>{children}</UiContext.Provider>;
}

export function useUi(): UiState {
  const ctx = useContext(UiContext);
  if (!ctx) throw new Error("useUi must be used inside UiProvider");
  return ctx;
}
