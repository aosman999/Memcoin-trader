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

## Research pass: what professional gold traders actually use (Aug 2026)

Researched written sources on institutional/professional gold method, extracted
the testable claims, and measured each on both models against a random-entry
baseline. (No video sources — that capability does not exist here.)

### ADOPTED

| change | effect |
|---|---|
| **Skip dead hours (trade 01-22 UTC)** | edge +0.463 -> +0.480, win 65.5% -> 66.0% |
| **eff 0.55 + 4 concurrent positions** | 7.6 -> 10.8 trades/wk at the same win rate and edge |

Certified on 50 FRESH seeds (4400-4449), 12,983 trades:

| config | win% | trades/wk | edge | worst-model | worst DD@3% |
|---|---|---|---|---|---|
| previous (eff.60, 3 pos) | 68.1 | 7.6 | +0.501 | +0.443 | 36% |
| **eff.55, 4 pos, session (ADOPTED)** | **67.4** | **10.8** | +0.498 | +0.428 | 49% |
| eff.50, 5 pos, session | 65.3 | 15.6 | +0.457 | +0.388 | 53% |
| eff.50, 4 pos, session | 65.1 | 14.6 | +0.449 | +0.380 | 49% |

42% more trades for 0.7 points of win rate and 0.003 of edge.

### REJECTED

| claim (as stated by practitioners) | measured |
|---|---|
| "Trade only the 12-16 UTC London/NY overlap — gold sets its daily high/low there ~70% of the time" | Cut trades SIX-FOLD (15.8 -> 2.6/wk) and lowered edge +0.463 -> +0.385. The session is real; making it an exclusive filter is not. |
| "Avoid the 4pm London fix (no-trade window either side)" | Exactly neutral: +0.464 vs +0.463 |
| **"Liquidity sweeps / stop hunts: 60-70% win rate with structure confirmation"** | **raw sweep win 41.8%, edge −0.178; +structure 43.6%, −0.159; +trend 49.9%, −0.025. Adding it to the working bot DROPPED it from 65.5%/+0.463 to 61.2%/+0.323.** |

The liquidity-sweep result is the notable one: it is probably the most widely
promoted gold day-trading concept in retail content, and it not only failed to
reach the claimed 60-70% win rate, it had NEGATIVE edge in every variant and
actively degraded a working strategy when combined.

**Caveat:** this simulator does not model order-flow or stop-hunt dynamics, so
treat the sweep result as strong evidence against rather than proof. Session
effects are also only as good as the simulator's synthetic session structure.

### ADX ablation

Prompted by the owner asking why ADX is used. It turns out to be the junior
partner by a wide margin:

| filter | win% | edge | trades/wk |
|---|---|---|---|
| efficiency + ADX (shipped) | 66.7 | +0.484 | 7.5 |
| efficiency only | 67.4 | +0.428 | 7.7 |
| **ADX only** | **54.3** | **+0.203** | 45 |
| neither | 54.1 | +0.181 | 49 |

**ADX alone is barely distinguishable from no filter at all.** The trend-quality
(efficiency) filter does nearly all the work. ADX 18/25/30 give near-identical
results, meaning it is close to inert once efficiency >= 0.55 — it is kept only
for the small edge/robustness contribution (+0.056 edge, +0.046 worst-model).

## The six voters are redundant (Aug 2026)

Leave-one-out testing on the confluence system, holding everything else fixed:

| voter removed | win% | edge | verdict |
|---|---|---|---|
| ema20>ema75 | 67.4 | +0.524 | no change |
| px>ema75 | 67.4 | +0.524 | no change |
| macd>0 | 67.5 | +0.524 | no change |
| rsi>50 | 67.5 | +0.524 | no change |
| ema20 rising | 67.5 | +0.524 | no change |
| px>px[-20] | 67.5 | +0.524 | no change |

**Removing any single voter changes edge by less than 0.001.** They are not six
independent opinions — they all answer "is price going up?" through different
lenses, so any five carry the same information as six.

Pushing further, certified on 50 FRESH seeds (3300-3349):

| voter set | win% | trades/wk | edge | worst-model |
|---|---|---|---|---|
| 6 voters, 5-of-6 | 67.0 | 11.0 | +0.502 | +0.445 |
| **3 voters (ema-cross, rsi, momentum), all 3** | **67.2** | 10.5 | **+0.505** | **+0.451** |
| 3 voters (ema-cross, px>ema75, macd), all 3 | 68.0 | 9.0 | +0.515 | +0.444 |
| **1 voter (px > px[-20]) alone** | **66.9** | 11.1 | **+0.500** | +0.442 |
| 2 voters | 66.7 | 10.6 | +0.493 | +0.442 |

