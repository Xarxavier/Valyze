using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Valyze.Domain.Application.Ingestion;
using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Enum;
using Valyze.Domain.Instruments;
using Valyze.Domain.Money;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Infraestructure.Brokers.TradeRepublic;

/// <summary>
/// Parses Trade Republic transaction confirmations (German Wertpapierabrechnung +
/// Spanish Liquidación de Transacción). Stores the asset symbol verbatim — real
/// ISIN for securities, ticker for crypto — into <see cref="TradeEntity.Instrument"/>.
/// </summary>
public class TradeRepublicPdfParser : IBrokerAdapter
{
    public const string Key = "trade-republic";

    private static readonly CultureInfo EuropeanLocale = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Regex IsinRegex = new(@"\b([A-Z]{2}[A-Z0-9]{9}[0-9])\b", RegexOptions.Compiled);

    private static readonly Regex DeQuantity = new(@"St(?:ü|u)ck\s+([\d.,]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DeUnitPrice = new(@"St(?:ü|u)ckkurs\s+([\d.,]+)\s+([A-Z]{3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DeDate = new(@"Ausf(?:ü|u)hrungstag\s+(\d{2}\.\d{2}\.\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] DeFeeLabels = ["Provision", "Fremdkostenzuschlag", "Externe Kosten", "Maklercourtage"];

    // Matches the crypto position line in natural reading order. Name is NOT
    // captured here — extracted in a separate pass, see ExtractNameBeforeMarker.
    //   "Bitcoin (BTC) 0,007611 65.691,66 EUR 499,98 EUR"
    // Groups: 1=ticker, 2=quantity, 3=unit price, 4=price ccy, 5=gross amount, 6=amount ccy.
    private static readonly Regex EsCryptoLine = new(
        @"\(\s*([A-Z0-9]{2,8})\s*\)\s+([\d.,]+)\s+([\d.,]+)\s+([A-Z]{3})\s+([\d.,]+)\s+([A-Z]{3})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Asset name on the line directly above an ISIN line — used by EX-ANTE
    /// documents where the name and ISIN are visually stacked.
    ///   "Bitcoin"
    ///   "ISIN: XF000BTC0017 Comprar 0.004296 tít. ..."
    /// Accepts both upper and lowercase first letters because some funds use
    /// lowercase camel case ("iShares Japan Index D EUR Acc"). Header labels
    /// that look like asset names are filtered post-match in IsHeaderLabel.
    /// </summary>
    private static readonly Regex NameAboveIsin = new(
        @"(?m)^([A-Za-z][^\r\n]{0,80}?)\s*\r?\n\s*ISIN:\s*[A-Z0-9]{12}",
        RegexOptions.Compiled);

    private static readonly Regex EsDate = new(
        @"(?:Comprar|Vender)\s+el\s+(\d{2}\.\d{2}\.\d{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EsCryptoFees = new(
        @"Costes del servicio de ejecuci[óo]n de terceros\s+(-?[\d.,]+)\s+([A-Z]{3})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ExecutionId = new(
        @"EJECUCI[ÓO]N\s+([A-Za-z0-9._:-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OrderId = new(
        @"ORDEN\s+([A-Za-z0-9._:-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // EX-ANTE document patterns — pre-trade cost disclosure, but contains everything
    // needed to record an intended trade. Distinct from a settlement (LIQUIDACIÓN).
    // Two title shapes seen in the wild:
    //   "INFORMACIÓN DE COSTES EX-ANTE SOBRE LA COMPRA DE CRIPTOMONEDAS"
    //   "INFORMACIÓN DE COSTES, GASTOS E INCENTIVOS EX ANTE DE COMPRA DE VALORES"
    // Lazy `.*?` between landmarks tolerates either wording.
    private static readonly Regex ExAnteTitle = new(
        @"INFORMACI[ÓO]N\s+DE\s+COSTES.*?EX[\s-]?ANTE.*?(COMPRA|VENTA)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // Securities EX-ANTE row, e.g. "ISIN: US8740391003 Comprar 0.831946 tít. 249,5838 €".
    // Quantity uses invariant locale (dot decimal); notional uses Spanish (comma decimal).
    private static readonly Regex ExAnteValoresRow = new(
        @"ISIN:\s*([A-Z]{2}[A-Z0-9]{9}[0-9])\s+(?:Comprar|Vender)\s+([\d.,]+)\s+t[íi]t\.\s+([\d.,]+)\s*€",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "Comprar 0.004296 tít. 248,989587 €" — qty + total notional in one line.
    private static readonly Regex ExAnteRow = new(
        @"(Comprar|Vender)\s+([\d.,]+)\s+t[íi]t\.\s+([\d.,]+)\s*€",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // TR's synthetic crypto ISIN format: XF + 3 digits + ticker (2-5 letters) + 2-5 digits.
    // Example: XF000BTC0017 → BTC; XF000USDT001 → USDT.
    private static readonly Regex SyntheticCryptoIsin = new(
        @"\bXF\d{3}([A-Z]{2,5})\d{2,5}\b",
        RegexOptions.Compiled);

    private static readonly Regex ExAnteFee = new(
        @"Tarifa\s+plana\s+por\s+costes\s+del\s+servicio\s+de\s+ejecuci[óo]n\s+de\s+terceros\s+([\d.,]+)\s*€",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DocFecha = new(
        @"FECHA\s+(\d{2}\.\d{2}\.\d{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Spanish securities settlement (LIQUIDACIÓN DE VALORES) — stocks/ETFs/ADRs.
    // Row example after extraction: "TSMC (ADR) ISIN: US8740391003 1,650165 303,00 EUR 500,00 EUR"
    // Name is NOT captured here; extracted post-match via ExtractNameBeforeMarker.
    // Groups: 1=ISIN, 2=qty, 3=price, 4=price ccy, 5=total, 6=total ccy.
    private static readonly Regex EsValoresRow = new(
        @"ISIN:\s*([A-Z]{2}[A-Z0-9]{9}[0-9])\s+([\d.,]+)\s+([\d.,]+)\s+([A-Z]{3})\s+([\d.,]+)\s+([A-Z]{3})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Side: "Market-Order Comprar", "Limit-Order Vender", "Stop-Order Comprar", etc.
    private static readonly Regex EsValoresSide = new(
        @"\b(?:Market|Limit|Stop)-Order\s+(Comprar|Vender)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Execution date in the description: "Comprar a día 03.03.2026" / "Vender a día ..."
    private static readonly Regex EsValoresDate = new(
        @"(?:Comprar|Vender)\s+a\s+d[ií]a\s+(\d{2}\.\d{2}\.\d{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogger<TradeRepublicPdfParser> _logger;

    public TradeRepublicPdfParser(ILogger<TradeRepublicPdfParser> logger)
    {
        _logger = logger;
    }

    public string BrokerKey => Key;

    public async Task<BrokerParseResult> ParseAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var text = ExtractText(buffer);

        // Non-trade documents that share keywords with settlements but contain no
        // useful trade payload (account statements, tax certificates).
        if (IsNonTradeDocument(text, out var docKind))
        {
            return new BrokerParseResult
            {
                Trades = [],
                Warnings = [$"{fileName}: Skipped — this is a {docKind}, not an executed transaction."],
                RawTextSample = null,
            };
        }

        // Routing in priority order:
        //   1. EX-ANTE cost disclosure (crypto OR securities) — contains a trade payload.
        //   2. LIQUIDACIÓN crypto (Spanish settlement)
        //   3. LIQUIDACIÓN valores (Spanish securities settlement)
        //   4. Wertpapierabrechnung (German settlement)
        var exAnteMatch = ExAnteTitle.Match(text);
        if (exAnteMatch.Success)
        {
            var verb = exAnteMatch.Groups[1].Value;
            var isExAnteCrypto = Regex.IsMatch(text, @"CRIPTOMONEDAS|CRIPTOMONEDA", RegexOptions.IgnoreCase);
            return isExAnteCrypto
                ? ParseSpanishCryptoExAnte(text, fileName, verb)
                : ParseSpanishValoresExAnte(text, fileName, verb);
        }

        var isCrypto = Regex.IsMatch(text, @"CRIPTOMONEDAS|CRIPTOMONEDA", RegexOptions.IgnoreCase);
        var isSpanishValores = Regex.IsMatch(text, @"LIQUIDACI[ÓO]N\s+DE\s+VALORES", RegexOptions.IgnoreCase);
        var isGermanSec = text.Contains("Wertpapierabrechnung", StringComparison.OrdinalIgnoreCase);

        if (isCrypto) return ParseSpanishCrypto(text, fileName);
        if (isSpanishValores) return ParseSpanishValores(text, fileName);
        if (isGermanSec) return ParseGermanSecurities(text, fileName);

        return Skipped(fileName, text,
            "Unrecognized Trade Republic document format. Supported in v1: 'Wertpapierabrechnung' (DE), 'LIQUIDACIÓN DE TRANSACCIÓN CON CRIPTOMONEDAS' (ES), 'LIQUIDACIÓN DE VALORES' (ES) and 'INFORMACIÓN DE COSTES EX-ANTE SOBRE LA COMPRA/VENTA DE CRIPTOMONEDAS' (ES).");
    }

    private BrokerParseResult ParseSpanishValores(string text, string fileName)
    {
        var sideMatch = EsValoresSide.Match(text);
        if (!sideMatch.Success)
            return Skipped(fileName, text,
                "Could not determine Comprar/Vender (looking for 'Market-Order Comprar' or similar).");
        var side = sideMatch.Groups[1].Value.Equals("Vender", StringComparison.OrdinalIgnoreCase)
            ? TradeSide.Sell
            : TradeSide.Buy;

        var rowMatch = EsValoresRow.Match(text);
        if (!rowMatch.Success)
            return Skipped(fileName, text,
                "Could not parse the position row ('ISIN: <ISIN> QTY PRICE CCY TOTAL CCY').");

        var dateMatch = EsValoresDate.Match(text);
        if (!dateMatch.Success)
        {
            // Fall back to the FECHA at the document header.
            dateMatch = DocFecha.Match(text);
            if (!dateMatch.Success)
                return Skipped(fileName, text, "Missing execution date.");
        }

        try
        {
            // Groups: 1=ISIN, 2=qty, 3=price, 4=ccy, 5=total, 6=ccy.
            var isin = rowMatch.Groups[1].Value;
            var quantity = ParseEuropeanDecimal(rowMatch.Groups[2].Value);
            var priceAmount = ParseEuropeanDecimal(rowMatch.Groups[3].Value);
            var priceCurrency = new Currency(rowMatch.Groups[4].Value);
            var price = new MoneyValue(priceAmount, priceCurrency);
            var fees = ParseSpanishCryptoFees(text, priceCurrency);
            var executedAt = ParseEuropeanDate(dateMatch.Groups[1].Value);
            var reference = ExtractBrokerReference(text);

            // Two LIQUIDACIÓN layouts seen in the wild:
            //   single-line: "TSMC (ADR) ISIN: US8740391003 1,650165 303,00 EUR ..."
            //   multi-line:  "Unity Software\nISIN: US91332U1016\n6,411078 15,598 EUR ..."
            // First try the single-line case (name immediately before "ISIN:" on the same
            // line); if that's empty, fall back to "name on the line directly above ISIN".
            var name = ExtractNameBeforeMarker(text, rowMatch.Index)
                ?? ExtractNameAboveIsin(text);

            var trade = BuildTrade(
                new InstrumentRef(isin),
                side,
                quantity,
                price,
                fees,
                executedAt,
                reference,
                name);

            _logger.LogInformation(
                "Parsed ES securities TR trade from {File}: {Side} {Qty} {Isin} ({Name}) @ {Price} (ref {Ref})",
                fileName, side, quantity, isin, name, price, reference);

            return new BrokerParseResult { Trades = [trade] };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return Skipped(fileName, text, $"Field conversion failed: {ex.Message}");
        }
    }

    private BrokerParseResult ParseSpanishCryptoExAnte(string text, string fileName, string headerVerb)
    {
        var side = headerVerb.Equals("VENTA", StringComparison.OrdinalIgnoreCase)
            ? TradeSide.Sell
            : TradeSide.Buy;

        var rowMatch = ExAnteRow.Match(text);
        if (!rowMatch.Success)
            return Skipped(fileName, text, "Could not parse EX-ANTE position row ('Comprar/Vender QTY tít. NOTIONAL €').");

        var dateMatch = DocFecha.Match(text);
        if (!dateMatch.Success)
            return Skipped(fileName, text, "Missing document date ('FECHA DD.MM.YYYY').");

        var isinMatch = SyntheticCryptoIsin.Match(text);
        if (!isinMatch.Success)
            return Skipped(fileName, text, "Could not extract crypto ticker from synthetic ISIN (expected XF\\d{3}<TICKER>\\d{2,5}).");

        try
        {
            var ticker = isinMatch.Groups[1].Value;
            // EX-ANTE quirk: TR formats the crypto quantity in invariant ("0.004296") —
            // crypto-native dot decimals — while monetary amounts on the same row keep
            // Spanish locale ("248,989587"). Parse them with the right culture each.
            var quantity = decimal.Parse(rowMatch.Groups[2].Value, NumberStyles.Number, CultureInfo.InvariantCulture);
            var notional = ParseEuropeanDecimal(rowMatch.Groups[3].Value);
            if (quantity <= 0)
                return Skipped(fileName, text, "Parsed quantity is zero — refusing to ingest a no-op trade.");

            var unitPrice = notional / quantity;
            var currency = new Currency("EUR");
            var price = new MoneyValue(unitPrice, currency);

            // The first "Tarifa plana" matches the BUY costs section; for SELL EX-ANTE
            // documents this is also the relevant fee (the second occurrence is informational
            // about the future opposite side).
            var feeMatch = ExAnteFee.Match(text);
            var fees = feeMatch.Success
                ? new MoneyValue(ParseEuropeanDecimal(feeMatch.Groups[1].Value), currency)
                : new MoneyValue(0m, currency);

            var executedAt = ParseEuropeanDate(dateMatch.Groups[1].Value);
            var reference = ExtractExAnteReference(text);
            var rawName = ExtractNameAboveIsin(text);
            var displayName = string.IsNullOrEmpty(rawName) ? null : $"{rawName} ({ticker.ToUpperInvariant()})";

            var trade = BuildTrade(
                new InstrumentRef(ticker),
                side,
                quantity,
                price,
                fees,
                executedAt,
                reference,
                displayName);

            _logger.LogInformation(
                "Parsed ES EX-ANTE TR trade from {File}: {Side} {Qty} {Ticker} ({Name}) @ {Price} (ref {Ref})",
                fileName, side, quantity, ticker, displayName, price, reference);

            return new BrokerParseResult
            {
                Trades = [trade],
                Warnings =
                [
                    $"{fileName}: Imported from a pre-trade cost disclosure (EX-ANTE). If the corresponding settlement (LIQUIDACIÓN) PDF arrives later for the same order, it will import as a separate trade — delete one of the two to avoid double-counting.",
                ],
                RawTextSample = null,
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return Skipped(fileName, text, $"Field conversion failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the asset name from the text on the same line as a row match,
    /// up to (but not including) the matched marker (e.g. "ISIN:" or "(BTC)").
    /// Used by LIQUIDACIÓN documents where the row layout is one-line:
    ///   "TSMC (ADR) ISIN: US8740391003 ..."
    ///   "Bitcoin (BTC) 0,007611 ..."
    /// </summary>
    private static string? ExtractNameBeforeMarker(string text, int markerIndex)
    {
        if (markerIndex <= 0) return null;
        // Walk back to the start of the line containing the marker.
        var lineStart = text.LastIndexOf('\n', markerIndex - 1);
        if (lineStart < 0) lineStart = 0;
        else lineStart++; // skip the newline itself

        var prefix = text.Substring(lineStart, markerIndex - lineStart).Trim();
        if (string.IsNullOrEmpty(prefix)) return null;

        // Drop common label prefixes that may precede the name.
        prefix = StripLabelPrefix(prefix);

        // Reject obvious column-header artifacts.
        if (IsHeaderLabel(prefix)) return null;

        return NormalizeName(prefix);
    }

    private static string StripLabelPrefix(string s)
    {
        // "Wertpapierbezeichnung: Apple Inc." → "Apple Inc."
        var colonIdx = s.IndexOf(':');
        if (colonIdx > 0 && colonIdx < s.Length - 1)
        {
            var afterColon = s[(colonIdx + 1)..].Trim();
            if (!string.IsNullOrEmpty(afterColon)) return afterColon;
        }
        return s;
    }

    private static bool IsHeaderLabel(string s)
    {
        // Common column headers that may bleed into name extraction.
        var normalized = s.Trim().ToUpperInvariant();
        return normalized switch
        {
            "POSICIÓN" or "POSICION" or "POSITION" => true,
            "INSTRUMENTO" or "INSTRUMENT" => true,
            "VALOR DE CRIPTOMONEDAS" => true,
            "CANTIDAD" or "PRECIO" or "IMPORTE" => true,
            "POSICIÓN CANTIDAD PRECIO IMPORTE" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Looks for an asset name on the line directly above an ISIN line — the
    /// shape used by EX-ANTE crypto/valores documents and German Wertpapier-
    /// abrechnungen ("Wertpapierbezeichnung" header).
    /// </summary>
    private static string? ExtractNameAboveIsin(string text)
    {
        var match = NameAboveIsin.Match(text);
        if (!match.Success) return null;
        var name = match.Groups[1].Value.Trim();
        // Reject obvious headers that happened to land directly above the ISIN
        if (name.Equals("INSTRUMENTO", StringComparison.OrdinalIgnoreCase)) return null;
        if (name.Equals("POSICIÓN", StringComparison.OrdinalIgnoreCase)) return null;
        if (name.Equals("VALOR DE CRIPTOMONEDAS", StringComparison.OrdinalIgnoreCase)) return null;
        return NormalizeName(name);
    }

    /// <summary>
    /// EX-ANTE has only ORDEN, no EJECUCIÓN. We tag the reference with an "exante:" prefix
    /// so it never collides with a settlement that uses "{order}_{exec}" — the user explicitly
    /// chooses to keep one or the other when both arrive.
    /// </summary>
    private static string? ExtractExAnteReference(string text)
    {
        var order = OrderId.Match(text);
        return order.Success ? $"exante:{order.Groups[1].Value}" : null;
    }

    private BrokerParseResult ParseSpanishValoresExAnte(string text, string fileName, string headerVerb)
    {
        var side = headerVerb.Equals("VENTA", StringComparison.OrdinalIgnoreCase)
            ? TradeSide.Sell
            : TradeSide.Buy;

        var rowMatch = ExAnteValoresRow.Match(text);
        if (!rowMatch.Success)
            return Skipped(fileName, text, "Could not parse EX-ANTE valores row ('ISIN: <ISIN> Comprar/Vender QTY tít. NOTIONAL €').");

        var dateMatch = DocFecha.Match(text);
        if (!dateMatch.Success)
            return Skipped(fileName, text, "Missing document date ('FECHA DD.MM.YYYY').");

        try
        {
            var isin = rowMatch.Groups[1].Value;
            // Same locale split as the crypto EX-ANTE: invariant qty, Spanish notional.
            var quantity = decimal.Parse(rowMatch.Groups[2].Value, NumberStyles.Number, CultureInfo.InvariantCulture);
            var notional = ParseEuropeanDecimal(rowMatch.Groups[3].Value);
            if (quantity <= 0)
                return Skipped(fileName, text, "Parsed quantity is zero — refusing to ingest a no-op trade.");

            var unitPrice = notional / quantity;
            var currency = new Currency("EUR");
            var price = new MoneyValue(unitPrice, currency);

            var feeMatch = ExAnteFee.Match(text);
            var fees = feeMatch.Success
                ? new MoneyValue(ParseEuropeanDecimal(feeMatch.Groups[1].Value), currency)
                : new MoneyValue(0m, currency);

            var executedAt = ParseEuropeanDate(dateMatch.Groups[1].Value);
            var reference = ExtractExAnteReference(text);
            var name = ExtractNameAboveIsin(text);

            var trade = BuildTrade(
                new InstrumentRef(isin),
                side,
                quantity,
                price,
                fees,
                executedAt,
                reference,
                name);

            _logger.LogInformation(
                "Parsed ES EX-ANTE valores trade from {File}: {Side} {Qty} {Isin} ({Name}) @ {Price} (ref {Ref})",
                fileName, side, quantity, isin, name, price, reference);

            return new BrokerParseResult
            {
                Trades = [trade],
                Warnings =
                [
                    $"{fileName}: Imported from a pre-trade cost disclosure (EX-ANTE). If the corresponding settlement (LIQUIDACIÓN DE VALORES) PDF arrives later for the same order, it will import as a separate trade — delete one of the two to avoid double-counting.",
                ],
                RawTextSample = null,
            };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return Skipped(fileName, text, $"Field conversion failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true for TR document types that should NOT be ingested as trades:
    /// pre-trade cost disclosures, account statements, tax certificates, recurring-order
    /// confirmations, etc. The corresponding executed settlement PDF is what becomes a trade.
    /// </summary>
    private static bool IsNonTradeDocument(string text, out string kind)
    {
        // Account statement / monthly summary
        if (Regex.IsMatch(text, @"(EXTRACTO\s+DE\s+CUENTA|Kontoauszug)", RegexOptions.IgnoreCase))
        {
            kind = "account statement";
            return true;
        }
        // Tax certificate / annual fiscal summary
        if (Regex.IsMatch(text, @"(CERTIFICACI[ÓO]N\s+FISCAL|Jahressteuerbescheinigung|Steuerbescheinigung|Steuer[üu]bersicht)",
                RegexOptions.IgnoreCase))
        {
            kind = "tax certificate";
            return true;
        }
        kind = "";
        return false;
    }

    private BrokerParseResult ParseGermanSecurities(string text, string fileName)
    {
        if (!TryDetectGermanSide(text, out var side))
            return Skipped(fileName, text, "Could not determine Kauf/Verkauf in German document.");

        var isinMatch = IsinRegex.Match(text);
        if (!isinMatch.Success) return Skipped(fileName, text, "Could not find ISIN.");

        var quantityMatch = DeQuantity.Match(text);
        if (!quantityMatch.Success) return Skipped(fileName, text, "Missing 'Stück' (quantity).");

        var priceMatch = DeUnitPrice.Match(text);
        if (!priceMatch.Success) return Skipped(fileName, text, "Missing 'Stückkurs' (unit price).");

        var dateMatch = DeDate.Match(text);
        if (!dateMatch.Success) return Skipped(fileName, text, "Missing 'Ausführungstag' (execution date).");

        try
        {
            var instrument = new InstrumentRef(isinMatch.Groups[1].Value);
            var quantity = ParseEuropeanDecimal(quantityMatch.Groups[1].Value);
            var priceAmount = ParseEuropeanDecimal(priceMatch.Groups[1].Value);
            var priceCurrency = new Currency(priceMatch.Groups[2].Value);
            var price = new MoneyValue(priceAmount, priceCurrency);
            var fees = SumGermanFees(text, priceCurrency);
            var executedAt = ParseEuropeanDate(dateMatch.Groups[1].Value);
            var reference = ExtractBrokerReference(text);
            var name = ExtractNameAboveIsin(text);

            var trade = BuildTrade(instrument, side, quantity, price, fees, executedAt, reference, name);
            _logger.LogInformation("Parsed DE TR trade from {File}: {Side} {Qty} {Symbol} (ref {Ref})",
                fileName, side, quantity, instrument, reference);
            return new BrokerParseResult { Trades = [trade] };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return Skipped(fileName, text, $"Field conversion failed: {ex.Message}");
        }
    }

    private BrokerParseResult ParseSpanishCrypto(string text, string fileName)
    {
        if (!TryDetectSpanishSide(text, out var side))
            return Skipped(fileName, text, "Could not determine Comprar/Vender in Spanish document.");

        var lineMatch = EsCryptoLine.Match(text);
        if (!lineMatch.Success)
            return Skipped(fileName, text, "Could not parse crypto position line ('NAME (TICKER) QTY PRICE CCY AMOUNT CCY').");

        var dateMatch = EsDate.Match(text);
        if (!dateMatch.Success)
            return Skipped(fileName, text, "Missing execution date ('Comprar/Vender el DD.MM.YYYY').");

        try
        {
            // Groups: 1=ticker, 2=qty, 3=price, 4=ccy, 5=total, 6=ccy.
            var ticker = lineMatch.Groups[1].Value;
            var quantity = ParseEuropeanDecimal(lineMatch.Groups[2].Value);
            var priceAmount = ParseEuropeanDecimal(lineMatch.Groups[3].Value);
            var priceCurrency = new Currency(lineMatch.Groups[4].Value);
            var price = new MoneyValue(priceAmount, priceCurrency);

            var instrument = new InstrumentRef(ticker);
            var fees = ParseSpanishCryptoFees(text, priceCurrency);
            var executedAt = ParseEuropeanDate(dateMatch.Groups[1].Value);
            var reference = ExtractBrokerReference(text);

            // Name is the text on the same line, immediately before the "(TICKER)" marker.
            var rawName = ExtractNameBeforeMarker(text, lineMatch.Index);
            var displayName = string.IsNullOrEmpty(rawName)
                ? null
                : $"{rawName} ({ticker.ToUpperInvariant()})";

            var trade = BuildTrade(instrument, side, quantity, price, fees, executedAt, reference, displayName);
            _logger.LogInformation("Parsed ES crypto TR trade from {File}: {Side} {Qty} {Ticker} ({Name}) (ref {Ref})",
                fileName, side, quantity, instrument, displayName, reference);
            return new BrokerParseResult { Trades = [trade] };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return Skipped(fileName, text, $"Field conversion failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a stable reference from the EJECUCIÓN id (preferred — identifies the
    /// fill) plus the ORDEN id when available, e.g. "f971-fedb_7dc2-8e49".
    /// Combining both protects against the rare case where two PDFs share an order
    /// id (partial fills) but have distinct execution ids.
    /// </summary>
    private static string? ExtractBrokerReference(string text)
    {
        var exec = ExecutionId.Match(text);
        var order = OrderId.Match(text);
        var execValue = exec.Success ? exec.Groups[1].Value : null;
        var orderValue = order.Success ? order.Groups[1].Value : null;
        if (string.IsNullOrEmpty(execValue) && string.IsNullOrEmpty(orderValue))
            return null;
        if (!string.IsNullOrEmpty(orderValue) && !string.IsNullOrEmpty(execValue))
            return $"{orderValue}_{execValue}";
        return execValue ?? orderValue;
    }

    private static bool TryDetectGermanSide(string text, out TradeSide side)
    {
        if (Regex.IsMatch(text, @"Wertpapierabrechnung\s+Kauf", RegexOptions.IgnoreCase)) { side = TradeSide.Buy; return true; }
        if (Regex.IsMatch(text, @"Wertpapierabrechnung\s+Verkauf", RegexOptions.IgnoreCase)) { side = TradeSide.Sell; return true; }
        side = default; return false;
    }

    private static bool TryDetectSpanishSide(string text, out TradeSide side)
    {
        if (Regex.IsMatch(text, @"Orden\s+de\s+mercado\s+Comprar", RegexOptions.IgnoreCase)) { side = TradeSide.Buy; return true; }
        if (Regex.IsMatch(text, @"Orden\s+de\s+mercado\s+Vender", RegexOptions.IgnoreCase)) { side = TradeSide.Sell; return true; }
        side = default; return false;
    }

    private static MoneyValue SumGermanFees(string text, Currency fallback)
    {
        decimal total = 0m;
        var currency = fallback;
        foreach (var label in DeFeeLabels)
        {
            var m = Regex.Match(text, $@"{label}\s+([\d.,]+)\s*([A-Z]{{3}})?", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            total += ParseEuropeanDecimal(m.Groups[1].Value);
            if (m.Groups[2].Success && !string.IsNullOrEmpty(m.Groups[2].Value))
                currency = new Currency(m.Groups[2].Value);
        }
        return new MoneyValue(total, currency);
    }

    private static MoneyValue ParseSpanishCryptoFees(string text, Currency fallback)
    {
        var match = EsCryptoFees.Match(text);
        if (!match.Success) return new MoneyValue(0m, fallback);
        var amount = Math.Abs(ParseEuropeanDecimal(match.Groups[1].Value));
        var currency = new Currency(match.Groups[2].Value);
        return new MoneyValue(amount, currency);
    }

    private static decimal ParseEuropeanDecimal(string input) =>
        decimal.Parse(input.Trim(), NumberStyles.Number, EuropeanLocale);

    private static DateTimeOffset ParseEuropeanDate(string input)
    {
        var local = DateTime.ParseExact(input, "dd.MM.yyyy", CultureInfo.InvariantCulture);
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc), TimeSpan.Zero);
    }

    private static TradeEntity BuildTrade(
        InstrumentRef instrument,
        TradeSide side,
        decimal quantity,
        MoneyValue price,
        MoneyValue fees,
        DateTimeOffset executedAt,
        string? brokerReference,
        string? name = null) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = Guid.Empty,
        Instrument = instrument,
        Side = side,
        Quantity = quantity,
        Price = price,
        Fees = fees,
        ExecutedAt = executedAt,
        BrokerKey = Key,
        BrokerReference = brokerReference,
        Name = NormalizeName(name),
    };

    private static string? NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > 120) trimmed = trimmed[..120];
        return trimmed;
    }

    /// <summary>
    /// PDF text extraction. <see cref="Page.Text"/> concatenates glyphs with no
    /// whitespace, which destroys regex anchors. PdfPig's
    /// <see cref="ContentOrderTextExtractor"/> uses content-stream reading order
    /// and inserts whitespace between glyph runs, which gives clean tokens for
    /// regex matching across the whole document.
    /// </summary>
    private static string ExtractText(Stream stream)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(stream);
        foreach (Page page in document.GetPages())
        {
            sb.AppendLine(ContentOrderTextExtractor.GetText(page));
        }
        return sb.ToString();
    }

    private static BrokerParseResult Skipped(string fileName, string fullText, string reason) =>
        new()
        {
            Trades = [],
            Warnings = [$"{fileName}: {reason}"],
            RawTextSample = fullText.Length > 2000 ? fullText[..2000] : fullText,
        };
}
