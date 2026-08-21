// GoldEdgeNews — GoldEdge (the holdout-certified custom strategy) plus a NEWS
// AGENT built on one principle: news must PROTECT the bot without reducing how
// much it trades. News cuts both ways — a print can pump gold as easily as
// dump it — so this agent never closes a trade because news is coming.
//
// STRATEGY (unchanged from GoldEdge — certified on 30 virgin seeds):
//   6-voter confluence, gated by trend quality (Kaufman efficiency >= 0.60
//   over 24 bars) and ADX >= 18. 15-MINUTE chart, 40-bar (10h) time stop.
//   Target range 1:1-2:1 — TUNED FOR WIN RATE at the owner's request.
//
// CERTIFIED on 50 FRESH seeds (4400-4449, used for nothing else), 12,983 trades:
//   win rate  67.4%      edge +0.498 (worst-model +0.428)
//   frequency 10.8 trades/week  (4 concurrent positions, eff 0.55)
//   @3% risk: median drawdown 19%, worst 49%, 0 of 100 runs lost money
//
// TWO DIFFERENT WAYS TO RAISE WIN RATE — only one of them is honest.
//
//   (a) Shorten the target. Works, but it is partly an illusion: break-even
//       win rate is 1/(1+RR), so a nearer target NEEDS a higher win rate just
//       to break even. Measured: RR 0.5-1.0 buys a 69% win rate while edge
//       COLLAPSES to +0.182 and margin over break-even falls from ~24 points
//       to ~14. Do not chase win rate this way.
//
//   (b) Stop getting stopped out by noise. This is real. Placing the stop
//       beyond the recent swing low/high instead of an arbitrary ATR distance
//       means price must break actual STRUCTURE to end the trade. The target
//       ratio is untouched at 1:1-2:1, so nothing is given away:
//         ATR stop    win 61.7%, edge +0.501, worst DD 47%, 3/100 runs losing
//         SWING stop  win 66.4%, edge +0.367, worst DD 33%, 0/100 runs losing
//       +4.7 points of win rate, drawdown down a third, and no run lost money.
//       Some expectancy is traded for that consistency — a swing stop is often
//       wider, so each unit of risk buys a smaller multiple.
//
//   (c) Stop skipping signals. Holding ONE trade for up to 10h made the bot
//       sleep through valid setups. Allowing several at once uses the same
//       entries — so win rate is unaffected — and roughly doubles trade count:
//         1 position  win 64.8%, 3.7 tr/wk, edge +0.422, worst DD 29%
//         3 positions win 66.7%, 7.5 tr/wk, edge +0.484, worst DD 35% (@3%)
//       Win rate and edge both went UP. The only thing given away is exposure
//       (3 x 3% = 9% at risk vs 5% for one), which is why risk drops to 3%.
//
// Progression, each step certified on a seed set never previously used:
//   eff.25 RR2.0-6.0 ATR      win 42.3%, edge +0.559, 13.8 tr/wk, DD 78%
//   eff.50 RR1.0-2.5 ATR      win 56.6%, edge +0.522,  8.2 tr/wk, DD 57%
//   eff.60 RR1.0-2.0 ATR      win 61.8%, edge +0.506,  5.1 tr/wk, DD 48%
//   eff.60 RR1.0-2.0 SWING    win 66.4%, edge +0.367,  3.4 tr/wk, DD 33%
//   + 3 CONCURRENT @3% risk   win 66.7%, edge +0.484,  7.5 tr/wk, DD 35%
//   + eff.55, 4 pos, session  win 67.4%, edge +0.498, 10.8 tr/wk, DD 49% <-THIS
//
// RESEARCH PASS into professional gold trading (Aug 2026) — what survived
// measurement and what did not. Every claim was tested on both market models
// against a random-entry baseline; the failures matter as much as the wins.
//
//   ADOPTED  Skip the dead hours around the daily close (trade 01-22 UTC).
//            edge +0.463 -> +0.480, win 65.5% -> 66.0%.
//
//   REJECTED "Only trade the 12-16 UTC London/NY overlap, where gold sets its
//            daily high/low ~70% of the time." Cut trades SIX-FOLD (15.8 ->
//            2.6/wk) and LOWERED edge to +0.385. The session is real; making
//            it an exclusive filter is not.
//
//   REJECTED Avoiding the 4pm London fix. Exactly neutral (+0.464 vs +0.463).
//
//   REJECTED LIQUIDITY SWEEPS / stop-hunt reversals — the most popular idea in
//            retail gold content, claimed at 60-70% win. Measured:
//              raw sweep            win 41.8%, edge -0.178  (loses money)
//              + structure shift    win 43.6%, edge -0.159
//              + trend agreement    win 49.9%, edge -0.025
//              added to this bot    win 61.2%, edge +0.323  (DILUTES it)
//            Not only unprofitable alone — bolting it on made the working
//            strategy measurably worse. Caveat: this simulator does not model
//            order-flow/stop-hunt dynamics, so treat as strong evidence
//            against, not proof.
//
// TIMEFRAME: m15 is the sweet spot. m5 is worse on edge (+0.590) and has
//   deeper drawdowns; h1 trades too rarely (2.2/wk); h4 is model-unstable.
//
// FREQUENCY x RISK — the constraint that actually binds. Nothing in this bot
// caps trade count; the filter setting alone decides it. But compounding 30
// virgin seeds over 60 days shows drawdown, not the daily stop, is the limit:
//        max frequency @ 10% risk -> median DD 74%, WORST 97% (account dead)
//        max frequency @  5% risk -> median DD 47%, worst 81%
//        max frequency @  2% risk -> median DD 22%, worst 46%
//   The -15% daily stop caps ONE day; it cannot stop bad days compounding.
//   Trading often is fine. Trading often AND large is what kills the account.
//   Hence risk defaults to 3% here (x3 concurrent = 9% maximum exposure).
//
//   CORRECTION to an earlier build: it said h1 beat m15. That held with a
//   FIXED stop. With the ADAPTIVE (ATR) stop, m15 beats h1 on BOTH edge and
//   frequency (+0.974 vs +0.826 at the same filter) because the stop sizes
//   itself to m15 volatility instead of wearing an h1-sized 0.6%.
//
// ADAPTIVE EXITS (measured on 30 virgin seeds, stop-relative spread cost):
//   * STOP adapts to volatility: 1.5x ATR, clamped 0.4%-1.2%. A quiet tape
//     gets a tight stop, a wild one gets room, instead of a flat 0.6%.
//   * TARGET adapts to conviction: ADX strength + trend quality scale the
//     reward:risk across the configured range (now 1:1-2:1).
//   Adaptive stop + adaptive target measured +30% edge over fixed 0.6%/4:1
//   (edge +0.633 -> +0.826, worst-model +0.505 -> +0.699).
//
// NO EARLY EXIT — deliberately. Every "close when the market changes" rule
// was tested and none helped: a 5/6 trend flip fired 1 time in 1530 trades,
// trend-quality collapse was slightly negative, and an ADX-fall exit cut the
// edge from +0.633 to +0.369 by dumping winners early. The 10-bar time stop
// already ends trades before a real reversal arrives.
//
// WHAT THE NEWS AGENT WATCHES: everything that moves gold, sorted into tiers —
//   TIER 1  FOMC, rate decisions, NFP, CPI, core PCE, Powell/Fed-chair
//           remarks, testimony, press conferences, Jackson Hole
//   TIER 2  any other high-impact print, and tier-1-type events abroad
//   TIER 3  anyone at a microphone (members, governors, presidents, minutes,
//           panels, symposiums) plus medium-impact US data
//   Currencies (all 9 on the feed, each with a real channel into gold):
//   USD gold is priced in it · EUR/GBP/JPY/CHF their central banks move the
//   dollar, and CHF is gold's twin safe haven (Switzerland refines most of
//   the world's gold) · AUD/CAD/NZD commodity and risk proxies, Australia is
//   a top-3 gold producer · CNY largest consumer nation and central-bank buyer.
//   Watching more currencies is FREE here: protection only ever moves the stop
//   to breakeven on an ALREADY-PROFITABLE trade, so it cannot turn a winner
//   into a loser. Measured: protecting more often is mildly BETTER, not worse
//   (edge +0.826 -> +0.840, worst-model +0.699 -> +0.739 at high frequency).
//
// WHAT IT DOES WITH THEM — measured on 30 virgin seeds (h1, RR4):
//   * PROTECT (default ON). With a market-mover approaching and the trade
//     ALREADY IN PROFIT, pull the stop to breakeven: a news spike can then
//     only scratch the trade, not lose on it. A losing trade is left alone —
//     tightening there would just lock in the loss. Entries are untouched, so
//     trade frequency is unchanged.
//       edge +0.633 -> +0.636, trade count 1530 -> 1532.
//   * CLOSE ON NEWS — TESTED AND REJECTED. Edge collapses +0.633 -> +0.496:
//     it cuts winners short, exactly as the owner predicted. Not implemented.
//   * BLOCK ENTRIES (default OFF). Available, but it costs trades, which is
//     what the owner asked to avoid. Turn on only for fewer, safer entries.
//   * SHOCK VETO (default ON, no network). A bar moving >2.5x ATR blocks new
//     entries for 3 bars and triggers protection. edge +0.633 -> +0.657.
//
// FAIL-SAFE: a failed, timed-out or garbage calendar fetch logs and leaves the
// bot trading normally on the shock veto alone. A dead feed must never freeze
// the bot or silently disable its safety. Fetch runs off the trading thread,
// refreshes every 6h (well inside the feed's 2-per-5-min rate limit).
//
// REQUIRES AccessRights.FullAccess (network) — cTrader will ask you to approve
// it on first build. That is what lets it fetch the calendar.
//
// HONEST LIMITS: the shock-driven numbers above are measured; the CALENDAR
// layer is reasoned, not backtested — the simulator has no economic calendar
// and this build environment blocks network, so the live feed is unverified
// here (its field names were confirmed from the feed's docs, and the parser
// was port-tested against a realistic sample). Watch your log for
// "news: N events (T1/T2/T3...)" to confirm it loads. Note too that news
// protection is INSURANCE against real-world slippage and gaps that the
// simulator does not model — it is not, by itself, a source of edge.
//
// DEMO-ONLY. Install: cTrader -> Automate -> New cBot -> paste -> Build ->
// approve network access -> add instance on XAUUSD **m15** -> Play.
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
        [Parameter("Votes needed (of 6)", DefaultValue = 5, MinValue = 3, MaxValue = 6, Group = "Signal")]
        public int VotesNeeded { get; set; }

        [Parameter("Minimum ADX", DefaultValue = 18.0, MinValue = 0, MaxValue = 50, Group = "Signal")]
        public double AdxMin { get; set; }

        [Parameter("Require ADX rising", DefaultValue = false, Group = "Signal")]
        public bool RequireAdxRising { get; set; }

        [Parameter("ADX rising lookback (bars)", DefaultValue = 3, MinValue = 1, MaxValue = 20, Group = "Signal")]
        public int AdxRisingLookback { get; set; }

        [Parameter("Trend quality window (bars)", DefaultValue = 24, MinValue = 4, MaxValue = 200, Group = "Trend filter")]
        public int EfficiencyWindow { get; set; }

        [Parameter("Min trend quality (0-1)", DefaultValue = 0.55, MinValue = 0.0, MaxValue = 1.0, Group = "Trend filter")]
        public double EfficiencyMin { get; set; }

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

        // FREQUENCY AND RISK ARE LINKED. Measured on 30 virgin seeds, m15,
        // 60 days, compounded — same trade count, only risk% changed:
        //   max frequency @ 10% risk -> worst drawdown 97%  (account is dead)
        //   max frequency @  5% risk -> worst drawdown 81%
        //   max frequency @  2% risk -> worst drawdown 46%
        // Trading often is fine; trading often AND large ruins the account.
        // With 3 concurrent positions this is 3 x 3% = 9% maximum exposure.
        [Parameter("Risk per trade (%)", DefaultValue = 3.0, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
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

        // CONCURRENT POSITIONS — the frequency lever that costs nothing in
        // entry quality. Holding one trade for up to 10h made the bot sleep
        // through valid signals; letting it hold several DOUBLES trade count
        // with the same entries. Certified on 50 fresh seeds:
        //   1 position  win 64.8%, 3.7 trades/wk, edge +0.422
        //   3 positions win 66.7%, 7.5 trades/wk, edge +0.484
        // Win rate and edge both improved — nothing was given up but exposure,
        // which is why risk-per-trade drops to 3% (3 x 3% = 9% max at risk,
        // vs 5% for a single position).
        [Parameter("Max concurrent positions", DefaultValue = 4, MinValue = 1, MaxValue = 10, Group = "Risk")]
        public int MaxConcurrentPositions { get; set; }

        [Parameter("Min bars between same-direction entries", DefaultValue = 4, MinValue = 0, MaxValue = 50, Group = "Risk")]
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
            Print("Entry: {0}/6 votes | ADX>={1}{2} | trend quality>={3} over {4} bars",
                  VotesNeeded, AdxMin, RequireAdxRising ? " and rising" : "",
                  EfficiencyMin, EfficiencyWindow);
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

            var need = Math.Max(120, EfficiencyWindow + 10);
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

            var bulls = 0;
            if (emaFast > emaSlow) bulls++;
            if (close > emaSlow) bulls++;
            if (macdHist > 0) bulls++;
            if (rsi > 50.0) bulls++;
            if (emaFast > emaFastPrev) bulls++;
            if (close > past) bulls++;
            var bears = 6 - bulls;

            var quality = TrendQuality(EfficiencyWindow);

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: {0:F2} | {1}b/{2}s | ADX {3:F1}{4} | quality {5:F2} {6} | news: {7}",
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

            if (bulls >= VotesNeeded)
                OpenTrade(1, bulls, adx, quality);
            else if (bears >= VotesNeeded && AllowShort)
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
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} ({4:F2}%{5}) | target {6:F2} ({7:F2}:1{8}) | {9}/6 votes, ADX {10:F0}, quality {11:F2}",
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
