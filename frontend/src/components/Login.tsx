import { useState } from "react";
import { api } from "../api/client";
import { ApiException } from "../api/types";
import { useAuth } from "../auth/AuthContext";

export function Login() {
  const { setToken } = useAuth();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleDevLogin() {
    setPending(true);
    setError(null);
    try {
      const response = await api.devLogin();
      await setToken(response.accessToken);
    } catch (err) {
      if (err instanceof ApiException) {
        setError(`${err.code}${err.detail ? ` — ${err.detail}` : ""}`);
      } else {
        setError(err instanceof Error ? err.message : "Unexpected error");
      }
      setPending(false);
    }
  }

  return (
    <div className="card">
      <h2>Sign in</h2>
      <p className="muted">
        Personal mode is active. The dev login issues a JWT for the seeded account.
      </p>
      <button className="button" onClick={handleDevLogin} disabled={pending}>
        {pending ? "Signing in…" : "Dev login"}
      </button>
      {error ? <p className="error">{error}</p> : null}
    </div>
  );
}
