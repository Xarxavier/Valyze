import {
  ApiException,
  type DevLoginResponse,
  type ImportResult,
  type PortfolioView,
  type PositionsView,
} from "./types";

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";

async function request<T>(
  path: string,
  init: RequestInit & { token?: string | null } = {},
): Promise<T> {
  const { token, headers, ...rest } = init;
  const finalHeaders = new Headers(headers);
  finalHeaders.set("Accept", "application/json");
  // FormData sets its own multipart boundary header; never stomp it.
  const bodyIsFormData = typeof FormData !== "undefined" && rest.body instanceof FormData;
  if (rest.body && !bodyIsFormData && !finalHeaders.has("Content-Type")) {
    finalHeaders.set("Content-Type", "application/json");
  }
  if (token) {
    finalHeaders.set("Authorization", `Bearer ${token}`);
  }

  const response = await fetch(`${baseUrl}${path}`, { ...rest, headers: finalHeaders });

  if (!response.ok) {
    let code = `HTTP_${response.status}`;
    let detail: string | undefined;
    try {
      const body = (await response.json()) as { code?: string; detail?: string };
      if (body?.code) code = body.code;
      if (body?.detail) detail = body.detail;
    } catch {
      // Body wasn't JSON; keep the status-based code.
    }
    throw new ApiException(response.status, code, detail);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

async function uploadPdf(
  token: string,
  broker: string,
  file: File,
): Promise<ImportResult> {
  const form = new FormData();
  form.append("file", file, file.name);
  return request<ImportResult>(`/api/trades/import?broker=${encodeURIComponent(broker)}`, {
    method: "POST",
    token,
    body: form,
  });
}

export const api = {
  baseUrl,
  devLogin: () => request<DevLoginResponse>("/auth/dev-login", { method: "POST" }),
  getPortfolio: (token: string) =>
    request<PortfolioView>("/api/portfolio/", { method: "GET", token }),
  getPositions: (token: string) =>
    request<PositionsView>("/api/positions/", { method: "GET", token }),
  importTradeRepublicPdf: (token: string, file: File) =>
    uploadPdf(token, "trade-republic", file),
};
