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

## Process fix — the C# is now compiled, not eyeballed

Every cBot in this repo was previously verified by counting braces. That
catches almost nothing: not undefined names, not wrong types, not bad
overloads, not duplicate locals. This project had already shipped a `CS0128`
duplicate-variable error once and only caught it by luck.

`tools/verify/build-check.sh` now compiles a bot offline against
`tools/verify/calgo_stubs.cs` — a minimal cAlgo.API surface carrying the
signatures the bots actually use. Requires `apt-get install -y mono-mcs`.

```
./tools/verify/build-check.sh tools/GoldEdgeNews.cs
```

Warnings are escalated to errors. **The script also runs a negative control on
every invocation**: it injects a deliberate type error and requires the
compiler to reject it. A check that cannot fail proves nothing, and stubs can
silently drift loose enough to accept broken code.

Verified when introduced — all four negative controls caught:

| injected fault | compiler response |
|---|---|
| duplicate local `need` (the real past bug) | `CS0128` |
| typo'd field name | `CS0103` |
| `string` assigned to `double` | `CS0029` |
| negative control in the script | rejected |

All five bots in `tools/` compile clean: GoldEdgeNews, GoldEdge, GoldBotTrend,
GoldBotPro, GoldBot.

**Run this before sending any bot file.** Shipping code that does not build
wastes a cTrader build cycle and, worse, teaches the owner to distrust files
that are otherwise fine.

## The assumption underneath everything, finally stated in standard units

The trend filter gates on the Kaufman efficiency ratio, which is a
project-specific quantity. Converted to the **Hurst exponent** — the standard
measure of whether a series trends more than chance — the whole project's
assumption becomes checkable against published research.

Method: generate fractional Brownian motion at known H by random midpoint
displacement, normalise to gold's ~0.174% per-15m volatility, and measure the
same median 48-bar efficiency the cBot reports.

| Hurst H | median 48-bar efficiency | vs random-walk floor | meaning |
|---|---|---|---|
| 0.40 | 0.088 | -0.036 | mean-reverting |
| 0.45 | 0.103 | -0.021 | mean-reverting |
| **0.50** | **0.121** | -0.003 | **random walk** |
| 0.55 | 0.144 | +0.020 | trending |
| 0.60 | 0.172 | +0.048 | trending |
| 0.65 | 0.208 | +0.084 | trending |

Control: H=0.50 reproduces the independently measured random-walk floor of
0.124 (got 0.121), so the generator is sound.

### What this project's simulators actually assume

| model | median efficiency | **implied H** |
|---|---|---|
| M1 (`simulator.py`) | 0.165 | **~0.59** |
| M2 (`simulator2.py`) | 0.185 | **~0.62** |

**Both simulators assume gold has a Hurst exponent near 0.60** — strongly
trending. Every result in this document inherits that assumption.

### What the literature says about real gold

Published findings, from search summaries rather than papers read in full, so
treat as indicative:

- Intraday Hurst estimates on high-frequency data cluster **close to 0.50**.
- Persistence *falls* with frequency: monthly persistent, daily near a random
  walk, **intraday anti-persistent** (H < 0.5) — consistent with well-known
  microstructure effects.
- One study reports **gold at H = 0.41 on eight years of daily returns** —
  mean-reverting, not trending.

If real 15-minute gold sits anywhere near H = 0.50, its median efficiency is
about 0.12 and the strategy's edge is roughly zero. At H = 0.45 it is 0.103,
*below* the random-walk floor, and a trend filter is gating on noise.

**This is the single largest risk in the project, and no amount of
certification against M1/M2 could ever have surfaced it** — those two models
are where the H≈0.60 assumption lives.

### Caveats, stated honestly

- Hurst estimation is notoriously noisy and method-dependent; different
  estimators disagree by more than the gap being argued about here.
- The cited figures are second-hand from search summaries.
- The relevant scale is 15 minutes to 12 hours. Tick-level anti-persistence
  (bid-ask bounce) need not carry to that horizon.
- None of this proves the strategy fails live. It shows the assumption is
  unverified and that published evidence points the wrong way.

### The bot now reports it directly

```
   -> implied Hurst exponent ~0.52. (0.50 = random walk, nothing to trade;
      below 0.50 = mean-reverting; this bot's simulators assumed 0.59-0.62.)
```

A few live sessions produce a number comparable with published research.
**That measurement now outranks every other open item in this project** — if
real gold reads near 0.50, no threshold, no ensemble and no exit tweak will
help, and the correct decision is to stop trading this strategy rather than to
tune it further.

## Two more of my own measurement errors, found by a control that had to fail

Running the strategy across markets of known Hurst exponent produced an
impossible result: **+0.186 edge on a pure random walk**, where by construction
no entry rule can predict anything. Chasing it found two problems.

### 1. A baseline bug in the rebuilt harness

`score()` computed the coin-flip trade rate **per minute** but the coin is
evaluated **per bar**, so the baseline took roughly 1/15th of the intended
trades. That does not bias its mean, but it made it noisy enough to move
measured edge by up to ~0.13. Fixed by converting the rate to per-bar.

Effect on the shipped configuration (same seeds, before -> after):

| seeds | edge before | edge after |
|---|---|---|
| 3500-3549 | +0.474 | **+0.463** |
| 3600-3649 | +0.484 | **+0.467** |
| 3700-3749 | +0.527 | **+0.498** |

Small, and no conclusion changes — but the numbers stated earlier in this
document were wrong by that much.

### 2. A residual bias that does NOT go away: trade-geometry selection

With the baseline fixed, the random-walk control still reads **+0.057** instead
of zero. That residual is real and worth naming, because it inflates every
edge figure here by roughly that amount.

Measured directly on the random-walk control:

| | mean stop (% of price) | mean reward:risk | exits by stop | by target | by time |
|---|---|---|---|---|---|
| strategy | 1.006 | 1.52 | 31.5% | 15.6% | **51.4%** |
| coin flip | 0.547 | 1.19 | 48.3% | 41.4% | 10.1% |

The strategy only fires when the recent swing range is wide — that is what a
high efficiency reading *is* — so it gets stops nearly twice as wide. Wide
stops mean the 40-bar time stop fires on half its trades, and a time-stop exit
marks out near 0R instead of a full -1R. **That is favourable trade geometry,
not prediction**, and the rate-matched coin flip does not receive it.

**Treat ~+0.06 as a floor to subtract from every edge number in this document.**
The honest reading of the shipped configuration is roughly **+0.40 to +0.44**,
not +0.46 to +0.50.

### The corrected map of where this strategy works

| Hurst H | market | edge (measured) | edge (less the +0.06 floor) |
|---|---|---|---|
| 0.45 | mean-reverting | -0.101 | **-0.16** |
| 0.50 | random walk | +0.057 | **~0.00** |
| 0.55 | trending | +0.197 | **+0.14** |
| 0.60 | trending | +0.417 | **+0.36** |

**The strategy needs roughly H > 0.52 to have any real edge, and materially
more than that to be worth trading.** This project's simulators sit at
H ~0.59-0.62. Published intraday estimates cluster near 0.50 and below.

Mean reversion, re-tested on genuinely mean-reverting markets this time
(H=0.40 and 0.45) rather than on trending or random-walk ones, still never
turns positive (-0.064 at H=0.40, its best result). So there is no
"trade the other side when gold is choppy" fallback available.

## CORRECTION — mean reversion was rejected on a measurement bug

Earlier in this document mean reversion is rejected three times, most recently
with "it loses on the choppy model too, so the original rejection stands". That
was **wrong**, and for the same reason as the trend-side numbers: the
mean-reversion test carried the per-minute/per-bar baseline bug, so its
baseline was 1/15th the intended size and its mean was noise.

Re-run with the baseline fixed, on markets of known Hurst exponent:

