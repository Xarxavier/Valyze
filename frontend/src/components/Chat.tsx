import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuth } from "../auth/AuthContext";
import { aiVendors, defaultVendorId, getVendor } from "../ai/registry";
import {
  chatStorage,
  deriveTitle,
  type StoredChatSessionMeta,
} from "../ai/chatStorage";
import type { ChatMessage, ChatChunk } from "../ai/types";

interface Props {
  /** Bumped after imports — the next assistant turn should re-fetch via MCP. */
  reloadKey: number;
}

/**
 * Tiny system prompt — the heavy persona + domain orientation lives in the
 * MCP server's own `instructions` (see Valyze.Mcp/Program.cs). We DON'T
 * inline a portfolio snapshot here: snapshots get stale within the
 * conversation AND inflate the Windows command line past the CreateProcess
 * limit. The model fetches fresh data via MCP tools when it needs it.
 *
 * If the user types something like "/portfolio-checkup" or "/explain-position",
 * Claude Code resolves it as a Valyze MCP prompt — see Valyze.Mcp/Prompts.
 */
const SYSTEM_PROMPT = `You are inside Valyze's local desktop chat (Tauri webview).
The MCP server "valyze" is connected — read-only portfolio + news tools, plus prompts
(/valyze:portfolio-checkup, /valyze:explain-position, /valyze:risk-assessment,
/valyze:daily-briefing, /valyze:explain-concept). WebSearch + WebFetch are also enabled
for research the local news cache can't cover. Match the user's language automatically.`;