**A single momentum check scores the same as the whole six-voter apparatus.**

Adopted the 3-voter set — not because it earns more (the differences are within
noise) but because fewer fitted parts means less to overfit and less to break.
The 6-voter path is retained behind `UseSimpleVoters = false`.

The real lesson: **the edge is in the trend-quality FILTER and the EXITS (swing
stop, adaptive target), not in stacking entry indicators.** Adding indicators
was never what made this work, and the long list of rejected filters above says
the same thing from the other direction.

## Final configuration (Aug 2026) — 71.3% win, 9.9 trades/week

Two further levers found after the research pass:

**1. A LONGER trend-quality window is the biggest win-rate lever found.**

| window | win% | edge | trades/wk |
|---|---|---|---|
| 12 bars | 62.9 | +0.393 | 21.2 |
| 24 bars (previous) | 67.9 | +0.532 | 10.5 |
| 36 bars | 71.0 | +0.600 | 5.8 |
| **48 bars** | **73.1** | **+0.640** | 3.3 |

Twelve bars is too short to tell a real trend from a wiggle. Longer windows
judge trend quality over a fuller stretch and select far better setups — but
they trade less, so the lost frequency has to be bought back elsewhere.

**2. Concurrency buys the frequency back at no cost to quality**, because the
extra trades are signals that were previously skipped while the bot was busy.

Combining them, certified on 50 FRESH seeds (2200-2249), 11,897 trades:

| config | win% | trades/wk | edge | worst-model | worst DD |
|---|---|---|---|---|---|
| previous (24-bar, 4 pos, 3%) | 65.7 | 10.5 | +0.463 | +0.414 | 46% |
| **48-bar, 10 pos, gap2, eff.50, 1.5%** | **71.3** | **9.9** | **+0.584** | **+0.498** | **37%** |
| 36-bar, 8 pos, gap2, eff.50 | 68.9 | 14.3 | +0.538 | +0.470 | 62% |
| 48-bar, 8 pos, gap3, eff.55 | 73.1 | 4.6 | +0.623 | +0.543 | 52% |

Better win rate, better edge, better drawdown, same frequency.

**Sizing note.** 10 concurrent positions means exposure is 10x the per-trade
risk, so risk drops to 1.5% (= 15% maximum exposure). The same config at 3%
risk has a 60% worst drawdown; at 1.5% it is 37%. Win rate is identical either
way (71.3%) — only survivability changes.

### Also tested in this pass, not adopted

| idea | result |
|---|---|
| Dynamic threshold by volatility | 66.9%, +0.520 — slightly worse |
| Pyramiding into winners | 67.9%, +0.535 — neutral |
| Asymmetric long/short thresholds | 68.2%, +0.533 (longs easier) — within noise |
| Wider entry spacing (gap 8) | 67.1%, +0.515 — worse |

## Trader-technique pass (Aug 2026) — three claims tested, none adopted

Source note: YouTube itself is blocked by this environment's egress proxy
(`EGRESS_BLOCKED`), and there is no video capability here, so the claims below
came from written summaries and transcripts of widely-followed gold traders
rather than from watching anything. Treat the sourcing as second-hand; the
measurement is what decides.

Each claim was reduced to a testable rule and run with **the reward:risk ratio
held fixed**, so nothing could win by quietly shortening the target.

| claim (as commonly taught) | rule tested | result |
|---|---|---|
| "Trade the 20 EMA / 9 SMA crossover" | enter only on the bar the fast MA crosses | **starved — 12 trades** across the whole seed set. Unmeasurable, not adoptable. |
| "Take the 4H bias, enter on the 15M" | require higher-timeframe direction to agree | 71.9% win, edge +0.618 vs 71.6% / +0.610 for the champion — see certification below |
| "Buy at discount, never chase extended price" | skip entries far from the fast EMA | **starved — 2 trades**. Same failure as the earlier pullback test: in a high-efficiency trend price is never near the fast EMA. |

The two starved rules are the same lesson twice: **a rule that only fires when
the trend filter is already excluding the setup does not exist as a strategy.**

### 4H-bias certification — within noise, NOT adopted

50 fresh seeds (1100-1149), never used for tuning:

| config | win% | trades/wk | edge | worst-model | n | med DD | worst DD | losing runs |
|---|---|---|---|---|---|---|---|---|
| current (no HTF bias) | 69.8 | 9.4 | +0.563 | +0.473 | 11,297 | 12% | 43% | 6/100 |
| + 4H bias filter | 70.1 | 9.2 | +0.570 | +0.479 | 11,056 | 12% | 42% | 4/100 |

