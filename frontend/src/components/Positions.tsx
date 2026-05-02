import { useEffect, useState } from "react";
import { api } from "../api/client";
import {
  ApiException,
  type MoneyAmount,
  type Position,
  type PositionsView,
} from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { useUi } from "../ui/UiContext";

function formatMoney(m: MoneyAmount | null | undefined): string {
  if (!m) return "—";
  try {
    return new Intl.NumberFormat(undefined, {
      style: "currency",
      currency: m.currency,
      maximumFractionDigits: 2,
    }).format(m.amount);
  } catch {
    return `${m.amount.toFixed(2)} ${m.currency}`;
  }
}

function formatSignedMoney(m: MoneyAmount | null | undefined): string {
  if (!m) return "—";
  const sign = m.amount > 0 ? "+" : "";
  return `${sign}${formatMoney(m)}`;
}

function formatPercent(p: number | null | undefined): string {
  if (p === null || p === undefined) return "—";
  const sign = p > 0 ? "+" : "";
  return `${sign}${p.toFixed(2)}%`;
}

function formatQty(q: number): string {
  if (q === 0) return "0";
  if (Math.abs(q) >= 1) return q.toFixed(4).replace(/\.?0+$/, "");
  return q.toFixed(8).replace(/\.?0+$/, "");
}

function formatDate(s: string | null | undefined): string {
  if (!s) return "—";
  try {
    return new Date(s).toLocaleDateString();
  } catch {
    return s;
  }
}

