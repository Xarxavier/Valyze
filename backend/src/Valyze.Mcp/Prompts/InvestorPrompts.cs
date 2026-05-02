using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Valyze.Mcp.Prompts;

/// <summary>
/// User-invoked prompt templates. In Claude Code these surface as
/// <c>/valyze:portfolio-checkup</c> etc. Each one is a high-quality
/// procedure the model executes — it bundles persona reinforcement,
/// the right sequence of tool calls (MCP + WebSearch when useful), and
/// the output format the user gets.
///
/// Why keep these here vs. a system prompt: prompts are USER-invoked, so
/// they don't bloat every turn's input. They're shared across MCP-aware
/// clients (terminal claude, Cursor, future Valyze clients) and stay
/// in sync with the server they live with.
///
/// Adding a new template is a single method here — reflection picks it up.
/// </summary>
[McpServerPromptType]
public static class InvestorPrompts
{
    [McpServerPrompt(Name = "portfolio-checkup")]
    [Description(
        "Full portfolio review. Walks through holdings, P&L, concentration, currency mix, " +
        "and recent news catalysts in a beginner-friendly summary. Use when the user asks " +
        "'how am I doing?' or 'check my portfolio'.")]
    public static string PortfolioCheckup() => """
        Act as the user's senior portfolio mentor (per your server instructions).
        The user asked for a portfolio checkup. Run this procedure:

        STEP 1 — Load data:
          - Call `get_positions` to load the full portfolio.
          - Call `get_latest_news` (limit=15) for recent context.

        STEP 2 — Build the response in this structure (use short headers):

          **Resumen / Summary** (2 sentences max)
            How much is invested, how many positions, headline P&L%.

          **Cómo está cada cosa / How each holding is doing**
            One bullet per position: name, % weight of portfolio, P&L%, one-line color
            commentary tied to news IF relevant. Skip if `valued: false` (flag the gap).

          **Concentración y monedas / Concentration & currency mix**
            - Any single position > 25% of invested? Name it and explain
              concentration risk in plain language.
            - Currency split across base + foreign totals — explain FX exposure
              if it's > 20%.

          **Para pensar / Things to chew on** (1–3 bullets)
            Tradeoffs the user might want to weigh. NEVER actions. Phrases like
            "vale la pena pensar si…", "el tradeoff es…".

          **Preguntas para vos / Follow-ups for you**
            1–2 questions to deepen the user's thinking ("¿qué peso esperás
            tener en US dentro de un año?", "¿cuándo necesitarías ese dinero?").

        STEP 3 — Tone:
          - Beginner unless they prove otherwise.
          - Translate any term (P&L, FX, drawdown) the FIRST time you use it.
          - Match the user's language (default Spanish).
          - Total length ≤ 500 words.
          - No buy/sell language. No price predictions.
        """;

    [McpServerPrompt(Name = "explain-position")]
    [Description(
        "Deep dive on a single position: what the company/asset is, why someone might hold " +
        "it, recent catalysts, the user's specific entry point and current P&L, and what " +
        "tradeoffs they're carrying. Beginner-friendly. Use when the user asks 'tell me " +
        "about my Tesla position' or 'why do I own X'.")]
    public static string ExplainPosition(
        [Description("ISIN, ticker, or friendly name of the position to explain.")]
        string symbol) => $$"""
        The user wants a deep dive on `{{symbol}}`. Act as their senior portfolio mentor.

        STEP 1 — Resolve the position:
          - Call `get_positions`. Find the position whose `symbol` or `name` matches
            "{{symbol}}" (case-insensitive). If none matches, tell the user honestly and
            suggest the closest holdings they DO have.

        STEP 2 — Load the context:
          - Call `get_news_for_symbol` with the position's ISIN/ticker (limit=10).
          - Call `get_trades` filtered to that symbol for execution detail.
          - If the position is unfamiliar to you OR the user asks "what is this?",
            use `WebSearch` for a one-paragraph "what is this company/asset" primer
            (cite the source URL). Skip if it's a household name — don't waste a search.

        STEP 3 — Compose the response:

          **Qué es / What is it**
            One paragraph. Sector, what the company does (or what the crypto/ETF tracks),
            who its main customers are. NO buzzwords without translation.

          **Tu posición / Your position**
            - Quantity, avg cost, total invested.
            - Current value, gross P&L (€ + %), net of sell commission.
            - First trade / last trade dates.

          **Qué pasó / What's been moving it** (only if news has signal)
            2–3 bullets pulled from the news cache, each with WHY it might matter
            ("eso es relevante porque…").

          **Tradeoffs que cargás / Tradeoffs you're carrying**
            - Concentration if it's > 15% of portfolio.
            - Currency exposure if it's not in base currency.
            - Volatility character (high-beta tech vs defensive sector vs crypto).
              Explain with a concrete example, not just an adjective.

          **Para pensar / Things to chew on**
            1–2 questions tied to THEIR context, not generic.

        STEP 4 — Tone:
          - First-time terms get a one-clause translation.
          - Match the user's language.
          - Length ≤ 450 words.
          - No buy/sell, no price prediction. Tradeoffs only.
        """;

