import { useCallback, useRef, useState, type DragEvent } from "react";
import { api } from "../api/client";
import { ApiException, type ImportResult } from "../api/types";
import { useAuth } from "../auth/AuthContext";

interface Props {
  onImported: () => void;
}

export function ImportTrades({ onImported }: Props) {
  const { token } = useAuth();
  const inputRef = useRef<HTMLInputElement>(null);
  const [pending, setPending] = useState(false);
  const [results, setResults] = useState<ImportResult[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [dragActive, setDragActive] = useState(false);

  const handleFiles = useCallback(
    async (files: FileList | File[] | null) => {
      if (!files || !token) return;
      const list = Array.from(files).filter(
        (f) => f.type === "application/pdf" || f.name.toLowerCase().endsWith(".pdf"),
      );
      if (list.length === 0) {
        setError("Only PDF files are accepted.");
        return;
      }
      setPending(true);
      setError(null);
      const newResults: ImportResult[] = [];
      for (const file of list) {
        try {
          const res = await api.importTradeRepublicPdf(token, file);
          newResults.push(res);
        } catch (err) {
          const msg =
            err instanceof ApiException
              ? `${err.code}${err.detail ? ` — ${err.detail}` : ""}`
              : err instanceof Error
                ? err.message
                : "Unexpected error";
          newResults.push({
            fileName: file.name,
            brokerKey: "trade-republic",
            tradesImported: 0,
            tradesSkipped: 0,
            warnings: [msg],
            rawTextSample: null,
          });
          setError(msg);
        }
      }
      setResults((prev) => [...newResults, ...prev]);
      setPending(false);
      if (inputRef.current) inputRef.current.value = "";
      onImported();
    },
    [token, onImported],
  );

  const onDragOver = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(true);
  };

  const onDragLeave = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(false);
  };

  const onDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(false);
    if (e.dataTransfer.files?.length) {
      void handleFiles(e.dataTransfer.files);
    }
  };

  return (
    <section className="import-section">
      <h3>Import Trade Republic PDFs</h3>
      <p className="muted">
        Drop your <code>Wertpapierabrechnung</code> or
        <code> LIQUIDACIÓN DE TRANSACCIÓN</code> PDFs below.
      </p>

      <div
        className={`dropzone${dragActive ? " is-active" : ""}${pending ? " is-pending" : ""}`}
        onDragOver={onDragOver}
        onDragEnter={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
        onClick={() => inputRef.current?.click()}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") inputRef.current?.click();
        }}
      >
        <input
          ref={inputRef}
          type="file"
          accept="application/pdf,.pdf"
          multiple
          disabled={pending}
          onChange={(e) => void handleFiles(e.target.files)}
        />
        <div className="dropzone-message">
          {pending ? (
            <>Uploading…</>
          ) : dragActive ? (
            <>Drop to import</>
          ) : (
            <>
              <strong>Drag &amp; drop PDFs here</strong>
              <span className="muted"> or click to browse</span>
            </>
          )}
        </div>
      </div>

      {error ? <p className="error">{error}</p> : null}
      {results.length > 0 ? (
        <ul className="import-list">
          {results.map((r, idx) => (
            <li key={`${r.fileName}-${idx}`}>
              <strong>{r.fileName}</strong> — {r.tradesImported} imported
              {r.tradesSkipped > 0 ? `, ${r.tradesSkipped} skipped (duplicate)` : ""}
              {r.warnings.length > 0 ? (
                <ul className="warnings">
                  {r.warnings.map((w, i) => (
                    <li key={i}>{w}</li>
                  ))}
                </ul>
              ) : null}
              {r.rawTextSample ? (
                <details className="raw-sample">
                  <summary>Show extracted text (debug)</summary>
                  <pre>{r.rawTextSample}</pre>
                </details>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