+0.3 points of win rate and +0.007 of edge is **inside the noise band** for this
sample. The losing-run count fell from 6/100 to 4/100, which looks like a
robustness gain, but at n=100 runs that difference is about one standard error
— it is not evidence.

**Verdict: not adopted.** It costs 0.2 trades/week and adds a second timeframe
(a real crash risk in cBots — `MarketData.GetBars` is on the suspected-null
list) to buy nothing that can be distinguished from chance. Recorded here so it
is not re-tested without a specific reason.

The shipped configuration is unchanged: 48-bar trend window, 3 voters, swing
stop, adaptive RR 1.0-2.0, 10 concurrent positions, 1.5% risk —
**71.3% win, 9.9 trades/week, edge +0.584, 37% worst drawdown, 0/100 runs
losing money.** All figures SIMULATED.

## Podcast pass (Aug 2026) — the ensemble fix, ADOPTED

Source: Robert Carver on *Better System Trader* ep.26 and his book *Systematic
Trading*. The site itself is blocked by this environment's egress proxy, so the
claim came through search summaries — but unlike most podcast advice it is
directly testable, and it is aimed at **our own process**, not at the market.

**The claim:** the standard approach — sweep a parameter, keep whichever value
performed best — is beaten by simply *averaging across all the variations*. The
winner of a sweep was probably partly lucky, and that luck does not persist.

**Why it applies here:** we picked `EfficiencyWindow = 48` because it beat
12/24/36 in a sweep. That is exactly the pattern Carver describes.

### Is 48 a peak or a plateau?

Tuning seeds 3000-3029, gate varied, everything else identical:

| gate | win% | trades/wk | edge | worst-model |
|---|---|---|---|---|
| eff 36 alone | 70.4 | 23.3 | +0.600 | +0.523 |
| **eff 48 alone (champion)** | 71.6 | 15.0 | **+0.631** | +0.550 |
| eff 60 alone | 71.9 | 9.2 | +0.567 | +0.454 |
| eff 72 alone | 70.2 | 6.0 | +0.528 | +0.425 |
| mean of 5 windows | 73.6 | 12.1 | +0.642 | +0.539 |
| vote 3 of 5 | 73.8 | 12.3 | +0.650 | +0.571 |

**48 is a peak, not a plateau** — every neighbour is worse. That is the
signature of a partly-lucky parameter.

### Certification, 50 virgin seeds (3100-3149)

| gate | win% | trades/wk | edge | worst-model | med DD | worst DD | losing runs |
|---|---|---|---|---|---|---|---|
| single window 48 | 73.7 | 13.9 | +0.640 | +0.561 | 6% | 19% | 13/100 |
| **mean of 5 windows** | **75.8** | 11.1 | **+0.699** | **+0.652** | 5% | **15%** | 12/99 |
| vote 3 of 5 | 74.5 | 11.6 | +0.664 | +0.605 | 5% | 15% | 12/100 |

**ADOPTED: the mean of five windows** (`UseEnsembleQuality`, default on).

Honest reading of the significance: mean-vs-champion is +0.059 edge at a
combined SE of ~0.025 — about 2.4 sigma, and +2.1 points of win rate at ~1.9
sigma. Not overwhelming on edge alone. What makes it convincing is the shape:
it wins on *both* tuning and virgin seeds, and **the gain is largest on the
worse of the two market models** (+0.091 there vs +0.059 on the mean). An
improvement that grows when conditions get worse is robustness; one that
shrinks is a fit. It also removes a fitted parameter instead of adding one.

The choice *between* mean-of-5 and vote-3-of-5 is noise — they swapped ranks
between the tuning and certification sets. Took the mean because it is
Carver's actual prescription and better on the metric we rank by.

**Cost: ~2.8 trades/week.** Recorded plainly since "trade more" is a standing
requirement.

### Two measurement bugs caught while running this

1. **Trades/week was double-counted.** The rate was computed across both market
   models' bars, then multiplied by two again. Every frequency figure in this
   pass was ~2x too high until fixed.
2. **The gate was contaminating the target.** The variant's trend-quality value
   fed the conviction scaler that sets reward:risk, so a gate returning a high
   value automatically got a wider target and won on *exit geometry* rather
   than on entry selection. With the confound removed, vote-3/5's apparent
   +0.783 edge fell to +0.650. **The first run of this test produced a fake
   winner.** Conviction is now sourced identically for every variant.

### Harness note

