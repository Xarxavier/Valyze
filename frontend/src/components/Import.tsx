import { useCallback, useState } from "react";
import { useUi } from "../ui/UiContext";
import { ImportTrades } from "./ImportTrades";

/**
 * Import landing page. Lists available data sources and renders the
 * importer for the active one. v1 ships only the Trade Republic PDF
 * adapter; more sources (CSV, broker APIs) plug in by adding entries
 * to the `sources` array and wiring their renderers below.
 */

type SourceStatus = "available" | "coming-soon";

interface DataSource {
  id: string;
  displayName: string;
  description: string;
  status: SourceStatus;
}

const sources: DataSource[] = [
  {
    id: "trade-republic-pdf",
    displayName: "Trade Republic — PDFs",
    description:
      "Drop your Wertpapierabrechnung or LIQUIDACIÓN PDFs. Settlements and EX-ANTE pre-trade disclosures are both supported.",
    status: "available",
  },
  {
    id: "broker-api",
    displayName: "Broker API",
    description: "OAuth into an authorised broker (DEGIRO, IBKR, …). Coming soon.",
    status: "coming-soon",
  },
  {
    id: "csv",
    displayName: "Custom CSV",
    description: "Upload a CSV exported from any broker, mapped to a fixed schema. Coming soon.",
    status: "coming-soon",
  },
];

export function Import() {
  const { goHome, bumpPortfolioReload } = useUi();
  const [activeId, setActiveId] = useState<string>(
    () => sources.find((s) => s.status === "available")?.id ?? sources[0]?.id ?? "",
  );

  // After a successful import, refresh the dashboard cache and send the
  // user back to home so they see the new positions immediately.
  const onImported = useCallback(() => {
    bumpPortfolioReload();
    goHome();
  }, [bumpPortfolioReload, goHome]);

  const active = sources.find((s) => s.id === activeId);

  return (
    <div className="import-page">
      <header className="import-header">
        <h2>Import data</h2>
        <p className="muted">Pick a source to bring trades into Valyze.</p>
      </header>

      <div className="import-sources">
        {sources.map((s) => {
          const disabled = s.status === "coming-soon";
          const isActive = s.id === activeId;
          return (
            <button
              key={s.id}
              type="button"
              className={`import-source-card ${isActive ? "is-active" : ""} ${
                disabled ? "is-disabled" : ""
              }`}
              onClick={() => !disabled && setActiveId(s.id)}
              disabled={disabled}
              aria-pressed={isActive}
            >
              <div className="import-source-title">
                {s.displayName}
                {disabled ? <span className="import-source-tag">soon</span> : null}
              </div>
              <p className="import-source-desc">{s.description}</p>
            </button>
          );
        })}
      </div>

      <div className="import-active">
        {active?.id === "trade-republic-pdf" ? (
          <ImportTrades onImported={onImported} />
        ) : (
          <p className="muted">This source is not available yet.</p>
        )}
      </div>
    </div>
  );
}
