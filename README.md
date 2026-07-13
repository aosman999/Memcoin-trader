# Gold Trader — multi-agent XAU/USD trading system

Six cooperating AIs trade gold on simulated, live-paper, and MT5-demo
markets (never real money):

| AI | Job |
|---|---|
| **Session Agent** | Gold's liquidity clock (Asia / London / NY) — entries only when the market is truly alive, size scaled by session |
| **Event Sentinel** | Fast safety veto: detects news-grade price shocks in the tape and blocks entries until it calms (A/B-validated: lifts the worst-case outcome above break-even) |
| **News Agent** | Reads the headlines that move gold — wars, missile strikes, sanctions, Fed speeches, CPI prints. Fresh high-impact news opens a stand-aside window; a strong bullish/bearish news bias blocks counter-direction entries |
| **Regime Agent** | Classifies the tape — trending / ranging / chaotic (advisory: the strategies carry their own regime filters) |
| **Strategy AIs** | Trend-following, mean-reversion, breakout — long & short, one position at a time |
| **Gold Strategy Lab** | Evolutionary self-improvement over every parameter; champions must win across multiple simulated market seeds |

Everything sits on a shared risk engine: **stop-defined position sizing**
(risk % of equity / stop distance, hard-capped by the leverage limit),
stop-loss and take-profit attached **broker-side** on every order, a
**daily loss stop**, margin stop-out modeling, and **demo-only guards**
in both MT5 bridges — the code refuses real-money accounts.

## Quickstart (pure stdlib, Python 3.10+, zero installs)

```bash
python3 -m goldtrader backtest --days 30     # simulator (2026-regime calibrated)
python3 -m goldtrader campaign               # persistent day-by-day ledger
python3 -m goldtrader paper --minutes 480    # LIVE real gold prices, no broker
python3 -m goldtrader evolve                 # Gold Strategy Lab
python3 -m goldtrader mt5                    # MT5 demo bridge (Windows)
python3 -m goldtrader mac                    # MT5 demo via MetaApi (macOS/Linux)
```

## Documentation

- **[docs/GOLD.md](docs/GOLD.md)** — full setup: MT5 demo (Windows),
  MetaApi (MacBook/Linux), Telegram trade alerts and daily PnL reports
- **[docs/GOLD_MARKET_STUDY.md](docs/GOLD_MARKET_STUDY.md)** — the market
  research behind the calibration: gold since free trading opened (1971)
  through the January 2026 all-time high and today's high-volatility regime

## Project layout

```
goldtrader/
  config.py            every tunable (evolvable); champion in data/gold_params.json
  strategies.py        trend / mean-reversion / breakout (long & short)
  agents.py            session clock, event sentinel, regime classifier
  news_agent.py        headline tracker (wars, Fed, CPI) with entry gating
  leverage.py          margin engine: risk-based sizing, SL/TP, stop-out
  orchestrator.py      wires agents -> strategies -> risk engine
  day_guard.py         daily loss stop (profit lock available, off by default)
  portfolio.py         cash, trade ledger, equity curve, stats
  strategy_lab.py      evolutionary parameter search
  campaign.py          persistent daily ledger (data/gold_pnl_log.md)
  datafeed/            2026-calibrated simulator + live spot feeds (keyless)
  mt5_bridge.py        MetaTrader 5 demo bridge (Windows)
  metaapi_bridge.py    MT5 demo via MetaApi cloud (macOS/Linux)
  telegram.py          trade alerts + daily reports to your Telegram
```

## Disclaimer

Leveraged gold trading can lose money quickly; most retail traders lose.
This project is a research and DEMO-trading tool — it refuses real-money
accounts by design, and simulated results (however good) do not predict
live performance. Nothing here is financial advice.