| market | trades/wk | win% | edge (reported before) | edge (correct) |
|---|---|---|---|---|
| H=0.40 mean-reverting | 3.0 | 62.6 | -0.064 | **+0.298** |
| H=0.45 mean-reverting | 5.1 | 56.4 | -0.121 | **+0.160** |
| H=0.50 random walk | 7.6 | 49.3 | -0.077 | +0.021 |
| H=0.60 trending | 11.7 | 38.1 | -0.191 | -0.208 |

The H=0.50 control now reads ~zero, as it must. **Mean reversion works on
mean-reverting markets.** The three earlier rejections were all run on markets
that trend (M1/M2) or on a random walk (M3) — never on a mean-reverting one.
Testing a counter-trend strategy only on trending data is not a test.

### The full regime map

| Hurst | market | trend only | fade only | both |
|---|---|---|---|---|
| 0.40 | mean-reverting | **-0.265** | **+0.298** | +0.098 |
| 0.45 | mean-reverting | -0.104 | +0.160 | +0.016 |
| 0.50 | random walk | +0.052 | +0.021 | +0.038 |
| 0.55 | trending | +0.215 | -0.170 | +0.129 |
| 0.60 | trending | **+0.446** | -0.208 | +0.317 |

Subtract the ~0.06 geometry floor from every figure.

Two things follow.

**1. Trend-following does not merely fail on a mean-reverting tape — it loses
money there** (-0.265 at H=0.40, i.e. about -0.33 after the floor). The risk of
the unverified Hurst assumption is worse than previously stated: it is not
"no edge", it is negative edge.

**2. Running both sides is insurance, not an improvement.** It is positive at
every H, which trend-only is not, but the idle half drags on the working half:
+0.32 becomes +0.10 in the best case. It buys robustness with return.

### Shipped as an off-by-default option

`UseMeanReversion` (default **false**) adds the fade side: enter against an RSI
extreme when trend quality is at or below 0.20, using the same stop, sizing and
news machinery so the two sides cannot be compared unfairly.

**Off by default deliberately.** Turning it on is only correct once the DAY
SUMMARY has shown, across several live sessions, that gold's implied Hurst runs
at or below 0.50 — the point at which trend-following is the wrong strategy
rather than merely a quiet one. That decision now rests on a live measurement
instead of on the simulators that assumed the answer.

### The fade side certified on M1/M2 (the reference markets)

It had only been measured on synthetic Hurst paths. Certified at the shipped
settings on virgin seeds 3800-3839, both reference models — which are the
TRENDING case (implied H ~0.59-0.62), so this is the fade side's worst
environment:

| configuration | trades/wk | idle days | win% | edge | worst DD | growth | losing runs |
|---|---|---|---|---|---|---|---|
| trend only (**shipped default**) | 49.7 | 38% | 66.3 | **+0.508** | 38% | **x1.92** | 5/80 |
| fade only | 7.5 | 19% | 40.5 | -0.207 | 19% | x0.95 | 64/80 |
| both (`UseMeanReversion=true`) | 57.2 | **19%** | 63.0 | +0.424 | 39% | x1.76 | 8/80 |

Exactly as the regime map predicts: on a trending market the fade side loses
money (64 of 80 runs), and switching it on costs about **0.08 of edge and 16%
of growth**.

**But it halves idle days, 38% -> 19%.** That is the trade available today: if
fewer flat days matter more than growth, enabling it buys that immediately at a
known price. If gold turns out to be mean-reverting instead, the same switch is
worth +0.30 rather than -0.21, and leaving it off is the expensive mistake.

The default stays OFF because these two models say trending, and they are all
the evidence there is until live sessions report otherwise.

### Fade parameters tuned — they had been shipped at test defaults

`ChopMax=0.20, RSI 30/70, rr 1.0` were carried over from a test script, never
swept. Tuned on MEAN-REVERTING markets (H=0.40/0.45), scored by the WORSE of
the two, then certified on virgin seeds 750-789:

| settings | H=0.40 edge | H=0.45 edge | trades/wk |
|---|---|---|---|
| old (chop 0.20, RSI 30/70, rr 1.0) | +0.272 | +0.114 | 3.2 |
| **new (chop 0.30, RSI 35/65, rr 1.5)** | +0.263 | +0.110 | **52.7** |

**Same edge, 16x the trades.** The old thresholds were so strict they almost
never fired — the identical mistake the trend side was making at eff>=0.50.
Deliberately tuned where the rule is meant to operate: tuning a counter-trend
rule on trending data would just select whichever settings fire least.

### ...and the fade tuning was then REVERTED — it optimised only one regime

The sweep above picked `chop<=0.30, RSI 35/65, rr 1.5` on mean-reverting
markets: same edge, 16x the trades. That looked like a clear win and it was
not. **The sweep only scored the regime the rule is meant for.** Checked on
M1/M2 — trending, the case where enabling the fade side is *wrong*, virgin
seeds 3900-3939:

| configuration | trades/wk | win% | edge | worst DD | losing runs |
|---|---|---|---|---|---|
| trend only (default) | 50.6 | 66.2 | +0.519 | 41% | 9/80 |
| **+ fade, strict (kept)** | 58.0 | 63.2 | +0.430 | 44% | 11/80 |
| + fade, "tuned" | 137.5 | 46.5 | **+0.100** | **74%** | **26/80** |

The looser settings **triple the losing runs and take drawdown to 74%** if gold
trends, in exchange for +0.009 of edge where they are right.

**Reverted to `chop<=0.20, RSI 30/70, rr 1.0`.** Since the regime is precisely
what is unknown, the correct setting is the one that is nearly as good when
right and far cheaper when wrong — which is this project's own worst-model rule,
broken by tuning a switch on only half the cases it will face.

---

## The regime switch: measure which market you are in, run only that side

*Certified 24 Aug 2026, virgin seeds 9100-9129 and 9200-9217.*

The revert above ended with a real problem left open. The strict fade settings
are cheap when wrong — but they fire **4.1 times a week**, which is not enough
to cover a choppy day. On the tape the owner logged on 24 Aug (ensemble median
0.100, implied H≈0.42) the bot opened **zero** trades in a session. Running
both halves always-on was measured at **−0.177** there. So the choice looked
like: idle, or lose.

It was a false choice. The reason the wider fade band was dangerous is that it
ran on trending tapes, where fading is the wrong bet. **The bot already knows
which tape it is on** — it computes trend quality every bar.

### The rule

Keep a rolling median of trend quality; compare it to the random-walk floor for
that same measure:

```
median >= floor + margin  ->  trends more than chance  ->  TREND side only
median <  floor + margin  ->  mean-reverting           ->  FADE side only
```

The floor must match the measure. For a single 48-bar window it is **0.124**;
for the 5-window ensemble it is **0.131**, because efficiency scales ~1/√n and
the shorter windows read higher on chance alone. The bot now derives it from
whichever windows are configured rather than hard-coding a number.

### Certification — 30 virgin seeds per market, 22 days, 1% risk

Three configurations, across five markets of known Hurst exponent plus both
project models. "both on" is what was previously shipped.