This pass used a rebuilt harness. It reproduces the champion's win rate closely
(73.7% here vs 71.3% recorded) but reports higher frequency (13.9 vs 9.9
trades/week), so it is not bit-identical to the one behind the earlier numbers.
Every comparison above is internally consistent — both arms, same harness —
but do not compare these absolute frequencies against pre-August figures.

## Head-to-head vs the previously deployed config (Aug 2026)

The owner was still running the pre-research build and asked whether the new
one really beats it. The two numbers on record came from DIFFERENT harnesses
(+0.484 from the old one, +0.640 from the rebuilt one), so the comparison was
not valid as stated. Re-ran the old configuration through the current harness
so both arms are measured the same way.

Virgin seeds 3100-3149, both market models:

| config | win% | trades/wk | edge | n |
|---|---|---|---|---|
| **previous** — eff24@0.60, 3 positions, gap 4, 6 voters, no session filter | 68.3 | 8.7 | +0.536 | 2,205 |
| **current** — eff48@0.50, 10 positions, gap 2, 3 voters, session filter | **73.7** | **13.9** | **+0.640** | 3,548 |

Edge difference +0.104 at a combined SE of 0.027 — about **3.9 sigma**. Real,
not noise. Better on all three of win rate, frequency and per-trade edge.

### The part that is easy to get wrong

Per-trade edge and per-week growth are not the same question, and here they
give different answers:

| config | R/week | risk/trade | account growth/week |
|---|---|---|---|
| previous | 4.65 R | 3.0% | 13.9% |
| current | 8.92 R | 1.5% | 13.4% |

**Nearly double the R per week, and essentially identical expected account
growth**, because risk per trade was halved when concurrency went from 3 to 10.
The improvement is not more money — it is the *same* money earned from more
trades at lower risk each, which is why worst drawdown falls (35% -> 19%).

Stated plainly here because "higher edge" reads as "grows faster" and in this
case it does not. Anyone raising risk to capture the difference should read the
sizing table above first: 10 concurrent positions at 3% is 30% exposure, well
past the ~15.6% Kelly estimate, where added size increases drawdown without
increasing return.

### Deployment note

The owner deployed the **single-window** build (`UseEnsembleQuality = false`)
in preference to the ensemble, trading ~2.8 trades/week more for a slightly
lower per-trade edge and a worst drawdown of 19% rather than 15%. Both ship in
the same file; it is a parameter, not a code change.

**Next evidence should be live demo fills, not more simulation.** Every number
in this document is SIMULATED, on models where a coin flip scores +0.11 to
+0.28R. The tuning has reached the point where further gains measured here are
worth less than one week of real fills.

## Entry timing: bar close vs intra-bar (Aug 2026) — keep bar close

Owner asked why entries wait for the 15m bar to close, wanting the bot to
trade without waiting. Measured rather than argued.

**Method.** Evaluating mid-bar, treating the current price as the bar's close,
is exactly equivalent to reading a 15m series with a phase offset — minute t
sits at phase t%15. So the test builds all 15 phase-shifted views and varies
only how often an entry is ALLOWED to fire. Both arms use identical exits,
sizing, concurrency and costs, and both check stops/targets every minute
(broker-side SL/TP works that way regardless). 20 virgin seeds (3200-3219).

| decide every | win% | trades/wk | edge | med DD | worst DD | growth |
|---|---|---|---|---|---|---|
| **15 min (bar close)** | **69.0** | 12.0 | **+0.602** | 6% | 24% | x1.24 |
| 5 min (3x a bar) | 66.6 | 16.9 | +0.466 | 9% | 25% | x1.35 |
| 1 min (intra-bar) | 66.3 | 20.9 | +0.513 | 11% | 35% | x1.45 |

Deciding intra-bar costs **0.09-0.14R of per-trade edge** (bar-close vs 1-min
is 2.4 sigma). The ordering between the 5-min and 1-min rows is within noise;
the gap from bar close to either is not. This is signal flicker: a setup that
has all three votes at minute 7 need not still have them at minute 15, and a
trade taken on the transient one was never actually signalled by the strategy.

### It is leverage, not edge

Intra-bar trades more and grows more, which looks like an improvement until
bar-close is dialled up to the same drawdown:

| config | med DD | worst DD | median growth | losing runs |
|---|---|---|---|---|
| intra-bar @ 1.50% | 11% | 35% | **x1.45** | 5/40 |
| bar close @ 2.50% | 10% | 37% | **x1.43** | 8/40 |

