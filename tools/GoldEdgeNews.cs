// GoldEdgeNews — gold (XAU/USD) strategy + news agent. DEMO-ONLY, XAUUSD m5.
//
// ============================ HOW IT TRADES ============================
// REGIME It measures which market it is in before choosing a side. Rolling
//        median of trend quality over 300 bars vs the RANDOM-WALK FLOOR (the
//        value pure chance produces on that same measure: 0.131 for the
//        5-window ensemble, 0.124 for a lone 48-bar window).
//          above the floor -> trending      -> only the TREND side runs
//          below the floor -> mean-reverting -> only the FADE side runs
//        Running both at once means one of them is always the wrong bet.
// TREND  price must BREAK STRUCTURE (close takes out every close in the
//        prior 12 bars) AND 3 voters (ema20>ema75, rsi14>50, close>close[-20])
//        must ALL agree,
//        only when trend quality >= 0.22 and ADX >= 18, during 01-22 UTC.
//        Trend quality = Kaufman efficiency (net move / path travelled),
//        AVERAGED over 24/36/48/60/72 bars rather than read from one window.
// FADE   on a mean-reverting tape: buy RSI <= 35 / sell RSI >= 65 while trend
//        quality <= 0.20, at 1:1. Same stop and sizing machinery as the trend
//        side, so the two cannot be compared unfairly on exit geometry.
// STOP   just beyond the recent 12-bar swing low/high (+5% buffer), clamped
//        0.4-1.4% of price. Binary — no trailing, no early exit.
// TARGET adaptive 1.0-2.0 reward:risk, scaled by conviction.
// SIZE   1.0% risk per trade, up to 6 at once = 6% maximum exposure.
//        -15% daily loss stop. 600-MINUTE time stop (wall clock, so it means
//        the same thing on every chart), 15 minutes between same-side entries.
// NEWS   Forex Factory calendar, 9 currencies + oil. Moves the stop to
//        breakeven before an event when ALREADY in profit, and vetoes entries
//        during price shocks. It NEVER closes a trade: news pumps gold as
//        often as it dumps it, and closing on news measured clearly worse.
//
// ========================= CERTIFIED PERFORMANCE ========================
// 40 VIRGIN tapes per market (seeds 9600-9639), 22 days, 1.0% risk, spread
// charged relative to the stop. SIMULATED. Reported as ABSOLUTE expectancy
// per trade and the resulting account growth, because that is what compounds
// -- edge-over-a-random-baseline answers "is there skill", not "does it pay".
//
//   market                    tr/wk   win     mean R   growth  losing  worstDD
//   mixed regime               81.0  53.1%   +0.200    x1.52    2/40     36%
//   mixed + microstructure     64.7  53.6%   +0.178    x1.32    4/40     30%
//   model M1                   89.4  54.8%   +0.284    x2.03    5/30     40%
//   model M2                   96.0  55.8%   +0.342    x2.63    3/30     38%
//
// "Mixed regime" is the honest market: trend character changes every half-day
// to two days, which is what the owner's own live logs show. The homogeneous
// tapes that earlier versions of this file quoted held one Hurst exponent for
// 22 days and flattered every result. "+ microstructure" adds observation
// noise of one minute's true move, which is what makes short timeframes hard
// in reality.
//
// SAME BUILD AT m15, for comparison: 45.6 tr/wk, +0.121 mean R, x1.17 growth,
// 12/40 losing. m5 roughly doubles the trades AND improves growth on all four
// markets. That reverses this file's old timeframe table, which was measured
// on a much stricter config (eff>=0.55, no fade side, no structure break) --
// timeframe and entry rules interact, so an old timeframe conclusion cannot
// be carried across a change to either.
//
// THE GEOMETRY BIAS IS NOW ~ZERO, and that is a correction. Earlier builds
// measured +0.057 on a PURE RANDOM WALK, where no rule can predict anything,
// because the trend-only entry fired solely when swings were wide and so held
// stops twice as wide as a coin flip. Re-measured on this build against 40
// random walks:
//        old trend-only config, m15   +0.039 (reproduces the documented bias)
//        this build, m15              +0.030
//        this build, m5               -0.026
//        this build, m3               -0.012
// Adding the fade side (which fires in chop, where swings are tight) balanced
// the stop distribution that caused it. Figures above are NOT reduced by the
// old 0.06 -- that subtraction no longer applies to this configuration.
//
// STRESS-TESTED, on the mixed tapes:
//   spread 0.50 -> 1.00 -> 2.00 (4x realistic):  mean R +0.157 -> +0.135 -> +0.092
//   microstructure noise 0 -> 1x -> 2x a minute: mean R +0.176 -> +0.154 -> +0.158
//   Under heavy noise it trades LESS (109 -> 34/wk) rather than worse: the
//   trend-quality filter stands down when the tape is noise-dominated.
//
// Quoted per market and as measured, never as one headline number. A single
// Scored as EDGE OVER RANDOM because a coin flip earns +0.11 to +0.28R on
// these simulators — raw returns from any trend strategy are inflated here.
// Spread is charged relative to the stop (spread/stop_distance), never as a
// flat per-trade fee, which would secretly reward tight stops.
//
// ===================== CORRECTIONS TO EARLIER BUILDS ====================
// Earlier versions of this file quoted win rates around 71-76%. Those were
// overstated, for two reasons, both now fixed:
//
//   1. THE BACKTESTER RESOLVED EXITS ON BAR CLOSES ONLY. A stop touched
//      mid-bar that recovered before the close was scored as never hit. A real
//      resting stop fills on touch. Worth -1.8 points of win rate, and the
//      error ran in the direction that flattered the strategy. Exits are now
//      resolved every minute.
//   1b. A BASELINE BUG in the rebuilt harness: the coin-flip trade rate was
//      computed per MINUTE but the coin is evaluated per BAR, so the baseline
//      took ~1/15th the intended trades. Not biased in mean, but noisy enough
//      to move measured edge by up to ~0.13. Fixed; edges above are post-fix.
//   1c. A RESIDUAL BIAS THAT DOES NOT GO AWAY, worth about +0.06. On a PURE
//      RANDOM WALK -- where no entry rule can predict anything -- this
//      strategy still measures +0.057 edge. Cause, measured directly: it only
//      fires when the recent swing range is wide, so its stops run ~2x wider
//      than a coin flip's (1.006% vs 0.547% of price), the 40-bar time stop
//      then ends 51% of its trades versus 10%, and a time-stop exit marks out
//      near 0R instead of a full -1R. That is favourable trade GEOMETRY, not
//      prediction. SUBTRACT ~0.06 FROM EVERY EDGE FIGURE IN THIS FILE.
//   2. SEED LUCK. A headline figure came from one 50-path sample; another
//      sample gives ~3 points less under identical rules. Win rate carries
//      +/-2-3 points of sampling noise and should never be quoted as exact.
//
// All A/B comparisons survived the correction (the bias was uniform across
// configurations) — but the absolute numbers were wrong and are restated here.
//
// ========================== WHY THESE SETTINGS =========================
// Measured on the corrected harness, virgin seeds, all else held equal.
//
// TREND QUALITY IS THE WHOLE FILTER. ADX, the vote count and the session
// window barely change anything — loosening ADX from 18 to 0 moved trade count
// 28.2 -> 28.9/wk and edge +0.534 -> +0.525. The efficiency ratio subsumes
// them. It is also what keeps the bot out of chop: ADX read 29-47 ("strong")
// through a whole flat afternoon where efficiency correctly read 0.22-0.27.
//
// THRESHOLD CALIBRATED TO REAL GOLD, NOT TO THE SIMULATORS. This is the
// correction that mattered most, and it came from the owner's own cTrader log
// rather than from any amount of re-testing here.
//
// Ten live readings, 21-23 Aug 2026: 0.23 0.25 0.22 0.27 0.30 0.26
// 0.21 0.12 0.12 0.09 — median 0.23, max 0.30.
//
//   Ensemble floor is 0.131, so that tape sat +0.10 above chance, an implied
//   Hurst of ~0.65. It was MORE trending than either simulator (median 0.165
//   and 0.185). The strategy premise was never the problem.
//
//   BUT THE NEXT SESSION READ THE OTHER WAY. 24 Aug: 0.19 0.14 0.12 0.08 0.13
//   0.07 0.07 0.07 0.07 0.10 0.10 0.11 — median 0.100, which is BELOW the
//   0.131 floor: implied Hurst ~0.42, genuinely mean-reverting. Three days
//   apart, opposite regimes. That is the entire argument for measuring the
//   regime each bar instead of choosing a side once and hoping.
//
//   THE THRESHOLD WAS. Share of those live bars that pass each gate:
//        gate 0.50   0 of 10     <- what was shipped for weeks. Never fires.
//        gate 0.35   0 of 10
//        gate 0.25   4 of 10
//        gate 0.22   5 of 10     <- default now
//        gate 0.20   7 of 10
//   A gate set at 0.50 against a market whose median is 0.23 is not selective,
//   it is switched off. Certified at 0.22 on virgin seeds: 72 trades/week,
//   win 59.3%, edge +0.346, worst DD 41% at 1% risk with 6 positions.
//
// IDLE DAYS ARE THE REAL COMPLAINT, and trades-per-week hides them. A config
// averaging 28/week can still stand aside three days running and then fire 20
// times in one trend. Share of days with ZERO trades:
//        eff >= 0.50   76% idle      eff >= 0.35   36% idle
//        eff >= 0.45   66% idle      eff >= 0.30   23% idle
//        eff >= 0.40   51% idle      eff >= 0.25   11% idle
//   Context: trend quality has a MEDIAN of 0.17 and a 95th percentile of 0.47,
//   so a 0.50 gate fires in roughly the top 3% of bars. A live session logging
//   0.22-0.27 is an ORDINARY tape near the 70th percentile, not unusual chop.
//   0.35 was chosen as the point where the bot trades about two days in three
//   while edge stays well clear of the ~+0.3R slippage floor.
//
// THE THRESHOLD IS THE FREQUENCY DIAL, and looser is not automatically worse:
//        eff >= 0.50   11.4 tr/wk   edge +0.648
//        eff >= 0.45   18.6 tr/wk   edge +0.589
//        eff >= 0.40   28.2 tr/wk   edge +0.534
//        eff >= 0.30   56.2 tr/wk   edge +0.429
//   Edge decays gracefully while trade count multiplies. Below about +0.3R the
//   cushion stops reliably covering real-world slippage — that is the floor,
//   not the win rate.
//
// MORE TRADES AT A SMALLER SIZE BEATS FEWER AT A LARGER ONE. Buying frequency
// with the threshold and paying for it with position size, virgin seeds:
//        eff.50 gap2 10pos @1.50%   11 tr/wk  76% idle  DD 31%  growth x1.24
//        eff.45 gap1 14pos @1.00%   29 tr/wk  66% idle  DD 31%  growth x1.50
//        eff.35 gap1 10pos @1.00%   52 tr/wk  34% idle  DD 35%  growth x1.81
//   Roughly 5x the trades and half the idle days for ~4 points of drawdown.
//   Total exposure also FALLS to 10 x 1.0% = 10%, from 10 x 1.5% = 15%.
//   Win rate drops to ~65% — that is the honest price of a looser gate, and it
//   is paid back in frequency and in fewer losing runs (6-8/100 vs 14-18/100).
//   TO TRADE LESS AND MORE SELECTIVELY: raise the threshold back toward 0.45.
//   TO CUT DRAWDOWN: lower max concurrent positions; edge is flat across
//   6-10, so it costs only trades. SHIPPED AT 6 for that reason — the table
//   above was measured at 10, which is why its exposure line says 10%.
//
// ENSEMBLE TREND QUALITY, not a single window. A sweep picked 48 bars, but 48
//   turned out to be a PEAK rather than a plateau — its neighbours all scored
//   clearly worse, the signature of a partly-lucky parameter. Averaging the
//   whole family is better on win rate, edge AND drawdown:
//        single window 48    win 70.0%, edge +0.620, worst DD 30%
//        mean of 5 windows   win 73.0%, edge +0.656, worst DD 21%
//   The window set is deliberately fixed, not five new tunable knobs.
//
// ENTRIES DECIDE ON BAR CLOSE, and this is deliberate. Evaluating mid-bar
//   costs 0.09-0.14R per trade to signal flicker — a setup with all three
//   votes at minute 7 need not still have them at minute 15. It does trade
//   more and grow faster, but risk-matched it is identical to simply turning
//   the risk dial up, so it buys nothing and pays in per-trade edge.
//
// NO EARLY EXIT. Every "close when the market changes" rule was tested and
//   none helped. An ADX-fall exit was the worst idea measured: it fired 1,144
//   times and dumped winners. The time stop already ends trades before a real
//   reversal arrives.
//
// ALSO REJECTED after measurement: liquidity-sweep/stop-hunt reversals (lose
//   money alone AND dilute this strategy), the 12-16 UTC overlap as an
//   exclusive filter (cut trades six-fold), partial take-profit ladders (raise
//   win rate, cut money), trailing stops, EMA200 alignment, and closing
//   positions on news. See docs/PERFORMANCE.md for the full list — checking it
//   first saves re-discovering the same losers.
//
// MEAN REVERSION WAS ON THAT LIST AND CAME OFF IT. It was rejected twice, and
//   both rejections were sound *on the markets they were run on* — M1 and M2
//   trend, so fading them loses. It had never been tested on a mean-reverting
//   market because the project owned no mean-reverting market to test it on.
//   Built one (fractional Brownian motion at known Hurst exponent) and the
//   sign flips: -0.191 at H=0.60, +0.259 at H=0.40. The lesson is about
//   method, not about mean reversion: a strategy tested only where it is
//   expected to fail will duly fail, and that proves nothing.
//
// ============================== CAVEATS ================================
// Every number above is SIMULATED on two synthetic gold models. They contain
// no gaps, no spread widening and no spikes, so they cannot show the tail risk
// that matters most: several correlated positions gapping through their stops
// together. Live demo fills are the only evidence that settles this.
//
// ACCOUNT SIZE: gold's minimum trade is 1 oz, which risks roughly $18-64 per
// trade at a 0.4-1.4% stop. On a $3,000 account, 1.0% risk is a $30 budget, so
// the widest-stop setups will be skipped by the too-small guard. Below about
// $2,500 this configuration cannot size properly at all.
//
// Needs AccessRights.FullAccess (network) for the news calendar.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class GoldEdgeNews : Robot
    {
        // SIMPLIFIED VOTER SET. Leave-one-out testing showed the six voters
        // are almost entirely REDUNDANT — they all answer "is price going up?"
        // through different lenses. Dropping any one changed edge by <0.001,
        // and a single momentum check scored the same as all six:
        //   6 voters   win 67.0%, edge +0.502, 11.0 tr/wk
        //   3 voters   win 67.2%, edge +0.505, 10.5 tr/wk   <- default
        //   1 voter    win 66.9%, edge +0.500, 11.1 tr/wk
        // The edge lives in the trend-quality FILTER and the EXITS, not in
        // stacking indicators. Three is chosen over six for robustness — fewer
        // fitted parts, less to break — not because it earns more.
        [Parameter("Use simplified 3-voter set", DefaultValue = true, Group = "Signal")]
        public bool UseSimpleVoters { get; set; }

        [Parameter("Votes needed (of 6, when simplified is OFF)", DefaultValue = 5, MinValue = 3, MaxValue = 6, Group = "Signal")]
        public int VotesNeeded { get; set; }

        [Parameter("Minimum ADX", DefaultValue = 18.0, MinValue = 0, MaxValue = 50, Group = "Signal")]
        public double AdxMin { get; set; }

        [Parameter("Require ADX rising", DefaultValue = false, Group = "Signal")]
        public bool RequireAdxRising { get; set; }

        // BREAK OF STRUCTURE. The one idea from standard trader education that
        // survived measurement here: don't take the trade until price has
        // actually taken out the prior swing. "Wait for BOS before looking for
        // entries" — the votes can all agree while price is still inside the
        // previous range, and those are the entries that were being paid for.
        //
        // Certified on VIRGIN seeds, added on top of the 3-voter rule:
        //   market            baseline            + BOS
        //   mixed regime      49.6/wk +0.092      43.3/wk +0.121   losing 26->23/60
        //   model M1          65.3/wk +0.287      54.7/wk +0.311   worst DD 42%->28%
        //   model M2          77.4/wk +0.334      63.5/wk +0.345   worst DD 42%->41%
        // Better on all three, and the same direction on the tuning set
        // (+0.070 -> +0.088). Five samples, one sign — which is the bar this
        // project uses, because any single one of these is only 1-2 SE.
        //
        // It costs about 13% of the trade count. That is the honest price.
        //
        // NOT adopted alongside it: filtering the FADE side to price being at
        // a support/resistance level with 3+ touches. It measured slightly
        // better again (mixed +0.126, M1 +0.345) but cost a further 15% of
        // trades for a gain inside one standard error — not worth the
        // frequency when frequency is already the complaint.
        [Parameter("Require break of structure (prior swing taken out)", DefaultValue = true, Group = "Signal")]
        public bool RequireBreakOfStructure { get; set; }

        [Parameter("Break of structure: lookback bars", DefaultValue = 12, MinValue = 2, MaxValue = 100, Group = "Signal")]
        public int BosLookback { get; set; }

        [Parameter("ADX rising lookback (bars)", DefaultValue = 3, MinValue = 1, MaxValue = 20, Group = "Signal")]
        public int AdxRisingLookback { get; set; }

        // Only used when the ensemble below is switched OFF. A longer window
        // judges trend quality over a fuller stretch; 12 bars is too short to
        // tell a real trend from a wiggle. Prefer the ensemble — picking one
        // window is exactly the mistake it exists to avoid.
        [Parameter("Trend quality window (bars)", DefaultValue = 48, MinValue = 4, MaxValue = 200, Group = "Trend filter")]
        public int EfficiencyWindow { get; set; }

        [Parameter("Min trend quality (0-1)", DefaultValue = 0.22, MinValue = 0.0, MaxValue = 1.0, Group = "Trend filter")]
        public double EfficiencyMin { get; set; }

        // Carver's overfitting rule, applied to our own tuning. A sweep picked
        // 48 because it beat 12/24/36 — but 48 turned out to be a PEAK, not a
        // plateau: its neighbours all score clearly worse (edge +0.600 at 36,
        // +0.567 at 60, +0.528 at 72, vs +0.631 at 48). A parameter whose
        // neighbours are worse is a parameter that was partly lucky.
        //
        // The fix is not a better single window, it is to stop picking one:
        // average trend quality across the whole family. Certified on 50 virgin
        // seeds (3100-3149):
        // Re-certified on the corrected harness (exits resolved every minute,
        // virgin seeds 3300-3349):
        //   single window 48   win 70.0%, edge +0.620, worst-model +0.489, worstDD 30%
        //   mean of 5 windows  win 73.0%, edge +0.656, worst-model +0.494, worstDD 21%
        // Better on win rate, edge AND drawdown. The decision survived the
        // harness correction unchanged.
        [Parameter("Ensemble trend quality (avg of 5 windows)", DefaultValue = true, Group = "Trend filter")]
        public bool UseEnsembleQuality { get; set; }

        // Deliberately a fixed spread around the old value rather than five more
        // tunable knobs — replacing one fitted number with five would defeat the
        // point of the exercise.
        private static readonly int[] EnsembleWindows = { 24, 36, 48, 60, 72 };

        // ---- MEAN-REVERSION SIDE (off by default) --------------------------
        // The trend filter only fires when the tape is trending, so on a
        // mean-reverting market this bot stands aside all day and, worse, its
        // few trades lose: edge -0.265 at H=0.40. Fading an RSI extreme when
        // trend quality is LOW is the opposite bet, and it is the right one
        // there. Measured across markets of known Hurst exponent:
        //        H     market          trend-only   fade-only   both
        //       0.40   mean-reverting    -0.265      +0.298    +0.098
        //       0.45   mean-reverting    -0.104      +0.160    +0.016
        //       0.50   random walk       +0.052      +0.021    +0.038
        //       0.55   trending          +0.215      -0.170    +0.129
        //       0.60   trending          +0.446      -0.208    +0.317
        // (subtract the ~0.06 geometry floor from all of these)
        //
        // NOW ON BY DEFAULT, and the reason is live evidence rather than
        // simulation. The owner's session of 24 Aug 2026 logged ensemble
        // readings of 0.19 0.14 0.12 0.08 0.13 0.07 0.07 0.07 0.07 0.10 0.10
        // 0.11 — median 0.100.
        //
        // The random-walk floor for the ENSEMBLE is 0.131, not the 0.124 that
        // applies to a single 48-bar window: efficiency scales as ~1/sqrt(n) on
        // chance alone, so the shorter windows in the average read higher and
        // lift the floor. A median of 0.100 is therefore BELOW chance — that
        // tape was genuinely mean-reverting, not weakly trending.
        //
        // On such a tape the trend side does not merely go quiet, it loses
        // money (-0.265 measured at H=0.40) while fading earns +0.298. Two
        // consecutive live sessions produced no trend signal at all. Running
        // only the trend half means a dead bot on every day like that.
        //
        // The cost, measured on the trending simulators (virgin seeds
        // 4200-4239, eff>=0.22, 6 positions, 1% risk):
        //   trend only    71.2 tr/wk, edge +0.397, worst DD 40%
        //   trend + fade  77.9 tr/wk, edge +0.351, worst DD 49%
        // About 0.05 of edge and 9 points of drawdown, in exchange for a bot
        // that has something to do when gold is not trending.
        //
        // Was OFF BY PURPOSE before. Running both halves is robust to being
        // wrong about the regime, but the loser drags on the winner, so it is
        // insurance rather than an improvement. Turn it on only once the DAY
        // SUMMARY has shown, over several sessions, that gold's implied Hurst
        // is running at or below 0.50 — at which point trend-following is the
        // wrong strategy and this is the right one.
        // These stay STRICT on purpose, and that decision was nearly got wrong —
        // the sweep below argued convincingly for the opposite.
        // Sweeping them on mean-reverting markets picked chop<=0.30, RSI 35/65,
        // rr 1.5: same edge in the right regime (+0.263 vs +0.272 at H=0.40)
        // with 16x the trades, which looked like a clear win. It was not — the
        // sweep only scored the regime the rule is MEANT for. Checked against
        // the WRONG regime (M1/M2, trending), enabling it costs:
        //        settings                trades/wk   edge    worst DD   losing
        //   trend only (default)             50.6   +0.519      41%      9/80
        //   + fade, strict (chosen)          58.0   +0.430      44%     11/80
        //   + fade, "tuned"                 137.5   +0.100      74%     26/80
        // The looser settings triple the losing runs and take drawdown to 74%
        // if gold happens to trend. Since the regime is exactly what is NOT
        // known, the strict settings are correct: they are nearly as good when
        // right and far cheaper when wrong.
        [Parameter("Also fade RSI extremes when the tape is CHOPPY", DefaultValue = true, Group = "Mean reversion")]
        public bool UseMeanReversion { get; set; }

        [Parameter("Fade only when trend quality is BELOW", DefaultValue = 0.20, MinValue = 0.0, MaxValue = 1.0, Group = "Mean reversion")]
        public double ChopMax { get; set; }

        // WIDENED from 30/70 to 35/65, and this reverses the note above — but
        // only because the regime switch below now exists. Re-read that note:
        // the loose band was rejected because it "triples the losing runs if
        // gold happens to trend". The switch removes exactly that exposure by
        // refusing to fade a trending tape at all, so the band can be judged
        // on the tape it actually runs on. At 30/70 the fade side fired 4.1
        // times a week — too rare to cover a choppy day, which is what left
        // the bot idle.
        [Parameter("Fade: RSI oversold (buy below)", DefaultValue = 35.0, MinValue = 5, MaxValue = 50, Group = "Mean reversion")]
        public double FadeRsiLow { get; set; }

        [Parameter("Fade: RSI overbought (sell above)", DefaultValue = 65.0, MinValue = 50, MaxValue = 95, Group = "Mean reversion")]
        public double FadeRsiHigh { get; set; }

        [Parameter("Fade: reward:risk", DefaultValue = 1.3, MinValue = 0.5, MaxValue = 5.0, Group = "Mean reversion")]
        public double FadeRewardRisk { get; set; }

        // ---- REGIME SWITCH -------------------------------------------------
        // Running both halves at once means one of them is always wrong, and
        // the wrong one bleeds. The bot already computes trend quality every
        // bar, so it can measure which regime it is in instead of guessing:
        // keep a rolling median of trend quality and compare it to the
        // RANDOM-WALK FLOOR — the value pure chance produces on this same
        // measure (0.124 for a single 48-bar window, 0.131 for the ensemble,
        // because efficiency scales ~1/sqrt(n) and the shorter windows in the
        // average read higher).
        //
        //   median >= floor + margin  ->  the tape trends more than chance
        //                                 -> TREND side only
        //   median <  floor + margin  ->  the tape mean-reverts
        //                                 -> FADE side only
        //
        // Certified on 30 VIRGIN seeds per market (22 days, 1% risk), against
        // the previous shipped setting (both sides always on, fade 30/70):
        //
        //   market            config          tr/wk    win     edge   losing runs
        //   H=0.40 revert     both on          20.3   37.4%  -0.188      25/30
        //                     switch+35/65     27.5   62.7%  +0.259       4/30
        //   H=0.45 revert     both on          35.3   42.7%  -0.073      22/30
        //                     switch+35/65     37.5   53.2%  +0.079      10/30
        //   H=0.50 random     both on          52.1   47.8%  +0.026      16/30
        //                     switch+35/65     50.5   49.3%  +0.016      16/30
        //   H=0.55 trending   both on          68.1   52.7%  +0.171       7/30
        //                     switch+35/65     62.1   51.9%  +0.125      11/30
        //   H=0.60 trending   both on          84.2   55.6%  +0.286       1/30
        //                     switch+35/65     75.1   56.4%  +0.310       2/30
        //   M1+M2 (models)    both on          79.6   61.4%  +0.409       1/60
        //                     switch+35/65     71.1   60.6%  +0.372       3/60
        //   (subtract the ~0.06 trade-geometry floor from every edge)
        //
        // Read it honestly: the switch COSTS about 0.04 of edge where the old
        // setting was already right (trending), and it is the only tested
        // configuration that is positive in every regime. On the tape the
        // owner actually logged — implied H ~0.42 — it turns -0.188 into
        // +0.259 and cuts losing runs from 25/30 to 4/30. That trade is worth
        // making because the regime is precisely what is not known in advance.
        //
        // Worst drawdown did not deteriorate (32-48% vs 32-46%), which is the
        // check that killed the last attempt at a wider fade band.
        [Parameter("Regime switch: run only the side that fits the tape", DefaultValue = true, Group = "Regime switch")]
        public bool UseRegimeSwitch { get; set; }

        // Both knobs sit on a plateau, not a peak — every one of nine
        // combinations (window 100/200/400 x margin 0.000/0.005/0.015) was
        // positive on BOTH regimes on a second virgin block: choppy +0.110 to
        // +0.157, trending +0.290 to +0.356. 300 and 0.005 are the middle of
        // that region rather than the sweep winner, per the same
        // averaging-over-variations rule that produced the ensemble.
        [Parameter("Regime: bars of history in the median", DefaultValue = 300, MinValue = 60, MaxValue = 2000, Group = "Regime switch")]
        public int RegimeWindow { get; set; }

        [Parameter("Regime: margin above the random-walk floor", DefaultValue = 0.005, MinValue = 0.0, MaxValue = 0.05, Group = "Regime switch")]
        public double RegimeMargin { get; set; }

        [Parameter("News: use economic calendar", DefaultValue = true, Group = "News agent")]
        public bool UseCalendar { get; set; }

        // PROTECTION (default) — guards an OPEN trade through the event without
        // touching entries, so trade frequency is unchanged. Measured neutral
        // in sim; kept as insurance against slippage and gaps the sim cannot
        // model. It never closes a trade — news moves in your favour just as
        // often (closing on news measured edge +0.633 -> +0.496).
        [Parameter("News: protect open trade (stop -> breakeven)", DefaultValue = true, Group = "News agent")]
        public bool ProtectOnNews { get; set; }

        [Parameter("News: start protecting N min before event", DefaultValue = 30, MinValue = 1, MaxValue = 600, Group = "News agent")]
        public int ProtectBeforeMinutes { get; set; }

        // BLOCKING (off by default) — blocking entries around news costs trades.
        // Turn on only if you want fewer, safer entries.
        [Parameter("News: also BLOCK new entries near events", DefaultValue = false, Group = "News agent")]
        public bool BlockEntriesOnNews { get; set; }

        [Parameter("News: TIER1 block +/- min (FOMC, NFP, CPI, Powell)", DefaultValue = 30, MinValue = 0, MaxValue = 600, Group = "News agent")]
        public int Tier1Minutes { get; set; }

        [Parameter("News: TIER2 block +/- min (other high impact)", DefaultValue = 15, MinValue = 0, MaxValue = 600, Group = "News agent")]
        public int Tier2Minutes { get; set; }

        [Parameter("News: TIER3 block +/- min (speeches, medium)", DefaultValue = 10, MinValue = 0, MaxValue = 600, Group = "News agent")]
        public int Tier3Minutes { get; set; }

        [Parameter("News: also watch low-impact speakers", DefaultValue = true, Group = "News agent")]
        public bool WatchSpeakers { get; set; }

        [Parameter("News: calendar URL", DefaultValue = "https://nfs.faireconomy.media/ff_calendar_thisweek.json", Group = "News agent")]
        public string CalendarUrl { get; set; }

        // Every currency on the feed that has a real channel into gold:
        //   USD  gold is priced in it — direct and dominant
        //   EUR/GBP/JPY/CHF  their central banks move the dollar, and CHF is
        //        gold's twin safe haven (Switzerland refines most of the world's)
        //   AUD/CAD/NZD  commodity and risk proxies; Australia is a top gold producer
        //   CNY  largest consumer nation and a major central-bank buyer
        [Parameter("News: currencies to watch (comma)", DefaultValue = "USD,EUR,GBP,JPY,CHF,AUD,CAD,NZD,CNY", Group = "News agent")]
        public string WatchCurrencies { get; set; }

        // Which tiers actually trigger stop-to-breakeven protection.
        // 1 = only gold-critical US events (FOMC/NFP/CPI/PCE/Powell)
        // 2 = also every other high-impact print (default)
        // 3 = also speakers and medium data (most cautious, most scratches)
        [Parameter("News: protect on tier (1=critical only, 3=everything)", DefaultValue = 2, MinValue = 1, MaxValue = 3, Group = "News agent")]
        public int ProtectMaxTier { get; set; }

        [Parameter("News: shock veto (no network)", DefaultValue = true, Group = "News agent")]
        public bool UseShockVeto { get; set; }

        [Parameter("News: shock size (x ATR)", DefaultValue = 2.5, MinValue = 1.0, MaxValue = 10.0, Group = "News agent")]
        public double ShockAtrMult { get; set; }

        [Parameter("News: shock cooldown (bars)", DefaultValue = 3, MinValue = 1, MaxValue = 20, Group = "News agent")]
        public int ShockCooldownBars { get; set; }

        // FREQUENCY AND RISK ARE LINKED. Compounded over virgin seeds with the
        // same trades and only risk% varying:
        //   @10% risk -> worst drawdown 97% (the account is dead)
        //   @ 5% risk -> worst drawdown 81%
        //   @ 2% risk -> worst drawdown 46%
        // Trading often is fine; trading often AND large ruins the account.
        // 1.0% x 14 concurrent = 14% maximum exposure — slightly LESS than the
        // previous 1.5% x 10, while trading 2.6x more often. Win rate does not
        // change with sizing; only survivability does.
        //
        // FLOOR: gold's 1-oz minimum risks ~$18-64 per trade at a 0.4-1.4%
        // stop. On $3,000 that makes 1.0% ($30) about the lowest workable
        // setting — go under it and the configured percentage stops being what
        // is actually risked.
        [Parameter("Risk per trade (%)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        // ---- STOP PLACEMENT -------------------------------------------------
        // STRUCTURE STOP — the biggest win-rate gain found that does NOT
        // shorten the target. The stop sits just beyond the recent swing
        // low/high instead of an arbitrary ATR distance, so ordinary noise no
        // longer reaches it. Certified on 50 fresh seeds, target ratio
        // unchanged at 1:1-2:1: win rate 61.7% -> 66.4%, worst drawdown
        // 47% -> 33%, and 0 of 100 runs lost money (was 3).
        [Parameter("Stop: use swing structure (else ATR)", DefaultValue = true, Group = "Exits")]
        public bool UseSwingStop { get; set; }

        [Parameter("Swing stop: lookback bars", DefaultValue = 12, MinValue = 4, MaxValue = 100, Group = "Exits")]
        public int SwingLookback { get; set; }

        [Parameter("Swing stop: buffer beyond the swing (%)", DefaultValue = 5.0, MinValue = 0, MaxValue = 50, Group = "Exits")]
        public double SwingBufferPercent { get; set; }

        [Parameter("Adaptive stop (volatility-based) — used when swing stop is OFF", DefaultValue = true, Group = "Exits")]
        public bool AdaptiveStop { get; set; }

        [Parameter("Adaptive stop: ATR multiple", DefaultValue = 1.5, MinValue = 0.2, MaxValue = 6.0, Group = "Exits")]
        public double StopAtrMult { get; set; }

        [Parameter("Adaptive stop: MIN stop (%)", DefaultValue = 0.4, MinValue = 0.05, Group = "Exits")]
        public double MinStopPercent { get; set; }

        [Parameter("Adaptive stop: MAX stop (%)", DefaultValue = 1.4, MinValue = 0.1, Group = "Exits")]
        public double MaxStopPercent { get; set; }

        [Parameter("Stop loss (%) — used when adaptive stop is OFF", DefaultValue = 0.6, MinValue = 0.05, Group = "Exits")]
        public double StopPercent { get; set; }

        // ---- ADAPTIVE TARGET -----------------------------------------------
        // The target scales with conviction (ADX strength + trend quality):
        // a marginal setup aims 1:1, a powerful one 2:1. Do NOT shrink this
        // range to chase win rate — break-even win rate is 1/(1+RR), so a
        // nearer target needs a higher win rate merely to stand still.
// REWARD FLOOR RAISED 1.0 -> 1.3 (and the fade side with it), because a
        // live account showed the average DOLLAR loss exceeding the average
        // DOLLAR win while every trade still had reward >= risk in points.
        //
        // Two things cause that, and neither is visible in an R-based backtest:
        //   1. Spread AND commission are paid on both ends. At 1:1 they come
        //      straight off the winner and get added to the loser.
        //   2. Gold's minimum trade is 1 oz. On a $3,000 account the bot wants
        //      0.5-1.6 oz, so EVERY trade is 1 oz and the dollar risk is set by
        //      the stop width, not by the risk setting: a 0.4% stop risks
        //      $18.40 and a 1.3% stop risks $59.80. The configured risk % is
        //      almost inert at that account size.
        //
        // Measured in real dollars with whole-ounce sizing, spread and
        // commission, on a $3,000 account. Two independent virgin blocks:
        //
        //   block / market      config      win%   avg win  avg loss  W/L   median $
        //   9800-9829 mixed     1.0 floor   59.2   +25.82   -27.59   0.94    3865
        //   9900-9939 mixed     1.0 floor   60.8   +30.53   -29.40   1.04    4019
        //   9900-9939 mixed     1.3 floor   60.8   +32.54   -29.99   1.09    4154
        //   9900-9939 M1        1.0 floor   62.3   +28.32   -29.43   0.96    4563
        //   9900-9939 M1        1.3 floor   62.2   +30.54   -30.08   1.02    4741
        //   9900-9939 M2        1.0 floor   62.5   +28.70   -29.46   0.97    4606
        //   9900-9939 M2        1.3 floor   62.2   +30.36   -29.73   1.02    4884
        //
        // The floor lifts the win/loss ratio above 1.0 on every market and
        // raises the median account on five of six combinations tested.
        //
        // NOT adopted: capping the maximum stop at 0.8% so one ounce always
        // fits the risk budget. It looked good on the first block (median
        // $4005 vs $3865) and reversed on the second ($3964 vs $4019), and it
        // never moved the win/loss ratio at all (0.96-1.00). Two samples, two
        // orderings — that is noise, and fitting it to whichever ran last is
        // the mistake this file exists to prevent.
        //
        // NOTE the interaction with the trailing stop: most winners now exit on
        // the trail rather than at the target, which is why raising the target
        // by 0.3 moves the average win by much less than 0.3R.
                [Parameter("Adaptive target (conviction-scaled)", DefaultValue = true, Group = "Exits")]
        public bool AdaptiveTarget { get; set; }

        [Parameter("Adaptive target: MIN reward:risk", DefaultValue = 1.3, MinValue = 0.5, MaxValue = 10.0, Group = "Exits")]
        public double MinRewardRisk { get; set; }

        [Parameter("Adaptive target: MAX reward:risk", DefaultValue = 2.3, MinValue = 0.5, MaxValue = 20.0, Group = "Exits")]
        public double MaxRewardRisk { get; set; }

        [Parameter("Reward:risk — used when adaptive target is OFF", DefaultValue = 4.0, MinValue = 0.5, MaxValue = 10.0, Group = "Exits")]
        public double RewardRisk { get; set; }

        // EXPRESSED IN MINUTES, NOT BARS, and that is a bug fix rather than a
        // preference. "40 bars" means 10 hours on m15 and 3h20 on m5 -- the same
        // setting silently becomes a different strategy when the chart changes,
        // and the timeframe test below was run at a fixed 10-hour horizon. A
        // wall-clock horizon is what was certified, so a wall-clock horizon is
        // what ships.
// TRAILING STOP — adopted on evidence that REVERSES an earlier rejection.
        // "Trailing stop after +2R" was measured at +0.649 vs +0.633 and shelved
        // as noise. That test ran on a 2:1-6:1 reward config on m15, where +2R
        // was rarely reached. This build runs 1.0-2.0 on the trend side and 1.0
        // on the fade side, on m5, where a trade that reaches 90% of target and
        // then reverses gives back the whole move. The old conclusion does not
        // transfer, so it was re-measured.
        //
        // Once the trade is +0.7R in front, the stop follows 0.7R behind price.
        // At the moment it activates the stop is already at breakeven, so a
        // near-miss becomes a scratch instead of a full loss.
        //
        // Certified on VIRGIN tapes 9800-9839, m5, 1% risk:
        //   market   variant     win%    mean R   growth  worst DD  near-misses
        //   mixed    binary      51.3   +0.1413   x1.35     40%        155
        //   mixed    trailing    61.2   +0.1409   x1.37     35%          0
        //   M1       binary      57.1   +0.3642   x2.67     43%        114
        //   M1       trailing    66.1   +0.3043   x2.47     36%          0
        //   M2       binary      53.9   +0.3000   x2.41     29%        128
        //   M2       trailing    64.2   +0.2634   x2.29     29%          0
        //
        // Read honestly, this is a TRADE, not a free win. On a strongly
        // trending market it cuts winners short: mean R drops 0.06 and growth
        // falls x2.67 -> x2.47 on M1. What it buys is +9 to +10 points of win
        // rate, worst drawdown 40% -> 35% and 43% -> 36%, and it removes the
        // near-miss-then-full-loss case entirely (155/114/128 -> 0).
        //
        // Both knobs are a plateau, not a peak: all nine combinations of
        // activation 0.5/0.7/1.0 x distance 0.5/0.7/1.0 measured positive, with
        // 0.5-0.7 on both consistently at 62-68% win rate and 23-25% drawdown.
        //
        // MEASURED AND REJECTED alongside it, same tapes:
        //   partial close of half at +0.5R  mean R +0.157 -> +0.068 (the old
        //     partial-ladder result holds: raises win rate, cuts money)
        //   pulling the target in x0.90     +0.157 -> +0.149, and x0.80 worse
        //     still — near-misses become hits but every full winner shrinks
        //   plain breakeven at +0.7R        equal money, but win rate 41% and
        //     no better drawdown than trailing. Strictly dominated.
        [Parameter("Trailing stop (locks in a near-miss instead of losing it)", DefaultValue = true, Group = "Exits")]
        public bool UseTrailingStop { get; set; }

        [Parameter("Trail: activate at (R in profit)", DefaultValue = 0.7, MinValue = 0.1, MaxValue = 5.0, Group = "Exits")]
        public double TrailActivateR { get; set; }

        [Parameter("Trail: hold this far behind (R)", DefaultValue = 0.7, MinValue = 0.1, MaxValue = 5.0, Group = "Exits")]
        public double TrailDistanceR { get; set; }

        [Parameter("Max hold (minutes)", DefaultValue = 600, MinValue = 5, MaxValue = 20000, Group = "Exits")]
        public int MaxHoldMinutes { get; set; }

        [Parameter("Daily loss stop (%)", DefaultValue = 15.0, MinValue = 1.0, Group = "Risk")]
        public double DailyLossStopPercent { get; set; }

        // CONCURRENT POSITIONS — the frequency lever that costs almost nothing
        // in entry quality, because the extra trades are signals the bot would
        // otherwise have slept through while busy. Corrected harness, virgin
        // seeds, everything else fixed:
        //   3 positions   8.1 tr/wk, edge +0.610, worst DD 15%
        //   7 positions  13.6 tr/wk, edge +0.627, worst DD 23%
        //  10 positions  15.8 tr/wk, edge +0.620, worst DD 30%
        //  14 positions  16.6 tr/wk, edge +0.615, worst DD 31%
        // Edge is flat across the range; only exposure changes, which is why
        // risk-per-trade is set against it rather than independently.
        //
        // KNOWN CONCENTRATION RISK: when 5+ positions are open they are the
        // same direction 100% of the time — this is one directional bet in
        // several pieces. Staggered entries and per-trade swing stops keep them
        // from dying at one price (worst simulated day -13.9%, daily stop never
        // fired), but the simulators contain no GAPS, and a gap through several
        // correlated stops is exactly what they cannot show. Lower this to 5-7
        // if that risk matters more than frequency.
        [Parameter("Max concurrent positions", DefaultValue = 6, MinValue = 1, MaxValue = 20, Group = "Risk")]
        public int MaxConcurrentPositions { get; set; }

        // Also wall-clock, for the same reason.
        [Parameter("Min minutes between same-direction entries", DefaultValue = 15, MinValue = 0, MaxValue = 600, Group = "Risk")]
        public int MinMinutesBetweenSameSide { get; set; }

        // SESSION FILTER — from research into professional gold trading, then
        // measured. The strong claim ("only trade the 12-16 UTC London/NY
        // overlap") FAILED: it cut trades 6x and lowered edge +0.463 -> +0.385.
        // What did help was simply skipping the dead hours around the daily
        // close: edge +0.463 -> +0.480, win 65.5% -> 66.0%, on both models.
        [Parameter("Skip dead hours (UTC)", DefaultValue = true, Group = "Session")]
        public bool UseSessionFilter { get; set; }

        [Parameter("Trade from UTC hour", DefaultValue = 1, MinValue = 0, MaxValue = 23, Group = "Session")]
        public int SessionStartHour { get; set; }

        [Parameter("Trade until UTC hour", DefaultValue = 22, MinValue = 1, MaxValue = 24, Group = "Session")]
        public int SessionEndHour { get; set; }

        [Parameter("Allow shorts", DefaultValue = true, Group = "Risk")]
        public bool AllowShort { get; set; }

        // Was 4, which on m15 meant a full HOUR of silence between lines — long
        // enough to look broken while working normally. Every closed bar now
        // reports. The cost is log volume; the benefit is that "is it alive?"
        // is answerable in 15 minutes instead of 60.
        [Parameter("Log status every N bars", DefaultValue = 1, MinValue = 0, Group = "Diagnostics")]
        public int StatusEveryBars { get; set; }

        private const string Label = "GoldEdgeNews";
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private RelativeStrengthIndex _rsi;
        private MacdHistogram _macd;
        private DirectionalMovementSystem _dms;
        private AverageTrueRange _atr;
        private int _barCount;
        private bool _stopped;

        // ---- regime switch state ------------------------------------------
        // Rolling trend-quality history, updated once per bar whether or not
        // the bot trades that bar — the regime reading must not depend on
        // whether we happened to have room for a position.
        private readonly List<double> _regimeHistory = new List<double>();
        private double _regimeMedian;
        private bool _regimeTrending = true;
        private bool _regimeKnown;
        private string _regimeLogged = "";

        // ---- daily diagnostics -------------------------------------------
        // "It didn't trade today" is unanswerable from the status line alone.
        // These counters make each session self-explaining: how close the
        // market actually came to the gate, and what threshold WOULD have
        // traded. One live day of this settles the setting from real gold
        // instead of from a simulator.
        private static readonly double[] DiagThresholds = { 0.45, 0.40, 0.35, 0.30, 0.25, 0.20 };
        private DateTime _statsDay = DateTime.MinValue;
        private int _barsToday;
        private int _tradesToday;
        private double _bestQualityToday;
        private readonly int[] _wouldSignal = new int[6];
        // THE MEDIAN IS THE NUMBER THAT MATTERS, and it has an absolute
        // reference point. Trend quality on a PURE RANDOM WALK is not zero —
        // it is mechanically about 1/sqrt(window), measured at 0.124 for a
        // 48-bar window. So 0.12 does not mean "weak trend", it means NO
        // trend: pure chance. The strategy's edge comes entirely from the
        // EXCESS above that floor, and across four markets it tracked it
        // almost linearly:
        //     random walk   median 0.124  (+0.000)   edge  0.00
        //     choppy model  median 0.135  (+0.011)   edge +0.08
        //     model M1      median 0.165  (+0.041)   edge +0.57
        //     model M2      median 0.185  (+0.061)   edge +0.43
        // Both models this bot was certified on sit 0.04-0.06 above chance.
        // Whether real gold does was never tested — it is the assumption the
        // whole strategy rests on, and no amount of re-tuning against those
        // same two models could ever reveal it. Hence this reading.
        private readonly List<double> _qualitiesToday = new List<double>();
        private DateTime _lastProtectCheck = DateTime.MinValue;
        // original stop distance per position, so R can still be computed
        // after the stop has been moved by trailing or news protection.
        private readonly Dictionary<int, double> _initialStopDistance =
            new Dictionary<int, double>();

        // ---- news agent state (written by a background task) --------------
        private readonly object _newsLock = new object();
        private List<NewsEvent> _events = new List<NewsEvent>();
        private DateTime _lastFetchUtc = DateTime.MinValue;
        private bool _fetchInFlight;
        private bool _lastFetchOk;
        private string _newsStatus = "not fetched yet";

        private class NewsEvent
        {
            public DateTime UtcTime;
            public string Title;
            public string Currency;
            public int Tier;              // 1 = gold-critical, 2 = high, 3 = speaker/medium
        }

        // Events that reprice gold on their own. Anything matching here is
        // TIER 1 regardless of what the calendar calls its "impact".
        private static readonly string[] Tier1Keywords =
        {
            "FOMC", "FEDERAL FUNDS", "INTEREST RATE", "RATE DECISION", "RATE STATEMENT",
            "PRESS CONFERENCE", "POWELL", "FED CHAIR", "MONETARY POLICY",
            "NON-FARM", "NONFARM", "NFP", "CPI", "CORE PCE", "PCE PRICE",
            "JACKSON HOLE", "TESTIMONY", "BEIGE BOOK", "UNEMPLOYMENT RATE"
        };

        // Anyone stepping up to a microphone. Central-bank speakers move gold
        // even when the calendar marks them low impact.
        private static readonly string[] SpeakerKeywords =
        {
            "SPEAK", "SPEECH", "TESTIF", "TESTIMONY", "PRESS CONFERENCE",
            "MEMBER", "GOVERNOR", "PRESIDENT", "CHAIR", "MINUTES", "SYMPOSIUM",
            "CONFERENCE", "PANEL", "REMARKS", "STATEMENT"
        };

        // Commodity events. Oil feeds gold through inflation expectations, and
        // both trade as dollar-denominated commodities, so the complex moves
        // together. These are promoted to TIER 2 even when the calendar marks
        // them medium impact (EIA crude inventories are usually "Medium").
        private static readonly string[] CommodityKeywords =
        {
            "CRUDE OIL", "OPEC", "NATURAL GAS", "GASOLINE", "INVENTORIES",
            "GOLD", "SILVER", "COMMODITY", "OIL STOCKS", "DISTILLATE",
            "BAKER HUGHES", "RIG COUNT"
        };

        protected override void OnStart()
        {
            if (Account.IsLive)
            {
                Print("REFUSING TO RUN: live account. GoldEdgeNews is DEMO-ONLY until proven. No order placed.");
                _stopped = true;
                Stop();
                return;
            }

            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 75);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _macd = Indicators.MacdHistogram(Bars.ClosePrices, 26, 12, 9);
            _dms = Indicators.DirectionalMovementSystem(14);
            _atr = Indicators.AverageTrueRange(14, MovingAverageType.Exponential);

            Print("GoldEdgeNews started | {0} {1} | account {2} (DEMO) | balance {3:F2}",
                  SymbolName, Bars.TimeFrame, Account.Number, Account.Balance);
            Print("Entry: {0}{6} | ADX>={1}{2} | trend quality>={3} over {4} bars | up to {5} positions",
                  UseSimpleVoters ? "3/3 votes (simplified)" : VotesNeeded + "/6 votes",
                  AdxMin, RequireAdxRising ? " and rising" : "",
                  EfficiencyMin,
                  UseEnsembleQuality ? "24/36/48/60/72 (ensemble avg)"
                                     : EfficiencyWindow.ToString(),
                  MaxConcurrentPositions,
                  RequireBreakOfStructure
                    ? string.Format(" + break of structure ({0}-bar)", BosLookback)
                    : "");
            Print("Exit: stop {0} | target {1} | max hold {2} min | risk {3}%",
                  UseSwingStop
                    ? string.Format("SWING structure ({0}-bar, +{1}% buffer, clamped {2}-{3}%)",
                                    SwingLookback, SwingBufferPercent, MinStopPercent, MaxStopPercent)
                    : AdaptiveStop
                      ? string.Format("ADAPTIVE {0}x ATR clamped {1}-{2}%", StopAtrMult, MinStopPercent, MaxStopPercent)
                      : string.Format("fixed {0}%", StopPercent),
                  AdaptiveTarget
                    ? string.Format("ADAPTIVE {0}:1-{1}:1 by conviction", MinRewardRisk, MaxRewardRisk)
                    : string.Format("fixed {0}:1", RewardRisk),
                  MaxHoldMinutes, RiskPercent);
            Print("Regime switch: {0} | fade side {1} (RSI {2}/{3} when quality<={4:F2}, {5}:1)",
                  UseRegimeSwitch
                    ? string.Format("ON — rolling median of {0} bars vs random-walk floor {1:F3} +{2:F3}",
                                    RegimeWindow, RandomWalkFloor(), RegimeMargin)
                    : "OFF — both sides always active",
                  UseMeanReversion ? "ON" : "OFF",
                  FadeRsiLow, FadeRsiHigh, ChopMax, FadeRewardRisk);
            Print("Trailing stop: {0}",
                  UseTrailingStop
                    ? string.Format("ON — once +{0:F1}R in front, stop follows {1:F1}R behind " +
                                    "(a near-miss becomes a scratch, not a full loss)",
                                    TrailActivateR, TrailDistanceR)
                    : "OFF — binary stop/target only");
            Print("No other early exit: every 'close when the market changes' rule tested was neutral or harmful (ADX-fall exit cut edge +0.633 -> +0.369).");
            Print("News agent: calendar {0} watching {1} | protect-open-trade {2} ({3} min before) | block-entries {4} | shock veto {5}",
                  UseCalendar ? "ON" : "OFF", WatchCurrencies,
                  ProtectOnNews ? "ON" : "OFF", ProtectBeforeMinutes,
                  BlockEntriesOnNews ? "ON" : "OFF", UseShockVeto ? "ON" : "OFF");
            Print("News policy: never closes a trade on news (measured worse) — protects it instead.");
            if (Bars.TimeFrame != TimeFrame.Minute5)
                Print("NOTE: certified on the 5-MINUTE chart; you are on {0}. m5 measured " +
                      "roughly double the trades of m15 with better growth on all four test " +
                      "markets. Exits are wall-clock so they are unchanged by the chart, but " +
                      "the quality windows and the regime window are in BARS and do shift.",
                      Bars.TimeFrame);

            SetupCheck();

            PrimeRegimeHistory();

            if (UseCalendar)
                BeginCalendarFetch();
        }

        protected override void OnBar()
        {
            if (_stopped)
                return;
            try { Evaluate(); }
            catch (Exception ex) { Print("ERROR in OnBar: {0} — {1}", ex.GetType().Name, ex.Message); }
        }

        // On an h1 chart a bar closes only once an hour — far too coarse to
        // catch a 30-minute pre-news window. Check protection once a minute
        // instead. Entries are still decided on bar close only.
        protected override void OnTick()
        {
            if (_stopped || (!ProtectOnNews && !UseTrailingStop))
                return;
            var now = Server.TimeInUtc;
            if ((now - _lastProtectCheck).TotalSeconds < 60)
                return;
            _lastProtectCheck = now;
            try { ProtectPositions(); ManageTrailingStops(); }
            catch (Exception ex) { Print("ERROR in position management: {0} — {1}", ex.GetType().Name, ex.Message); }
        }

        private void Evaluate()
        {
            _barCount++;
            RollDailyDiagnostics();

            // refresh the calendar every 6 hours (fail-safe, off-thread)
            // Normal refresh is 6-hourly. After a FAILED fetch (usually the
            // feed's 2-downloads-per-5-minutes rate limit, which is easy to
            // trip by restarting the bot) retry in 10 minutes instead of
            // sitting blind for six hours.
            var refreshHours = _lastFetchOk ? 6.0 : 0.17;
            if (UseCalendar && (Server.TimeInUtc - _lastFetchUtc).TotalHours >= refreshHours)
                BeginCalendarFetch();

            foreach (var pos in OwnPositions().ToList())
            {
                if ((Server.TimeInUtc - pos.EntryTime).TotalMinutes >= MaxHoldMinutes)
                {
                    Print("Closing {0} — max hold {1} minutes reached.", pos.Id, MaxHoldMinutes);
                    ClosePosition(pos);
                }
            }

            // news defence: protect the open trade, never close it — a news
            // move can just as easily run in our favour.
            ProtectPositions();

            // The ensemble reaches back to the longest window, not to the
            // configured one — warm up for whichever is greater.
            var longest = EfficiencyWindow;
            if (UseEnsembleQuality)
                foreach (var w in EnsembleWindows)
                    if (w > longest) longest = w;
            var need = Math.Max(120, longest + 10);
            if (Bars.ClosePrices.Count < need)
                return;

            var dayStart = DayStartEquity();
            if (dayStart > 0 && Account.Equity <= dayStart * (1.0 - DailyLossStopPercent / 100.0))
            {
                foreach (var pos in OwnPositions().ToList())
                    ClosePosition(pos);
                return;
            }

            var close = Bars.ClosePrices.Last(0);
            var emaFast = _emaFast.Result.Last(0);
            var emaFastPrev = _emaFast.Result.Last(3);
            var emaSlow = _emaSlow.Result.Last(0);
            var rsi = _rsi.Result.Last(0);
            var macdHist = _macd.Histogram.Last(0);
            var adx = _dms.ADX.Last(0);
            var adxPrev = _dms.ADX.Last(AdxRisingLookback);
            var past = Bars.ClosePrices.Last(20);

            int bulls, total;
            if (UseSimpleVoters)
            {
                // Three near-independent reads: trend structure, momentum
                // oscillator, and raw displacement. All three must agree.
                bulls = 0;
                if (emaFast > emaSlow) bulls++;
                if (rsi > 50.0) bulls++;
                if (close > past) bulls++;
                total = 3;
            }
            else
            {
                bulls = 0;
                if (emaFast > emaSlow) bulls++;
                if (close > emaSlow) bulls++;
                if (macdHist > 0) bulls++;
                if (rsi > 50.0) bulls++;
                if (emaFast > emaFastPrev) bulls++;
                if (close > past) bulls++;
                total = 6;
            }
            var votesNeeded = UseSimpleVoters ? 3 : VotesNeeded;
            var bears = total - bulls;

            var quality = CurrentTrendQuality();
            // Regime first, and unconditionally: it must see every bar, not
            // only the bars on which the bot was free to trade.
            UpdateRegime(quality);
            RecordDiagnostics(quality, adx, bulls, bears, votesNeeded);

            // Which half of the book is allowed to speak right now. Until the
            // history is long enough to have an opinion, both are — that is
            // the previous behaviour, and it only lasts until the median fills.
            var regimeUnknown = !UseRegimeSwitch || !_regimeKnown;
            var trendAllowed = regimeUnknown || _regimeTrending;
            var fadeAllowed = regimeUnknown || !_regimeTrending;

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: {0:F2} | {1}/{2} votes | ADX {3:F1}{4} | quality {5:F2} {6} (need {7:F2}, best today {8:F2}) | regime {9} | {10} trades today | news: {11}",
                      close, bulls, bears, adx, adx > adxPrev ? "+" : "-", quality,
                      quality >= EfficiencyMin ? "TREND" : "chop", EfficiencyMin,
                      _bestQualityToday,
                      !UseRegimeSwitch ? "OFF (both sides)"
                        : !_regimeKnown ? "warming up (both sides)"
                        : _regimeTrending
                          ? string.Format("TRENDING med {0:F3} -> trend side", _regimeMedian)
                          : string.Format("MEAN-REVERTING med {0:F3} -> fade side", _regimeMedian),
                      _tradesToday, NewsStatusLine());

            // Room for another position? Holding several at once is what
            // lifts trade count without touching entry quality.
            if (OwnPositions().Count() >= MaxConcurrentPositions)
                return;

            // Dead hours around the daily close are thin and choppy — the one
            // session restriction that measured better (see the param comment).
            if (UseSessionFilter)
            {
                var h = Server.TimeInUtc.Hour;
                if (h < SessionStartHour || h >= SessionEndHour)
                    return;
            }

            // ---- news agent: entry blocking is OPTIONAL and OFF by default,
            // because blocking entries costs trades. The default protection
            // works on OPEN positions instead (see ProtectPositions).
            if (UseCalendar && BlockEntriesOnNews)
            {
                string evtName;
                if (InNewsWindow(Server.TimeInUtc, out evtName))
                {
                    Print("NEWS: skipping entry near \"{0}\".", evtName);
                    return;
                }
            }
            if (UseShockVeto && RecentShock())
                return;

            // ---- which side of the book has anything to say here? --------
            if (trendAllowed && quality >= EfficiencyMin)
            {
                if (adx < AdxMin) return;
                if (RequireAdxRising && adx <= adxPrev) return;
                if (bulls >= votesNeeded && BrokeStructure(1))
                    OpenTrade(1, bulls, adx, quality);
                else if (bears >= votesNeeded && AllowShort && BrokeStructure(-1))
                    OpenTrade(-1, bears, adx, quality);
                return;
            }

            // CHOPPY tape: the trend side has nothing to say here. Fade an RSI
            // extreme instead, if the mean-reversion side is enabled.
            if (!UseMeanReversion || !fadeAllowed || quality > ChopMax) return;
            if (rsi <= FadeRsiLow)
                OpenFade(1, rsi, quality);
            else if (rsi >= FadeRsiHigh && AllowShort)
                OpenFade(-1, rsi, quality);
        }

        // ================= news agent ====================================

        // A bar that moved more than ShockAtrMult x ATR is a news-grade shock;
        // block new entries for ShockCooldownBars afterwards. No network.
        private bool RecentShock()
        {
            var c = Bars.ClosePrices;
            for (var k = 0; k <= ShockCooldownBars; k++)
            {
                if (c.Count < k + 2)
                    break;
                var move = Math.Abs(c.Last(k) - c.Last(k + 1));
                var atr = _atr.Result.Last(k);
                if (atr > 0 && move > ShockAtrMult * atr)
                    return true;
            }
            return false;
        }

        // THE MAIN NEWS DEFENCE — it never touches entries, so the bot trades
        // exactly as often as it would without a news feed.
        //
        // When a market-moving event is coming up and the position is already
        // in profit, pull the stop to breakeven so a news spike can turn a
        // winner into a scratch, not a loss. If the trade is NOT yet in profit
        // the stop is left alone: yanking it to breakeven there would just
        // guarantee the loss it is trying to avoid.
        //
        // MEASURED (30 virgin seeds, shock-driven analogue): edge +0.633 ->
        // +0.636 with the SAME trade count (1530 -> 1532). Free insurance.
        // Tested and REJECTED: closing the position on news — edge collapses
        // to +0.496 because it cuts winning trades short.
        private void ProtectPositions()
        {
            if (!ProtectOnNews)
                return;

            var now = Server.TimeInUtc;
            string evt = null;
            var newsSoon = false;

            if (UseCalendar)
            {
                List<NewsEvent> snapshot;
                lock (_newsLock)
                    snapshot = _events;
                if (snapshot != null)
                {
                    foreach (var e in snapshot)
                    {
                        if (e.Tier > ProtectMaxTier)
                            continue;                       // sensitivity set by ProtectMaxTier
                        var mins = (e.UtcTime - now).TotalMinutes;
                        if (mins >= 0 && mins <= ProtectBeforeMinutes)
                        {
                            newsSoon = true;
                            evt = string.Format("{0} {1} in {2:F0} min", e.Currency, e.Title, mins);
                            break;
                        }
                    }
                }
            }
            // a shock already in progress counts as news too (no feed needed)
            if (!newsSoon && UseShockVeto && RecentShock())
            {
                newsSoon = true;
                evt = "price shock in progress";
            }
            if (!newsSoon)
                return;

            foreach (var pos in OwnPositions())
            {
                var inProfit = pos.NetProfit > 0;
                if (!inProfit)
                    continue;                                // never tighten a losing trade
                var be = pos.EntryPrice;
                var already = pos.StopLoss.HasValue &&
                              ((pos.TradeType == TradeType.Buy && pos.StopLoss.Value >= be) ||
                               (pos.TradeType == TradeType.Sell && pos.StopLoss.Value <= be));
                if (already)
                    continue;                                // already protected
                var r = ModifyPosition(pos, be, pos.TakeProfit);
                if (r.IsSuccessful)
                    Print("NEWS PROTECT: {0} — stop moved to breakeven {1:F2} ({2}).",
                          pos.Id, be, evt);
                else
                    Print("NEWS PROTECT failed on {0}: {1}", pos.Id, r.Error);
            }
        }

        private double InitialStopDistance(Position pos)
        {
            double sd;
            if (_initialStopDistance.TryGetValue(pos.Id, out sd))
                return sd;
            // First sighting (including positions that predate a restart): the
            // stop has not been moved yet, so its distance IS the original.
            sd = pos.StopLoss.HasValue ? Math.Abs(pos.EntryPrice - pos.StopLoss.Value) : 0.0;
            if (sd > 0)
                _initialStopDistance[pos.Id] = sd;
            return sd;
        }

        private void ManageTrailingStops()
        {
            if (!UseTrailingStop)
                return;

            foreach (var pos in OwnPositions().ToList())
            {
                var sd = InitialStopDistance(pos);
                if (sd <= 0)
                    continue;
                var dir = pos.TradeType == TradeType.Buy ? 1 : -1;
                // the price this position would actually exit at
                var price = dir > 0 ? Symbol.Bid : Symbol.Ask;
                if (price <= 0)
                    continue;
                var r = (price - pos.EntryPrice) / sd * dir;
                if (r < TrailActivateR)
                    continue;

                var candidate = price - dir * TrailDistanceR * sd;
                // NEVER move a stop against the position.
                if (pos.StopLoss.HasValue &&
                    ((dir > 0 && candidate <= pos.StopLoss.Value) ||
                     (dir < 0 && candidate >= pos.StopLoss.Value)))
                    continue;

                var res = ModifyPosition(pos, candidate, pos.TakeProfit);
                if (res.IsSuccessful)
                    Print("TRAIL {0}: +{1:F2}R reached, stop -> {2:F2} (locks in {3:F2}R)",
                          pos.Id, r, candidate, r - TrailDistanceR);
                else
                    Print("TRAIL failed on {0}: {1}", pos.Id, res.Error);
            }

            // drop bookkeeping for positions that have closed
            if (_initialStopDistance.Count > 200)
            {
                var live = new HashSet<int>(OwnPositions().Select(p => p.Id));
                foreach (var id in _initialStopDistance.Keys.Where(k => !live.Contains(k)).ToList())
                    _initialStopDistance.Remove(id);
            }
        }

        private static double Clamp01(double x)
        {
            return x < 0.0 ? 0.0 : (x > 1.0 ? 1.0 : x);
        }

        private int TierMinutes(int tier)
        {
            if (tier == 1) return Tier1Minutes;
            if (tier == 2) return Tier2Minutes;
            return Tier3Minutes;
        }

        private bool InNewsWindow(DateTime nowUtc, out string eventName)
        {
            eventName = null;
            List<NewsEvent> snapshot;
            lock (_newsLock)
                snapshot = _events;
            if (snapshot == null || snapshot.Count == 0)
                return false;                        // fail-safe: no data, keep trading

            foreach (var e in snapshot)
            {
                var w = TierMinutes(e.Tier);
                if (w <= 0)
                    continue;
                var mins = (nowUtc - e.UtcTime).TotalMinutes;
                if (mins >= -w && mins <= w)
                {
                    eventName = string.Format("T{0} {1} {2} at {3:HH:mm} UTC",
                                              e.Tier, e.Currency, e.Title, e.UtcTime);
                    return true;
                }
            }
            return false;
        }

        // How much of the week this configuration actually blocks. Printed so
        // the cost of "watch everything" is visible instead of hidden.
        private string CoverageReport(List<NewsEvent> evs)
        {
            var t1 = evs.Count(e => e.Tier == 1);
            var t2 = evs.Count(e => e.Tier == 2);
            var t3 = evs.Count(e => e.Tier == 3);
            var minutes = t1 * 2.0 * Tier1Minutes + t2 * 2.0 * Tier2Minutes + t3 * 2.0 * Tier3Minutes;
            var pct = minutes / (7.0 * 24.0 * 60.0) * 100.0;   // upper bound; windows can overlap
            return string.Format("{0} events (T1 {1}, T2 {2}, T3 {3}) — blocks at most {4:F0}h/week (~{5:F0}% of the week)",
                                 evs.Count, t1, t2, t3, minutes / 60.0, pct);
        }

        private string NewsStatusLine()
        {
            lock (_newsLock)
                return _newsStatus;
        }

        // Fetch off the trading thread. Never throws into the bot; on any
        // failure the bot keeps trading with the shock veto only.
        private void BeginCalendarFetch()
        {
            lock (_newsLock)
            {
                if (_fetchInFlight)
                    return;
                _fetchInFlight = true;
            }
            _lastFetchUtc = Server.TimeInUtc;

            var url = CalendarUrl;
            var speakers = WatchSpeakers;
            var watch = (WatchCurrencies ?? "USD")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length > 0)
                .ToList();

            Task.Run(() =>
            {
                string status;
                List<NewsEvent> parsed = null;
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; cTraderBot/1.0)");
                        var json = wc.DownloadString(url);
                        parsed = ParseCalendar(json, watch, speakers);
                        status = CoverageReport(parsed);
                    }
                }
                catch (Exception ex)
                {
                    status = "FETCH FAILED (" + ex.GetType().Name + ") — retrying in 10 min; "
                           + "trading on shock veto meanwhile. If this repeats, you are likely "
                           + "hitting the feed's 2-downloads-per-5-minutes limit by restarting.";
                }

                lock (_newsLock)
                {
                    if (parsed != null && parsed.Count > 0)
                        _events = parsed;            // keep the old list if the new one is empty
                    _lastFetchOk = parsed != null && parsed.Count > 0;
                    _newsStatus = status;
                    _fetchInFlight = false;
                }
                // Print/API calls must come back to cTrader's main thread.
                var msg = status;
                // List what was actually loaded, so coverage is visible rather
                // than just counted — e.g. you can see "Crude Oil Inventories"
                // and OPEC meetings really are being tracked.
                var upcoming = new List<string>();
                if (parsed != null)
                {
                    var nowUtc = DateTime.UtcNow;
                    foreach (var e in parsed.Where(x => x.UtcTime >= nowUtc)
                                            .OrderBy(x => x.UtcTime).Take(8))
                        upcoming.Add(string.Format("T{0} {1:ddd HH:mm} {2} {3}",
                                                   e.Tier, e.UtcTime, e.Currency, e.Title));
                }
                BeginInvokeOnMainThread(() =>
                {
                    Print("news: {0}", msg);
                    foreach (var u in upcoming)
                        Print("   next: {0}", u);
                });
            });
        }

        // Minimal, dependency-free JSON reader for the flat calendar array.
        // Keeps EVERY event that can move gold and sorts it into a tier:
        //   tier 1 — reprices gold by itself (FOMC/NFP/CPI/Powell/rate decisions)
        //   tier 2 — any other high-impact print in a watched currency
        //   tier 3 — anyone speaking, plus medium-impact prints
        private static List<NewsEvent> ParseCalendar(string json, List<string> watch,
                                                     bool watchSpeakers)
        {
            var list = new List<NewsEvent>();
            if (string.IsNullOrEmpty(json))
                return list;

            var idx = 0;
            while (true)
            {
                var start = json.IndexOf('{', idx);
                if (start < 0) break;
                var end = json.IndexOf('}', start);
                if (end < 0) break;
                var obj = json.Substring(start, end - start + 1);
                idx = end + 1;

                var cur = Field(obj, "country") ?? Field(obj, "currency");
                if (cur == null)
                    continue;
                cur = cur.Trim().ToUpperInvariant();
                // "ALL" is how OPEC meetings and other global events are tagged
                if (watch.Count > 0 && cur != "ALL" && !watch.Contains(cur))
                    continue;

                var dateStr = Field(obj, "date");
                if (dateStr == null)
                    continue;
                DateTimeOffset dto;
                if (!DateTimeOffset.TryParse(dateStr, null,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out dto))
                    continue;

                var title = Field(obj, "title") ?? "event";
                var impact = Field(obj, "impact") ?? "";
                var upper = title.ToUpperInvariant();
                var isHigh = impact.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0;
                var isMedium = impact.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0;
                var isSpeaker = SpeakerKeywords.Any(k => upper.Contains(k));
                var isCommodity = CommodityKeywords.Any(k => upper.Contains(k));
                // "FOMC Member Speaks" contains "FOMC" but is a routine speech,
                // not a rate decision — do not let it rank as gold-critical.
                // The CHAIR is different: Powell moves gold on his own.
                var isChair = upper.Contains("POWELL") || upper.Contains("FED CHAIR");
                var isMemberSpeech = !isChair &&
                                     (upper.Contains("MEMBER") || upper.Contains("SPEAK"));
                var isCritical = !isMemberSpeech && Tier1Keywords.Any(k => upper.Contains(k));

                int tier;
                if (isCritical && cur == "USD")
                    tier = 1;
                else if (isCommodity)
                    tier = 2;                       // oil/commodity complex                       // gold-critical US event
                else if (isCritical || isHigh)
                    tier = 2;                       // big print, or critical abroad
                else if (isSpeaker && watchSpeakers)
                    tier = 3;                       // someone at a microphone
                else if (isMedium && cur == "USD")
                    tier = 3;                       // medium US data
                else
                    continue;                       // genuinely irrelevant to gold

                list.Add(new NewsEvent
                {
                    UtcTime = dto.UtcDateTime,
                    Title = title,
                    Currency = cur,
                    Tier = tier
                });
            }
            return list;
        }

        private static string Field(string obj, string key)
        {
            var needle = "\"" + key + "\"";
            var k = obj.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (k < 0) return null;
            var colon = obj.IndexOf(':', k + needle.Length);
            if (colon < 0) return null;
            var i = colon + 1;
            while (i < obj.Length && char.IsWhiteSpace(obj[i])) i++;
            if (i >= obj.Length) return null;
            if (obj[i] != '"')
            {
                var stop = i;
                while (stop < obj.Length && obj[stop] != ',' && obj[stop] != '}') stop++;
                return obj.Substring(i, stop - i).Trim();
            }
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < obj.Length && obj[i] != '"')
            {
                if (obj[i] == '\\' && i + 1 < obj.Length) i++;
                sb.Append(obj[i]);
                i++;
            }
            return sb.ToString();
        }

        // ================= strategy plumbing =============================

        // Every cTrader minute/hour timeframe, not a partial list. The old
        // version fell through to 60 for m2/m3/m4/m10/m20 and h2/h3 -- so on
        // those charts a "40 bar" hold silently became 40 HOURS. Nothing now
        // depends on this for exits (they are wall-clock), but it still feeds
        // the diagnostics, and a silent wrong answer is worse than a loud one.
        private double BarMinutes()
        {
            var tf = Bars.TimeFrame;
            if (tf == TimeFrame.Minute) return 1;
            if (tf == TimeFrame.Minute2) return 2;
            if (tf == TimeFrame.Minute3) return 3;
            if (tf == TimeFrame.Minute4) return 4;
            if (tf == TimeFrame.Minute5) return 5;
            if (tf == TimeFrame.Minute6) return 6;
            if (tf == TimeFrame.Minute10) return 10;
            if (tf == TimeFrame.Minute15) return 15;
            if (tf == TimeFrame.Minute20) return 20;
            if (tf == TimeFrame.Minute30) return 30;
            if (tf == TimeFrame.Minute45) return 45;
            if (tf == TimeFrame.Hour) return 60;
            if (tf == TimeFrame.Hour2) return 120;
            if (tf == TimeFrame.Hour3) return 180;
            if (tf == TimeFrame.Hour4) return 240;
            if (tf == TimeFrame.Hour6) return 360;
            if (tf == TimeFrame.Hour8) return 480;
            if (tf == TimeFrame.Hour12) return 720;
            if (tf == TimeFrame.Daily) return 1440;
            Print("WARNING: unrecognised timeframe {0} — assuming 5 minutes for diagnostics only.", tf);
            return 5;
        }

        // Distance from entry to just beyond the recent swing low (for longs)
        // or high (for shorts). Uses real bar highs/lows rather than closes,
        // which is where the structure actually sits.
        private double SwingStopDistance(int direction, double price)
        {
            var n = Math.Min(SwingLookback, Bars.ClosePrices.Count - 1);
            if (n < 2)
                return price * (MinStopPercent / 100.0);
            double extreme = direction > 0 ? double.MaxValue : double.MinValue;
            for (var k = 0; k <= n; k++)
            {
                var lo = Bars.LowPrices.Last(k);
                var hi = Bars.HighPrices.Last(k);
                if (direction > 0) extreme = Math.Min(extreme, lo);
                else extreme = Math.Max(extreme, hi);
            }
            var raw = direction > 0 ? price - extreme : extreme - price;
            if (raw <= 0)
                return price * (MinStopPercent / 100.0);
            return raw * (1.0 + SwingBufferPercent / 100.0);
        }

        // Trend quality actually used for gating and for target conviction.
        private double CurrentTrendQuality()
        {
            return TrendQualityAt(0);
        }

        // offset 0 is the bar that just closed; offset k is k bars before it.
        // The offset exists so the regime history can be pre-filled from real
        // history at start-up instead of taking 60 bars (15 hours) to become
        // usable — during which the bot would be running both sides blind on
        // exactly the tape the switch is meant to protect it from.
        private double TrendQualityAt(int offset)
        {
            if (!UseEnsembleQuality) return TrendQuality(EfficiencyWindow, offset);
            double sum = 0.0;
            var count = 0;
            foreach (var w in EnsembleWindows)
            {
                if (Bars.ClosePrices.Count <= w + offset + 1) continue;
                sum += TrendQuality(w, offset);
                count++;
            }
            // Fall back to the single window rather than reporting 0.0 (which
            // would silently gate every trade off) if history is still short.
            return count > 0 ? sum / count : TrendQuality(EfficiencyWindow, offset);
        }

        private double TrendQuality(int window, int offset)
        {
            var c = Bars.ClosePrices;
            var n = Math.Min(window, c.Count - 1 - offset);
            if (n < 2) return 0.0;
            var net = Math.Abs(c.Last(offset) - c.Last(offset + n));
            double path = 0.0;
            for (var i = offset; i < offset + n; i++)
                path += Math.Abs(c.Last(i) - c.Last(i + 1));
            return path > 0 ? net / path : 0.0;
        }

        // Loud, unmissable check of the two settings that are wrong most often.
        // Both have gone unnoticed repeatedly because the old notes were one
        // quiet line among eight. cTrader keeps an instance's saved parameters
        // when the code is edited, so a value set weeks ago silently survives
        // every update -- which is exactly how a 10% risk kept coming back.
        private void SetupCheck()
        {
            var problems = new List<string>();

            if (Bars.TimeFrame != TimeFrame.Minute5)
                problems.Add(string.Format(
                    "CHART IS {0}, NOT m5. Certified on m5: 78.8 trades/week vs 42.9 on m15 " +
                    "(virgin seeds), growth x1.45 vs x1.11, and drawdown barely moves. " +
                    "You are running the half-frequency version. Change the CHART timeframe " +
                    "— no code setting controls this.", Bars.TimeFrame));

            var exposure = RiskPercent * MaxConcurrentPositions;
            if (RiskPercent > 2.0)
                problems.Add(string.Format(
                    "RISK IS {0}% PER TRADE. Everything here was certified at 1%. With {1} " +
                    "concurrent positions that is {2}% of the account exposed to one " +
                    "correlated move. If this is not what you chose, the instance was reused " +
                    "rather than deleted — cTrader kept the old value.",
                    RiskPercent, MaxConcurrentPositions, exposure));
            else if (exposure > 12.0)
                problems.Add(string.Format(
                    "TOTAL EXPOSURE {0}% ({1}% x {2} positions). Certified at 6%.",
                    exposure, RiskPercent, MaxConcurrentPositions));

            if (problems.Count == 0)
            {
                Print("SETUP OK: m5 chart, {0}% risk, max {1} positions = {2}% exposure — " +
                      "matches what was certified.", RiskPercent, MaxConcurrentPositions, exposure);
                return;
            }

            Print("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            Print("!!  SETUP PROBLEM — {0} thing(s) do not match what was tested", problems.Count);
            for (var i = 0; i < problems.Count; i++)
                Print("!!  {0}) {1}", i + 1, problems[i]);
            Print("!!  Fix: DELETE this cBot instance and add a fresh one on an m5 chart.");
            Print("!!  Editing the code does NOT reset an existing instance's saved settings.");
            Print("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
        }

        // Break of structure: this close takes out every close in the lookback,
        // i.e. price has actually left the prior range rather than merely
        // looking bullish inside it. Deliberately reads CLOSES, not wicks — a
        // wick through a level is the thing the same course warns is a
        // rejection, not a break.
        private bool BrokeStructure(int direction)
        {
            if (!RequireBreakOfStructure) return true;
            var c = Bars.ClosePrices;
            var n = Math.Min(BosLookback, c.Count - 1);
            if (n < 2) return false;
            var close = c.Last(0);
            for (var k = 1; k <= n; k++)
            {
                if (direction > 0 && c.Last(k) >= close) return false;
                if (direction < 0 && c.Last(k) <= close) return false;
            }
            return true;
        }

        // What pure chance produces on whichever quality measure is in use.
        // Anchored on the measured 48-bar random-walk value (0.124) and scaled
        // by 1/sqrt(n), which is how efficiency behaves on a random walk. For
        // the default ensemble this returns 0.131, NOT 0.124 — getting that
        // wrong makes a mean-reverting tape look like a weakly trending one.
        private double RandomWalkFloor()
        {
            if (!UseEnsembleQuality)
                return 0.124 * Math.Sqrt(48.0 / Math.Max(1, EfficiencyWindow));
            double sum = 0.0;
            var count = 0;
            foreach (var w in EnsembleWindows)
            {
                sum += 0.124 * Math.Sqrt(48.0 / w);
                count++;
            }
            return count > 0 ? sum / count : 0.124;
        }

        // Averages the two middle values on an even count. That is the
        // convention the backtest used, and the regime call is a threshold
        // comparison — taking the upper middle instead would put the shipped
        // bot on a very slightly different series from the one certified.
        private static double Median(List<double> values)
        {
            var sorted = new List<double>(values);
            sorted.Sort();
            var n = sorted.Count;
            if (n == 0) return 0.0;
            return n % 2 == 1
                ? sorted[n / 2]
                : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
        }

        private int RegimeMinSamples()
        {
            return Math.Min(60, Math.Max(20, RegimeWindow / 5));
        }

        private void UpdateRegime(double quality)
        {
            _regimeHistory.Add(quality);
            var cap = Math.Max(RegimeMinSamples(), RegimeWindow);
            while (_regimeHistory.Count > cap)
                _regimeHistory.RemoveAt(0);
            if (_regimeHistory.Count < RegimeMinSamples())
            {
                _regimeKnown = false;
                return;
            }
            _regimeMedian = Median(_regimeHistory);
            _regimeKnown = true;
            _regimeTrending = _regimeMedian >= RandomWalkFloor() + RegimeMargin;

            if (!UseRegimeSwitch) return;
            var now = _regimeTrending ? "TRENDING" : "MEAN-REVERTING";
            if (now == _regimeLogged) return;
            _regimeLogged = now;
            Print("REGIME -> {0} | rolling median quality {1:F3} over {2} bars " +
                  "vs random-walk floor {3:F3} (+{4:F3} margin) | {5} side active",
                  now, _regimeMedian, _regimeHistory.Count, RandomWalkFloor(),
                  RegimeMargin, _regimeTrending ? "TREND" : "FADE");
        }

        // Fill the regime history from bars that already exist, so the switch
        // is live on the first bar rather than after 60 of them.
        private void PrimeRegimeHistory()
        {
            _regimeHistory.Clear();
            var longest = EfficiencyWindow;
            if (UseEnsembleQuality)
                foreach (var w in EnsembleWindows)
                    if (w > longest) longest = w;
            var room = Bars.ClosePrices.Count - longest - 2;
            if (room < 1) return;
            var take = Math.Min(RegimeWindow, room);
            for (var offset = take; offset >= 1; offset--)
                _regimeHistory.Add(TrendQualityAt(offset));
            if (_regimeHistory.Count < RegimeMinSamples()) return;
            _regimeMedian = Median(_regimeHistory);
            _regimeKnown = true;
            _regimeTrending = _regimeMedian >= RandomWalkFloor() + RegimeMargin;
            _regimeLogged = _regimeTrending ? "TRENDING" : "MEAN-REVERTING";
            Print("Regime primed from {0} historical bars: median quality {1:F3} " +
                  "vs floor {2:F3} -> {3} | {4} side active from the first bar",
                  _regimeHistory.Count, _regimeMedian, RandomWalkFloor(),
                  _regimeLogged, _regimeTrending ? "TREND" : "FADE");
        }

        // Several positions at once is fine; several near-identical ones on
        // consecutive bars is just one trade in disguise, sized larger. Space
        // same-direction entries out so concurrency adds diversity, not weight.
        private bool TooSoonForSameSide(int direction)
        {
            if (MinMinutesBetweenSameSide <= 0) return false;
            var side = direction > 0 ? TradeType.Buy : TradeType.Sell;
            var minutes = (double)MinMinutesBetweenSameSide;
            foreach (var pos in OwnPositions())
                if (pos.TradeType == side &&
                    (Server.TimeInUtc - pos.EntryTime).TotalMinutes < minutes)
                    return true;
            return false;
        }

        // Counter-trend entry. Same stop and sizing machinery as OpenTrade —
        // only the reason for entering differs — so the two sides cannot be
        // compared unfairly on exit geometry.
        private void OpenFade(int direction, double rsi, double quality)
        {
            if (TooSoonForSameSide(direction)) return;
            var price = direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0) return;

            var loClamp = price * (MinStopPercent / 100.0);
            var hiClamp = price * (MaxStopPercent / 100.0);
            var stopDist = Math.Max(loClamp, Math.Min(hiClamp,
                                    SwingStopDistance(direction, price)));
            if (stopDist <= 0) return;
            var tpDist = stopDist * FadeRewardRisk;

            var riskUsd = Account.Equity * (RiskPercent / 100.0);
            var units = Symbol.NormalizeVolumeInUnits(riskUsd / stopDist, RoundingMode.Down);
            var minRisk = Symbol.VolumeInUnitsMin * stopDist;
            if (minRisk > riskUsd * 2.0)
            {
                Print("SKIP (fade): account too small — smallest trade risks {0:F2}, budget {1:F2}.",
                      minRisk, riskUsd);
                return;
            }
            if (units < Symbol.VolumeInUnitsMin) units = Symbol.VolumeInUnitsMin;

            var side = direction > 0 ? TradeType.Buy : TradeType.Sell;
            var result = ExecuteMarketOrder(side, SymbolName, units, Label,
                                            stopDist / Symbol.PipSize, tpDist / Symbol.PipSize);
            if (result.IsSuccessful)
            {
                _tradesToday++;
                Print("FADE {0} {1} units @ {2:F2} | stop {3:F2} ({4:F2}%) | target {5:F2} ({6:F2}:1) | RSI {7:F0}, quality {8:F2} (choppy)",
                      side, units, price, price - direction * stopDist,
                      stopDist / price * 100.0, price + direction * tpDist,
                      FadeRewardRisk, rsi, quality);
            }
            else Print("FADE ORDER FAILED: {0}", result.Error);
        }

        private void OpenTrade(int direction, int votes, double adx, double quality)
        {
            if (TooSoonForSameSide(direction))
                return;
            var price = direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0) return;

            // ---- stop selection --------------------------------------------
            var loClamp = price * (MinStopPercent / 100.0);
            var hiClamp = price * (MaxStopPercent / 100.0);
            double stopDist;
            if (UseSwingStop)
            {
                // Place the stop just past the recent swing extreme. Price has
                // to break actual structure to stop us out, not merely wobble.
                stopDist = SwingStopDistance(direction, price);
                stopDist = Math.Max(loClamp, Math.Min(hiClamp, stopDist));
            }
            else if (AdaptiveStop)
            {
                var atr = _atr.Result.Last(0);
                stopDist = atr > 0 ? Math.Max(loClamp, Math.Min(hiClamp, StopAtrMult * atr)) : loClamp;
            }
            else
            {
                stopDist = price * (StopPercent / 100.0);
            }
            if (stopDist <= 0) return;

            // ---- adaptive target: scale with conviction --------------------
            var rrUsed = RewardRisk;
            if (AdaptiveTarget)
            {
                var adxScore = Clamp01((adx - AdxMin) / 25.0);
                var qualScore = Clamp01((quality - EfficiencyMin) / 0.35);
                var conviction = Clamp01(0.5 * adxScore + 0.5 * qualScore);
                rrUsed = MinRewardRisk + conviction * (MaxRewardRisk - MinRewardRisk);
            }
            var tpDist = stopDist * rrUsed;

            var riskUsd = Account.Equity * (RiskPercent / 100.0);
            var units = Symbol.NormalizeVolumeInUnits(riskUsd / stopDist, RoundingMode.Down);

            var minRisk = Symbol.VolumeInUnitsMin * stopDist;
            if (minRisk > riskUsd * 2.0)
            {
                Print("SKIP: account too small — smallest trade risks {0:F2}, budget {1:F2}.", minRisk, riskUsd);
                return;
            }
            if (units < Symbol.VolumeInUnitsMin)
                units = Symbol.VolumeInUnitsMin;

            var side = direction > 0 ? TradeType.Buy : TradeType.Sell;
            var result = ExecuteMarketOrder(side, SymbolName, units, Label,
                                            stopDist / Symbol.PipSize, tpDist / Symbol.PipSize);
            if (result.IsSuccessful)
                _tradesToday++;
            if (result.IsSuccessful)
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} ({4:F2}%{5}) | target {6:F2} ({7:F2}:1{8}) | {9} votes, ADX {10:F0}, quality {11:F2}",
                      side, units, price, price - direction * stopDist,
                      stopDist / price * 100.0, UseSwingStop ? " swing" : (AdaptiveStop ? " adaptive" : ""),
                      price + direction * tpDist, rrUsed, AdaptiveTarget ? " adaptive" : "",
                      votes, adx, quality);
            else
                Print("ORDER FAILED: {0}", result.Error);
        }

        private void RollDailyDiagnostics()
        {
            var today = Server.TimeInUtc.Date;
            if (_statsDay == today) return;
            if (_statsDay != DateTime.MinValue)
                PrintDaySummary();
            _statsDay = today;
            _barsToday = 0;
            _tradesToday = 0;
            _bestQualityToday = 0.0;
            _qualitiesToday.Clear();
            for (var k = 0; k < _wouldSignal.Length; k++) _wouldSignal[k] = 0;
        }

        private void RecordDiagnostics(double quality, double adx, int bulls,
                                       int bears, int votesNeeded)
        {
            _barsToday++;
            if (quality > _bestQualityToday) _bestQualityToday = quality;
            _qualitiesToday.Add(quality);
            // A "signal" here means everything EXCEPT the quality threshold
            // agreed — so the counts isolate what the threshold alone costs.
            var directional = bulls >= votesNeeded || bears >= votesNeeded;
            if (!directional || adx < AdxMin) return;
            for (var k = 0; k < DiagThresholds.Length; k++)
                if (quality >= DiagThresholds[k]) _wouldSignal[k]++;
        }

        private void PrintDaySummary()
        {
            var parts = new List<string>();
            for (var k = 0; k < DiagThresholds.Length; k++)
                parts.Add(string.Format("{0:F2}:{1}", DiagThresholds[k], _wouldSignal[k]));
            Print("DAY SUMMARY {0:yyyy-MM-dd} | {1} bars | best quality {2:F2} " +
                  "(threshold {3:F2}) | {4} trades opened",
                  _statsDay, _barsToday, _bestQualityToday, EfficiencyMin, _tradesToday);
            Print("   signals available at each threshold -> {0}",
                  string.Join("  ", parts));

            if (_qualitiesToday.Count >= 8)
            {
                var sorted = _qualitiesToday.ToList();
                sorted.Sort();
                var med = sorted[sorted.Count / 2];
                var p75 = sorted[sorted.Count * 3 / 4];
                Print("   trend quality distribution today: median {0:F2}, 75th {1:F2}, best {2:F2}",
                      med, p75, _bestQualityToday);
                // The single number that says whether this strategy can work
                // on real gold at all. See the note on _qualitiesToday.
                // Bands are set against the RANDOM-WALK FLOOR, not against the
                // simulators, so they mean something absolute.
                //
                // The floor has to match the measure. This median is the
                // ENSEMBLE average when the ensemble is on, whose chance value
                // is 0.131, not the 0.124 of a lone 48-bar window — comparing
                // the two was reporting a below-chance tape as a trending one.
                var floor = RandomWalkFloor();
                var excess = med - floor;
                Print("   -> that is {0:+0.000;-0.000} versus the random-walk floor of {1:F3} " +
                      "(what pure chance produces on this same measure).", excess, floor);
                // ImpliedHurst was calibrated on a single 48-bar window, so an
                // ensemble median has to be converted back to 48-bar terms
                // before it is looked up, or the reading comes out too high.
                var med48 = floor > 0 ? med * (0.124 / floor) : med;
                Print("   -> implied Hurst exponent ~{0:F2}. (0.50 = random walk, nothing to " +
                      "trade; below 0.50 = mean-reverting; this bot's simulators assumed 0.59-0.62.)",
                      ImpliedHurst(med48));
                if (excess >= 0.030)
                    Print("      GOOD: gold is genuinely trending, as much as the markets this " +
                          "was certified on (+0.041 and +0.061). The tested edge should carry " +
                          "over and the regime switch should be running the TREND side.");
                else if (excess >= 0.012)
                    Print("      THIN: trending, but less than either certified market. Expect a " +
                          "smaller edge than the backtest showed. Collect more days before " +
                          "changing anything — this is the band where both sides are near zero.");
                else if (excess >= -0.005)
                    Print("      FLAT: at {0:F3} above chance this tape is a random walk. Nothing " +
                          "works well on one — measured edge was +0.016 to +0.068 either way. Do " +
                          "NOT loosen the filter to force trades; in testing that raised drawdown " +
                          "without adding any edge.", excess);
                else
                    Print("      WRONG SIDE for trend-following: at {0:F3} this tape is " +
                          "MEAN-REVERTING, where the trend side does not merely go quiet — it " +
                          "LOSES money (-0.265 at H=0.40 in testing) while fading earns +0.259. " +
                          "The regime switch should have handed the day to the FADE side; check " +
                          "the REGIME line above says MEAN-REVERTING. Do not lower the trend " +
                          "threshold to compensate.", excess);
            }

            if (_regimeKnown)
                Print("   regime at close: {0} (rolling median {1:F3} vs floor {2:F3}) — {3} side was active",
                      _regimeTrending ? "TRENDING" : "MEAN-REVERTING", _regimeMedian,
                      RandomWalkFloor(),
                      !UseRegimeSwitch ? "both" : (_regimeTrending ? "TREND" : "FADE"));

            if (_tradesToday == 0)
                Print("   NO TRADES: neither side found a setup. The line above shows what the " +
                      "trend threshold alone would have allowed; if the regime reads " +
                      "MEAN-REVERTING then the trend side was held back on purpose and the fade " +
                      "side simply saw no RSI extreme below quality {0:F2}.", ChopMax);
        }

        // Median efficiency -> Hurst exponent, from fractional-Brownian-motion
        // paths generated at known H and measured with this same 48-bar window
        // (volatility held equal). H is the standard measure of whether a
        // series trends more than chance, which makes the reading comparable
        // to published research instead of only to this project's simulators.
        private static readonly double[] EffTable = { 0.088, 0.103, 0.121, 0.144, 0.172, 0.208 };
        private static readonly double[] HurstTable = { 0.40, 0.45, 0.50, 0.55, 0.60, 0.65 };

        private static double ImpliedHurst(double medianEfficiency)
        {
            if (medianEfficiency <= EffTable[0]) return HurstTable[0];
            var last = EffTable.Length - 1;
            if (medianEfficiency >= EffTable[last]) return HurstTable[last];
            for (var k = 0; k < last; k++)
            {
                if (medianEfficiency > EffTable[k + 1]) continue;
                var span = EffTable[k + 1] - EffTable[k];
                var frac = span > 0 ? (medianEfficiency - EffTable[k]) / span : 0.0;
                return HurstTable[k] + frac * (HurstTable[k + 1] - HurstTable[k]);
            }
            return HurstTable[last];
        }

        private IEnumerable<Position> OwnPositions()
        {
            return Positions.Where(p => p.Label == Label && p.SymbolName == SymbolName);
        }

        private double DayStartEquity()
        {
            var midnight = Server.TimeInUtc.Date;
            var realizedToday = History
                .Where(t => t.Label == Label && t.ClosingTime >= midnight)
                .Sum(t => t.NetProfit);
            return Account.Equity - realizedToday;
        }

        protected override void OnStop()
        {
            // Print the day's diagnostics on the way out too — otherwise a bot
            // stopped before midnight UTC never reports the session at all.
            if (_statsDay != DateTime.MinValue && _barsToday > 0)
                PrintDaySummary();
            Print("GoldEdgeNews stopped. Open positions keep their broker-side SL/TP.");
        }
    }
}