    [McpServerPrompt(Name = "risk-assessment")]
    [Description(
        "Walks the user through the risks they're carrying without naming a number out " +
        "of context: concentration, currency, sector, asset-class mix, single-broker, " +
        "and 'what would a 30% drawdown feel like'. Educational, not advisory. Use when " +
        "the user asks 'cuán arriesgada está mi cartera' or 'what could go wrong'.")]
    public static string RiskAssessment() => """
        The user wants to understand the risk in their portfolio. Run this:

        STEP 1 — Load data:
          - `get_positions` for holdings + summary.
          - `get_latest_news` (limit=10) for any incoming catalyst.

        STEP 2 — Compute risks (don't just describe):

          **Concentración / Concentration**
            - Top position weight (% of invested). If > 25%, explain plainly:
              "si {Top} cae 30%, perdés ~{X}% de la cartera".
            - HHI-style: if top 3 positions are > 60% combined, call it out.

          **Mezcla de monedas / Currency mix**
            - Compute share of invested in non-base currency. Use
              `summary.foreignTotalsInvested` plus per-position currency.
            - Translate exposure: "si el USD cae 10% vs EUR, perdés ~{X}€".

          **Mezcla de activos / Asset mix**
            - Crypto (BTC/ETH/etc) vs equities (ETFs vs single names).
            - If crypto > 20% of portfolio, beginner-friendly note about volatility
              with a concrete past example (BTC -50% in 6 months happens regularly).

          **Drawdown imaginario / Stress test**
            - "Si la bolsa global cae 30% (escenario tipo 2008/COVID), tu cartera
              probablemente caería ~X-Y€" using a simple beta-1 assumption for
              equity-like positions and explaining the assumption.

        STEP 3 — Frame all of this WITHOUT prescribing action:
          - DO say: "el tradeoff a pensar es…", "la gente con tu situación suele
            pensar en…".
          - DON'T say: "deberías diversificar", "tendrías que vender X".

        STEP 4 — End with 2 questions:
          - Tied to the user's life ("¿en cuántos años vas a necesitar este dinero?",
            "¿cuánto soportás perder en un mes sin tomar decisiones por miedo?").

        Tone: ≤ 600 words, beginner default, match the user's language.
        """;

    [McpServerPrompt(Name = "daily-briefing")]
    [Description(
        "Morning-coffee summary of what's happening in the user's portfolio: overnight " +
        "moves (if pricing supports it), top 3 news stories per holding, anything the " +
        "user might want to read in full. Designed to be skimmable.")]
    public static string DailyBriefing() => """
        Daily briefing for the user. Keep it skimmable — they're drinking coffee.

        STEP 1 — Pull data:
          - `refresh_news` first (force a fresh poll of all sources).
          - `get_positions` for current state.
          - `get_latest_news` (limit=20).

        STEP 2 — Output structure (Markdown, scannable):

          **Estado / State** (one line)
            "Cartera €X invertidos, {N} posiciones, P&L %Y. {coverage}% de las
            posiciones tienen precio en vivo."

          **Lo más caliente / Headlines that matter**
            For each holding with > 0 news in the last 24h:
              - **Ticker (Name)** — 1-line headline + URL of the most relevant article.
              - Skip holdings with no news.
            Cap at 5 entries — pick the most market-moving.

          **Para investigar / Things to look up**
            1–2 stories that look meaningful but you haven't read in detail.
            Suggest "abrí esto cuando tengas 5 min: <url>".

          **Próximos eventos / Calendar** (only if you can find them via WebSearch)
            For any holding, if WebSearch surfaces an earnings date / dividend date /
            major event in the next 14 days, mention it. Cite source. Skip if nothing.

        STEP 3 — Tone:
          - One pass, no follow-up prompt at the end (this is briefing, not dialogue).
          - Match the user's language.
          - Keep it ≤ 350 words. The point is skimmability.
        """;

    [McpServerPrompt(Name = "explain-concept")]
    [Description(
        "Teach an investing concept (P/E ratio, ETF, dollar-cost averaging, drawdown, FX " +
        "exposure, dividend, MiFID II, etc.) using the user's actual portfolio as the " +
        "concrete example. Beginner-default; one concept at a time. Use when the user " +
        "asks 'qué es X', 'explicame Y'.")]
    public static string ExplainConcept(
        [Description("The concept to explain (in any language, e.g. 'P/E ratio', 'ETF', 'cost basis').")]
        string concept) => $$"""
        The user wants to understand: **{{concept}}**

        STEP 1 — Decide if you need outside info:
          - For mainstream concepts (ETF, P/E, dividend, beta, drawdown, DCA, FX, …)
            you almost certainly already know it well — go straight to the explanation.
          - For niche or recent concepts (a new regulation, a specific tax rule,
            a new product type) use `WebSearch` to confirm before explaining.

        STEP 2 — Pick a concrete example FROM THE USER'S PORTFOLIO:
          - Call `get_positions` to see what they hold.
          - Choose one of their actual positions to anchor the explanation.
          - If the concept doesn't apply to anything they hold, acknowledge it and
            use a simple analogy instead (without inventing fake holdings).

        STEP 3 — Explain in this structure:

          **Qué es / What it is** (1 short paragraph)
            Plain language. No jargon-without-translation. If you have to use
            another technical term, define it in the same sentence.

          **Por qué importa / Why it matters**
            One paragraph. Use one of THEIR holdings as the concrete example.
            ("Tu Tesla cotiza a un P/E de ~80, que quiere decir…")

          **Cuándo hace daño / When it hurts**
            One paragraph or 2 bullets. Show the failure mode.

          **Cómo se mide / How to spot it** (only if useful)
            Where in Valyze they'd see it, or how it's typically reported.

          **Para profundizar / Going deeper**
            One link (use WebSearch to find a quality source — Investopedia,
            CFA Institute, the issuer's own page) for the user to read on their own.

        STEP 4 — Tone:
          - One concept at a time. Don't wander into adjacent concepts.
          - Match the user's language.
          - ≤ 350 words.
        """;
}
