import { invoke } from "@tauri-apps/api/core";
import { listen, type UnlistenFn } from "@tauri-apps/api/event";
import type { AiVendor, ChatChunk, SendOptions } from "./types";

/**
 * Local Claude Code CLI as an AI vendor.
 * Frontend → Tauri → spawn `claude.exe -p ... --output-format stream-json`.
 * The Rust side forwards each NDJSON line back via three events keyed by
 * the request id: `chat:chunk:<id>`, `chat:done:<id>`, `chat:error:<id>`.
 *
 * The CLI persists conversation state in `~/.claude/sessions/` keyed by the
 * UUID we generate per chat — so the backend (and database) never sees a
 * single message. Closing the app does NOT clean up; the user can wipe
 * sessions manually with the regular `claude` CLI if the directory grows.
 */
export class ClaudeCodeVendor implements AiVendor {
  readonly id = "claude-code";
  readonly displayName = "Claude Code (local)";

  async isAvailable(): Promise<boolean> {
    try {
      return await invoke<boolean>("claude_chat_available");
    } catch {
      return false;
    }
  }

  send(opts: SendOptions): AsyncIterable<ChatChunk> {
    return streamClaude(opts);
  }
}

async function* streamClaude(opts: SendOptions): AsyncIterable<ChatChunk> {
  const { requestId, sessionId, isFirstTurn, userMessage, systemPrompt, signal } = opts;

  const queue: ChatChunk[] = [];
  let resolveNext: ((_: void) => void) | null = null;
  let finished = false;
  let errored: string | null = null;

  const wakeup = () => {
    if (resolveNext) {
      const r = resolveNext;
      resolveNext = null;
      r();
    }
  };

  const unlisteners: UnlistenFn[] = [];

  unlisteners.push(
    await listen<string>(`chat:chunk:${requestId}`, (event) => {
      const parsed = parseClaudeLine(event.payload);
      if (parsed.length > 0) {
        queue.push(...parsed);
        wakeup();
      }
    }),
  );

  unlisteners.push(
    await listen<void>(`chat:done:${requestId}`, () => {
      queue.push({ type: "done" });
      finished = true;
      wakeup();
    }),
  );

  unlisteners.push(
    await listen<string>(`chat:error:${requestId}`, (event) => {
      errored = event.payload || "Claude reported an unknown error";
      finished = true;
      wakeup();
    }),
  );

  // Cancellation: ask Rust to kill the process. The error/done event will
  // close the iterator naturally afterwards.
  const onAbort = () => {
    invoke("claude_chat_cancel", { requestId }).catch(() => {
      /* nothing useful to do — exit handler will fire anyway */
    });
  };
  signal?.addEventListener("abort", onAbort, { once: true });

  try {
    await invoke("claude_chat_send", {
      req: {
        requestId,
        sessionId,
        isFirstTurn,
        prompt: userMessage,
        systemPrompt: isFirstTurn ? systemPrompt : null,
      },
    });
  } catch (e) {
    cleanup();
    throw e;
  }

  function cleanup() {
    signal?.removeEventListener("abort", onAbort);
    for (const u of unlisteners) {
      try {
        u();
      } catch {
        /* best effort */
      }
    }
  }

  try {
    while (true) {
      if (queue.length > 0) {
        const next = queue.shift()!;
        yield next;
        if (next.type === "done") return;
        continue;
      }
      if (finished) {
        if (errored) {
          yield { type: "error", message: errored };
        }
        return;
      }
      await new Promise<void>((resolve) => {
        resolveNext = resolve;
      });
    }
  } finally {
    cleanup();
  }
}

interface ClaudeStreamLine {
  type?: string;
  subtype?: string;
  message?: {
    content?: Array<{ type: string; text?: string; name?: string; input?: unknown; is_error?: boolean }>;
  };
  total_cost_usd?: number;
  usage?: {
    input_tokens?: number;
    output_tokens?: number;
  };
  is_error?: boolean;
}

/**
 * Map one stream-json line to zero, one, or several `ChatChunk`s.
 *
 * Claude Code emits objects shaped like the Anthropic Messages API plus a
 * top-level `type` ("system" | "assistant" | "user" | "result"). We pick out
 * text deltas, surface tool calls/results so the UI can show "thinking…"
 * states, and translate `result` into our terminal `done` chunk.
 */
function parseClaudeLine(line: string): ChatChunk[] {
  const trimmed = line.trim();
  if (!trimmed) return [];
  let parsed: ClaudeStreamLine;
  try {
    parsed = JSON.parse(trimmed) as ClaudeStreamLine;
  } catch {
    // The CLI sometimes prints diagnostic banners. Surface them as a system note.
    return [{ type: "system", message: trimmed }];
  }

  const out: ChatChunk[] = [];

  if (parsed.type === "assistant" && parsed.message?.content) {
    for (const block of parsed.message.content) {
      if (block.type === "text" && typeof block.text === "string" && block.text.length > 0) {
        out.push({ type: "text", delta: block.text });
      } else if (block.type === "tool_use") {
        out.push({ type: "tool_use", name: block.name ?? "tool", input: block.input });
      }
    }
  } else if (parsed.type === "user" && parsed.message?.content) {
    for (const block of parsed.message.content) {
      if (block.type === "tool_result") {
        out.push({ type: "tool_result", isError: !!block.is_error });
      }
    }
  } else if (parsed.type === "result") {
    out.push({
      type: "done",
      usage: {
        inputTokens: parsed.usage?.input_tokens,
        outputTokens: parsed.usage?.output_tokens,
      },
    });
  }

  return out;
}
