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

**Commodity coverage + a classifier bug fixed (Aug 2026).** Added oil and
commodity events (crude/gasoline/natural-gas inventories, OPEC meetings,
Baker Hughes rig count) at TIER 2, promoted above their calendar impact
rating — oil feeds gold through inflation expectations and both trade as
dollar-denominated commodities. OPEC entries are tagged country "ALL" on the
feed, which is now accepted.

Port-testing the classifier against 14 realistic titles caught a real bug:
**"FOMC Member Speaks" was ranking TIER 1** — the same as an actual rate
decision — because the title contains "FOMC". Routine member speeches now
demote to tier 3, while Powell/Fed-Chair remarks stay tier 1 (the chair does
move gold alone). All 14 cases now classify correctly.

Note: genuinely gold-SPECIFIC news (central-bank gold buying, ETF flows,
physical demand) is NOT on an economic calendar and is not covered here —
that would need a headline feed, not this source.

## Retuned for m15 and higher frequency (Aug 2026)

Owner wanted m15 and more than ~2 trades/week. Measured on 30 virgin seeds,
both models, stop-relative spread cost, with the adaptive stop + target:

| config | edge/trade | win% | trades/wk | R/day | worst-model |
|---|---|---|---|---|---|
| h1 eff.55 + ADX-rising (previous) | +0.826 | 58.8 | 2.2 | +0.365 | +0.699 |
| m15 eff.55 + ADX-rising | +0.974 | 53.6 | 4.4 | +0.859 | +0.826 |
| m15 eff.45 + ADX-rising | +0.772 | 49.9 | 7.0 | +1.088 | +0.668 |
| **m15 eff.40 no-rise (ADOPTED)** | **+0.687** | 48.3 | **9.0** | **+1.241** | +0.609 |
| m15 eff.35 no-rise | +0.607 | 46.7 | 10.5 | +1.275 | +0.553 |
| m15 eff.25 ADX15 loose | +0.508 | 44.9 | 13.6 | +1.381 | +0.467 |

Total growth (edge x frequency) keeps rising as the filter loosens, but the
per-trade cushion thins. Below roughly +0.5 edge the margin stops reliably
covering real slippage — that is the zone the original +0.320-edge bot died
in. eff.40 is the balance point: 4x the trade frequency of the h1 build at
3.4x the R/day, with the cushion intact.

**CORRECTION to the earlier finding that h1 beats m15.** That was true with a
FIXED 0.6% stop. With the ADAPTIVE (ATR) stop, m15 beats h1 on both edge and
frequency (+0.974 vs +0.826 at the same filter) — the stop can size itself to
m15 volatility instead of wearing an h1-sized one. The earlier conclusion was
correct only for the exit style it was tested with.

### Max frequency and the risk linkage (Aug 2026)

Owner asked for no trade cap — "trade as much as it can as long as it's above
the daily stop". Clarification: **nothing in the bot caps trade count**; the
filter threshold alone determines frequency. But compounding 30 virgin seeds
over 60 days shows the binding constraint is CUMULATIVE DRAWDOWN, which the
daily stop cannot prevent (it caps one day, not a losing streak of days):

| config | risk | trades/day | median DD | worst DD |
|---|---|---|---|---|
| max frequency (no filter) | 10% | 3.69 | 73.5% | **97.4%** |
| max frequency | 5% | 3.69 | 47.1% | 80.7% |
| max frequency | 2% | 3.69 | 21.9% | 45.9% |
| eff.25 | 5% | 2.72 | 41.1% | 60.9% |
| eff.40 | 10% | 1.81 | 55.1% | 84.0% |

Same trade counts, wildly different survival — the only variable is risk per
trade. A 97% drawdown is a dead account in reality (the sim has no margin call
and infinite patience). **You can have high frequency OR high risk-per-trade,
not both.**

Adopted: eff 0.25 / ADX 15 (~13.6 trades/week, edge +0.508) with risk
defaulting to **5%**, plus a startup warning when risk > 6%. Timeframe m15
confirmed best: m5 is worse on edge (+0.590) and drawdown; h1 trades too
rarely (2.2/wk).

## Tuned for win rate > 55% (Aug 2026)

Owner wanted a win rate above 55%. Win rate is governed by the TARGET, not by
entry quality — a nearer target gets hit more often. Shrinking the adaptive
reward:risk range and tightening the trend filter does it.

Certified on 40 FRESH seeds (7000-7039, never used in any prior tuning; the
earlier 9100-9129 holdout had become training data by this point), 7,621 trades:

| config | win% | edge | trades/wk | worst DD @5% | losing runs |
|---|---|---|---|---|---|
| eff.25 RR2.0-6.0 (previous) | 42.3 | +0.559 | 13.8 | 78% | 1/80 |
| **eff.50 RR1.0-2.5 (ADOPTED)** | **56.1** | +0.477 | 7.9 | **49%** | 1/80 |
| eff.45 RR1.0-2.5 | 54.4 | +0.433 | 10.0 | 59% | 0/80 |

The trade is honest: ~0.08R of expectancy and ~6 trades/week given up. In
return the win rate rises 14 points and worst-case drawdown nearly halves
(78% -> 49%), which matters more for whether a human keeps running the system.
Edge remains well clear of the ~+0.3 fragile zone.

Full sweep (on the 9100-9129 tuning set) showing the lever:

| filter | RR range | win% | edge |
|---|---|---|---|
| eff.25 | 2.0-6.0 | 44.9 | +0.508 |
| eff.25 | 1.0-2.0 | 53.3 | +0.315 |
| eff.40 | 1.0-2.0 | 56.4 | +0.325 |
| eff.50 | 1.0-2.5 | 57.0 | +0.485 |
| eff.55 +rise | 1.5-4.0 | 55.7 | +0.875 (only 4.9 tr/wk) |

