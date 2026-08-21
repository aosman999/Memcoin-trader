// GoldEdgeNews — gold (XAU/USD) strategy + news agent. DEMO-ONLY, XAUUSD m15.
//
// ============================ HOW IT TRADES ============================
// ENTRY  3 voters (ema20>ema75, rsi14>50, close>close[-20]) must ALL agree,
//        only when trend quality >= 0.45 and ADX >= 18, during 01-22 UTC.
//        Trend quality = Kaufman efficiency (net move / path travelled),
//        AVERAGED over 24/36/48/60/72 bars rather than read from one window.
// STOP   just beyond the recent 12-bar swing low/high (+5% buffer), clamped
//        0.4-1.4% of price. Binary — no trailing, no early exit.
// TARGET adaptive 1.0-2.0 reward:risk, scaled by conviction.
// SIZE   1.0% risk per trade, up to 14 at once = 14% maximum exposure.
//        -15% daily loss stop. 40-bar (10h) time stop.
// NEWS   Forex Factory calendar, 9 currencies + oil. Moves the stop to
//        breakeven before an event when ALREADY in profit, and vetoes entries
//        during price shocks. It NEVER closes a trade: news pumps gold as
//        often as it dumps it, and closing on news measured clearly worse.
//
// ========================= CERTIFIED PERFORMANCE ========================
// 50 virgin seeds (3500-3549), two market models, 7,292 trades. SIMULATED.
//   win rate   70.3%
//   frequency  28.7 trades/week
//   edge       +0.572 over a random-entry baseline (worst model +0.481)
//   drawdown   median 8%, worst 30%, at 1.0% risk
//
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
// THE THRESHOLD IS THE FREQUENCY DIAL, and looser is not automatically worse:
//        eff >= 0.50   11.4 tr/wk   edge +0.648
//        eff >= 0.45   18.6 tr/wk   edge +0.589
//        eff >= 0.40   28.2 tr/wk   edge +0.534
//        eff >= 0.30   56.2 tr/wk   edge +0.429
//   Edge decays gracefully while trade count multiplies. Below about +0.3R the
//   cushion stops reliably covering real-world slippage — that is the floor,
//   not the win rate.
//
// MORE TRADES AT A SMALLER SIZE BEATS FEWER AT A LARGER ONE. Holding drawdown
// fixed and buying frequency with the threshold while paying for it with risk:
//        eff.50 gap2 @1.50%   11.2 tr/wk   worst DD 31%   growth x1.24
//        eff.45 gap1 @1.00%   28.7 tr/wk   worst DD 30%   growth x1.46
//   Same drawdown, 2.6x the trades, more growth, and lower total exposure
//   (14 x 1.0% = 14%, versus 10 x 1.5% = 15%).
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
//   win rate, cut money), trailing stops, EMA200 alignment, mean reversion,
//   and closing positions on news. See docs/PERFORMANCE.md for the full list —
//   checking it first saves re-discovering the same losers.
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

        [Parameter("ADX rising lookback (bars)", DefaultValue = 3, MinValue = 1, MaxValue = 20, Group = "Signal")]
        public int AdxRisingLookback { get; set; }

        // Only used when the ensemble below is switched OFF. A longer window
        // judges trend quality over a fuller stretch; 12 bars is too short to
        // tell a real trend from a wiggle. Prefer the ensemble — picking one
        // window is exactly the mistake it exists to avoid.
        [Parameter("Trend quality window (bars)", DefaultValue = 48, MinValue = 4, MaxValue = 200, Group = "Trend filter")]
        public int EfficiencyWindow { get; set; }

        [Parameter("Min trend quality (0-1)", DefaultValue = 0.45, MinValue = 0.0, MaxValue = 1.0, Group = "Trend filter")]
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
        [Parameter("Adaptive target (conviction-scaled)", DefaultValue = true, Group = "Exits")]
        public bool AdaptiveTarget { get; set; }

        [Parameter("Adaptive target: MIN reward:risk", DefaultValue = 1.0, MinValue = 0.5, MaxValue = 10.0, Group = "Exits")]
        public double MinRewardRisk { get; set; }

        [Parameter("Adaptive target: MAX reward:risk", DefaultValue = 2.0, MinValue = 0.5, MaxValue = 20.0, Group = "Exits")]
        public double MaxRewardRisk { get; set; }

        [Parameter("Reward:risk — used when adaptive target is OFF", DefaultValue = 4.0, MinValue = 0.5, MaxValue = 10.0, Group = "Exits")]
        public double RewardRisk { get; set; }

        [Parameter("Max hold (bars)", DefaultValue = 40, MinValue = 1, MaxValue = 400, Group = "Exits")]
        public int MaxHoldBars { get; set; }

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
        [Parameter("Max concurrent positions", DefaultValue = 14, MinValue = 1, MaxValue = 20, Group = "Risk")]
        public int MaxConcurrentPositions { get; set; }

        [Parameter("Min bars between same-direction entries", DefaultValue = 1, MinValue = 0, MaxValue = 50, Group = "Risk")]
        public int MinBarsBetweenSameSide { get; set; }

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

        [Parameter("Log status every N bars", DefaultValue = 4, MinValue = 0, Group = "Diagnostics")]
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
        private DateTime _lastProtectCheck = DateTime.MinValue;

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
            Print("Entry: {0} | ADX>={1}{2} | trend quality>={3} over {4} bars | up to {5} positions",
                  UseSimpleVoters ? "3/3 votes (simplified)" : VotesNeeded + "/6 votes",
                  AdxMin, RequireAdxRising ? " and rising" : "",
                  EfficiencyMin,
                  UseEnsembleQuality ? "24/36/48/60/72 (ensemble avg)"
                                     : EfficiencyWindow.ToString(),
                  MaxConcurrentPositions);
            Print("Exit: stop {0} | target {1} | max hold {2} bars | risk {3}%",
                  UseSwingStop
                    ? string.Format("SWING structure ({0}-bar, +{1}% buffer, clamped {2}-{3}%)",
                                    SwingLookback, SwingBufferPercent, MinStopPercent, MaxStopPercent)
                    : AdaptiveStop
                      ? string.Format("ADAPTIVE {0}x ATR clamped {1}-{2}%", StopAtrMult, MinStopPercent, MaxStopPercent)
                      : string.Format("fixed {0}%", StopPercent),
                  AdaptiveTarget
                    ? string.Format("ADAPTIVE {0}:1-{1}:1 by conviction", MinRewardRisk, MaxRewardRisk)
                    : string.Format("fixed {0}:1", RewardRisk),
                  MaxHoldBars, RiskPercent);
            Print("No early exit: every 'exit when the market changes' rule tested was neutral or harmful (ADX-fall exit cut edge +0.633 -> +0.369).");
            Print("News agent: calendar {0} watching {1} | protect-open-trade {2} ({3} min before) | block-entries {4} | shock veto {5}",
                  UseCalendar ? "ON" : "OFF", WatchCurrencies,
                  ProtectOnNews ? "ON" : "OFF", ProtectBeforeMinutes,
                  BlockEntriesOnNews ? "ON" : "OFF", UseShockVeto ? "ON" : "OFF");
            Print("News policy: never closes a trade on news (measured worse) — protects it instead.");
            if (Bars.TimeFrame != TimeFrame.Minute15)
                Print("NOTE: certified on the 15-MINUTE chart; you are on {0}. Settings assume m15.",
                      Bars.TimeFrame);

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
            if (_stopped || !ProtectOnNews)
                return;
            var now = Server.TimeInUtc;
            if ((now - _lastProtectCheck).TotalSeconds < 60)
                return;
            _lastProtectCheck = now;
            try { ProtectPositions(); }
            catch (Exception ex) { Print("ERROR in news protect: {0} — {1}", ex.GetType().Name, ex.Message); }
        }

        private void Evaluate()
        {
            _barCount++;

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
                if ((Server.TimeInUtc - pos.EntryTime).TotalMinutes >= MaxHoldBars * BarMinutes())
                {
                    Print("Closing {0} — max hold {1} bars reached.", pos.Id, MaxHoldBars);
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

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: {0:F2} | {1}/{2} votes | ADX {3:F1}{4} | quality {5:F2} {6} | news: {7}",
                      close, bulls, bears, adx, adx > adxPrev ? "+" : "-", quality,
                      quality >= EfficiencyMin ? "TREND" : "chop", NewsStatusLine());

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

            // ---- the certified strategy gates ---------------------------
            if (adx < AdxMin)
                return;
            if (RequireAdxRising && adx <= adxPrev)
                return;
            if (quality < EfficiencyMin)
                return;

            if (bulls >= votesNeeded)
                OpenTrade(1, bulls, adx, quality);
            else if (bears >= votesNeeded && AllowShort)
                OpenTrade(-1, bears, adx, quality);
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

        private double BarMinutes()
        {
            var tf = Bars.TimeFrame;
            if (tf == TimeFrame.Minute) return 1;
            if (tf == TimeFrame.Minute5) return 5;
            if (tf == TimeFrame.Minute15) return 15;
            if (tf == TimeFrame.Minute30) return 30;
            if (tf == TimeFrame.Hour) return 60;
            if (tf == TimeFrame.Hour4) return 240;
            if (tf == TimeFrame.Daily) return 1440;
            return 60;
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
            if (!UseEnsembleQuality) return TrendQuality(EfficiencyWindow);
            double sum = 0.0;
            var count = 0;
            foreach (var w in EnsembleWindows)
            {
                if (Bars.ClosePrices.Count <= w + 1) continue;
                sum += TrendQuality(w);
                count++;
            }
            // Fall back to the single window rather than reporting 0.0 (which
            // would silently gate every trade off) if history is still short.
            return count > 0 ? sum / count : TrendQuality(EfficiencyWindow);
        }

        private double TrendQuality(int window)
        {
            var c = Bars.ClosePrices;
            var n = Math.Min(window, c.Count - 1);
            if (n < 2) return 0.0;
            var net = Math.Abs(c.Last(0) - c.Last(n));
            double path = 0.0;
            for (var i = 0; i < n; i++)
                path += Math.Abs(c.Last(i) - c.Last(i + 1));
            return path > 0 ? net / path : 0.0;
        }

        // Several positions at once is fine; several near-identical ones on
        // consecutive bars is just one trade in disguise, sized larger. Space
        // same-direction entries out so concurrency adds diversity, not weight.
        private bool TooSoonForSameSide(int direction)
        {
            if (MinBarsBetweenSameSide <= 0) return false;
            var side = direction > 0 ? TradeType.Buy : TradeType.Sell;
            var minutes = MinBarsBetweenSameSide * BarMinutes();
            foreach (var pos in OwnPositions())
                if (pos.TradeType == side &&
                    (Server.TimeInUtc - pos.EntryTime).TotalMinutes < minutes)
                    return true;
            return false;
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
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} ({4:F2}%{5}) | target {6:F2} ({7:F2}:1{8}) | {9} votes, ADX {10:F0}, quality {11:F2}",
                      side, units, price, price - direction * stopDist,
                      stopDist / price * 100.0, UseSwingStop ? " swing" : (AdaptiveStop ? " adaptive" : ""),
                      price + direction * tpDist, rrUsed, AdaptiveTarget ? " adaptive" : "",
                      votes, adx, quality);
            else
                Print("ORDER FAILED: {0}", result.Error);
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
            Print("GoldEdgeNews stopped. Open positions keep their broker-side SL/TP.");
        }
    }
}
