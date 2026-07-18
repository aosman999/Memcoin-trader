# Certified Performance Baseline

**Champion configuration** (as of 2026-07-17): binary SL/TP exits at
2:1 reward:risk (stop −0.6%, target +1.2%), 10% equity risked per trade
(owner-directed), session+sentinel agent gates, indicator confluence,
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

## Ideas tested and ADOPTED

- Boost-only vol targeting — upsizes modestly in calm tape, never
  shrinks: +17% certified median, better tail, +25% in calm regimes

- Binary exits (2:1, no trailing) — best median of three exit styles
- 10% risk/trade — best median at owner's direction, fat tails accepted
- Session + sentinel gates — small median cost, consistent tail benefit
- Indicator confluence v2 (MACD alignment / RSI stretch) — tail benefit, free
- Strategy Lab holdout save-guard — blocked two bad champions already
- Live-only: news agent + economic calendar, mentor sweep strategy,
  Valentini 3-loss discipline, small-account guard
