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

## GoldEdge — custom strategy research (Aug 2026)

Built after the live six-voter lost money. Method: fast lab harness that
precomputes indicator series, **spread modelled at 0.08R/trade**, both
market models, and — critically — every result quoted as **EDGE OVER A
RANDOM-ENTRY BASELINE**, because a control test showed a coin-flip entry
scores POSITIVE on these simulators (they trend more than real gold, and
the bias grows with reward:risk). Raw returns from this sim are not
trustworthy; edge-over-chance is.

**Certified on 30 virgin seeds (9100-9129) never used in tuning:**

| config | R/trade | edge over chance | win% |
|---|---|---|---|
| **GoldEdge (h1, eff>=0.55, ADX rising, RR4)** | **+0.844** | **+0.633** | 58.8% |
| GoldEdge RR3 | +0.752 | +0.602 | 59.1% |
| GoldEdge RR2 | +0.574 | +0.504 | 60.7% |
| m15 six-voter + eff 0.40 (GoldBotTrend) | +0.461 | +0.420 | 56.6% |
| plain six-voter h1 | +0.389 | +0.320 | 54.8% |

**What actually generates the edge (ablation, virgin seeds):**
- trend-quality (Kaufman efficiency) filter: edge +0.400 -> +0.633 — the
  single biggest lever; chop is what was killing the strategy
- ADX rising (trend accelerating): +0.603 -> +0.633
- timeframe: m5 +0.26R, m15 +0.42R, m30 +0.47R, h1 +0.55R (h4 breaks down,
  model-unstable +0.205/+0.482)
- efficiency threshold is STABLE 0.45-0.65 (edge +0.62..+0.70), not a
  knife-edge fit

**Rejected by measurement** (tested as add-ons, none kept): EMA200 trend
alignment (neutral), volatility-expansion gate (worse worst-model),
dual-window efficiency (worse), RSI-room (much worse), MACD acceleration
(neutral), pullback entry (starves to ~0 trades in high-efficiency trends),
multi-timeframe EMA-slope filter 1h/4h (+0.30R vs +0.90R for the efficiency
filter — the MTF idea sounds better than it measures).

**Status: DEMO-ONLY.** `tools/GoldEdge.cs` refuses live accounts. The sim's
trend bias means live edge will be smaller than +0.633; a demo run is the
only honest proof.

### News agent (Aug 2026) — `tools/GoldEdgeNews.cs`

Design constraint from the owner: news must **protect** the bot **without
reducing how much it trades**, and must never close a trade — a print can
pump gold as easily as dump it.

Measured on the 30 virgin seeds (h1, RR4), shock-driven analogue:

| news policy | edge over chance | trades | verdict |
|---|---|---|---|
| none (GoldEdge) | +0.633 | 1530 | baseline |
| **stop -> breakeven on news** | **+0.636** | **1532** | ADOPTED — free, no trades lost |
| close position on news | +0.496 | 1658 | **REJECTED** — cuts winners short |
| shock veto (blocks entries 3 bars) | +0.657 | 1304 | adopted, but costs ~15% of trades |

So protection is **insurance, not edge**: it is ~neutral in the simulator,
which does not model the slippage, spread blow-outs and gaps that make real
news dangerous. Closing on news is the one clearly harmful option.

**Coverage** — everything that moves gold, tiered: T1 FOMC/rate decisions/
NFP/CPI/core PCE/Powell/testimony/Jackson Hole; T2 any other high-impact
print; T3 anyone speaking (members, governors, minutes, panels) plus medium
US data. Currencies USD (direct) + EUR/GBP/JPY/CNY (via the dollar).

Entry blocking exists but defaults **OFF** (it costs trades). Fail-safe: a
failed fetch logs and leaves the bot trading on the shock veto alone; fetch
is off-thread and refreshes every 6h (inside the feed's 2-per-5-min limit).
The calendar layer is reasoned, not backtested — no calendar in the sim, and
this env blocks network; field names confirmed from feed docs and the parser
was port-tested against a realistic sample.

**Currency coverage (Aug 2026).** Widened from USD,EUR,GBP,JPY,CNY to all
nine majors on the feed: added **CHF** (gold's twin safe haven; Switzerland
refines most of the world's gold) and **AUD/CAD/NZD** (commodity/risk
proxies; Australia is a top-3 gold producer). Safe to widen because
protection only moves the stop to breakeven on an ALREADY-PROFITABLE trade,
so it can never convert a winner into a loser.

Measured — does protecting MORE OFTEN hurt? No, it mildly helps:

| protection frequency | edge | worst-model |
|---|---|---|
| off | +0.826 | +0.699 |
| rare (>3.5x ATR) | +0.827 | +0.700 |
| default (>2.5x ATR) | +0.830 | +0.711 |
| very frequent (>1.5x ATR) | +0.840 | +0.739 |
| constant (>1.0x ATR) | +0.839 | +0.750 |

New `ProtectMaxTier` parameter (default 2) controls which tiers trigger
protection: 1 = gold-critical US events only, 2 = also every high-impact
print, 3 = also speakers and medium data.