**Identical growth at identical drawdown.** Intra-bar entry buys nothing the
risk slider does not already give, and it pays for it in per-trade edge — the
cushion that absorbs real-world slippage. (Intra-bar does show fewer losing
runs, 5/40 vs 8/40, but at n=40 that is about one standard error.)

**The sim almost certainly understates the real cost.** These minute paths have
no spread widening, no spikes and no wicks. Live, a mid-bar signal is exactly
where a wick fires and reverses. Treat -0.09R as a floor on the damage.

**Verdict: keep bar-close decisions.** If more growth is wanted, raise risk to
2.0-2.5% — same effect, measured, no signal degradation, one parameter, and
reversible.

**Practical note.** On the day this was asked, the live log showed trend
quality 0.22-0.27 against a 0.50 gate for four straight hours. Intra-bar
entry would have changed nothing: the bot was not waiting for a bar to close,
it was waiting for a trend to exist.

## Win rate correction (Aug 2026) — the headline numbers were ~5 points high

Owner asked why recent tests kept showing ~70% when the shipped file claimed
73.7%. They were right to ask. Two causes, isolated by running the same
configuration four ways:

|  | exits on 15m closes | exits every minute |
|---|---|---|
| cert seeds 3100-3149 | **73.7%** (n=3,548) | 71.8% (n=3,579) |
| seeds 3200-3219 | 70.6% (n=1,212) | 69.0% (n=1,221) |

**1. The exit check was optimistic (-1.8 points).** The harness tested stops
only at 15-minute bar closes, so a stop touched mid-bar that recovered before
the close was scored as never hit. A real resting stop order fills on touch.
The effect is a consistent -1.8 points on both seed sets. Every win rate in
this document measured with closes-only exits carries that bias.

**2. Seed luck (-3.1 points).** 73.7% was one sample of 50 price paths;
another sample of 20 gives 70.6% under identical rules. That is ordinary
sampling variation, but it was quoted as though exact.

**Honest expectation: ~70-71% for the single-window build, +/-2-3 points.**

**What this does and does not invalidate.** The bias is uniform across
configurations (-1.8 on both seed sets), so the A/B comparisons — ensemble vs
single window, concurrency levels, entry timing, the head-to-head against the
previous build — are unaffected in direction or size. What was wrong is the
absolute figure quoted to the owner and written into the shipped file header.

**Process lesson.** A backtest that resolves exits on bar closes is not
conservative, it is optimistic, and the error runs in the direction that
flatters the strategy. Resolve exits at the finest granularity available, or
state the bias out loud. Added to the traps list.

Corrected in `tools/GoldEdgeNews.cs`.

## Harness rebuild + frequency pass (Aug 2026) — new shipped config

Goal from the owner: fix the measurement mistakes, and make it trade more —
it sat flat all day while the very first cTrader bot traded constantly.

### 1. The harness was rebuilt, then every adopted decision re-tested

`honest.py` resolves exits at MINUTE granularity (entries still on bar close,
as the cBot does). The old harness resolved exits at 15m bar closes, forgiving
every stop that was touched mid-bar and recovered — worth +1.8 points of win
rate in the strategy's favour.

Re-certified on virgin seeds 3300-3349:

| decision | verdict under the corrected harness |
|---|---|
| Ensemble trend quality vs single 48-bar window | **HOLDS.** 73.0% / +0.656 / worstDD 21% vs 70.0% / +0.620 / worstDD 30%. Better on all three. |
| Concurrency | **HOLDS, flat.** edge +0.610 / +0.627 / +0.620 / +0.615 at 3 / 7 / 10 / 14 positions. Only exposure and drawdown change. |
| Entry on bar close | **HOLDS.** (measured separately, see entry-timing section) |

No adopted decision reversed. The bias was uniform, as expected.

### 2. Which gate was actually blocking the trades

Everything except the trend-quality threshold turned out to be nearly inert:

| loosening (from ensemble @0.40) | trades/wk | edge |
|---|---|---|
| baseline | 28.2 | +0.534 |
| votes 2/3 instead of 3/3 | 28.2 | +0.533 |
| ADX >= 12 instead of 18 | 28.6 | +0.534 |
| no ADX gate at all | 28.9 | +0.525 |
| no session filter | 30.0 | +0.503 |
| gap 1 bar instead of 2 | 35.1 | +0.529 |

**The vote count and ADX do essentially nothing** — when efficiency is high the
three voters almost always agree anyway, and the efficiency ratio subsumes ADX.
Only the threshold, and to a lesser degree the entry spacing, move frequency.

The threshold is a clean dial, and edge decays gracefully rather than falling
off a cliff:

