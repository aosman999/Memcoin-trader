# Certified Performance Baseline

**Champion configuration** (as of 2026-07-17): binary SL/TP exits at
2:1 reward:risk (stop −0.6%, target +1.2%), 10% BASE risk per trade
(owner-directed; session-weight and calm-market boosts can scale a
single entry up to 1.44x base = ~14.4% — this boosted behavior IS
what was certified), session+sentinel agent gates, indicator confluence,
boost-only volatility targeting, −15% daily loss stop, 1:200 cap.

## Certification (20 virgin seeds, 14 trading days each, $3,000 start)

Deep-evolution champion (2026-07-18; passed the 3-seed holdout gate
+2.65 vs +0.49, then head-to-head certified below):

| metric | deep champion | prior champion |
|---|---|---|
| median outcome | **x2.52** (+152%) | x1.71 |
| mean outcome | x3.76 | x2.43 |
| profitable runs | 15/20 | 17/20 |
| best run | x14.24 | x6.70 |
| worst run | x0.37 (−63%) | x0.26 |

Champion changes: slower trend EMA (75m) with a looser slope filter
(more trends caught, judged over longer), mr_window 51, hotter calm
boost (vol_target_ref 3.8e-4), tighter mentor-sweep detection.

Regime dependence (12-seed sweeps): calm markets ≈ x6.4 median, current
hot regime ≈ x2-3, crisis volatility ≈ break-even. Spread sensitivity:
edge halves at 2x assumed spread — the news/sentinel stand-asides exist
partly to avoid trading when real spreads blow out.

These are SIMULATED numbers on a market model. The MT5 demo (from
July 22) is the real test; its results supersede this table.

## Cross-model certification (GoldSim2 — the unseen market)

The champion was certified cold on a structurally independent model
(Student-t fat tails, GARCH-style vol clustering, persistent AR(1)
drift). Across 40 virgin seeds: **median ~x0.84-1.05 (≈ break-even),
mean x1.6-1.9** — profits there come from occasional monster runs
(x13 observed). Read: the median edge is partly specific to the
primary model's mechanics; live reality likely sits between the two
models. Robust (worst-model) evolution and holdout gating are now
built in (`goldtrader evolve --cross-model`); exit-style re-test on
both models confirmed binary keeps the best worst-model floor.

## Stress map — the champion's weather report (10 seeds per cell)

| market personality | median | verdict |
|---|---|---|
| calm gold (vol 0.6x) | x5.37 | thrives |
| trend-rich (50% trend days) | x3.05 | thrives |
| 2026 base calibration | ~x2.0 | works |
| crisis vol (1.5x) | x0.68→x0.72* | bleeds |
| trend-starved (17% trend days) | x0.57→x0.83* | bleeds |
| news-heavy (5 jumps/day) | x0.47→x0.50* | bleeds |

*after the hostile-weather GOVERNOR (adopted): 2 straight red days →
risk halves until a green day breaks the streak. Improves every
hostile cell and every worst-case at ~zero cost in friendly weather
(base x1.97→x2.04, calm −3% median for a better tail). Runs in the
Mac bridge with cross-session persistence.

**Search closure (Jul 19):** a maximum-budget robust evolution (10
generations x 14 candidates x both models) was the FOURTH consecutive
challenger rejected by the cross-model holdout gate. The champion is
confirmed as this environment's optimum; further tuning waits for
live-market evidence (Jul 22+).

## Ideas tested and REJECTED by measurement (kept behind flags)

| idea | verdict |
|---|---|
| Regime gating (block strategies by regime) | −50% median: starves trend entries |
| Regime sizing (smaller counter-regime) | −35% median for +0.28 worst-case |
| ATR-adaptive stops | worse everywhere |
| Skip Asia session entirely | −23% median, worse tail |
| Mastery risk-weighting (bench losers) | −8-12% median: benches on noise |
| Fool-me-twice analyst gating | −55% median at current trade frequency |
| +20% daily profit lock | median collapses, fewer +20% days |
| Indicator v1 (RSI exhaustion caps) | blocked best trends; redesigned to v2 (kept) |

## Ideas tested and REJECTED (continued)

| idea | verdict |
|---|---|
| Full vol targeting (shrink in crisis) | crisis median 1.00→0.67: shrinks recovery wins |
| Strict all-indicator confluence | neutral across 40 seeds (±noise): filtering harder does not raise win rate |
| Momentum/ROC continuation strategy | collapsed battery M1-A x2.32→x1.00 |
| Opening-range breakout strategy | below baseline on 3 of 4 batteries |
| Bollinger-squeeze breakout strategy | below baseline on 3 of 4 batteries |
| ADX-proxy trend-strength gate | M1-A x2.32→x1.27, M2-B below water |
| StochRSI meanrev gate | won all 4 batteries, then FAILED the holdout (x0.79 vs x0.85) — the gate doing its job |
| Multi-timeframe ENTRIES (5m/15m resampled strategy runs) | split batteries (M1 down, M2 up), then FAILED the holdout (worst-model x0.68 vs x0.97). The bot keeps multi-timeframe CONTEXT (15m MTF filter, 75m EMA, 2-4h lookbacks) — extra entry timeframes added correlated risk, not edge |

## Ideas tested and ADOPTED

- **Trend-pullback strategy (Jul 21)** — enter established EMA trends on
  the retrace-and-turn at the fast EMA instead of only chasing the
  crossover. Beat baseline on 3 of 4 A/B batteries, PASSED the 911-913
  cross-model holdout (worst-model x0.97 vs x0.85), then certified on
  20 virgin seeds (1201-1220), BOTH models: M1 median x1.81→x2.22
  (+23%), M2 x1.58→x1.80 (+14%), floor x0.133→x0.157. The only
  survivor of the 6-candidate pre-live hunt (see rejected table).

- Boost-only vol targeting — upsizes modestly in calm tape, never
  shrinks: +17% certified median, better tail, +25% in calm regimes

- Binary exits (2:1, no trailing) — best median of three exit styles
- 10% risk/trade — best median at owner's direction, fat tails accepted
- Session + sentinel gates — small median cost, consistent tail benefit
- Indicator confluence v2 (MACD alignment / RSI stretch) — tail benefit, free
- Strategy Lab holdout save-guard — blocked two bad champions already
- Live-only: news agent + economic calendar, mentor sweep strategy,
  Valentini 3-loss discipline, small-account guard