| market | config | tr/wk | win% | edge | med DD | worst DD | losing runs |
|---|---|---|---|---|---|---|---|
| H=0.40 mean-revert | both on | 20.3 | 37.4 | −0.188 | 17% | 32% | 25/30 |
| | switch only | 4.9 | 48.6 | −0.000 | 2% | 21% | 12/30 |
| | **switch + 35/65** | 27.5 | 62.7 | **+0.259** | 9% | 33% | **4/30** |
| H=0.45 mean-revert | both on | 35.3 | 42.7 | −0.073 | 21% | 44% | 22/30 |
| | switch only | 13.2 | 43.1 | −0.081 | 10% | 25% | 21/30 |
| | **switch + 35/65** | 37.5 | 53.2 | **+0.079** | 16% | 33% | 10/30 |
| H=0.50 random walk | both on | 52.1 | 47.8 | +0.026 | 23% | 46% | 16/30 |
| | switch only | 29.5 | 49.0 | +0.068 | 13% | 32% | 13/30 |
| | switch + 35/65 | 50.5 | 49.3 | +0.016 | 20% | 46% | 16/30 |
| H=0.55 trending | both on | 68.1 | 52.7 | +0.171 | 21% | 40% | 7/30 |
| | switch only | 47.6 | 53.9 | **+0.182** | 19% | 44% | 8/30 |
| | switch + 35/65 | 62.1 | 51.9 | +0.125 | 21% | 48% | 11/30 |
| H=0.60 trending | both on | 84.2 | 55.6 | +0.286 | 21% | 40% | 1/30 |
| | switch only | 66.8 | 58.0 | **+0.359** | 18% | 40% | 2/30 |
| | switch + 35/65 | 75.1 | 56.4 | +0.310 | 21% | 39% | 2/30 |
| M1+M2 models | both on | 79.6 | 61.4 | +0.409 | 17% | 36% | 1/60 |
| | switch only | 63.5 | 62.5 | **+0.433** | 14% | 36% | 3/60 |
| | switch + 35/65 | 71.1 | 60.6 | +0.372 | 16% | 45% | 3/60 |

*(subtract the ~0.06 trade-geometry floor from every edge)*

**ADOPTED: switch + fade band 35/65.** Stated honestly, it is not the best
configuration in any single regime — the switch alone beats it on all three
trending markets. It is the only one that is **positive in every regime**, and
the case it fixes is the case that actually happened: −0.188 → +0.259 and
25/30 losing runs → 4/30 on the owner's measured tape. The price is about 0.04
of edge where the old setting was already right.

Worst drawdown did **not** deteriorate (32–48% vs 32–46%) — the check that
killed the previous attempt at a wider fade band. The switch is what makes the
difference: it stops the loose band ever running on a trending tape.

### The knobs are a plateau, not a peak

Virgin seeds 9200-9217, all nine combinations:

| window | margin | choppy tr/wk | choppy edge | trending tr/wk | trending edge |
|---|---|---|---|---|---|
| 100 | 0.000 | 34.3 | +0.110 | 73.4 | +0.311 |
| 100 | 0.005 | 34.3 | +0.122 | 72.9 | +0.302 |
| 100 | 0.015 | 34.7 | +0.124 | 72.3 | +0.290 |
| 200 | 0.000 | 32.9 | +0.113 | 72.2 | +0.341 |
| 200 | 0.005 | 33.0 | +0.122 | 71.3 | +0.331 |
| 200 | 0.015 | 32.9 | +0.136 | 70.4 | +0.304 |
| 400 | 0.000 | 32.1 | +0.137 | 73.0 | +0.356 |
| 400 | 0.005 | 31.9 | +0.141 | 72.0 | +0.350 |
| 400 | 0.015 | 31.5 | +0.157 | 70.0 | +0.314 |

Every cell positive on both regimes. **Shipped 300 / 0.005 — the middle of the
region, not the sweep winner**, per the same rule that produced the ensemble.

### Two reporting bugs found and fixed while shipping this

1. **The DAY SUMMARY compared an ensemble median against the single-window
   floor** (0.124). Since the ensemble floor is 0.131, a tape sitting exactly
   at chance was reported as +0.007 *above* it. The verdict bands were reading
   one notch too optimistic on every session since the ensemble was enabled.
2. **`ImpliedHurst` was calibrated on a lone 48-bar window** but was being fed
   the ensemble median, which runs ~5.6% higher. The owner's 24 Aug tape was
   reported as H≈0.44 when the correct reading is **H≈0.42**. Both now convert
   to 48-bar-equivalent terms first. Port-tested against the Python: the C#
   returns floor 0.1309 and maps an M1-like ensemble median of 0.174 to H=0.59,
   matching M1's independently measured H≈0.59.

### Live-only refinement, stated because it differs from the harness

The backtest lets the rolling median fill from an empty deque. The cBot instead
**pre-fills it from historical bars at start-up**, so the switch is live on the
first bar rather than after 60 of them (15 hours on m15) spent running both
sides blind. This is strictly in the direction the harness measured — it only
removes a warm-up period that the harness paid once per 22-day run.

---

## The regime switch met real gold and deadlocked — and why the certification missed it

*24 Aug 2026, from a live demo log. This is a correction to the section above.*

The owner ran the regime-switch build. It opened correctly:

```
Regime primed from 300 historical bars: median quality 0.161 vs floor 0.131
   -> TRENDING | TREND side active from the first bar
```

Then, over the next four hours, every bar read:

```
quality 0.14 / 0.20 / 0.21 / 0.10 / 0.04 / 0.09 / 0.11 / 0.10 / 0.03 / 0.06
   / 0.05 / 0.04 / 0.05 / 0.07 / 0.04 / 0.07 / 0.09
regime TRENDING med 0.161 -> trend side | 0 trades today
```

**Zero trades, and structurally so.** The trend side needs quality ≥ 0.22 and
16 of 17 bars were below 0.20. The fade side could have taken all 16 — and was
blocked, because a 300-bar (75-hour) median still remembered the trending
stretch of 21-23 Aug.

### Why the certification could never have caught it

Every market the switch was certified on — the five fBm tapes and both project
models — **held one Hurst exponent for 22 days straight**. On a tape like that
a slow rolling median is never stale, so the switch's central weakness was
invisible by construction. Real gold changed character between the owner's own
sessions: H≈0.65 on 21-23 Aug, H≈0.42 on 24 Aug, and inside that afternoon
quality fell from 0.21 to 0.03 in two hours.

This is the same class of error as the twice-rejected mean-reversion result:
**a mechanism tested only in conditions where it cannot fail.** Homogeneous
tapes are to a regime switch what trending markets were to mean reversion.

### The market that should have existed first

`mixed.py` concatenates fBm blocks of differing H (0.38-0.65) in runs of half a
day to two days, volatility held equal so the *only* thing changing is how much
the tape trends. 80 tapes x 22 days, 1% risk, standard errors shown:

| configuration | tr/wk | win% | edge | ± | idle days | blocked bars | worst DD | losing |
|---|---|---|---|---|---|---|---|---|
| switch w300 + fade 30/70 | 32.6 | 48.3 | +0.107 | 0.014 | 32% | 31% | 49% | 37/80 |
| relative gates, top/bottom 15% | 43.5 | 49.7 | +0.130 | 0.012 | 23% | — | 48% | 32/80 |
| **switch w300 + fade 35/65 (SHIPPED)** | 49.2 | 49.2 | +0.079 | 0.011 | 8% | **31%** | 54% | 33/80 |
| switch w150 + fade 35/65 | 51.2 | 50.1 | +0.101 | 0.011 | 7% | 30% | 51% | 29/80 |
| no switch, fade 30/70 | 55.5 | 48.1 | +0.095 | 0.011 | 14% | 0% | 56% | 31/80 |
| no switch, fade 35/65 | 84.0 | 48.7 | +0.056 | 0.009 | 2% | 0% | 69% | 35/80 |
| relative gates, top/bottom 25% | 59.6 | 48.6 | +0.095 | 0.010 | 9% | — | 58% | 29/80 |
| relative gates, top/bottom 40% | 82.9 | 47.2 | +0.059 | 0.009 | 2% | — | 67% | 29/80 |
| trend-side-only switch (fade always allowed) | 74.8 | 49.3 | +0.048 | 0.009 | 2% | 0% | 66% | 38/80 |

*(subtract the ~0.06 trade-geometry floor from every edge)*

### What this actually says

**Frequency and edge trade off monotonically, and there is no configuration
that escapes it.** Sort the table by trades/week and the edge falls almost
without exception. After the geometry bias, **everything above roughly 60
trades a week is at or below zero.**