| eff >= | trades/wk | edge | worst DD @1.5% |
|---|---|---|---|
| 0.50 | 11.4 | +0.648 | 19% |
| 0.45 | 18.6 | +0.589 | 22% |
| 0.40 | 28.2 | +0.534 | 33% |
| 0.35 | 40.4 | +0.493 | 39% |
| 0.30 | 56.2 | +0.429 | 42% |
| 0.25 | 74.8 | +0.378 | 43% |

### 3. The finding: buy frequency with the threshold, pay for it with size

Loosening alone raises drawdown. Loosening *and* cutting risk per trade does
not — and lands strictly ahead. Certified on virgin seeds 3500-3549:

| config | win% | trades/wk | edge | worst DD | growth |
|---|---|---|---|---|---|
| previous — eff.50, gap2, 10 pos @1.50% | 74.4 | 11.2 | +0.681 | 31% | x1.24 |
| **new — eff.45, gap1, 14 pos @1.00%** | **70.3** | **28.7** | +0.572 | **30%** | **x1.46** |
| eff.45, gap1, 20 pos @1.00% | 70.1 | 33.7 | +0.575 | 38% | x1.61 |
| eff.40, gap1, 14 pos @1.00% | 67.8 | 43.7 | +0.557 | 36% | x1.65 |

**2.6x the trades, the same drawdown, and more growth.** Win rate falls 4
points and per-trade edge falls 0.11 — both expected from a looser gate — but
many small diversified bets beat few large ones at matched risk.

Total exposure also *falls*: 14 x 1.0% = 14%, against 10 x 1.5% = 15%. That
partly answers the concentration problem noted below.

**ADOPTED as the shipped default:** ensemble trend quality >= 0.45, 1-bar entry
spacing, 14 concurrent positions, 1.0% risk per trade.

### Concentration risk — measured, not resolved

When 5 or more positions are open they are the **same direction 100% of the
time**. This is one directional bet in several pieces. Mitigations that are
real: entries are staggered, each carries its own swing-based stop at its own
price, and each has its own conviction-scaled target. Measured over 227
simulated days, worst single day was **-13.9%** and the -15% daily stop never
fired.

What the measurement cannot cover: **the simulators contain no gaps.** Several
correlated positions gapping through their stops together is precisely the tail
this evidence is blind to. Anyone who weights that risk above frequency should
set concurrency to 5-7; edge is flat across the range, so it costs only trades.

### Account-size floor

Gold's minimum trade is 1 oz, risking ~$18-64 depending on stop width
(0.4-1.4% of ~$4,580). At 1.0% risk a $3,000 account has a $30 budget, so the
widest-stop setups get skipped by the too-small guard. Below roughly $2,500
this configuration cannot size properly at all. Sub-1% risk settings are not
deployable at this account size however well they score in simulation.

### Correction to the section above — idle DAYS, not trades per week

The frequency fix above was measured against the wrong target. The owner's
complaint was "it hasn't traded today", and trades-per-week hides exactly that:
a config averaging 28/week can stand aside for three days and then fire twenty
times inside one trend.

Measured properly — share of days with ZERO trades:

| threshold | idle days | median trades/day |
|---|---|---|
| 0.50 (what was running) | **76%** | 0 |
| 0.45 (the "fix" above) | **66%** | 0 |
| 0.40 | 51% | 0 |
| **0.35** | **36%** | 3 |
| 0.30 | 23% | 10 |
| 0.25 | 11% | 17 |

**Lowering 0.50 to 0.45 barely moved the thing being complained about** — 76%
idle to 66%. The fix was real but aimed at the wrong metric.

Why: trend quality has a **median of 0.17** and a 95th percentile of **0.47**,
so a 0.50 gate fires in roughly the top 3% of bars. The live log reading
0.22-0.27 was an *ordinary* tape near the 70th percentile — not unusual chop.
The threshold had been set where the bot is nearly always standing aside.

### Final config, certified on FOUR independent virgin seed sets

`eff >= 0.35, gap 1 bar, 10 concurrent, 1.0% risk, ensemble trend quality`

| seeds | win% | trades/wk | edge | worst DD | growth | losing |
|---|---|---|---|---|---|---|
| 3300-3349 | 66.5 | 54.3 | +0.499 | 41% | x1.91 | 7/100 |
| 3500-3549 | 64.7 | 50.8 | +0.474 | 37% | x1.71 | 7/100 |
| 3600-3649 | 65.1 | 50.4 | +0.484 | 30% | x1.76 | 8/100 |
| 3700-3749 | 66.2 | 51.8 | +0.527 | 33% | x1.84 | 6/100 |

