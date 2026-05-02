import { createContext, useContext, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { tokenStorage } from "./tokenStorage";

interface AuthState {
  token: string | null;
  loading: boolean;
}

interface AuthContextValue extends AuthState {
  setToken: (token: string) => Promise<void>;
  clearToken: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ token: null, loading: true });

  useEffect(() => {
    let cancelled = false;
    tokenStorage
      .get()
      .then((token) => {
        if (!cancelled) setState({ token, loading: false });
      })
      .catch(() => {
        if (!cancelled) setState({ token: null, loading: false });
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      ...state,
      async setToken(token) {
        await tokenStorage.set(token);
        setState({ token, loading: false });
      },
      async clearToken() {
        await tokenStorage.clear();
        setState({ token: null, loading: false });
      },
    }),
    [state],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}