The direct fix for the deadlock — letting the fade side run whatever the regime
believes — is the *worst* row in the table: +0.048 against +0.101 for leaving
the block in place, worst drawdown 51% → 66%, losing runs 29/80 → 38/80. **The
block is earning its keep.** The bot standing still through that afternoon was
not a malfunction; it was the measured-correct action.

The relative-gate idea (fire in the top/bottom slice of the tape's own recent
quality distribution, so a threshold can never sit above everything the market
is producing) is the natural answer to "the gate is too high for this market".
It does not help: at 25% it matches the fixed gate inside one standard error,
and at 15% it *increases* idle days to 23%.

### The number that matters, restated honestly

The section above quotes +0.259 at H=0.40 and +0.372 on the project models.
**Those assume a regime that persists for weeks.** On a tape that changes
character every half-day to two days, the same strategy earns **+0.08 to +0.13
before the geometry bias, so roughly +0.02 to +0.07 after it.**

This project's own documented slippage floor is +0.3R. A strategy at +0.05R is
not a strategy; it is a coin flip paying rent to the spread. That applies to
the whole trend/fade family here, not just to the regime switch.

### Not changed, deliberately

The regime window looked worth shortening — 150 beat 300 by 0.044 on the tuning
seeds. It then **reversed on 20 virgin seeds** (+0.100 vs +0.149) and came back
+0.022 on the 80-tape set that partly overlaps the tuning seeds. Three samples,
two orderings: the window is not resolvable at this sample size. Per Carver's
rule and this project's own history of peak-picking, **it stays at 300** rather
than being set from the sample that happened to be run last.

---

## Testing a retail gold course: 7 lessons, one idea that survived

*24 Aug 2026. Owner sent Days 1-7 of a "XAUUSD Gold Masterclass" (SabFX Trader).*

Content by day: what trading is / sessions · candlestick anatomy · market
structure · support & resistance · BOS and CHOCH · trends · higher highs and
lower lows. Most of it is either already implemented, already in the rejected
ledger, or not a testable rule at all ("candles tell a story, not the future").

**Already settled here:**

| lesson | status |
|---|---|
| Top-down / higher-timeframe bias (Day 5, 6) | 4H bias + 15M entry: +0.570 vs +0.563 on 50 virgin seeds. Inside noise, costs frequency. **Rejected.** |
| "Enter on the pullback to structure" (Day 5) | Starved — 2 trades over a full seed set. In a high-quality trend price never returns to the level. |
| "Never buy because price is already going up" (Day 1) | The discount-entry rule. Same failure, 2 trades. |
| S/R for stop placement (Day 4) | **Already shipped** — the stop sits beyond the 12-bar swing plus a 5% buffer. That is a structure stop. |
| "Wait for confirmation, never trade one candle" (Day 2) | Already implied: entries decide on bar close and need 3 independent voters. |
| "Avoid ranging markets" (Day 7) | Already the trend-quality filter, and measured far more precisely than eyeballing a range. |

### The one that measured better: Break of Structure

Day 5's rule — *don't enter until price actually takes out the prior swing* —
added on top of the existing 3-voter test. All voters can agree while price is
still inside the previous range; those are the entries being paid for.

| market | baseline | + BOS | trades | worst DD |
|---|---|---|---|---|
| mixed regime (60 virgin tapes) | +0.092 | **+0.121** | 49.6 → 43.3/wk | 49% → 49% |
| model M1 (30 virgin seeds) | +0.287 | **+0.311** | 65.3 → 54.7/wk | 42% → **28%** |
| model M2 (30 virgin seeds) | +0.334 | **+0.345** | 77.4 → 63.5/wk | 42% → 41% |
| mixed regime (tuning set) | +0.070 | +0.088 | 48.8 → 43.0/wk | 49% → 38% |

Any one of these is 1-2 standard errors — but it is the same sign in every
market, on tuning and virgin sets alike, and losing runs fall (26/60 → 23/60).
That is the bar this project uses. **ADOPTED**, at a cost of ~13% of the trade
count. Reads closes, not wicks: a wick through a level is the rejection the
same course warns about, not a break.

### Measured and NOT adopted

- **HH/HL structure trend** (Day 7's core rule: long only while making higher
  highs *and* higher lows) — **+0.061 vs +0.070 baseline**, worst drawdown 51%
  vs 49%. It is a slower, noisier restatement of what the trend-quality filter
  already measures. Adding BOS to it (+0.073) only recovers what BOS
  contributes on its own.
- **BOS replacing the voters entirely** — +0.081 vs +0.088 for BOS *plus*
  voters. The structure break is a filter, not a signal.
- **Fading only at a support/resistance level with 3+ touches** — genuinely
  better (mixed +0.126, M1 +0.345, M2 +0.361) but costs a further 15% of trades
  for a gain inside one standard error. Held back because frequency is the
  binding complaint, not because it failed. Worth revisiting if the live ledger
  ever says the fade side is the problem. Levels were built as the course
  describes — swing pivots, clustered at 0.15%, counted by touches — and only
  usable after confirmation, so there is no lookahead.

The pattern across all seven lessons is the same one this file keeps recording:
the teachable, quotable parts ("trend is your friend", "structure is king") are
either already in the filter or unmeasurable, and the single mechanical rule
buried among them is worth about +0.03R.

---

## Timeframe, re-measured on the current build — m5 replaces m15

*24 Aug 2026. Certified on virgin seeds 9600-9639.*

This file's old timeframe table (m15 best at +0.974, m5 at +0.590) was measured
on a much stricter configuration: eff>=0.55, no fade side, no structure break.
Its own note says timeframe and exit style interact and a conclusion must not
be carried across a change to either. The entry rules have since changed twice,
so the table had to be re-run.

| market | tf | tr/wk | win% | mean R | growth | losing | worst DD |
|---|---|---|---|---|---|---|---|
| mixed regime | m15 | 45.6 | 52.1 | +0.121 | x1.17 | 12/40 | 39% |
| | **m5** | **81.0** | 53.1 | **+0.200** | **x1.52** | **2/40** | 36% |
| | m3 | 110.0 | 52.1 | +0.194 | x1.89 | 5/40 | 44% |
| mixed + microstructure | m15 | 41.7 | 52.2 | +0.113 | x1.11 | 12/40 | 34% |
| | **m5** | **64.7** | 53.6 | **+0.178** | **x1.32** | 4/40 | 30% |
| | m3 | 78.2 | 54.5 | +0.198 | x1.56 | 4/40 | 34% |
| model M1 | m15 | 54.5 | 59.9 | +0.351 | x1.71 | 2/30 | 28% |
| | **m5** | **89.4** | 54.8 | +0.284 | **x2.03** | 5/30 | 40% |
| model M2 | m15 | 62.9 | 60.2 | +0.372 | x1.80 | 3/30 | 30% |
| | **m5** | **96.0** | 55.8 | +0.342 | **x2.63** | 3/30 | 38% |

**Reported as ABSOLUTE mean R and growth, not edge-over-baseline** — and that
change was forced by a flaw found in the metric itself. Edge is measured
against a random-entry baseline that also pays the spread; because this
strategy holds wider stops than a coin flip, *raising* the spread *raises* the
measured edge. Edge answers "is there skill". It does not answer "does the
account grow". At 80+ trades a week the difference matters.

**ADOPTED: m5.** Roughly double the trades of m15 with higher growth on all
four markets. Per-trade expectancy falls on the two project models (0.35 →
0.28) and win rate drops about 5 points — frequency more than pays for it.

### Two artifact checks it had to survive

**1. Trade geometry.** On a pure random walk no rule can predict anything, so
any measured edge is geometry. Re-run on 40 random walks:

| config | timeframe | measured edge on a random walk |
|---|---|---|
| old trend-only (control) | m15 | +0.039 — reproduces the documented +0.057 |
| this build | m15 | +0.030 |
| this build | m5 | −0.026 |
| this build | m3 | −0.012 |

The control reproduces the old bias, so the measurement is sound — and **the
+0.06 geometry bias has essentially vanished on this build**. It existed
because a trend-only entry fires solely when swings are wide, giving stops
twice a coin flip's. Adding the fade side, which fires in chop where swings are
tight, balanced the stop distribution. **The instruction to subtract ~0.06 no
longer applies to this configuration** — earlier sections of this file that
apply it to the current build understate it.

**2. Self-similarity.** fBm has the same Hurst exponent at every scale by
construction. Real intraday gold does not: at 1-5 minutes it is dominated by
microstructure noise. A synthetic tape without that is unrealistically smooth
exactly where the m5 result claims its advantage. Adding observation noise:

| noise (× one minute's true move) | m15 mean R | m5 | m3 | m3 trades/wk |
|---|---|---|---|---|
| 0 (self-similar fBm) | +0.087 | +0.157 | +0.176 | 108.8 |
| 0.5 | +0.096 | +0.154 | +0.168 | 99.2 |
| 1.0 | +0.090 | +0.147 | +0.154 | 75.8 |
| 2.0 | +0.084 | +0.147 | +0.158 | 34.3 |

The advantage survives. What noise removes is trade *count*, not quality — the
trend-quality filter stands down when the tape is noise-dominated, which is the
correct behaviour and a good sign for the filter.

**3. Spread**, at 4x realistic: mean R +0.157 → +0.135 → +0.092 at m5. Still
clearly positive.

### Two timeframe bugs found while shipping this

1. **`BarMinutes()` returned 60 for any unlisted timeframe** — m2, m3, m4, m10,
   m20, h2, h3. On those charts a "40 bar" hold silently became 40 *hours*.
2. **Hold and entry spacing were expressed in BARS**, so the same setting meant
   10 hours on m15 and 3h20 on m5. The timeframe test was run at a fixed
   wall-clock horizon, so the bar-based version was not what was certified.
   Both are now **wall-clock minutes** and mean the same thing on every chart.

## Verification: a bot that is run, not just compiled

`tools/verify/all.sh` is now the gate before anything is sent:

1. **Compile** with warnings-as-errors, plus a compiler negative control.
2. **`bot-sim.sh`** — the *real* robot against a simulated broker
   (`calgo_sim.cs`: working indicators, positions that fill and stop out, an
   account whose equity moves). 16 behaviour assertions across trending,
   crashing, dead-flat and barely-any-history tapes and six timeframes.
3. **`bot-sim-negcontrol.py`** — injects six real faults and requires the
   simulation to catch each: stop on the wrong side, clamp ignored, sizing 50x,
   demo lock removed, position limit ignored, and both entry gates made
   unreachable (the silent no-trade failure that actually happened live).
4. **Port test** — C# and backtester agree bar for bar.
5. The 33 Python unit tests.

**The negative controls immediately earned their place.** Two faults initially
went undetected, and the cause was a real hole: the simulated tapes were random
walks, so the regime switch correctly chose the fade side and **the entire
trend entry path was never executed**. Half the bot was untested. The driver
now asserts both paths fire (151 trend / 67 fade entries) and the control's
contract is simply "a broken bot must not pass", rather than matching a
specific message that a skipped scenario would never print.

---

## Why the fade side fires so rarely — and why that is not a bug

*25 Aug 2026, prompted by a live log: the regime switch flipped to
MEAN-REVERTING at 06:00 and the fade side sat live for seven hours with no
entry.*

The suspicion was that the fade side is **gated twice by conditions that
anti-correlate**. It is only *allowed* to run when trend quality is low, and it
only *fires* when RSI reaches an extreme — but RSI reaches extremes when price
is moving, which is what chop is not. Measured across 30 mixed tapes:

**How often RSI is at an extreme, by trend quality (m5; m15 is identical):**

| bucket | bars | RSI 35/65 | RSI 40/60 | RSI 45/55 |
|---|---|---|---|---|
| chop, quality ≤ 0.20 | 132,151 | **9.0%** | 27.5% | 59.3% |
| middle, 0.20-0.22 | 8,108 | 36.4% | 64.3% | 85.9% |
| trending, quality ≥ 0.22 | 43,521 | **66.9%** | 84.2% | 94.4% |

**The suspicion was correct: the fade trigger fires 7x less often exactly where
it is the only side permitted to run.** Seven quiet hours is not a malfunction,
it is arithmetic — 28 m15 bars at 9% is ~2.5 expected opportunities, and drawing
zero from that has probability ~7%.

### The obvious fix measured worse

If chop produces displacement rather than momentum, measure displacement:
`z = (price − mean20) / stdev20`, which is large exactly when price has
stretched away from the middle of a range. 40 mixed tapes, m5:

| fade trigger | tr/wk | mean R | growth | worst DD |
|---|---|---|---|---|
| **RSI 35/65 (shipped)** | 78.7 | **+0.157** | x1.36 | **36%** |
| RSI 40/60 | 142.4 | +0.108 | x1.52 | 48% |
| z-score \|z\| ≥ 2.0 | 114.5 | +0.118 | x1.44 | 43% |
| z-score \|z\| ≥ 1.5 | 159.1 | +0.092 | x1.42 | 44% |
| z-score \|z\| ≥ 1.0 | 195.6 | +0.080 | x1.45 | 50% |
| z ≥ 1.5 **and** RSI agrees | 159.1 | +0.092 | x1.43 | 44% |

Every alternative trades far more at materially lower expectancy, and growth per
unit of drawdown *falls*: 0.038 for the shipped trigger against 0.032-0.033 for
the rest. **The RSI trigger is not broken, it is selective** — the rarity is
earning its keep, and the trades it declines are the mediocre ones.

### RSI 40/60 — REJECTED on virgin seeds (9700-9739)

| config | tr/wk | mean R | growth | losing | worst DD |
|---|---|---|---|---|---|
| m15, RSI 35/65 | 42.9 | +0.104 | x1.11 | 12/40 | **37%** |
| m15, RSI 40/60 | 81.6 | +0.055 | x1.11 | 17/40 | **57%** |
| m5, RSI 35/65 | 78.8 | +0.146 | x1.45 | 7/40 | **40%** |
| m5, RSI 40/60 | 140.6 | +0.091 | x1.52 | 7/40 | **52%** |
| M1 m5, RSI 35/65 | 90.2 | +0.299 | **x1.94** | **0/30** | **39%** |
| M1 m5, RSI 40/60 | 129.3 | +0.184 | x1.83 | 3/30 | 54% |
| M2 m5, RSI 35/65 | 96.2 | +0.257 | x1.99 | 4/30 | **45%** |
| M2 m5, RSI 40/60 | 116.8 | +0.205 | x2.07 | 5/30 | 53% |

It doubles the trade count and buys it entirely with drawdown: **+15 to +20
points of worst drawdown for growth that is unchanged on three of four markets
and worse on one.** No parameter changed.

### What the same table says about the timeframe, again

On the same virgin tapes, m15 → m5 is 42.9 → 78.8 trades/week, mean R +0.104 →
+0.146, growth x1.11 → x1.45, losing runs 12/40 → 7/40, with drawdown moving
only 37% → 40%. **The timeframe buys trade frequency almost for free; loosening
the fade trigger does not.** They are not interchangeable ways of getting more
trades, and the difference is the whole reason to measure rather than reason
about it.

---

## Trailing stop — ADOPTED, reversing an earlier rejection

*25 Aug 2026. Prompted by a live report: trades reaching within a few dollars
of target, then running to the stop.*

**The earlier rejection was stale, not wrong.** "Trailing stop after +2R"
measured +0.649 vs +0.633 and was shelved as noise — on a **2:1-6:1 reward
config on m15**, where +2R was rarely reached at all. This build runs 1.0-2.0
on the trend side and 1.0 on the fade side, on m5. At those targets a trade
that reaches 90% of the way and reverses gives back the entire move. The old
conclusion cannot be carried across that change, so it was re-measured.

### 40 mixed tapes, m5, 1% risk — every exit idea, same conditions

"near" counts trades that reached 90% of target and then stopped out.

| variant | tr/wk | win% | mean R | growth | losing | worst DD | near |
|---|---|---|---|---|---|---|---|
| binary SL/TP (was shipped) | 78.7 | 51.6 | +0.157 | x1.36 | 9/40 | 36% | 178 |
| breakeven at +0.3R | 80.9 | 29.4 | +0.155 | x1.35 | 3/40 | 22% | 0 |
| breakeven at +0.5R | 79.8 | 36.8 | +0.166 | x1.44 | 4/40 | 28% | 0 |
| breakeven at +0.7R | 79.2 | 42.3 | +0.170 | x1.47 | 4/40 | 29% | 0 |
| trail 1.0R after +0.5R | 79.3 | 48.5 | +0.167 | x1.40 | 4/40 | 30% | 194 |
| **trail 0.7R after +0.7R** | 79.6 | **62.9** | **+0.179** | x1.44 | 4/40 | **25%** | **0** |
| target pulled in x0.90 | 79.7 | 53.6 | +0.149 | x1.32 | 9/40 | 32% | 0 |
| target pulled in x0.80 | 80.9 | 55.9 | +0.137 | x1.33 | 10/40 | 31% | 0 |
| half off at +0.5R | 133.9 | 71.5 | +0.068 | x1.23 | 7/40 | 26% | 178 |
| half off at +0.7R | 128.9 | 70.4 | +0.081 | x1.30 | 8/40 | 28% | 178 |

The two ideas this file rejected years ago **still fail**: partial closes raise
win rate to 71% and cut mean R by more than half (the classic illusion), and
pulling the target in converts near-misses to hits while shrinking every real
winner. Only the trailing stop improves things.

### Plateau, not a peak — all nine neighbours

| activation | distance 0.5R | 0.7R | 1.0R |
|---|---|---|---|
| +0.5R | +0.173 (68.2% win, 23% DD) | +0.174 (56.3%, 25%) | +0.167 (48.5%, 30%) |
| +0.7R | +0.177 (64.2%, 23%) | **+0.179 (62.9%, 25%)** | +0.170 (51.1%, 30%) |
| +1.0R | +0.171 (56.4%, 31%) | +0.172 (56.4%, 32%) | +0.165 (56.0%, 34%) |

### Virgin certification (tapes 9800-9839) — and the honest cost

| market | variant | win% | mean R | growth | worst DD | near |
|---|---|---|---|---|---|---|
| mixed | binary | 51.3 | +0.1413 | x1.35 | 40% | 155 |
| mixed | **trailing** | **61.2** | +0.1409 | x1.37 | **35%** | **0** |
| M1 | binary | 57.1 | **+0.3642** | **x2.67** | 43% | 114 |
| M1 | trailing | **66.1** | +0.3043 | x2.47 | **36%** | **0** |
| M2 | binary | 53.9 | **+0.3000** | **x2.41** | 29% | 128 |
| M2 | trailing | **64.2** | +0.2634 | x2.29 | 29% | 0 |

**This is a trade, not a free win, and it should not be sold as one.** On a
strongly trending market the trail cuts winners short — mean R falls 0.06 and
growth x2.67 → x2.47 on M1. What it buys:

- **+9 to +10 points of win rate** (51-57% → 61-66%)
- **worst drawdown 40% → 35% and 43% → 36%**
- **the near-miss-then-full-loss case disappears entirely**: 155/114/128 → 0

On the mixed market — the honest one — the money is a wash (+0.1413 vs +0.1409)
and everything else improves. Adopted on that basis: same expectancy, materially
smoother, and it removes the failure mode that was actually being reported.

### A gap the negative controls found in the test harness itself

Injecting "trailing stop moves backwards" was **not caught** at first. The
driver checked that a stop never moves *against* a position, but not that it
stays on the correct **side of the current price** — and the injected sign flip
put a BUY stop above market, which a real broker rejects or fills instantly.
That check now exists, and the fault is caught. Two of the eight controls have
now found real holes in the harness rather than in the bot; the harness is only
as good as its worst assertion.

---

## The backtest was assuming fractional lots. It cannot have them.

*25 Aug 2026. From a live report: "the SL amount is higher than the TP amount."*

The owner was right, and the cause was invisible to every measurement in this
file until now.

### Every result above compounds a clean 1% per trade. That is impossible here.

Gold's minimum trade is **1 oz**. On a $3,000 account at 1% risk the budget is
$30, and the bot wants between 0.47 and 1.63 ounces — so **every trade is
exactly 1 oz**, and the dollar risk is set by the stop width rather than by the
risk setting:

| stop width | $ risk on 1 oz | vs the $30 budget |
|---|---|---|
| 0.4% | $18.40 | 0.61x |
| 0.7% | $32.20 | 1.07x |
| 1.0% | $46.00 | 1.53x |
| 1.3% | $59.80 | 1.99x |

**A 3.5x spread in dollar risk at a single "1%" setting.** The risk parameter is
close to inert at this account size — it only decides whether a trade is
skipped. A win on a tight-stop trade and a loss on a wide-stop trade produce
exactly the reported asymmetry, with every trade still satisfying reward ≥ risk
in points.

### Re-measured in real dollars — whole ounces, spread AND commission

30 virgin tapes (9800-9829), m5, $3,000 start, 22 days:

| config | trades | win% | avg win | avg loss | W/L | median final | losing |
|---|---|---|---|---|---|---|---|
| as shipped | 7,399 | 59.2 | +$25.82 | −$27.59 | **0.94** | $3,865 | 4/30 |

**The average loss really is larger than the average win.** The strategy earns
because it wins 59% of the time, not because its winners are bigger. That is a
legitimate way to make money, but it is not what the R-based tables imply, and
it should have been reported this way from the start.

### What fixed it, and what did not

Second virgin block (9900-9939), all in real dollars:

| market | config | win% | avg win | avg loss | W/L | median final | losing |
|---|---|---|---|---|---|---|---|
| mixed | shipped | 60.8 | +$30.53 | −$29.40 | 1.04 | $4,019 | 3/40 |
| mixed | **reward floor 1.3** | 60.8 | +$32.54 | −$29.99 | **1.09** | $4,154 | 4/40 |
| mixed | stop cap 0.8% | 61.5 | +$28.64 | −$28.63 | 1.00 | $3,964 | 4/40 |
| M1 | shipped | 62.3 | +$28.32 | −$29.43 | 0.96 | $4,563 | 2/30 |
| M1 | **reward floor 1.3** | 62.2 | +$30.54 | −$30.08 | **1.02** | $4,741 | 3/30 |
| M2 | shipped | 62.5 | +$28.70 | −$29.46 | 0.97 | $4,606 | 1/30 |
| M2 | **reward floor 1.3** | 62.2 | +$30.36 | −$29.73 | **1.02** | $4,884 | 1/30 |

**ADOPTED: minimum reward 1.0 → 1.3** (trend side 1.3-2.3, fade side 1.3). It
lifts the win/loss ratio above 1.0 on every market and raises the median
account on five of the six market/block combinations.

**NOT adopted: capping the maximum stop at 0.8%** so one ounce always fits the
budget. Median $4,005 vs $3,865 on the first block, then $3,964 vs $4,019 on
the second — two samples, two orderings — and it never moved the win/loss ratio
at all. Noise.

**Also rejected: tightening the minimum-lot skip guard** from 2.0x to 1.5/1.2/1.0x
so trades whose minimum size exceeds the budget are declined. It monotonically
destroys the account: median $3,865 → $3,444 → $3,411 → $3,174, losing runs
4/30 → 10/30, and it skips up to 43% of signals. Refusing the wide-stop trades
removes more edge than it removes risk.

### An interaction worth stating

Raising the target by 0.3R moved the average win by far less than 0.3R, because
**most winners now exit on the trailing stop rather than at the target**. The
two features adopted this session pull against each other, and the reward floor
is doing less work than its number suggests.

### The honest structural limit

None of this fixes the underlying problem, which is that **$3,000 is too small
for 1-oz gold to be sized properly**. The risk setting cannot express itself
when the minimum trade is 60-200% of the intended risk. A cent account, where
contract sizes are 100x smaller, is the only real fix — the same conclusion the
deployment notes already reached, now confirmed against measured results rather
than argued from arithmetic.

---

## Uptime is worth more than every parameter in this file

*26 Aug 2026. Owner: "it hit sl one trade today, laptop was off whole day."*

Every result in this document assumed the bot runs continuously. It does not —
it runs on a laptop. **The trailing stop, the time stop and the news protection
are all bot-side.** While the machine is off, a position has nothing but the
stop and target it was given at entry. Since the trail now handles ~53% of all
exits and the target only ~8%, running offline is not a degraded version of the
strategy — it is a different and much worse one.

Simulated with an ONLINE WINDOW: the bot manages positions and takes entries
only during those hours; outside it, open trades run unattended. 40 virgin
tapes, $3,000, m5, target 2.5-3.5:

| bot online | win% | hits target | exits on trail | hits stop | median account | losing |
|---|---|---|---|---|---|---|
| **24 hours** | 60.5 | 8.1% | 52.6% | 35.5% | **$4,528** | 1/30 |
| 14 hours | 57.8 | 9.4% | 47.1% | 38.5% | $3,622 | 4/30 |
| 8 hours | 52.3 | 10.3% | 41.7% | 44.2% | $3,224 | 10/30 |
| 4 hours | 49.8 | 13.0% | 34.8% | 47.1% | $3,147 | 11/30 |

**Going from 24-hour to 8-hour uptime costs about $1,300 of a $1,528 gain, and
takes losing runs from 1/30 to 10/30.** No parameter change measured in this
file comes close to that. A VPS is not a nicety, it is the single largest
available improvement.

### Does the best TARGET change when the bot is offline? No.

The obvious hypothesis: if the trail cannot run, a closer target should do the
exiting instead. Tested on 40 virgin tapes at both uptimes:

| target | hits target | median (24h online) | median (8h online) |
|---|---|---|---|
| 1.0-1.5 | 39.7% / 40.3% | $3,788 | $3,146 |
| 1.5-2.0 | 24.4% / 26.1% | $4,141 | $3,151 |
| 2.0-2.5 | 15.0% / 17.7% | $4,273 | $3,216 |
| **2.5-3.5 (shipped)** | 7.9% / 10.8% | **$4,308** | **$3,269** |
| 3.5-4.5 | 3.8% / 5.8% | $4,414 | $3,296 |

**The ordering is identical in both columns.** A tighter target is worse
whether the bot is watching or not — it does not become the right answer when
the trail is unavailable. At 1.0-1.5 the target is hit on 40% of trades, which
is what "a TP that actually hits" looks like, and it costs $500-600 in both
scenarios.

This is the third distinct test of the same question (fixed targets with trail
off, the reward sweep, and now the uptime split) and all three agree: making
the target easier to reach is how the account loses money.

### Shipped in response

Nothing in the strategy. `AuditExistingPositions()` now runs at start-up: any
position found already open is reported with its age and P&L, flagged if it has
outlived the max hold, and accompanied by the measured cost of downtime. The
failure was invisible before — the bot simply started up and said nothing about
the trade that had been sitting unmanaged for hours.

---

## Target set from market structure — ADOPTED at the owner's direction, and it costs money

*26 Aug 2026. Requested four times, most recently: "it just sets it depending on
the sl ... FIX IT SO THAT IT SETS A GOOD TP AND SL BOTH".*

Until now the target was purely `stop distance x reward ratio`. It is now the
price of the **120-bar extreme** — a level price has actually reached — floored
at `MinRewardRisk x stop` so a win still beats a loss, and capped at `8 x stop`.

### Every raw structural anchor lands CLOSER than the stop

30 virgin tapes, $3,000, m5, SL identical in every row:

| target rule | median RR | hits target | median account |
|---|---|---|---|
| pure ratio 2.5-3.5x SL | 3.00 | 8.1% | **$4,528** |
| 1.5-4.0x ATR(14) | 1.00 | 46.7% | $3,618 |
| 30 / 60 / 120-bar swing | 1.00 | 33-36% | $3,677-3,765 |
| S/R level, 2-3 touches | 1.00 | 41-43% | $3,222-3,302 |

The ATR rows are identical at 1.5x through 4.0x because **all of them clamp to
the floor**. The stop is held to a minimum of 0.4% of price, which is wide
relative to m5 structure, so every structural target is nearer than the stop.
Taken raw they cost $760-$1,300 — and they reintroduce exactly the 1:1 the owner
objected to two days earlier.

### Floor sweep — the combination that satisfies both requirements

Target = the further of (120-bar extreme) and (floor x stop). 40 virgin tapes:

| floor | median RR | hits target | median account | losing |
|---|---|---|---|---|
| 1.0x | 1.00 | 32.5% | $3,708 | 6/40 |
| 1.5x | 1.50 | 22.4% | $4,033 | 4/40 |
| 2.0x | 2.00 | 15.7% | $4,262 | 4/40 |
| **2.5x** | **2.50** | **10.5%** | **$4,350** | 3/40 |
| 3.0x | 3.00 | 7.4% | $4,350 | 2/40 |

### Independent confirmation, and the honest verdict

| market | pure ratio | structure, floor 2.5 |
|---|---|---|
| mixed A | $4,349 · hit 7.6% | $4,306 · hit **9.6%** |
| mixed B | $4,308 · hit 7.9% | **$4,350** · hit **10.5%** |
| M1 | **$5,840** · hit 8.4% | $5,506 · hit **12.7%** |
| M2 | **$6,113** · hit 8.3% | $6,006 · hit **13.0%** |

**This is worse on three of the four markets.** Targets are hit about 50% more
often and the win rate is flat to marginally better, at a cost of roughly 4% of
the gain. It is shipped because the owner asked for it with that cost stated,
not because it measured better. `UseStructureTarget = false` restores the pure
ratio exactly.

This is the fifth independent test of "make the target easier to reach" in this
file — fixed targets with the trail off, the reward sweep, the uptime split, the
raw structural anchors, and now the floored version. Every one of them agrees:
a target that is hit more often costs money. The floored version is the least
expensive way to have it, not a way to avoid the cost.

The order log now names the source of every target, so this is checkable live:
`= 2.50:1 from floor` or `= 3.80:1 from structure` or `from capped`.

## A target calibrated to what trades ACTUALLY reach (Aug 27)

Owner: *"TP should be set because of the trade where it thinks it will hit
everytime."* Fair — every earlier target rule derived the TP from the stop or
from a chart level, never from evidence about how far trades of this kind
actually get.

So the bot now measures it. For every closed trade it records the **maximum
favourable excursion** — how far price ran in our favour, in stop-units, before
the trade ended. The next target is placed at the Pth percentile of the last
100. By construction roughly P% of trades reach it, and it re-calibrates itself:
tighter when the market stops running, wider when it starts.

40 virgin mixed-regime tapes, $3,000, m5, floor 1.0x:

| target rule | median RR | win rate | hits target | avg win | avg loss | median account |
|---|---|---|---|---|---|---|
| 40th pct of reach | 1.00 | 61.0% | **41.9%** | $25.33 | $29.71 | $3,639 |
| 50th pct | 1.00 | 61.0% | 41.5% | $25.35 | $29.71 | $3,639 |
| 60th pct | 1.03 | 61.0% | 39.6% | $25.85 | $29.75 | $3,610 |
| 70th pct | 1.18 | 61.0% | 34.5% | $27.63 | $29.88 | $3,664 |
| 80th pct | 1.45 | 60.9% | 27.0% | $30.06 | $30.06 | $3,813 |
| **90th pct (shipped)** | **1.90** | **60.7%** | **18.1%** | **$33.38** | **$30.25** | **$4,132** |
| structure, floor 2.5 (previous) | 3.00 | 60.5% | 7.9% | $36.49 | $30.86 | $4,308 |

**The win-rate column is the finding.** It does not move — 60.5% to 61.0% across
the whole range. Hitting the target five times more often does not win more
often, because the trailing stop was already closing those trades in profit. The
only thing that changes is what each win is worth: $36.49 down to $25.33. Every
extra "target hit" is a trail exit that got cut short.

Shipped at the 90th percentile: target hits go 7.9% → **18.1%**, more than
double, for about $176 — roughly 4% of the gain. `ReachPercentile` is the dial
and the table above is the exchange rate. This is the **sixth** independent test
of "make the target easier to reach" in this file, and it agrees with the other
five.

### Two bugs caught before this shipped

The first implementation floored the reach target at `MinRewardRisk` (2.5).
Since the calibrated target is ~1.9x, that clamped nearly every one straight
back to the old value — **the feature would have logged as though it worked and
changed almost nothing.** Measured:

| reach floor | target used | hits target | median account |
|---|---|---|---|
| 1.0x | 1.90 | 18.1% | $4,132 |
| **1.3x (shipped)** | **1.90** | **18.0%** | **$4,132** |
| 1.5x | 1.90 | 17.6% | $4,132 |
| 2.0x | 2.11 | 14.4% | $4,247 |
| 2.5x (the bug) | 2.50 | **10.2%** | $4,300 |

1.0–1.5 is a flat plateau, so 1.3 is not a fitted value. It is chosen from
within the plateau because it also guarantees the owner's own rule on the
narrowest stop the bot can take: a win must **pay** more than a loss **costs**.
Break-even is `1 + 2*(spread+commission)/stop`; at the 0.4% minimum stop that is
1.10x, so 1.3 clears it with margin on every single order.

The second: MFE tracking and harvesting lived inside `ManageTrailingStops()`,
which returns immediately when the trail is off — and `OnTick` returned before
even calling it. With `UseTrailingStop = false` the target would never have
learned anything and would have sat on its default forever, silently. Reach
tracking is now its own pass that always runs.

Both bugs are now negative controls (`bot-sim-negcontrol.py`), so neither can
come back: reintroducing either makes the behaviour simulation fail.

### Does the target eat its own training data?

A trade that closes AT the target records an MFE of exactly the target ratio —
price might have run further, but the trade was gone. So the training set is
censored at whatever the target currently is, which could ratchet downward
forever. Measured over 20 tapes × 90 days:

| | Q1 | Q2 | Q3 | Q4 |
|---|---|---|---|---|
| censored (shipped) | 1.97 | 1.83 | 1.80 | 1.83 |
| uncensored (target hits teach nothing) | 1.34 | 1.30 | 1.30 | 1.30 |

Real but self-limiting: −0.14R, and flat after Q2 — only ~18% of trades close at
the target, so 82% of the training data is uncoupled. The obvious "fix" of
ignoring target hits is **worse**, settling at 1.30R, because it throws away the
biggest runs. Shipped censored, as measured.

## Making the target realistic, at the owner's direction (Aug 28)

Owner: *"the bot still sets unrealistic tps fix the TP problem idc if its less
than sl just should be realistic."* This **reverses** the rule they set on Aug
27 ("i want when it hits tp it makes more than when it hits sl"), so the 1.3x
floor that guaranteed it comes off. Recorded as a deliberate reversal.

### The real bug: the warm-up target

The reach rule needs 30 closed trades before it has an opinion. Until then the
bot fell back to the **structural target, floored at 2.5x the stop** — the very
target the reach rule exists to replace, hit 7.9% of the time. So the first 30
trades of every run got exactly the unrealistic TP that was complained about,
and on a fresh instance that is all the owner ever sees. 40 virgin tapes, p60:

| warm-up fallback | hits target | median account |
|---|---|---|
| 2.5x structural (the bug) | 39.6% | $3,610 |
| 2.0x | 40.5% | $3,629 |
| **1.5x (shipped)** | **41.8%** | **$3,649** |
| 1.2x | 43.6% | $3,630 |
| 1.0x | 45.7% | $3,560 |

1.5x is best on money and near the top on hit rate, so this one is not a
trade-off — it is strictly better than what was there. Orders now log
`from warm-up`, and the bot never falls back to the structural target while the
reach rule is on.

### The percentile, with the floor removed

| target rule | median RR | win | hits target | avg win | avg loss | median | losing |
|---|---|---|---|---|---|---|---|
| p90, floor 1.3 (previous) | 1.90 | 60.7% | 18.0% | $33.41 | $30.27 | **$4,132** | **3/40** |
| p80, floor 1.0 | 1.45 | 60.9% | 27.0% | $30.92 | $29.59 | $3,813 | 4/40 |
| p70, floor 1.0 | 1.18 | 61.0% | 34.5% | $28.38 | $29.12 | $3,664 | 6/40 |
| **p60, floor 1.0 (shipped)** | **1.03** | **61.0%** | **41.8%** | **$25.38** | **$27.96** | **$3,649** | 8/40 |
| p50, floor 1.0 | 1.00 | 61.0% | 41.5% | $25.58 | $28.14 | $3,639 | 6/40 |
| p30, floor 1.0 | 1.00 | 61.0% | 42.0% | $25.29 | $28.03 | $3,639 | 6/40 |

Hit rate plateaus around p50-p60 — below that the 1.0x floor binds and buying
more hits is no longer possible. p60 is the top of that plateau.

**The cost, stated plainly:** target hits go 18.0% -> 41.8%, and the median
account goes $4,132 -> $3,649, about 12%. Losing runs go 3/40 -> 8/40, which is
the larger risk: the win rate is unchanged (60.7% -> 61.0%) but each win is
smaller ($33.41 -> $25.38) while each loss is barely smaller ($30.27 ->
$27.96), so there is less cushion. This is the **seventh** independent
measurement in this file of "make the target easier to reach", and it agrees
with the other six. Shipped because the owner asked for it with the cost
stated. `ReachPercentile` back to 90 restores the previous behaviour exactly.

### Three take-profits, measured and not adopted

Owner asked for scaling out at 3 targets. Re-measured on the current build
rather than trusting the old rejection (which predates the trailing stop and
the reach target). Identical total risk per signal:

| exit shape | win | hits target | median | mean |
|---|---|---|---|---|
| 1 TP (shipped) | 60.7% | 18.0% | **$4,132** | **$5,050** |
| 3 TPs 0.5/1.0/1.5x | 50.0% | 20.5% | $4,089 | $4,771 |
| 3 TPs 0.4/0.8/1.2x | 56.0% | 27.8% | $3,937 | $4,557 |
| 3 TPs 0.6/1.0/1.4x | 45.7% | 18.3% | $4,010 | $4,911 |

Median is close to a tie; mean and win rate are clearly worse, because the far
third is usually stopped after the near thirds have banked. A first run showed
a much larger gap and was wrong: the 6-position cap counted *parts*, so a
3-part ladder got 2 signals where the single TP got 6. Fixed, and a
1.0/1.0/1.0 control now reproduces the single-TP result exactly before any
ladder is scored. Not adopted — and it needs 0.03 lots per signal, roughly a
$5,500+ account, to be placeable at all.