Against the eff.45/14-position config from the previous section, on the same
four sets: **1.7x the trades, half the idle days, consistently higher growth
(x1.71-1.91 vs x1.43-1.61), and fewer losing runs (6-8/100 vs 7-18/100)**, for
about 4 points more drawdown and 5 points less win rate.

Reported as a RANGE across seed sets rather than one number — quoting a single
sample as though exact is the mistake that produced the "73.7%" episode.

Edge +0.47 to +0.53 stays clear of the ~+0.3R floor below which the cushion
stops reliably covering real slippage.

**Dials, both flat in edge so they cost only trades:** raise the threshold
toward 0.45 to trade less and more selectively; lower concurrency to 6-7 to cut
drawdown.

## The live tape vs the simulators — and a diagnostic to settle it

The owner's log showed trend quality 0.22-0.27 for four straight hours. Checked
whether the new 0.35 threshold would have helped there. It would not.

**The ensemble reads the same as the single window, not higher.** Measured over
68,400 bars — when the single 48-bar window reads 0.22-0.27, the ensemble reads
a median of **0.23**, and clears a 0.35 gate only **3%** of the time.

| single-48 reads | ensemble median | clears 0.35 |
|---|---|---|
| 0.15-0.20 | 0.17 | 0% |
| 0.20-0.25 | 0.21 | 2% |
| 0.25-0.30 | 0.26 | 6% |
| 0.30-0.35 | 0.30 | 21% |
| 0.35-0.40 | 0.35 | 48% |
| 0.40-0.50 | 0.41 | 81% |

So the shipped build would also have stood aside through that window. Lowering
0.50 to 0.35 cuts idle days from 76% to 34% *on average*, but that particular
afternoon sat below even the new gate. Simulated days of exactly that shape
exist and look identical: best quality 0.23, zero signals at every threshold
down to 0.25, seven at 0.20.

### Rather than guess the threshold a third time, instrument it

Added daily diagnostics to the cBot. Every session now reports:

```
DAY SUMMARY 2026-08-22 | 84 bars | best quality 0.23 (threshold 0.35) | 0 trades opened
   signals available at each threshold -> 0.45:0  0.40:0  0.35:0  0.30:0  0.25:0  0.20:7
   NO TRADES: the market never reached the threshold. The line above shows
   which setting would have traded, and how often.
```

The counts hold everything except the quality threshold constant, so they
isolate exactly what that one setting costs. The status line also now carries
`need 0.35, best today 0.23` and a running trade count.

**Why this matters more than another tuning round.** Every threshold
recommendation so far rests on two synthetic gold models. Whether real gold
produces clean trends as often as they do is unverified, and it is the
assumption the whole filter rests on. One live day of these summaries answers
it from real prices — and if real gold's efficiency distribution sits below the
simulators', the threshold has been wrong all along for reasons no amount of
re-certification here would have caught.

Printed at UTC day rollover and again on stop, so a bot shut down before
midnight still reports its session.

## THE CRITICAL FINDING — the edge is contingent on how much gold trends

Everything certified in this document rests on two simulated markets built for
this project. Whether real gold trends as much as they do was never tested. It
is the assumption the entire strategy sits on, and it turns out to be the
assumption that matters most.

Built a **third market model** with trend persistence dialled down, then
matched its volatility to the other two so the comparison isolates trending
alone (15m vol 0.172% vs M1 0.179%, M2 0.170%).

| model | median 48-bar efficiency | edge | win% | growth | losing runs |
|---|---|---|---|---|---|
| M1 (existing) | 0.17 | **+0.565** | 68.1 | x1.64 | 2/40 |
| M2 (existing) | 0.19 | **+0.432** | 63.9 | x1.80 | 4/40 |
| **M3 (choppier)** | **0.12** | **+0.083** | 51.3 | x1.02 | 19/40 |

**A drop in median trend quality from 0.17-0.19 to 0.12 destroys the edge
entirely.** Win rate falls to a coin flip, growth goes flat, and half the runs
lose money. Both models were built by this project and both trend more than the
third; nothing in dozens of prior certifications could have surfaced this,
because every one of them used the same two markets.

### Controls run before believing it

1. **Plumbing.** The identical code path reproduces the known M1/M2 numbers
   (edge +0.565 / +0.432 at threshold 0.35). Not a harness bug.
2. **Volatility confound.** The first version of M3 ran at 0.103% per 15m
   against ~0.175%. With the stop clamped at a 0.4% floor, a quiet tape cannot
   reach its targets, which would sink any strategy. Re-matched to 0.172% and
   the collapse persisted — so it is trending, not volatility.
