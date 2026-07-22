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
| 2026-07-17 | 4 | $4,009.42 | $2,895.69 | $2,439.92 | $-455.7714 | -15.740% | 3 | 33% |
| 2026-07-20 | 5 | $4,009.42 | $2,439.92 | $2,052.11 | $-387.8044 | -15.894% | 3 | 0% |
| 2026-07-21 | 6 | $4,009.42 | $2,052.11 | $2,534.98 | $+482.8628 | +23.530% | 6 | 50% |
| 2026-07-22 | 7 | $4,112.00 | $2,534.98 | $2,127.04 | $-407.9362 | -16.092% | 3 | 0% |

**Campaign total: $3,000.00 → $2,127.04  (-872.9608 / -29.099%) over 7 trading day(s).**

*Note: day 1 (2026-07-11, a Saturday, anchor $0) predates the weekend-skip and real-price-anchor features; kept unaltered for ledger honesty.*