export function Chat({ reloadKey: _reloadKey }: Props) {
  // `token` is not used here — the chat doesn't talk to the backend directly,
  // the MCP server does. The auth context is still consumed elsewhere; this
  // hook stays for symmetry / future per-user scoping.
  useAuth();
  const [vendorId, setVendorId] = useState<string>(defaultVendorId);
  const [vendorAvailable, setVendorAvailable] = useState<boolean | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sessions, setSessions] = useState<StoredChatSessionMeta[]>([]);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [restored, setRestored] = useState(false);

  // One conversation id per chat. New session → new UUID, so the local CLI
  // keeps its scratch state cleanly partitioned per chat. We also store the
  // same id on disk and use it as `--session-id` / `--resume <id>` for claude.
  const sessionIdRef = useRef<string>(crypto.randomUUID());
  const createdAtRef = useRef<number>(Date.now());
  const abortRef = useRef<AbortController | null>(null);
  const listRef = useRef<HTMLDivElement | null>(null);

  const vendor = useMemo(() => getVendor(vendorId), [vendorId]);

  // Probe the vendor on mount + whenever the user changes selection.
  useEffect(() => {
    let cancelled = false;
    if (!vendor) {
      setVendorAvailable(false);
      return;
    }
    setVendorAvailable(null);
    vendor.isAvailable().then((ok) => {
      if (!cancelled) setVendorAvailable(ok);
    });
    return () => {
      cancelled = true;
    };
  }, [vendor]);

  // First-load: list saved chats and restore the most recent one (if any),
  // so closing + reopening the app drops the user back where they were.
  useEffect(() => {
    let cancelled = false;
    chatStorage
      .list()
      .then(async (list) => {
        if (cancelled) return;
        setSessions(list);
        const mostRecent = list[0];
        if (mostRecent) {
          const session = await chatStorage.load(mostRecent.id);
          if (cancelled || !session) return;
          sessionIdRef.current = session.id;
          createdAtRef.current = session.createdAt;
          setMessages(session.messages);
          if (session.vendor) setVendorId(session.vendor);
        }
        setRestored(true);
      })
      .catch(() => {
        // No filesystem access (sandboxed?) — start fresh.
        if (!cancelled) setRestored(true);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  // Auto-scroll to bottom on new content.
  useEffect(() => {
    const el = listRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [messages]);

  const isFirstTurn = messages.length === 0;

  const buildSystemPrompt = useCallback((): string | undefined => {
    return isFirstTurn ? SYSTEM_PROMPT : undefined;
  }, [isFirstTurn]);

  // Persist whenever the conversation has settled (no streaming) and there
  // are messages worth saving. Refresh the picker after every save so the
  // dropdown reflects the latest title / position in the list.
  useEffect(() => {
    if (!restored || isStreaming || messages.length === 0) return;
    const id = sessionIdRef.current;
    chatStorage
      .save({
        id,
        title: deriveTitle(messages),
        createdAt: createdAtRef.current,
        updatedAt: Date.now(),
        messages,
        vendor: vendorId,
      })
      .then(() => chatStorage.list())
      .then((list) => setSessions(list))
      .catch(() => {
        /* best effort — don't surface storage errors to the user */
      });
  }, [restored, isStreaming, messages, vendorId]);

  const send = useCallback(async () => {
    const trimmed = input.trim();
    if (!trimmed || !vendor || isStreaming) return;

    setError(null);
    const userMsg: ChatMessage = {
      id: crypto.randomUUID(),
      role: "user",
      content: trimmed,
      createdAt: Date.now(),
    };
    const assistantMsgId = crypto.randomUUID();
    const assistantMsg: ChatMessage = {
      id: assistantMsgId,
      role: "assistant",
      content: "",
      vendor: vendor.id,
      createdAt: Date.now(),
    };
    const firstTurn = isFirstTurn;
    const systemPrompt = buildSystemPrompt();

    setMessages((prev) => [...prev, userMsg, assistantMsg]);
    setInput("");
    setIsStreaming(true);

    const abort = new AbortController();
    abortRef.current = abort;

    try {
      const stream = vendor.send({
        requestId: crypto.randomUUID(),
        sessionId: sessionIdRef.current,
        isFirstTurn: firstTurn,
        userMessage: trimmed,
        systemPrompt,
        signal: abort.signal,
      });

      for await (const chunk of stream) {
        applyChunk(setMessages, assistantMsgId, chunk);
        if (chunk.type === "error") {
          setError(chunk.message);
        }
      }
    } catch (e: unknown) {
      // Tauri rejects with a string, JS errors carry .message, anything else
      // we serialize so the real cause makes it to the user.
      const msg =
        e instanceof Error
          ? e.message
          : typeof e === "string"
            ? e
            : (() => {
                try {
                  return JSON.stringify(e);
                } catch {
                  return String(e);
                }
              })();
      setError(msg);
      setMessages((prev) =>
        prev.map((m) =>
          m.id === assistantMsgId && m.content === ""
            ? { ...m, content: `(failed: ${msg})` }
            : m,
        ),
      );
    } finally {
      setIsStreaming(false);
      abortRef.current = null;
    }
  }, [input, vendor, isStreaming, isFirstTurn, buildSystemPrompt]);

  const stop = useCallback(() => {
    abortRef.current?.abort();
  }, []);

  const startNewChat = useCallback(() => {
    if (isStreaming) abortRef.current?.abort();
    sessionIdRef.current = crypto.randomUUID();
    createdAtRef.current = Date.now();
    setMessages([]);
    setError(null);
    setPickerOpen(false);
  }, [isStreaming]);

  const loadChat = useCallback(
    async (id: string) => {
      if (isStreaming) abortRef.current?.abort();
      const session = await chatStorage.load(id);
      if (!session) return;
      sessionIdRef.current = session.id;
      createdAtRef.current = session.createdAt;
      setMessages(session.messages);
      setError(null);
      setPickerOpen(false);
      if (session.vendor) setVendorId(session.vendor);
    },
    [isStreaming],
  );

  const deleteChat = useCallback(
    async (id: string) => {
      await chatStorage.delete(id);
      const list = await chatStorage.list();
      setSessions(list);
      // If we deleted the chat we're currently in, fall through to a new one.
      if (id === sessionIdRef.current) {
        startNewChat();
      }
    },
    [startNewChat],
  );

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        send();
      }
    },
    [send],
  );

  const currentId = sessionIdRef.current;

  return (
    <section className="chat-panel">
      <header className="chat-header">
        <div>
          <h2>Assistant</h2>
          <p className="chat-sub">
            {vendorAvailable === false ? (
              <span className="chat-warning">
                {vendor?.displayName ?? "Vendor"} is not reachable on this machine.
              </span>
            ) : (
              <span className="muted">
                {vendor?.displayName} — answers stay on your machine.
              </span>
            )}
          </p>
        </div>
        <div className="chat-controls">
          <select
            className="chat-vendor-select"
            value={vendorId}
            onChange={(e) => setVendorId(e.target.value)}
            disabled={isStreaming}
          >
            {aiVendors.map((v) => (
              <option key={v.id} value={v.id}>
                {v.displayName}
              </option>
            ))}
          </select>
          <div className="chat-history-wrap">
            <button
              type="button"
              className="button-ghost"
              onClick={() => setPickerOpen((v) => !v)}
              disabled={sessions.length === 0}
              title="Recent chats"
            >
              History ({sessions.length})
            </button>
            {pickerOpen ? (
              <RecentPicker
                sessions={sessions}
                currentId={currentId}
                onPick={loadChat}
                onDelete={deleteChat}
                onClose={() => setPickerOpen(false)}
              />
            ) : null}
          </div>
          <button
            type="button"
            className="button-ghost"
            onClick={startNewChat}
            disabled={isStreaming}
            title="Start a new conversation"
          >
            New chat
          </button>
        </div>
      </header>

      <div className="chat-messages" ref={listRef}>
        {messages.length === 0 ? (
          <EmptyState
            ready={vendorAvailable === true}
            warning={
              vendorAvailable === false
                ? "Install Claude Code (claude CLI) and make sure it's on your PATH."
                : null
            }
          />
        ) : (
          messages.map((m) => <MessageBubble key={m.id} message={m} streaming={isStreaming} />)
        )}
      </div>

      {error ? <p className="error chat-error">{error}</p> : null}

      <form
        className="chat-input"
        onSubmit={(e) => {
          e.preventDefault();
          send();
        }}
      >
        <textarea
          rows={2}
          value={input}
          placeholder={
            vendorAvailable === false
              ? "Claude Code unavailable…"
              : "Ask about your portfolio (Enter to send, Shift+Enter for newline)"
          }
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={onKeyDown}
          disabled={isStreaming || vendorAvailable === false}
        />
        <div className="chat-input-actions">
          {isStreaming ? (
            <button type="button" className="button-ghost" onClick={stop}>
              Stop
            </button>
          ) : (
            <button
              type="submit"
              className="button"
              disabled={!input.trim() || vendorAvailable === false}
            >
              Send
            </button>
          )}
        </div>
      </form>
    </section>
  );
}

function applyChunk(
  setMessages: React.Dispatch<React.SetStateAction<ChatMessage[]>>,
  assistantId: string,
  chunk: ChatChunk,
) {
  if (chunk.type === "text") {
    setMessages((prev) =>
      prev.map((m) => (m.id === assistantId ? { ...m, content: m.content + chunk.delta } : m)),
    );
  } else if (chunk.type === "tool_use") {
    setMessages((prev) =>
      prev.map((m) =>
        m.id === assistantId
          ? { ...m, content: m.content + `\n\n_(using tool: ${chunk.name})_\n` }
          : m,
      ),
    );
  }
  // tool_result / system / error / done don't mutate the message body.
}

function MessageBubble({ message, streaming }: { message: ChatMessage; streaming: boolean }) {
  const isAssistant = message.role === "assistant";
  const showCursor = isAssistant && streaming && message.content === "";
  return (
    <div className={`chat-bubble chat-bubble-${message.role}`}>
      {showCursor ? (
        <span className="chat-typing">…</span>
      ) : (
        <pre className="chat-bubble-text">{message.content || (isAssistant ? "…" : "")}</pre>
      )}
    </div>
  );
}

function EmptyState({ ready, warning }: { ready: boolean; warning: string | null }) {
  return (
    <div className="chat-empty">
      <p>
        Ask anything about your portfolio: diversification, sector exposure, P&amp;L drivers, or
        what news you should be paying attention to. Informational analysis only — not advice.
      </p>
      {warning ? <p className="chat-warning">{warning}</p> : null}
      {!ready && !warning ? <p className="muted">Checking Claude Code availability…</p> : null}
    </div>
  );
}

function RecentPicker({
  sessions,
  currentId,
  onPick,
  onDelete,
  onClose,
}: {
  sessions: StoredChatSessionMeta[];
  currentId: string;
  onPick: (id: string) => void;
  onDelete: (id: string) => void;
  onClose: () => void;
}) {
  // Close on outside click / Escape so the dropdown behaves like a menu.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    const onDoc = (e: MouseEvent) => {
      const target = e.target as HTMLElement;
      if (!target.closest(".chat-history-popup")) onClose();
    };
    window.addEventListener("keydown", onKey);
    window.addEventListener("mousedown", onDoc);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("mousedown", onDoc);
    };
  }, [onClose]);

  return (
    <div className="chat-history-popup" role="menu">
      {sessions.length === 0 ? (
        <div className="chat-history-empty">No saved chats yet.</div>
      ) : (
        <ul className="chat-history-list">
          {sessions.map((s) => {
            const isCurrent = s.id === currentId;
            return (
              <li
                key={s.id}
                className={`chat-history-item ${isCurrent ? "is-current" : ""}`}
              >
                <button
                  type="button"
                  className="chat-history-pick"
                  onClick={() => onPick(s.id)}
                  title="Open this chat"
                >
                  <span className="chat-history-title">
                    {s.title ?? "(empty)"}
                  </span>
                  <span className="chat-history-meta">
                    {s.messageCount} msg · {formatRelative(s.updatedAt)}
                  </span>
                </button>
                <button
                  type="button"
                  className="chat-history-delete"
                  onClick={() => onDelete(s.id)}
                  title="Delete this chat"
                  aria-label="Delete chat"
                >
                  ×
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

function formatRelative(ms: number): string {
  const diff = Date.now() - ms;
  const min = Math.round(diff / 60000);
  if (min < 1) return "just now";
  if (min < 60) return `${min}m`;
  const h = Math.round(min / 60);
  if (h < 24) return `${h}h`;
  const d = Math.round(h / 24);
  return `${d}d`;
}
