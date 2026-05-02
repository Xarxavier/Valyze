import { invoke } from "@tauri-apps/api/core";
import type { ChatMessage } from "./types";

/**
 * Bridges the React side to the Tauri filesystem commands in
 * `tauri/src-tauri/src/chat_storage.rs`. JSON shape is shared via
 * `serde(rename_all = "camelCase")` on the Rust side.
 *
 * One JSON file per chat lives under <app-data>/valyze/chats/<id>.json.
 * The id doubles as Claude Code's `--session-id` so resuming a saved
 * chat picks up Claude's own internal context too.
 */

export interface StoredChatSession {
  id: string;
  title: string | null;
  createdAt: number;
  updatedAt: number;
  messages: ChatMessage[];
  vendor: string | null;
}

export interface StoredChatSessionMeta {
  id: string;
  title: string | null;
  createdAt: number;
  updatedAt: number;
  messageCount: number;
  vendor: string | null;
}

export const chatStorage = {
  save(session: StoredChatSession): Promise<void> {
    return invoke("chat_save_session", { session });
  },

  async load(id: string): Promise<StoredChatSession | null> {
    return await invoke<StoredChatSession | null>("chat_load_session", { id });
  },

  async list(): Promise<StoredChatSessionMeta[]> {
    return await invoke<StoredChatSessionMeta[]>("chat_list_sessions");
  },

  delete(id: string): Promise<void> {
    return invoke("chat_delete_session", { id });
  },
};

/**
 * Build a short title from the first user message. Used when we save a
 * session but the user hasn't named it. Trimmed to ~60 chars so it fits
 * in the picker without truncation noise.
 */
export function deriveTitle(messages: ChatMessage[]): string | null {
  const firstUser = messages.find((m) => m.role === "user");
  if (!firstUser) return null;
  const trimmed = firstUser.content.trim().replace(/\s+/g, " ");
  if (!trimmed) return null;
  return trimmed.length > 60 ? trimmed.slice(0, 57) + "…" : trimmed;
}