## Tuned for win rate > 60% (Aug 2026)

Certified on 45 FRESH seeds (8200-8244), 5,476 trades:

| config | win% | edge | worst-model | trades/wk | worst DD @5% | losing runs |
|---|---|---|---|---|---|---|
| eff.25 RR2.0-6.0 | 42.3 | +0.559 | +0.547 | 13.8 | 78% | 1/80 |
| eff.50 RR1.0-2.5 | 56.6 | +0.522 | +0.492 | 8.2 | 57% | 0/90 |
| **eff.60 RR1.0-2.0 (ADOPTED)** | **61.8** | +0.506 | +0.473 | 5.1 | **48%** | **0/90** |
| eff.55+rise RR1.0-2.0 | 60.4 | +0.483 | +0.466 | 6.2 | 43% | 0/90 |

Each win-rate step was certified on a seed set never previously used — 7000-7039
for the 56% config, 8200-8244 for this one — because the earlier 9100-9129
holdout had become training data once it was used for tuning.

**Where to stop.** Break-even win rate for a target of RR is `1/(1+RR)`, so a
nearer target needs a higher win rate merely to break even. Measured:

| RR range | win% | break-even% | margin | edge |
|---|---|---|---|---|
| 1.0-2.0 | 61.0 | 38.5 | +22.5 | +0.503 |
| 0.8-1.6 | 64.0 | 43.8 | +20.1 | +0.388 |
| 0.7-1.4 | 65.5 | 47.1 | +18.4 | +0.323 |
| 0.5-1.0 | 69.0 | 55.3 | **+13.7** | **+0.182** |

A 69%-winning system that barely clears break-even is worse than a 62% one with
real cushion — it only feels better. Do not chase the win rate past ~62% by
shortening the target.

## Higher win rate WITHOUT shortening the target (Aug 2026)

Owner asked for a higher win rate but explicitly not by lowering the TP — i.e.
improve the entry/exit mechanics, not the arithmetic. Correct instinct: a
nearer target raises win rate partly as an illusion, since break-even win rate
is `1/(1+RR)`.

Held the target ratio fixed at 1.0-2.0 and tested entry filters and stop
placement (30-seed tuning set, then certified on 50 FRESH seeds 6500-6549):

| change (target ratio unchanged) | win% | delta | edge | tr/wk |
|---|---|---|---|---|
| baseline (ATR stop, eff.60) | 62.7 | — | +0.543 | 5.0 |
| **swing-structure stop (20-bar)** | **68.1** | **+5.4** | +0.439 | 3.5 |
| eff 0.70 | 64.8 | +2.1 | +0.594 | 2.4 |
| wider ATR stop 2.5x | 63.6 | +0.9 | +0.557 | 4.9 |
| EMA200 alignment | 63.1 | +0.3 | +0.554 | 4.7 |
| MACD still growing | 62.8 | +0.1 | +0.560 | 3.9 |
| 6/6 votes | 62.2 | −0.6 | +0.538 | 4.4 |
| volatility not collapsed | 61.5 | −1.2 | +0.522 | 3.4 |
| "not overextended" | — | starved to 1 trade | | |

**Certified (50 fresh seeds, 4,135 trades):**

| | ATR stop | **SWING stop (adopted)** |
|---|---|---|
| win rate | 61.7% | **66.4%** |
| edge | +0.501 | +0.367 |
| worst drawdown @5% | 47% | **33%** |
| runs that lost money | 3/100 | **0/100** |

The swing stop wins because price must break real structure to stop the trade
out, rather than merely wobbling an arbitrary ATR distance. It is usually wider
than the ATR stop, so each unit of risk buys a smaller multiple — that is the
honest cost of the extra consistency.

Higher-win variants exist (swing + eff.70 = 69.1%) but fall to 1.7 trades/week.

## More trades at the SAME entry quality — concurrent positions (Aug 2026)

Owner wanted the ~65% win-rate config to trade more than 3.7/week. The
limiter was not the filter: holding ONE position for up to 10 hours made the
bot sleep through valid setups. Allowing several at once reuses the identical
entries, so entry quality — and therefore win rate — is untouched.

Certified on 50 FRESH seeds (5500-5549):

| config | win% | trades/wk | edge | median DD | worst DD | losing runs |
|---|---|---|---|---|---|---|
| 1 position @5% | 64.8 | 3.7 | +0.422 | 15% | 29% | 0/100 |
| 2 positions @5% | 65.9 | 6.0 | +0.460 | 20% | 40% | 0/100 |
| 3 positions @5% | 66.7 | 7.5 | +0.484 | 25% | 52% | 0/100 |
| **3 positions @3% (ADOPTED)** | **66.7** | **7.5** | **+0.484** | 15% | **35%** | 0/100 |
| 4 positions @3% | 67.0 | 8.4 | +0.495 | 17% | 44% | 0/100 |

Win rate and edge both went UP while trade count doubled — the extra trades are
signals that were previously skipped, not lower-quality ones. The only thing
given away is simultaneous exposure (3 x 3% = 9% at risk vs 5% for a single
position), which is why risk-per-trade drops from 5% to 3%: that keeps worst
drawdown at 35%, close to the single-position 29%.

Also added `MinBarsBetweenSameSide` (default 4). Several positions at once is
diversification; several near-identical ones on consecutive bars is one trade
in disguise, sized larger.

Shorter max-hold was tested as an alternative frequency lever and is worse:
hold 16 lifts trades only 3.7 -> 4.4/wk while edge falls +0.498 -> +0.309.
