/**
 * Generic AI vendor contract. Adding OpenAI/Gemini later means dropping a new
 * file under `src/ai/` that implements `AiVendor` and registering it in
 * `registry.ts`. The Chat component is vendor-agnostic.
 */

export type ChatRole = "user" | "assistant";

export interface ChatMessage {
  id: string;
  role: ChatRole;
  /** Final rendered text. Streamed assistant messages mutate this in place. */
  content: string;
  /** Vendor that produced the message (assistant only). */
  vendor?: string;
  createdAt: number;
}

/** Discrete events emitted while the assistant is replying. */
export type ChatChunk =
  | { type: "text"; delta: string }
  | { type: "tool_use"; name: string; input?: unknown }
  | { type: "tool_result"; isError: boolean }
  | { type: "system"; message: string }
  | { type: "error"; message: string }
  | { type: "done"; usage?: { inputTokens?: number; outputTokens?: number } };

export interface SendOptions {
  /** Stable id for cancellation + event routing. */
  requestId: string;
  /** Conversation id. The vendor decides what (if anything) it means. */
  sessionId: string;
  isFirstTurn: boolean;
  /** What the user just typed. */
  userMessage: string;
  /** Role guidance + portfolio snapshot, only sent on the first turn. */
  systemPrompt?: string;
  /** Aborts the stream when triggered. */
  signal?: AbortSignal;
}

export interface AiVendor {
  /** Stable id used by the registry and persisted in URL params if needed. */
  readonly id: string;
  readonly displayName: string;
  /** True when the vendor is reachable from this machine. */
  isAvailable(): Promise<boolean>;
  /** Streams the assistant's reply chunk by chunk. */
  send(opts: SendOptions): AsyncIterable<ChatChunk>;
}