function formatDateTime(s: string | null | undefined): string {
  if (!s) return "—";
  try {
    return new Date(s).toLocaleString(undefined, {
      year: "numeric",
      month: "short",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return s;
  }
}

function pnlClass(amount: number | null | undefined): string {
  if (amount === null || amount === undefined) return "";
  if (amount > 0) return "pnl-positive";
  if (amount < 0) return "pnl-negative";
  return "";
}

/**
 * Money cell that respects the global privacy toggle. When `hideAmounts`
 * is on, the actual value is replaced with a neutral mask. P&L percentages
 * stay visible so the user still gets directional context — that's the
 * whole point of "hide absolute amounts, keep ratios".
 */
function Money({
  m,
  signed = false,
  fallback = "—",
}: {
  m: MoneyAmount | null | undefined;
  signed?: boolean;
  fallback?: string;
}) {
  const { hideAmounts } = useUi();
  if (hideAmounts) {
    if (!m) return <span className="amount-masked">•••</span>;
    return <span className="amount-masked">•••</span>;
  }
  if (!m) return <>{fallback}</>;
  return <>{signed ? formatSignedMoney(m) : formatMoney(m)}</>;
}

interface Props {
  reloadKey: number;
}

export function Positions({ reloadKey }: Props) {
  const { token } = useAuth();
  const [view, setView] = useState<PositionsView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [detail, setDetail] = useState<Position | null>(null);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    setLoading(true);
    api
      .getPositions(token)
      .then((data) => {
        if (!cancelled) {
          setView(data);
          setError(null);
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        if (err instanceof ApiException) {
          setError(`${err.code}${err.detail ? ` — ${err.detail}` : ""}`);
        } else {
          setError(err instanceof Error ? err.message : "Unexpected error");
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [token, reloadKey]);

  if (loading && !view) return <p className="muted">Loading positions…</p>;
  if (error) return <p className="error">{error}</p>;
  if (!view) return null;

  const { positions } = view;
  const open = positions.filter((p) => p.quantity > 0);
  const closed = positions.filter((p) => p.quantity === 0);

  return (
    <>
      <SummaryTiles view={view} />

      {open.length > 0 ? (
        <section className="positions">
          <h3>Open positions</h3>
          <div className="positions-table positions-table-compact">
            <div className="positions-row positions-row-compact positions-head">
              <span>Asset</span>
              <span>Invested</span>
              <span>Current</span>
              <span title="Current value minus the broker's sell commission">Net</span>
              <span>P&amp;L</span>
              <span>%</span>
              <span></span>
            </div>
            {open.map((p) => (
              <div className="positions-row positions-row-compact" key={p.symbol}>
                <span className="positions-symbol">
                  <strong>{p.name ?? p.symbol}</strong>
                  {p.name ? <small>{p.symbol}</small> : null}
                </span>
                <span>
                  <Money m={p.totalCost} />
                </span>
                <span>{p.valued ? <Money m={p.currentValue} /> : <em>no quote</em>}</span>
                <span>{p.valued ? <Money m={p.netCurrentValue} /> : "—"}</span>
                <span className={pnlClass(p.netUnrealizedPnl?.amount)}>
                  {p.valued ? <Money m={p.netUnrealizedPnl} signed /> : "—"}
                </span>
                <span className={pnlClass(p.netUnrealizedPnl?.amount)}>
                  {p.valued && p.netUnrealizedPnl && p.totalCost.amount !== 0
                    ? formatPercent((p.netUnrealizedPnl.amount / p.totalCost.amount) * 100)
                    : "—"}
                </span>
                <button
                  type="button"
                  className="position-details-btn"
                  onClick={() => setDetail(p)}
                  aria-label={`Show details for ${p.name ?? p.symbol}`}
                >
                  Details
                </button>
              </div>
            ))}
          </div>
        </section>
      ) : (
        <section className="positions">
          <h3>Open positions</h3>
          <p className="muted">No open positions yet. Import some trades to get started.</p>
        </section>
      )}

      {closed.length > 0 ? (
        <section className="positions">
          <h3>Closed positions (realized P&amp;L)</h3>
          <div className="positions-table">
            <div className="positions-row positions-head positions-head-closed">
              <span>Asset</span>
              <span>Trades</span>
              <span>Realized</span>
              <span></span>
            </div>
            {closed.map((p) => (
              <div className="positions-row positions-row-closed" key={p.symbol}>
                <span className="positions-symbol">
                  <strong>{p.name ?? p.symbol}</strong>
                  {p.name ? <small>{p.symbol}</small> : null}
                </span>
                <span>{p.tradeCount}</span>
                <span className={pnlClass(p.realizedPnl.amount)}>
                  <Money m={p.realizedPnl} signed />
                </span>
                <button
                  type="button"
                  className="position-details-btn"
                  onClick={() => setDetail(p)}
                  aria-label={`Show details for ${p.name ?? p.symbol}`}
                >
                  Details
                </button>
              </div>
            ))}
          </div>
        </section>
      ) : null}

      {detail ? <PositionDetailDialog position={detail} onClose={() => setDetail(null)} /> : null}
    </>
  );
}

interface DialogProps {
  position: Position;
  onClose: () => void;
}

function PositionDetailDialog({ position: p, onClose }: DialogProps) {
  // Close on Escape
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onClose]);

  return (
    <div className="modal-overlay" onClick={onClose} role="presentation">
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="modal-title"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <div>
            <h3 id="modal-title">{p.name ?? p.symbol}</h3>
            {p.name ? <code className="modal-symbol">{p.symbol}</code> : null}
          </div>
          <button
            type="button"
            className="modal-close"
            onClick={onClose}
            aria-label="Close details"
          >
            ×
          </button>
        </div>

        <div className="modal-body">
          <Section title="Position">
            <KV label="Quantity">{formatQty(p.quantity)}</KV>
            <KV label="Avg cost">
              <Money m={p.avgCost} />
            </KV>
            <KV label="Total invested">
              <Money m={p.totalCost} />
            </KV>
          </Section>

          <Section title="Market">
            <KV label="Current price (native)">
              {p.valued ? <Money m={p.currentPrice} /> : "no quote"}
            </KV>
            <KV label="Current value (gross)">
              {p.valued ? <Money m={p.currentValue} /> : "—"}
            </KV>
            <KV label="Estimated sell commission">
              {p.estimatedSellCommission && p.estimatedSellCommission.amount > 0 ? (
                <>
                  − <Money m={p.estimatedSellCommission} />
                </>
              ) : (
                "—"
              )}
            </KV>
            <KV label="Net current value">
              <strong>{p.valued ? <Money m={p.netCurrentValue} /> : "—"}</strong>
            </KV>
          </Section>

          <Section title="P&L">
            <KV label="Unrealized (gross)" className={pnlClass(p.unrealizedPnl?.amount)}>
              {p.valued ? <Money m={p.unrealizedPnl} signed /> : "—"}
            </KV>
            <KV
              label="Unrealized (net of sell commission)"
              className={pnlClass(p.netUnrealizedPnl?.amount)}
            >
              {p.valued ? <Money m={p.netUnrealizedPnl} signed /> : "—"}
            </KV>
            <KV label="Unrealized %" className={pnlClass(p.netUnrealizedPnl?.amount)}>
              {p.valued && p.netUnrealizedPnl && p.totalCost.amount !== 0
                ? formatPercent((p.netUnrealizedPnl.amount / p.totalCost.amount) * 100)
                : "—"}
            </KV>
            <KV label="Realized" className={pnlClass(p.realizedPnl.amount)}>
              <Money m={p.realizedPnl} signed />
            </KV>
          </Section>

          <Section title="History">
            <KV label="Trades">{p.tradeCount}</KV>
            <KV label="First trade">{formatDate(p.firstTradeAt)}</KV>
            <KV label="Last trade">{formatDate(p.lastTradeAt)}</KV>
          </Section>

          {p.trades.length > 0 ? (
            <section className="modal-section">
              <h4>Trade history</h4>
              <div className="trade-history">
                <div className="trade-row trade-head">
                  <span>Date</span>
                  <span>Side</span>
                  <span>Quantity</span>
                  <span>Price</span>
                  <span>Fees</span>
                </div>
                {p.trades.map((t) => (
                  <div className="trade-row" key={t.id}>
                    <span>{formatDateTime(t.executedAt)}</span>
                    <span className={t.side === "Buy" ? "trade-buy" : "trade-sell"}>
                      {t.side}
                    </span>
                    <span>{formatQty(t.quantity)}</span>
                    <span>
                      <Money m={t.price} />
                    </span>
                    <span>
                      <Money m={t.fees} />
                    </span>
                  </div>
                ))}
              </div>
            </section>
          ) : null}
        </div>

        <div className="modal-footer">
          <button type="button" className="button-ghost" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

/**
 * Top-of-dashboard tiles. P&L is shown both as money (masked when privacy
 * is on) and as a percentage of total invested — the percent stays visible
 * either way, which is the whole point of the privacy toggle.
 */
function SummaryTiles({ view }: { view: PositionsView }) {
  const { hideAmounts } = useUi();
  const { summary } = view;

  const totalCost = summary.totalInvested.amount;
  const pnlPct =
    totalCost !== 0 ? Math.round((summary.totalPnl.amount / totalCost) * 10000) / 100 : null;
  const unrealizedPct =
    totalCost !== 0
      ? Math.round((summary.totalUnrealizedPnl.amount / totalCost) * 10000) / 100
      : null;
  const realizedPct =
    totalCost !== 0
      ? Math.round((summary.totalRealizedPnl.amount / totalCost) * 10000) / 100
      : null;

  return (
    <section className="summary-grid">
      <div className="summary-tile">
        <div className="summary-label">Invested</div>
        <div className="summary-value">
          <Money m={summary.totalInvested} />
        </div>
      </div>
      <div className="summary-tile">
        <div className="summary-label">Current value</div>
        <div className="summary-value">
          <Money m={summary.totalCurrentValue} />
        </div>
        {summary.valuationCoverage < 1 ? (
          <div className="summary-hint">
            {Math.round(summary.valuationCoverage * 100)}% priced
          </div>
        ) : null}
      </div>
      <div className="summary-tile">
        <div className="summary-label">Total P&amp;L</div>
        <div className={`summary-value ${pnlClass(summary.totalPnl.amount)}`}>
          {hideAmounts ? formatPercent(pnlPct) : formatSignedMoney(summary.totalPnl)}
        </div>
        <div className="summary-hint">
          {hideAmounts ? (
            <>
              <span className={pnlClass(summary.totalUnrealizedPnl.amount)}>
                {formatPercent(unrealizedPct)}
              </span>{" "}
              unrealized,{" "}
              <span className={pnlClass(summary.totalRealizedPnl.amount)}>
                {formatPercent(realizedPct)}
              </span>{" "}
              realized
            </>
          ) : (
            <>
              <span className={pnlClass(summary.totalUnrealizedPnl.amount)}>
                {formatSignedMoney(summary.totalUnrealizedPnl)}
              </span>{" "}
              unrealized,{" "}
              <span className={pnlClass(summary.totalRealizedPnl.amount)}>
                {formatSignedMoney(summary.totalRealizedPnl)}
              </span>{" "}
              realized
            </>
          )}
        </div>
      </div>
      <div className="summary-tile">
        <div className="summary-label">Open positions</div>
        <div className="summary-value">{summary.openPositionCount}</div>
        <div className="summary-hint">{summary.tradeCount} trades</div>
      </div>
    </section>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="modal-section">
      <h4>{title}</h4>
      <dl className="modal-kv">{children}</dl>
    </section>
  );
}

function KV({
  label,
  className,
  children,
}: {
  label: string;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <>
      <dt>{label}</dt>
      <dd className={className}>{children}</dd>
    </>
  );
}
