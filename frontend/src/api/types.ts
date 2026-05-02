export interface DevLoginResponse {
  accessToken: string;
  accountId: string;
  email: string;
}

export interface MoneyAmount {
  amount: number;
  currency: string;
}

export interface PortfolioView {
  accountId: string;
  baseCurrency: string;
  positionCount: number;
  tradeCount: number;
  totalInvested: MoneyAmount;
  foreignTotals: MoneyAmount[];
}

export interface ImportResult {
  fileName: string;
  brokerKey: string;
  tradesImported: number;
  tradesSkipped: number;
  warnings: string[];
  rawTextSample: string | null;
}

export interface PortfolioSummary {
  totalInvested: MoneyAmount;
  totalCurrentValue: MoneyAmount;
  totalUnrealizedPnl: MoneyAmount;
  totalRealizedPnl: MoneyAmount;
  totalPnl: MoneyAmount;
  openPositionCount: number;
  tradeCount: number;
  valuationCoverage: number;
  foreignTotalsInvested: MoneyAmount[];
}

export type TradeSide = "Buy" | "Sell";

export interface PositionTrade {
  id: string;
  executedAt: string;
  side: TradeSide;
  quantity: number;
  price: MoneyAmount;
  fees: MoneyAmount;
  brokerKey: string;
  brokerReference: string | null;
}

export interface Position {
  symbol: string;
  name: string | null;
  quantity: number;
  avgCost: MoneyAmount;
  totalCost: MoneyAmount;
  realizedPnl: MoneyAmount;
  valued: boolean;
  currentPrice: MoneyAmount | null;
  currentValue: MoneyAmount | null;
  unrealizedPnl: MoneyAmount | null;
  unrealizedPnlPercent: number | null;
  estimatedSellCommission: MoneyAmount | null;
  netCurrentValue: MoneyAmount | null;
  netUnrealizedPnl: MoneyAmount | null;
  tradeCount: number;
  firstTradeAt: string | null;
  lastTradeAt: string | null;
  trades: PositionTrade[];
}

export interface PositionsView {
  accountId: string;
  asOf: string;
  baseCurrency: string;
  summary: PortfolioSummary;
  positions: Position[];
}

export interface ApiError {
  code: string;
  detail?: string;
}

export class ApiException extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    public readonly detail?: string,
  ) {
    super(detail ?? code);
    this.name = "ApiException";
  }
}
