# Gold Trader — spot XAU/USD, MetaTrader 5 demo

The project's gold module: same engine discipline as the original bot
(risk-based sizing, hard stops, full take-profits, day guard), pointed at
gold. Three strategies — trend (EMA cross), mean reversion (z-score fade
in ranges), breakout (session high/low with range expansion) — long and
short, one position at a time, never pyramided.

## Religious-compliance note (read me)

This module was created after the owner moved away from memecoins for
religious reasons. Points to verify with your own scholar — this is
engineering, not a fatwa:

- **Spot & swap-free**: the model assumes an Islamic (swap-free) account:
  no overnight interest is modeled, and the time-stop closes intraday.
- **Leverage is the debated part.** Margin trading is considered
  impermissible by many scholars even on swap-free accounts, because the
  broker's margin loan resembles riba. `max_leverage` in
  `goldtrader/config.py` (and `data/gold_params.json`) goes down to
  **1.0 = fully unleveraged**, and `allow_short: false` restricts to
  long-only spot behavior. The bot is fully functional in that mode.
- The MT5 bridge **refuses to trade real-money accounts** — demo only.

## Quick start (simulator — works anywhere)

```bash
python3 -m goldtrader backtest --days 30 --bankroll 1000
python3 -m goldtrader campaign            # persistent day-ledger (data/gold_pnl_log.md)
python3 -m goldtrader paper --minutes 480 # live spot prices, no broker needed
```

## MetaTrader 5 demo setup (Windows)

1. Install MT5 from your broker; create a **DEMO** account. Deposit
   $1,000+ demo dollars (gold's minimum lot 0.01 = 1 oz needs ~$35
   margin at 1:100 — a $20 demo can't open the minimum trade).
2. MT5 → Tools → Options → Expert Advisors → enable algo trading.
3. `pip install MetaTrader5` (Windows-only package).
4. Create `data/mt5_config.json`:
   ```json
   {"login": 12345678, "password": "YOUR_DEMO_PASSWORD",
    "server": "YourBroker-Demo", "symbol": "XAUUSD"}
   ```
5. Run: `python3 -m goldtrader mt5 --minutes 480`

Every order is placed with stop-loss and take-profit attached at the
broker, so exits execute even if your computer disconnects.

## What to expect (honesty section)

Simulator results (~+4%/day average at ~4x effective leverage, red days
capped at −5% by the day guard) are calibration-quality, not forecasts.
Real intraday gold trading at this leverage doing **+5-15% per month**
with capped drawdowns would be a strong live result. Leverage multiplies
both directions — the −5% day-guard cap and the 1.5%-risk sizing are
what keep it survivable. Judge the bot on weeks of demo results, not
days, before any other decision.