3. **Random baseline.** Edge is measured against a matched-rate coin flip on
   each model separately, so a model that trends more cannot flatter the
   strategy through the baseline.

### What this changes

**Loosening the filter to trade more is the WRONG response to a choppy market.**
On M3 the edge is worst and drawdown highest at low thresholds:

| threshold | edge on M3 | worst DD |
|---|---|---|
| 0.45 | +0.135 | 16% |
| 0.35 | +0.083 | 29% |
| 0.30 | +0.080 | 42% |
| 0.25 | +0.042 | 53% |

If real gold looks like M3, the answer is not a lower threshold — it is not
trading this strategy at all. Ranked by the WORSE model, as this project's own
rule requires, 0.45 is the robust setting and 0.35 is a bet that gold trends
like the simulators.

**0.35 ships anyway**, because the owner's priority is frequency and on the
choppy model the difference between 0.35 and 0.45 is small in absolute terms
(both near zero) while on the trending models 0.35 is clearly better. But it is
a bet, and it is now labelled as one.

### The diagnostic settles it from one live session

The cBot now reports the discriminating statistic directly:

```
   trend quality distribution today: median 0.14, 75th 0.22, best 0.31
   -> median 0.14 is BELOW the certified range (0.17-0.19). Edge is likely
      thinner live than in testing. Collect more days before tuning.
```

Thresholds in that message: **>=0.16** in line with certification; **0.13-0.16**
below it, edge likely thinner; **<0.13** the collapse zone, where the tested
edge is roughly zero at every threshold and loosening the filter makes things
worse rather than better.

**This is now the highest-value measurement in the project.** A few live
sessions reporting real gold's median trend quality is worth more than any
further certification against the two models that produced these numbers.

## The random-walk floor — the reference point that was missing

Trend quality on a **pure random walk** is not zero. It is mechanically about
`1/sqrt(window)`; measured over 40 random-walk series it is **0.124** for a
48-bar window (theory: 1/sqrt(48) = 0.144).

That reframes every threshold discussion in this document. A reading of 0.12
does not mean "weak trend" — it means **no trend at all**. The strategy's edge
comes entirely from the EXCESS above that floor, and across four markets it
tracked that excess almost linearly:

| market | median 48-bar efficiency | excess over chance | edge |
|---|---|---|---|
| pure random walk | 0.124 | +0.000 | — (nothing works) |
| M3 choppy | 0.135 | +0.011 | +0.083 |
| M1 | 0.165 | +0.041 | +0.565 |
| M2 | 0.185 | +0.061 | +0.432 |

**Both markets this strategy was certified on sit 0.04-0.06 above chance.**
Whether real gold does was never tested.

### Mean reversion re-tested, and re-rejected — but for the right reason now

Mean reversion was rejected early on, but only ever on M1/M2. A counter-trend
strategy losing on trending markets proves nothing about a choppy one, so it
was worth re-testing on M3. Fading RSI extremes when efficiency is low:

| market | trades/wk | win% | edge |
|---|---|---|---|
| M1 trending | 6.9 | 39.3 | -0.121 |
| M2 trending | 7.2 | 43.7 | -0.211 |
| **M3 choppy** | 7.6 | 45.0 | **-0.125** |

It loses on the choppy model too — so the original rejection stands, and now
for a defensible reason. The explanation is the floor above: M3 has a lag-1
autocorrelation of +0.005. It is not a *mean-reverting* market, it is a
*random walk*. Low efficiency does not imply exploitable reversion; it implies
nothing is there. **No strategy of any kind can extract edge from a random
walk**, which is exactly why trend-following and mean reversion both scored
~zero on it.

### What the live diagnostic now reports

Bands are set against the 0.124 floor rather than against the simulators, so
they carry absolute meaning:

| excess over chance | verdict |
|---|---|
| **>= +0.030** | gold trends as much as the certified markets; tested edge should carry over |
| **+0.012 to +0.030** | trending, but less than either certified market; expect a thinner edge |
| **< +0.012** | no exploitable trend. Nothing works here — not trend-following, not mean reversion. Do NOT loosen the filter. |

The last band matters most, because the intuitive response to "it isn't
trading" is to loosen the filter, and on a random-walk tape that raises
drawdown without adding any edge at all.

**Open question, and now the only one worth spending effort on: where does real
gold's median 48-bar efficiency actually sit relative to 0.124?** Nothing in
this repository can answer it — every model here was built by this project.
A few live sessions can.
