# Gold Paper-Trading Campaign — PnL Ledger

XAU/USD, risk-based sizing under a 1:200 cap, one trading day per
calendar date, each day anchored to the REAL spot price.
**SIMULATED paper results** (this environment cannot stream live
ticks); live trading runs via the MT5/MetaApi bridges on the
owner's machine.

| date | day | real spot anchor | start | end | day pnl | day % | trades | win rate |
|---|---|---|---|---|---|---|---|---|
| 2026-07-11 | 1 | $0.00 | $3,000.00 | $2,926.12 | $-73.8812 | -2.463% | 5 | 60% |
| 2026-07-13 | 2 | $4,053.92 | $2,926.12 | $3,412.58 | $+486.4576 | +16.625% | 6 | 67% |
| 2026-07-15 | 3 | $4,074.00 | $3,412.58 | $2,895.69 | $-516.8880 | -15.147% | 2 | 0% |

**Campaign total: $3,000.00 → $2,895.69  (-104.3116 / -3.477%) over 3 trading day(s).**
