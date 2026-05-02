const SERVICE_KEY = "access_token";

interface TauriGlobal {
  __TAURI_INTERNALS__?: unknown;
  __TAURI__?: unknown;
}

function isTauri(): boolean {
  const w = window as unknown as TauriGlobal;
  return Boolean(w.__TAURI_INTERNALS__ ?? w.__TAURI__);
}

async function tauriInvoke<T>(command: string, args?: Record<string, unknown>): Promise<T> {
  const { invoke } = await import("@tauri-apps/api/core");
  return invoke<T>(command, args);
}

export const tokenStorage = {
  async get(): Promise<string | null> {
    if (isTauri()) {
      try {
        const value = await tauriInvoke<string | null>("get_token", { key: SERVICE_KEY });
        return value ?? null;
      } catch {
        return null;
      }
    }
    return sessionStorage.getItem(SERVICE_KEY);
  },

  async set(token: string): Promise<void> {
    if (isTauri()) {
      await tauriInvoke<void>("store_token", { key: SERVICE_KEY, token });
      return;
    }
    sessionStorage.setItem(SERVICE_KEY, token);
  },

  async clear(): Promise<void> {
    if (isTauri()) {
      await tauriInvoke<void>("clear_token", { key: SERVICE_KEY });
      return;
    }
    sessionStorage.removeItem(SERVICE_KEY);
  },
};
