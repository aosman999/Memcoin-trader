// GoldICT — gold (XAU/USD) bot built on the ICT Core Mentorship Month 4 model,
// with the news agent folded into the strategy rather than bolted beside it.
// DEMO-ONLY. Refuses to run on a live account.
//
// ========================= WHERE THE RULES COME FROM ====================
// Every entry below is encoded from the owner's own Month 4 notes, mechanically,
// with no interpretation added and no text from those notes reproduced here —
// only the geometry needed to place an order. Three models were measured; a
// fourth and a fifth were measured and REJECTED. All of it is in
// docs/PERFORMANCE.md under "ICT Month 4".
//
//   1. MARKET STRUCTURE SHIFT + MITIGATION BLOCK
//      High A, a low, a LOWER high B, then price breaks the low between them.
//      Attention moves to that broken low; the rally that made it is the
//      mitigation block. Price returning there is the short. Bullish mirrors.
//      Measured +0.090 / +0.131 / +0.099 R on mixed / M1 / M2, 63-66% win.
//
//   2. BREAKER BLOCK
//      Low A, rally to swing high X, decline that VIOLATES A, then price trades
//      back up through X. The up-close candle at X is the breaker; the return
//      to it is the buy. Bearish mirrors. The steadiest of the three:
//      +0.130 / +0.110 / +0.094 R, and it is the only one that never had more
//      than 12 of 30 runs finish red.
//
//   3. LIQUIDITY VOID / VACUUM BLOCK
//      A displacement candle (range > 1.5x ATR) leaves a three-candle gap.
//      Price returns into the gap; that return is the entry, in the direction
//      the displacement went. +0.088 / +0.088 / +0.146 R, and the widest
//      separation from its own random control of anything measured on this
//      project (+0.277 R on the first pass).
//      THE NOTES NAME NFP AS A CREATOR OF VACUUM BLOCKS. That is why the news
//      agent is inside this bot and not next to it: the event supplies the
//      displacement, and the void the event leaves is the setup.
//
//   REJECTED, and off by default with the switch left in so the finding stays
//   checkable:
//     * ORDERBLOCK (lowest down-close candle with the most open-to-close range)
//       — +0.107 R on mixed but only +0.024 on M2, where 22 of 30 runs finished
//       BELOW the $3,000 start. Positive R, negative money. Fails the project's
//       worst-model rule.
//     * LIQUIDITY POOL RAID (buy limit at/below the recent low, fixed 30-50 pip
//       stop) — -0.044 / -0.041 R on M1 / M2 with 28 and 30 of 30 runs red, and
//       its own random control BEAT it. The fixed stop the notes call ideal for
//       forex is $3-$5 on gold; at $0.92 round-trip cost that is a fifth of the
//       risk given away per trade. Not a flaw in the idea, a flaw in porting a
//       pip-denominated stop to a $4,300 instrument.
//
// NOT MODELLED, and this matters: the notes' Action Plan says to confirm every
// idea against the Interest Rate Triad (ZB/ZN/ZF) and USDX, and to PASS when
// there is no obvious indication. Nothing here does that — a single-instrument
// backtest cannot. So this is the ENTRY MODEL ONLY, and the notes are explicit
// that the entry model without the confirmation is not the method. Treat the
// numbers above as a floor, not a verdict.
//
// ============================== THE EXITS ===============================
// Not from the notes — these are this project's own, measured over months:
//   * 3 take profits per signal sharing ONE stop (a "TP tower"). Total risk is
//     exactly what one undivided position would risk; the split changes the
//     shape of the exit, never the size.
//   * a trailing stop that arms at +0.7R and follows 0.7R behind, so a
//     near-miss scratches instead of losing.
//   * the target is the 60th percentile of how far the last 100 closed trades
//     ACTUALLY got, not a ratio picked in advance. Only the furthest ladder
//     part teaches it — the near parts are closed early on purpose, and letting
//     them into the history makes the target eat its own training data.
//
// ============================== THE NEWS AGENT ==========================
// Same feeds as GoldNewsWatch — Forex Factory calendar, Google News searches,
// and the named wires (Al Jazeera, Al Arabiya, CNN, BBC, CNBC) plus a Trump
// fast lane. Inside this bot it does four things:
//   * ALERTS you, to the log and to Telegram, exactly as GoldNewsWatch does.
//   * PROTECTS open trades before a scheduled high-impact event (stop to
//     breakeven; it never closes a trade on news, which measured worse).
//   * BLOCKS new entries inside the event window.
//   * ARMS THE VACUUM WINDOW: for VacuumWindowMinutes after a strong headline
//     or a released event, void setups are accepted with a wider displacement
//     allowance, because that is the condition the notes say creates them.
//
// IT CANNOT READ X/TWITTER DIRECTLY — that needs a paid API key. It reads wire
// copy about what was said, which lands in minutes. Put any RSS bridge you buy
// into ExtraFeedUrls and it will be read like any other feed.
//
// THE DIRECTION CALL ON NEWS IS NOT BACKTESTED. There is no timestamped
// headline corpus here to test against. The lexicon is a documented convention,
// not measured evidence. The ENTRY MODELS above are measured; the news
// direction is not, and it only ever gates or widens — it never opens a trade
// on its own.
//
// Add to an XAUUSD m5 chart. Needs AccessRights.FullAccess for the feeds.
// ============================ TELEGRAM SETUP ===========================
//   1. In Telegram, message @BotFather, send /newbot, pick a name. It replies
//      with a token like 8123456789:AAF...  That token IS a credential — do not
//      paste it into a chat, a screenshot, or any file in this repo.
//   2. Send any message to your new bot. It cannot message you first.
//   3. Open https://api.telegram.org/bot<YOUR_TOKEN>/getUpdates in a browser and
//      find  "chat":{"id":123456789  — that number is your chat id.
//   4. Put the token in TelegramBotToken and the number in TelegramChatId.
//   Every trade, every news alert and the day guard then arrive on your phone.
//   The token is stripped from everything this bot prints, so the log stays
//   safe to share — see Redact().
//
// ============================== INSTALLING ==============================
//   cTrader > Automate > New, paste this file over the template, Build, then
//   add it to an XAUUSD m5 chart.
//   IMPORTANT: cTrader keeps the parameters saved on an EXISTING instance.
//   Pasting new code does not change an instance you already have running —
//   remove it from the chart and add it again, or the old settings stick.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class GoldICT : Robot
    {
        // ======================= WHICH MODELS RUN ==========================
        [Parameter("Market structure shift + mitigation block", DefaultValue = true, Group = "Models")]
        public bool UseMss { get; set; }

        [Parameter("Breaker block", DefaultValue = true, Group = "Models")]
        public bool UseBreaker { get; set; }

        [Parameter("Liquidity void / vacuum block", DefaultValue = true, Group = "Models")]
        public bool UseVoid { get; set; }

        // OFF by default and it should stay off: positive R, negative money on
        // the worse of the two market models. See the header.
        [Parameter("Orderblock (REJECTED — loses money on M2)", DefaultValue = false, Group = "Models")]
        public bool UseOrderblock { get; set; }

        // ======================= STRUCTURE ==================================
        [Parameter("Swing fractal size (bars each side)", DefaultValue = 2, MinValue = 1, MaxValue = 10, Group = "Structure")]
        public int SwingFractal { get; set; }

        [Parameter("Structure lookback (bars)", DefaultValue = 60, MinValue = 10, MaxValue = 400, Group = "Structure")]
        public int StructureLookback { get; set; }

        [Parameter("Breaker: candles either side of the swing to search", DefaultValue = 2, MinValue = 0, MaxValue = 10, Group = "Structure")]
        public int BreakerSpan { get; set; }

        [Parameter("Orderblock lookback (bars)", DefaultValue = 24, MinValue = 4, MaxValue = 200, Group = "Structure")]
        public int OrderblockLookback { get; set; }

        [Parameter("Orderblock must be validated within (bars)", DefaultValue = 12, MinValue = 1, MaxValue = 100, Group = "Structure")]
        public int OrderblockValidWithin { get; set; }

        [Parameter("Void: displacement must exceed N x ATR", DefaultValue = 1.5, MinValue = 0.5, MaxValue = 6.0, Group = "Structure")]
        public double VoidDisplacementAtr { get; set; }

        [Parameter("A setup expires if unfilled after (bars)", DefaultValue = 36, MinValue = 2, MaxValue = 400, Group = "Structure")]
        public int SetupExpiryBars { get; set; }

        // ======================= RISK ======================================
        [Parameter("Risk % of equity per signal", DefaultValue = 1.0, MinValue = 0.05, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Max concurrent SIGNALS", DefaultValue = 2, MinValue = 1, MaxValue = 20, Group = "Risk")]
        public int MaxConcurrentSignals { get; set; }

        [Parameter("Minutes between entries on the same side", DefaultValue = 15, MinValue = 0, MaxValue = 600, Group = "Risk")]
        public int GapMinutes { get; set; }

        [Parameter("Stop: MIN % of price", DefaultValue = 0.4, MinValue = 0.05, MaxValue = 5.0, Group = "Risk")]
        public double MinStopPercent { get; set; }

        [Parameter("Stop: MAX % of price", DefaultValue = 1.4, MinValue = 0.1, MaxValue = 10.0, Group = "Risk")]
        public double MaxStopPercent { get; set; }

        [Parameter("Stop buffer beyond structure (%)", DefaultValue = 5.0, MinValue = 0.0, MaxValue = 50.0, Group = "Risk")]
        public double StopBufferPercent { get; set; }

        [Parameter("Close anything still open after (minutes)", DefaultValue = 600, MinValue = 10, MaxValue = 10080, Group = "Risk")]
        public int MaxHoldMinutes { get; set; }

        [Parameter("Stop trading for the day after losing (% of equity)", DefaultValue = 15.0, MinValue = 1.0, MaxValue = 100.0, Group = "Risk")]
        public double DayGuardPercent { get; set; }

        [Parameter("Trade only between these UTC hours (from)", DefaultValue = 1, MinValue = 0, MaxValue = 23, Group = "Risk")]
        public int SessionFromHour { get; set; }

        [Parameter("Trade only between these UTC hours (to)", DefaultValue = 22, MinValue = 1, MaxValue = 24, Group = "Risk")]
        public int SessionToHour { get; set; }

        // ======================= EXITS =====================================
        [Parameter("Take profits per signal (the TP tower)", DefaultValue = 3, MinValue = 1, MaxValue = 3, Group = "Exits")]
        public int TakeProfitCount { get; set; }

        [Parameter("Nearest TP as a fraction of the full target", DefaultValue = 0.5, MinValue = 0.1, MaxValue = 1.0, Group = "Exits")]
        public double LadderNearFraction { get; set; }

        [Parameter("Furthest TP as a multiple of the full target", DefaultValue = 1.5, MinValue = 1.0, MaxValue = 4.0, Group = "Exits")]
        public double LadderFarMultiple { get; set; }

        [Parameter("Trailing stop", DefaultValue = true, Group = "Exits")]
        public bool UseTrailingStop { get; set; }

        [Parameter("Trail arms at +R", DefaultValue = 0.7, MinValue = 0.1, MaxValue = 5.0, Group = "Exits")]
        public double TrailActivateR { get; set; }

        [Parameter("Trail follows R behind", DefaultValue = 0.7, MinValue = 0.1, MaxValue = 5.0, Group = "Exits")]
        public double TrailDistanceR { get; set; }

        [Parameter("Aim at what trades actually reach", DefaultValue = true, Group = "Exits")]
        public bool UseReachTarget { get; set; }

        [Parameter("Reach percentile", DefaultValue = 60.0, MinValue = 5.0, MaxValue = 95.0, Group = "Exits")]
        public double ReachPercentile { get; set; }

        [Parameter("Closed trades needed before the reach rule speaks", DefaultValue = 30, MinValue = 5, MaxValue = 100, Group = "Exits")]
        public int ReachMinTrades { get; set; }

        [Parameter("Reach target floor (x stop)", DefaultValue = 1.0, MinValue = 0.2, MaxValue = 5.0, Group = "Exits")]
        public double ReachMinRR { get; set; }

        [Parameter("Target used before the reach rule has data (x stop)", DefaultValue = 1.5, MinValue = 0.2, MaxValue = 6.0, Group = "Exits")]
        public double ReachWarmupRR { get; set; }

        [Parameter("Target cap (x stop)", DefaultValue = 8.0, MinValue = 1.0, MaxValue = 30.0, Group = "Exits")]
        public double TargetMaxRR { get; set; }

        // ======================= NEWS AGENT ================================
        [Parameter("Run the news agent", DefaultValue = true, Group = "News")]
        public bool UseNews { get; set; }

        [Parameter("Check the wires every N minutes", DefaultValue = 3, MinValue = 1, MaxValue = 120, Group = "News")]
        public int PollMinutes { get; set; }

        [Parameter("Watch gold / XAU headlines", DefaultValue = true, Group = "News")]
        public bool FeedGold { get; set; }

        [Parameter("Watch Iran / Israel / Middle East", DefaultValue = true, Group = "News")]
        public bool FeedMideast { get; set; }

        [Parameter("Watch Trump / US foreign policy", DefaultValue = true, Group = "News")]
        public bool FeedTrump { get; set; }

        [Parameter("Watch the Fed / inflation / rates / dollar", DefaultValue = true, Group = "News")]
        public bool FeedFed { get; set; }

        [Parameter("Al Jazeera", DefaultValue = true, Group = "News")]
        public bool WireAlJazeera { get; set; }

        [Parameter("Al Arabiya", DefaultValue = true, Group = "News")]
        public bool WireAlArabiya { get; set; }

        [Parameter("CNN world", DefaultValue = true, Group = "News")]
        public bool WireCnn { get; set; }

        [Parameter("CNN money / markets", DefaultValue = true, Group = "News")]
        public bool WireCnnMoney { get; set; }

        [Parameter("BBC world", DefaultValue = true, Group = "News")]
        public bool WireBbc { get; set; }

        [Parameter("CNBC markets", DefaultValue = true, Group = "News")]
        public bool WireCnbc { get; set; }

        [Parameter("Trump / statements fast lane", DefaultValue = true, Group = "News")]
        public bool WireTrumpFast { get; set; }

        [Parameter("Extra RSS/Atom feed URLs (comma separated)", DefaultValue = "", Group = "News")]
        public string ExtraFeedUrls { get; set; }

        [Parameter("Economic calendar URL", DefaultValue = "https://nfs.faireconomy.media/ff_calendar_thisweek.json", Group = "News")]
        public string CalendarUrl { get; set; }

        [Parameter("Alert threshold (higher = fewer, stronger alerts)", DefaultValue = 3.0, MinValue = 1.0, MaxValue = 20.0, Group = "News")]
        public double AlertThreshold { get; set; }

        [Parameter("Protect open trades before an event", DefaultValue = true, Group = "News")]
        public bool ProtectOnNews { get; set; }

        [Parameter("Protect this many minutes before", DefaultValue = 15, MinValue = 1, MaxValue = 240, Group = "News")]
        public int ProtectBeforeMinutes { get; set; }

        [Parameter("Block new entries around an event", DefaultValue = true, Group = "News")]
        public bool BlockEntriesOnNews { get; set; }

        [Parameter("Block from N minutes before", DefaultValue = 15, MinValue = 0, MaxValue = 240, Group = "News")]
        public int BlockBeforeMinutes { get; set; }

        [Parameter("Block until N minutes after", DefaultValue = 10, MinValue = 0, MaxValue = 240, Group = "News")]
        public int BlockAfterMinutes { get; set; }

        // The notes name NFP as a creator of vacuum blocks. After an event or a
        // strong headline the market is exactly in the state that makes them, so
        // for a window afterwards the void model is allowed a smaller
        // displacement bar than usual. It never opens a trade by itself: a void
        // still has to form and price still has to return into it.
        [Parameter("Arm the vacuum window after news", DefaultValue = true, Group = "News")]
        public bool UseVacuumWindow { get; set; }

        [Parameter("Vacuum window length (minutes)", DefaultValue = 90, MinValue = 5, MaxValue = 600, Group = "News")]
        public int VacuumWindowMinutes { get; set; }

        [Parameter("Displacement needed inside the vacuum window (x ATR)", DefaultValue = 1.1, MinValue = 0.5, MaxValue = 6.0, Group = "News")]
        public double VacuumDisplacementAtr { get; set; }

        [Parameter("Telegram bot token (blank = off)", DefaultValue = "", Group = "Telegram")]
        public string TelegramBotToken { get; set; }

        [Parameter("Telegram chat id", DefaultValue = "", Group = "Telegram")]
        public string TelegramChatId { get; set; }

        [Parameter("Telegram: alert on every trade too", DefaultValue = true, Group = "Telegram")]
        public bool TelegramTrades { get; set; }

        [Parameter("Log label", DefaultValue = "GoldICT", Group = "Diagnostics")]
        public string Label { get; set; }

        [Parameter("Explain every setup, filled or not", DefaultValue = false, Group = "Diagnostics")]
        public bool Verbose { get; set; }

        // ======================= STATE =====================================
        private class Setup
        {
            public string Model;          // "MSS", "BREAKER", "VOID", "OB"
            public int Direction;         // +1 buy, -1 sell
            public double Entry;          // the level price must return to
            public double StopDistance;   // already clamped
            public DateTime Expires;
            public string Detail;
        }

        private AverageTrueRange _atr;
        private bool _stopped;
        private readonly List<Setup> _pending = new List<Setup>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Queue<string> _seenOrder = new Queue<string>();
        private readonly Dictionary<int, double> _peakR = new Dictionary<int, double>();
        private readonly Dictionary<int, double> _initialStopDistance = new Dictionary<int, double>();
        private readonly HashSet<int> _reachEligible = new HashSet<int>();
        private readonly List<double> _reachHistory = new List<double>();
        private readonly Dictionary<int, DateTime> _lastEntry = new Dictionary<int, DateTime>();
        private DateTime _dayStart = DateTime.MinValue;
        private double _dayStartEquity;
        private bool _dayBlocked;
        private int _tradesToday;

        // news state, written by a background task
        private readonly object _newsLock = new object();
        private List<CalEvent> _events = new List<CalEvent>();
        private readonly List<Scored> _newsPending = new List<Scored>();
        private string _feedStatus = "not polled yet";
        private DateTime _vacuumUntil = DateTime.MinValue;
        private DateTime _lastPoll = DateTime.MinValue;
        private bool _pollInFlight;
        private readonly HashSet<string> _seenHeadlines = new HashSet<string>();
        private readonly Queue<string> _seenHeadlineOrder = new Queue<string>();

        protected override void OnStart()
        {
            if (Account.IsLive)
            {
                Print("REFUSING TO RUN: live account. GoldICT is DEMO-ONLY. No order placed.");
                _stopped = true;
                Stop();
                return;
            }

            _atr = Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            _dayStart = Server.TimeInUtc.Date;
            _dayStartEquity = Account.Equity;

            Print("GoldICT started | {0} {1} | account {2} (DEMO) | equity {3:F2}",
                  SymbolName, Bars.TimeFrame, Account.Number, Account.Equity);
            Print("Models: MSS {0} | breaker {1} | void {2} | orderblock {3}",
                  UseMss ? "ON" : "off", UseBreaker ? "ON" : "off",
                  UseVoid ? "ON" : "off",
                  UseOrderblock ? "ON (REJECTED — it loses money on the worse market model)" : "off");
            Print("Measured together on 30 seeds x 3 markets, through these exits: " +
                  "+0.107 / +0.183 / +0.201 R on mixed / M1 / M2, 62-64% win, " +
                  "8 / 1 / 1 of 30 runs red. Matched random controls through the SAME " +
                  "machinery: -0.039 / +0.009 / +0.009 R, 30 / 27 / 27 of 30 red. " +
                  "SIMULATED — see docs/PERFORMANCE.md.");
            Print("Exits: {0} take profit(s) sharing one stop, {1:F2}x to {2:F2}x the target | " +
                  "trail {3} | target = {4}",
                  TakeProfitCount, LadderNearFraction, LadderFarMultiple,
                  UseTrailingStop
                    ? string.Format("arms at +{0:F1}R, follows {1:F1}R behind", TrailActivateR, TrailDistanceR)
                    : "OFF",
                  UseReachTarget
                    ? string.Format("the {0:F0}th percentile of what the last 100 trades reached " +
                                    "(needs {1}, uses {2:F1}x until then), floored {3:F1}x capped {4:F1}x",
                                    ReachPercentile, ReachMinTrades, ReachWarmupRR, ReachMinRR, TargetMaxRR)
                    : string.Format("fixed {0:F1}x the stop", ReachWarmupRR));
            Print("Risk: {0:F2}% per signal | max {1} signals ({2} positions) | day guard -{3:F0}% | " +
                  "session {4:00}:00-{5:00}:00 UTC",
                  RiskPercent, MaxConcurrentSignals, MaxConcurrentSignals * TakeProfitCount,
                  DayGuardPercent, SessionFromHour, SessionToHour);
            Print("NOT MODELLED: the notes' Interest Rate Triad / USDX confirmation step. " +
                  "This is the entry model only, and the notes say the entry model without " +
                  "the confirmation is not the method. Treat the numbers as a floor.");

            if (Bars.TimeFrame != TimeFrame.Minute5)
                Print("NOTE: every number above was measured on 5-MINUTE bars; you are on {0}. " +
                      "The structure lookbacks are in BARS, so they mean a different span here.",
                      Bars.TimeFrame);

            AuditExistingPositions();

            if (UseNews)
            {
                Print("News agent: ON | {0} | protect {1} | block entries {2} | vacuum window {3}",
                      DescribeFeeds(),
                      ProtectOnNews ? ProtectBeforeMinutes + " min before" : "off",
                      BlockEntriesOnNews ? string.Format("-{0}/+{1} min", BlockBeforeMinutes, BlockAfterMinutes) : "off",
                      UseVacuumWindow
                        ? string.Format("{0} min at {1:F1}x ATR (vs {2:F1}x normally)",
                                        VacuumWindowMinutes, VacuumDisplacementAtr, VoidDisplacementAtr)
                        : "off");
                Print("The news DIRECTION call is a documented convention, not a measured edge. " +
                      "It gates and widens; it never opens a trade on its own.");
                BeginCalendarFetch();
            }
        }

        // Positions that predate a restart still need their original stop
        // distance known, or the trail and the reach rule are blind to them.
        private void AuditExistingPositions()
        {
            foreach (var pos in OwnPositions())
            {
                if (pos.StopLoss.HasValue)
                    _initialStopDistance[pos.Id] = Math.Abs(pos.EntryPrice - pos.StopLoss.Value);
                Print("Adopting existing position {0}: {1} {2} @ {3:F2}", pos.Id,
                      pos.TradeType, pos.VolumeInUnits, pos.EntryPrice);
            }
        }

        private IEnumerable<Position> OwnPositions()
        {
            return Positions.Where(p => p.Label == Label && p.SymbolName == SymbolName);
        }

        private int OpenSignals()
        {
            var parts = Math.Max(1, TakeProfitCount);
            return (int)Math.Ceiling(OwnPositions().Count() / (double)parts);
        }

        // ======================= THE LOOP ==================================
        protected override void OnTick()
        {
            if (_stopped)
                return;
            try
            {
                TrackReach();
                ManageTrailingStops();
                CloseStaleTrades();
                if (UseNews)
                {
                    ProtectBeforeNews();
                    PollNewsIfDue();
                }
                FillPendingSetups();
            }
            catch (Exception ex)
            {
                Print("ERROR in OnTick: {0} — {1}", ex.GetType().Name, ex.Message);
            }
        }

        protected override void OnBar()
        {
            if (_stopped)
                return;
            try
            {
                RollDay();
                ExpirePending();
                FindSetups();
            }
            catch (Exception ex)
            {
                Print("ERROR in OnBar: {0} — {1}", ex.GetType().Name, ex.Message);
            }
        }

        private void RollDay()
        {
            var today = Server.TimeInUtc.Date;
            if (today == _dayStart)
                return;
            if (_tradesToday > 0 || _dayBlocked)
                Print("--- day rolled: {0} trades, equity {1:F2} (started {2:F2}){3}",
                      _tradesToday, Account.Equity, _dayStartEquity,
                      _dayBlocked ? ", day guard had fired" : "");
            _dayStart = today;
            _dayStartEquity = Account.Equity;
            _dayBlocked = false;
            _tradesToday = 0;
        }

        private bool DayGuardTripped()
        {
            if (_dayStartEquity <= 0)
                return false;
            var down = (_dayStartEquity - Account.Equity) / _dayStartEquity * 100.0;
            if (down < DayGuardPercent)
                return false;
            if (!_dayBlocked)
            {
                _dayBlocked = true;
                var msg = string.Format("DAY GUARD: down {0:F1}% today ({1:F2} -> {2:F2}). " +
                                        "No new entries until the date rolls. Open trades keep " +
                                        "their stops.", down, _dayStartEquity, Account.Equity);
                Print(msg);
                Telegram("⛔ " + msg);
            }
            return true;
        }

        private void ExpirePending()
        {
            var now = Server.TimeInUtc;
            var gone = _pending.RemoveAll(s => now >= s.Expires);
            if (gone > 0 && Verbose)
                Print("{0} setup(s) expired unfilled.", gone);
        }

        // Setups are levels price has to come BACK to. Checked on every tick,
        // not on bar close, because the return can happen and reverse inside
        // one bar.
        private void FillPendingSetups()
        {
            if (_pending.Count == 0)
                return;

            for (var k = _pending.Count - 1; k >= 0; k--)
            {
                var s = _pending[k];
                var price = s.Direction > 0 ? Symbol.Ask : Symbol.Bid;
                if (price <= 0)
                    continue;
                var reached = s.Direction > 0 ? price <= s.Entry : price >= s.Entry;
                if (!reached)
                    continue;

                // Price came back to the level. That is the trade this setup
                // was, and it is now spent either way: if a guard says no, the
                // setup is DISCARDED, not held over to fire later at a price
                // the model never chose. The measured runs behave exactly this
                // way, and holding it over would be an untested change.
                _pending.RemoveAt(k);

                DateTime lastOnSide;
                var tooSoon = _lastEntry.TryGetValue(s.Direction, out lastOnSide) &&
                              (Server.TimeInUtc - lastOnSide).TotalMinutes < GapMinutes;
                if (tooSoon || !CanEnterNow())
                {
                    if (Verbose)
                        Print("SKIP {0} {1} at {2:F2} — {3}", s.Model,
                              s.Direction > 0 ? "BUY" : "SELL", s.Entry,
                              tooSoon ? "too soon after the last entry on this side"
                                      : "no room, outside session, day guard, or a news window");
                    continue;
                }

                if (PlaceLadder(s, price) > 0)
                    _lastEntry[s.Direction] = Server.TimeInUtc;
            }
        }

        private bool CanEnterNow()
        {
            if (DayGuardTripped())
                return false;
            if (OpenSignals() >= MaxConcurrentSignals)
                return false;
            var hour = Server.TimeInUtc.Hour;
            if (hour < SessionFromHour || hour >= SessionToHour)
                return false;
            string evt;
            if (UseNews && BlockEntriesOnNews && InNewsWindow(Server.TimeInUtc, out evt))
                return false;
            return true;
        }

        private void CloseStaleTrades()
        {
            foreach (var pos in OwnPositions().ToList())
            {
                if ((Server.TimeInUtc - pos.EntryTime).TotalMinutes < MaxHoldMinutes)
                    continue;
                var r = ClosePosition(pos);
                if (r.IsSuccessful)
                    Print("TIME EXIT {0}: open {1:F0} min, closed at {2:F2} for {3:F2}",
                          pos.Id, (Server.TimeInUtc - pos.EntryTime).TotalMinutes,
                          pos.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask,
                          pos.NetProfit);
            }
        }

        // ======================= THE MODELS ================================
        // Fractal swings, confirmed SwingFractal bars ago and strictly in the
        // past. A swing that needs k bars either side is not a swing until k
        // bars after it printed, so nothing here can see the future.
        private void CollectSwings(int i, out List<int> highs, out List<int> lows)
        {
            highs = new List<int>();
            lows = new List<int>();
            var k = SwingFractal;
            var from = Math.Max(k, i - StructureLookback);
            for (var j = from; j < i - k; j++)
            {
                var isHigh = true;
                var isLow = true;
                for (var m = j - k; m <= j + k; m++)
                {
                    if (Bars.HighPrices[m] > Bars.HighPrices[j]) isHigh = false;
                    if (Bars.LowPrices[m] < Bars.LowPrices[j]) isLow = false;
                }
                if (isHigh) highs.Add(j);
                if (isLow) lows.Add(j);
            }
        }

        private double HighestSince(int from, int to)
        {
            var v = double.MinValue;
            for (var j = Math.Max(0, from); j <= to; j++)
                if (Bars.HighPrices[j] > v) v = Bars.HighPrices[j];
            return v;
        }

        private double LowestSince(int from, int to)
        {
            var v = double.MaxValue;
            for (var j = Math.Max(0, from); j <= to; j++)
                if (Bars.LowPrices[j] < v) v = Bars.LowPrices[j];
            return v;
        }

        private void FindSetups()
        {
            var i = Bars.Count - 2;                 // last CLOSED bar
            if (i < StructureLookback + SwingFractal + 5)
                return;

            List<int> highs, lows;
            CollectSwings(i, out highs, out lows);
            var close = Bars.ClosePrices[i];
            var from = i - StructureLookback;

            if (UseMss)
            {
                // bearish: high A, low X, LOWER high B, close breaks under l[X]
                if (highs.Count >= 2 && lows.Count >= 1)
                {
                    var b = highs[highs.Count - 1];
                    var a = highs[highs.Count - 2];
                    var x = LastBetween(lows, a, b);
                    if (x >= 0 && Bars.HighPrices[b] < Bars.HighPrices[a] &&
                        close < Bars.LowPrices[x] && Fresh("MSS-D", a, b, x))
                        Add("MSS", -1, Bars.LowPrices[x], Bars.HighPrices[b], LowestSince(from, i),
                            "market structure shift lower; return to the mitigation block");
                }
                // bullish mirror
                if (lows.Count >= 2 && highs.Count >= 1)
                {
                    var b = lows[lows.Count - 1];
                    var a = lows[lows.Count - 2];
                    var x = LastBetween(highs, a, b);
                    if (x >= 0 && Bars.LowPrices[b] > Bars.LowPrices[a] &&
                        close > Bars.HighPrices[x] && Fresh("MSS-U", a, b, x))
                        Add("MSS", 1, Bars.HighPrices[x], Bars.LowPrices[b], HighestSince(from, i),
                            "market structure shift higher; return to the mitigation block");
                }
            }

            if (UseBreaker)
            {
                // bullish: low A, swing high X, LOWER low B violating A, then
                // close back above h[X]. The up-close candle at X is the breaker.
                if (lows.Count >= 2 && highs.Count >= 1)
                {
                    var b = lows[lows.Count - 1];
                    var a = lows[lows.Count - 2];
                    var x = LastBetween(highs, a, b);
                    if (x >= 0 && Bars.LowPrices[b] < Bars.LowPrices[a] &&
                        close > Bars.HighPrices[x] && Fresh("BRK-U", a, b, x))
                    {
                        var k = LastUpClose(x);
                        if (k >= 0)
                            Add("BREAKER", 1, Bars.HighPrices[k], Bars.LowPrices[k],
                                HighestSince(from, i),
                                "old low taken, price back through the swing high; buying its breaker");
                    }
                }
                // bearish mirror
                if (highs.Count >= 2 && lows.Count >= 1)
                {
                    var b = highs[highs.Count - 1];
                    var a = highs[highs.Count - 2];
                    var x = LastBetween(lows, a, b);
                    if (x >= 0 && Bars.HighPrices[b] > Bars.HighPrices[a] &&
                        close < Bars.LowPrices[x] && Fresh("BRK-D", a, b, x))
                    {
                        var k = LastDownClose(x);
                        if (k >= 0)
                            Add("BREAKER", -1, Bars.LowPrices[k], Bars.HighPrices[k],
                                LowestSince(from, i),
                                "old high taken, price back through the swing low; selling its breaker");
                    }
                }
            }

            if (UseVoid && i >= 2)
            {
                var atr = _atr.Result[i];
                var need = VoidDisplacementAtr;
                var armed = UseVacuumWindow && Server.TimeInUtc <= _vacuumUntil;
                if (armed)
                    need = VacuumDisplacementAtr;
                var range = Bars.HighPrices[i] - Bars.LowPrices[i];
                if (atr > 0 && range > need * atr)
                {
                    var why = armed
                        ? "vacuum block — displacement inside the news window"
                        : "liquidity void — displacement leaves an unfilled gap";
                    if (Bars.LowPrices[i] > Bars.HighPrices[i - 2] && Fresh("VOID-U", i, 0, 0))
                        Add("VOID", 1, Bars.LowPrices[i], Bars.HighPrices[i - 2],
                            HighestSince(from, i), why);
                    else if (Bars.HighPrices[i] < Bars.LowPrices[i - 2] && Fresh("VOID-D", i, 0, 0))
                        Add("VOID", -1, Bars.HighPrices[i], Bars.LowPrices[i - 2],
                            LowestSince(from, i), why);
                }
            }

            if (UseOrderblock)
            {
                var k = FindBullOrderblock(i);
                if (k >= 0 && ValidatedUp(k, i) && close > Bars.HighPrices[k] && Fresh("OB-U", k, 0, 0))
                    Add("OB", 1, Bars.HighPrices[k], Bars.LowPrices[k], HighestSince(from, i),
                        "return to the bullish orderblock high");
                k = FindBearOrderblock(i);
                if (k >= 0 && ValidatedDown(k, i) && close < Bars.LowPrices[k] && Fresh("OB-D", k, 0, 0))
                    Add("OB", -1, Bars.LowPrices[k], Bars.HighPrices[k], LowestSince(from, i),
                        "return to the bearish orderblock low");
            }
        }

        private static int LastBetween(List<int> xs, int a, int b)
        {
            var found = -1;
            foreach (var x in xs)
                if (x > a && x < b) found = x;
            return found;
        }

        private int LastUpClose(int x)
        {
            var best = -1;
            for (var k = Math.Max(0, x - BreakerSpan); k <= Math.Min(Bars.Count - 1, x + BreakerSpan); k++)
                if (Bars.ClosePrices[k] > Bars.OpenPrices[k]) best = k;
            return best;
        }

        private int LastDownClose(int x)
        {
            var best = -1;
            for (var k = Math.Max(0, x - BreakerSpan); k <= Math.Min(Bars.Count - 1, x + BreakerSpan); k++)
                if (Bars.ClosePrices[k] < Bars.OpenPrices[k]) best = k;
            return best;
        }

        // The notes' definition: the LOWEST down-close candle, and among any
        // ties the one with the most range between open and close.
        private int FindBullOrderblock(int i)
        {
            var best = -1;
            var bestLow = double.MaxValue;
            var bestBody = -1.0;
            for (var k = Math.Max(0, i - OrderblockLookback); k < i; k++)
            {
                if (Bars.ClosePrices[k] >= Bars.OpenPrices[k]) continue;
                var body = Bars.OpenPrices[k] - Bars.ClosePrices[k];
                if (Bars.LowPrices[k] < bestLow ||
                    (Bars.LowPrices[k] == bestLow && body > bestBody))
                {
                    bestLow = Bars.LowPrices[k];
                    bestBody = body;
                    best = k;
                }
            }
            return best;
        }

        private int FindBearOrderblock(int i)
        {
            var best = -1;
            var bestHigh = double.MinValue;
            var bestBody = -1.0;
            for (var k = Math.Max(0, i - OrderblockLookback); k < i; k++)
            {
                if (Bars.ClosePrices[k] <= Bars.OpenPrices[k]) continue;
                var body = Bars.ClosePrices[k] - Bars.OpenPrices[k];
                if (Bars.HighPrices[k] > bestHigh ||
                    (Bars.HighPrices[k] == bestHigh && body > bestBody))
                {
                    bestHigh = Bars.HighPrices[k];
                    bestBody = body;
                    best = k;
                }
            }
            return best;
        }

        // "validated when the high of that candle is traded through"
        private bool ValidatedUp(int k, int i)
        {
            var to = Math.Min(i, k + OrderblockValidWithin);
            for (var j = k + 1; j <= to; j++)
                if (Bars.HighPrices[j] > Bars.HighPrices[k]) return true;
            return false;
        }

        private bool ValidatedDown(int k, int i)
        {
            var to = Math.Min(i, k + OrderblockValidWithin);
            for (var j = k + 1; j <= to; j++)
                if (Bars.LowPrices[j] < Bars.LowPrices[k]) return true;
            return false;
        }

        // One setup per structure, ever. Without this the same broken low fires
        // a fresh order on every bar it stays broken.
        private bool Fresh(string tag, int a, int b, int c)
        {
            var key = string.Format("{0}|{1}|{2}|{3}", tag, a, b, c);
            if (_seen.Contains(key))
                return false;
            _seen.Add(key);
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > 4000)
                _seen.Remove(_seenOrder.Dequeue());
            return true;
        }

        private void Add(string model, int direction, double entry, double riskLevel,
                         double liquidity, string detail)
        {
            var price = Symbol.Bid;
            if (price <= 0)
                return;
            var raw = Math.Abs(entry - riskLevel) * (1.0 + StopBufferPercent / 100.0);
            var lo = price * MinStopPercent / 100.0;
            var hi = price * MaxStopPercent / 100.0;
            var sd = raw > 0 ? Math.Max(lo, Math.Min(hi, raw)) : lo;

            _pending.Add(new Setup
            {
                Model = model,
                Direction = direction,
                Entry = entry,
                StopDistance = sd,
                Expires = Server.TimeInUtc.AddMinutes(SetupExpiryBars * BarMinutes()),
                Detail = detail
            });

            if (Verbose)
                Print("SETUP {0} {1} — wait for {2:F2}, stop {3:F2} away | {4}",
                      model, direction > 0 ? "BUY" : "SELL", entry, sd, detail);
        }

        private int BarMinutes()
        {
            if (Bars.TimeFrame == TimeFrame.Minute) return 1;
            if (Bars.TimeFrame == TimeFrame.Minute5) return 5;
            if (Bars.TimeFrame == TimeFrame.Minute15) return 15;
            if (Bars.TimeFrame == TimeFrame.Minute30) return 30;
            if (Bars.TimeFrame == TimeFrame.Hour) return 60;
            if (Bars.TimeFrame == TimeFrame.Hour4) return 240;
            if (Bars.TimeFrame == TimeFrame.Daily) return 1440;
            return 5;
        }

        // ======================= THE EXITS =================================
        // The target is not a ratio picked in advance. It is the ReachPercentile
        // percentile of how far the last 100 closed trades actually ran, in
        // stop-units. Until enough have closed there is nothing to learn from,
        // so the warm-up ratio is used — NOT a structural target, which is the
        // bug that made the old bot set take profits it never reached.
        private bool ReachTargetRatio(out double ratio)
        {
            ratio = 0.0;
            if (!UseReachTarget || _reachHistory.Count < ReachMinTrades)
                return false;
            var sorted = new List<double>(_reachHistory);
            sorted.Sort();
            var k = (int)Math.Round(ReachPercentile / 100.0 * (sorted.Count - 1));
            if (k < 0) k = 0;
            if (k > sorted.Count - 1) k = sorted.Count - 1;
            ratio = sorted[k];
            return ratio > 0;
        }

        private double TargetDistance(double stopDist, out string how)
        {
            double reach;
            if (ReachTargetRatio(out reach))
            {
                var want = stopDist * reach;
                var lo = stopDist * ReachMinRR;
                var hi = stopDist * TargetMaxRR;
                if (want <= lo) { how = "reach-floored"; return lo; }
                if (want >= hi) { how = "reach-capped"; return hi; }
                how = string.Format("reach {0:F2}x", reach);
                return want;
            }
            how = string.Format("warm-up {0:F1}x ({1}/{2} trades learned)",
                                ReachWarmupRR, _reachHistory.Count, ReachMinTrades);
            return stopDist * ReachWarmupRR;
        }

        // One signal becomes TakeProfitCount positions sharing a single stop,
        // each with its own take profit from LadderNearFraction to
        // LadderFarMultiple of the full target distance. Total risk is exactly
        // what one undivided position would risk — the split changes the shape
        // of the exit, never the size.
        private int PlaceLadder(Setup s, double price)
        {
            var side = s.Direction > 0 ? TradeType.Buy : TradeType.Sell;
            var stopDist = s.StopDistance;
            string how;
            var tpDist = TargetDistance(stopDist, out how);

            var riskUsd = Account.Equity * (RiskPercent / 100.0);
            var totalUnits = Symbol.NormalizeVolumeInUnits(riskUsd / stopDist, RoundingMode.Down);
            if (totalUnits < Symbol.VolumeInUnitsMin)
                totalUnits = Symbol.VolumeInUnitsMin;

            var wanted = Math.Max(1, Math.Min(3, TakeProfitCount));
            var affordable = (int)Math.Floor(totalUnits / Symbol.VolumeInUnitsMin);
            var parts = Math.Max(1, Math.Min(wanted, affordable));
            if (parts < wanted)
                Print("{0}: only {1} of {2} take profits fit — the whole position is {3} units " +
                      "and the broker minimum is {4}. A full TP tower needs a bigger account; " +
                      "running {1} for now.", s.Model, parts, wanted, totalUnits, Symbol.VolumeInUnitsMin);

            var each = Symbol.NormalizeVolumeInUnits(totalUnits / parts, RoundingMode.Down);
            if (each < Symbol.VolumeInUnitsMin)
                each = Symbol.VolumeInUnitsMin;

            var opened = 0;
            var levels = new List<string>();
            for (var k = 0; k < parts; k++)
            {
                var frac = parts == 1
                    ? 1.0
                    : LadderNearFraction +
                      (LadderFarMultiple - LadderNearFraction) * k / (parts - 1.0);
                var dist = tpDist * frac;
                var units = k == parts - 1 ? totalUnits - each * (parts - 1) : each;
                if (units < Symbol.VolumeInUnitsMin)
                    units = Symbol.VolumeInUnitsMin;

                var res = ExecuteMarketOrder(side, SymbolName, units, Label,
                                             stopDist / Symbol.PipSize, dist / Symbol.PipSize);
                if (!res.IsSuccessful)
                {
                    Print("{0} ORDER FAILED (TP {1} of {2}): {3}", s.Model, k + 1, parts, res.Error);
                    continue;
                }
                opened++;
                // Only the FURTHEST part teaches the reach rule. The near parts
                // are closed early on purpose; letting them into the history
                // would drag the target down every time it is used — the target
                // would be eating its own training data.
                if (k == parts - 1 && res.Position != null)
                    _reachEligible.Add(res.Position.Id);

                levels.Add(string.Format("TP{0} {1:F2}", k + 1, price + s.Direction * dist));
                Print("{0} {1} {2} units @ {3:F2} | TP {4}/{5} at {6:F2} (+{7:F2} = {8:F2}:1, {9}) | " +
                      "stop {10:F2} (-{11:F2}) | risks {12:F2} = {13:F2}% of equity | {14}",
                      s.Model, side, units, price, k + 1, parts,
                      price + s.Direction * dist, dist, dist / stopDist, how,
                      price - s.Direction * stopDist, stopDist,
                      units * stopDist, units * stopDist / Account.Equity * 100.0, s.Detail);
            }

            if (opened > 0)
            {
                _tradesToday++;
                if (TelegramTrades)
                    Telegram(string.Format(
                        "{0} {1} XAUUSD @ {2:F2}\n{3}\nstop {4:F2}\n{5}\n({6})",
                        s.Direction > 0 ? "🟢 BUY" : "🔴 SELL", s.Model, price,
                        string.Join("\n", levels), price - s.Direction * stopDist,
                        s.Detail, how));
            }
            return opened;
        }

        private double InitialStopDistance(Position pos)
        {
            double sd;
            if (_initialStopDistance.TryGetValue(pos.Id, out sd))
                return sd;
            // First sighting: the stop has not been moved yet, so its distance
            // IS the original.
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
            }
        }

        // Record how far every open position runs in our favour, and bank the
        // result when it closes. Runs unconditionally from OnTick and never
        // inside the trailing-stop block — putting it there once meant turning
        // the trail off silently stopped the bot learning anything.
        private void TrackReach()
        {
            foreach (var pos in OwnPositions().ToList())
            {
                var sd = InitialStopDistance(pos);
                if (sd <= 0)
                    continue;
                var dir = pos.TradeType == TradeType.Buy ? 1 : -1;
                var price = dir > 0 ? Symbol.Bid : Symbol.Ask;
                if (price <= 0)
                    continue;
                var r = (price - pos.EntryPrice) / sd * dir;
                double peak;
                if (!_peakR.TryGetValue(pos.Id, out peak) || r > peak)
                    _peakR[pos.Id] = r;
            }
            HarvestClosedTrades();
        }

        private void HarvestClosedTrades()
        {
            var live = new HashSet<int>(OwnPositions().Select(p => p.Id));
            foreach (var id in _peakR.Keys.Where(k => !live.Contains(k)).ToList())
            {
                var reached = _peakR[id];
                if (reached > 0 && (TakeProfitCount <= 1 || _reachEligible.Contains(id)))
                {
                    _reachHistory.Add(reached);
                    if (_reachHistory.Count > 100)
                        _reachHistory.RemoveAt(0);
                }
                _peakR.Remove(id);
                _reachEligible.Remove(id);
                _initialStopDistance.Remove(id);
            }
            foreach (var id in _initialStopDistance.Keys.Where(k => !live.Contains(k)).ToList())
                _initialStopDistance.Remove(id);
        }

        // ======================= THE NEWS AGENT ============================
        // Ported from GoldNewsWatch, which has its own test harness in
        // tools/verify/newswatch_test.cs. Same feeds, same lexicon, same
        // redaction of the Telegram token from every line this bot prints.

        public class Scored
        {
            public string Title;
            public string Source;
            public double Impact;      // how much it matters, always >= 0
            public double Direction;   // + lifts gold, - sinks gold
            public List<string> Why = new List<string>();
        }

        private class CalEvent
        {
            public DateTime UtcTime;
            public string Title;
            public string Currency;
            public int Tier;
        }

        // ---- the lexicon ---------------------------------------------------
        // Sign convention: POSITIVE lifts gold, NEGATIVE sinks it. Weight is
        // how strongly. Gold rises on fear, war, easing and a weak dollar; it
        // falls on calm, deals, tightening and a strong dollar.
        //
        // These are conventional macro relationships, not fitted values. No
        // claim is made that any particular weight is optimal — see caveat 3
        // at the top of the file.
        private static readonly KeyValuePair<string, double>[] Lexicon =
        {
            // --- escalation: gold up
            new KeyValuePair<string, double>("NUCLEAR", 3.0),
            new KeyValuePair<string, double>("AIRSTRIKE", 3.0),
            new KeyValuePair<string, double>("AIR STRIKE", 3.0),
            new KeyValuePair<string, double>("MISSILE", 2.5),
            new KeyValuePair<string, double>("STRIKE ON", 2.5),
            new KeyValuePair<string, double>("INVASION", 3.0),
            new KeyValuePair<string, double>("INVADE", 3.0),
            new KeyValuePair<string, double>("WAR", 2.5),
            new KeyValuePair<string, double>("ATTACK", 2.5),
            new KeyValuePair<string, double>("BOMB", 2.5),
            new KeyValuePair<string, double>("RETALIAT", 2.5),
            new KeyValuePair<string, double>("ESCALAT", 2.0),
            new KeyValuePair<string, double>("SANCTION", 1.5),
            new KeyValuePair<string, double>("ENRICHMENT", 2.0),
            new KeyValuePair<string, double>("HORMUZ", 3.0),
            new KeyValuePair<string, double>("TANKER", 1.5),
            new KeyValuePair<string, double>("DRONE", 1.5),
            new KeyValuePair<string, double>("CASUALT", 1.5),
            new KeyValuePair<string, double>("KILLED", 1.5),
            new KeyValuePair<string, double>("EVACUAT", 1.5),
            new KeyValuePair<string, double>("MOBILIZ", 2.0),
            new KeyValuePair<string, double>("ULTIMATUM", 2.0),
            new KeyValuePair<string, double>("THREATEN", 1.5),
            new KeyValuePair<string, double>("EMERGENCY", 1.5),
            new KeyValuePair<string, double>("CRISIS", 1.5),
            new KeyValuePair<string, double>("DEFAULT", 2.0),
            new KeyValuePair<string, double>("SHUTDOWN", 1.0),
            // --- easing money: gold up
            new KeyValuePair<string, double>("RATE CUT", 2.5),
            new KeyValuePair<string, double>("CUTS RATES", 2.5),
            new KeyValuePair<string, double>("DOVISH", 2.0),
            new KeyValuePair<string, double>("STIMULUS", 1.5),
            new KeyValuePair<string, double>("QUANTITATIVE EASING", 2.5),
            new KeyValuePair<string, double>("WEAK DOLLAR", 2.0),
            new KeyValuePair<string, double>("DOLLAR FALLS", 2.0),
            new KeyValuePair<string, double>("DOLLAR SLIDES", 2.0),
            new KeyValuePair<string, double>("RECESSION", 1.5),
            new KeyValuePair<string, double>("SAFE HAVEN", 2.0),
            new KeyValuePair<string, double>("SAFE-HAVEN", 2.0),
            new KeyValuePair<string, double>("GOLD RECORD", 2.0),
            new KeyValuePair<string, double>("RECORD HIGH", 1.5),
            // --- de-escalation: gold down
            new KeyValuePair<string, double>("CEASEFIRE", -3.0),
            new KeyValuePair<string, double>("CEASE-FIRE", -3.0),
            new KeyValuePair<string, double>("TRUCE", -2.5),
            new KeyValuePair<string, double>("PEACE DEAL", -3.0),
            new KeyValuePair<string, double>("PEACE TALKS", -2.0),
            new KeyValuePair<string, double>("DE-ESCALAT", -2.5),
            new KeyValuePair<string, double>("DEESCALAT", -2.5),
            new KeyValuePair<string, double>("AGREEMENT REACHED", -2.0),
            new KeyValuePair<string, double>("DIPLOMA", -1.5),
            new KeyValuePair<string, double>("NEGOTIAT", -1.0),
            new KeyValuePair<string, double>("SANCTIONS LIFTED", -2.5),
            new KeyValuePair<string, double>("WITHDRAW TROOPS", -2.0),
            // --- tightening money / strong dollar: gold down
            new KeyValuePair<string, double>("RATE HIKE", -2.5),
            new KeyValuePair<string, double>("RAISES RATES", -2.5),
            new KeyValuePair<string, double>("HAWKISH", -2.0),
            new KeyValuePair<string, double>("HIGHER FOR LONGER", -2.0),
            new KeyValuePair<string, double>("STRONG DOLLAR", -2.0),
            new KeyValuePair<string, double>("DOLLAR RISES", -2.0),
            new KeyValuePair<string, double>("DOLLAR SURGES", -2.0),
            new KeyValuePair<string, double>("YIELDS RISE", -1.5),
            new KeyValuePair<string, double>("HOT INFLATION", -1.5),
            new KeyValuePair<string, double>("STRONG JOBS", -1.5),
            new KeyValuePair<string, double>("BEATS EXPECTATIONS", -1.0),
            // --- added at the owner's direction: "anything that can make gold
            // dump or pump". These are the other well-known gold drivers.
            new KeyValuePair<string, double>("ASSASSINAT", 3.0),
            new KeyValuePair<string, double>("COUP", 2.5),
            new KeyValuePair<string, double>("TERROR", 2.0),
            new KeyValuePair<string, double>("MARTIAL LAW", 2.5),
            new KeyValuePair<string, double>("DEBT CEILING", 2.0),
            new KeyValuePair<string, double>("CREDIT RATING", 1.5),
            new KeyValuePair<string, double>("DOWNGRADE", 1.5),
            new KeyValuePair<string, double>("BANK RUN", 2.5),
            new KeyValuePair<string, double>("BANK COLLAPSE", 2.5),
            new KeyValuePair<string, double>("BAILOUT", 1.5),
            new KeyValuePair<string, double>("TARIFF", 2.0),
            new KeyValuePair<string, double>("TRADE WAR", 2.5),
            new KeyValuePair<string, double>("CENTRAL BANKS BUY", 2.5),
            new KeyValuePair<string, double>("GOLD RESERVES", 1.5),
            new KeyValuePair<string, double>("DE-DOLLAR", 2.0),
            new KeyValuePair<string, double>("BRICS", 1.0),
            new KeyValuePair<string, double>("BLOCKADE", 2.5),
            new KeyValuePair<string, double>("OIL SPIKE", 1.5),
            new KeyValuePair<string, double>("OIL SURGES", 1.5),
            new KeyValuePair<string, double>("PROXY", 1.0),
            new KeyValuePair<string, double>("UKRAINE", 1.0),
            new KeyValuePair<string, double>("TAIWAN", 1.5),
            // and the other side of them
            new KeyValuePair<string, double>("TARIFFS LIFTED", -2.0),
            new KeyValuePair<string, double>("TRADE DEAL", -1.5),
            new KeyValuePair<string, double>("RISK APPETITE", -1.5),
            new KeyValuePair<string, double>("STOCKS RALLY", -1.0),
            new KeyValuePair<string, double>("PROFIT-TAKING", -1.0),
            new KeyValuePair<string, double>("ETF OUTFLOW", -2.0),
            new KeyValuePair<string, double>("ETF INFLOW", 2.0),
        };

        // Words that only matter because they say the story is ABOUT gold or
        // about a body that moves it. They carry impact but no direction.
        // Matched against the headline PADDED with spaces, so a token that
        // starts or ends with a space is a whole word. " FED " is padded on
        // purpose: bare "FED" also matches Federer, federation and "fed up".
        private static readonly string[] Relevance =
        {
            "GOLD", "XAU", "BULLION", "IRAN", "ISRAEL", "TEHRAN", "TRUMP",
            "WHITE HOUSE", "PENTAGON", "FEDERAL RESERVE", "POWELL", "FOMC",
            "CPI", "INFLATION", "NONFARM", "NON-FARM", "PAYROLL", "TREASURY",
            "MIDDLE EAST", "OPEC", "CENTRAL BANK", "ECB", "GEOPOLIT",
            // Added after a test caught it: "Fed signals higher for longer as
            // dollar surges" scored ZERO and would never have alerted, because
            // the everyday shorthand for the central bank was not on this list
            // and neither was the dollar.
            " FED ", " FED'S ", "DOLLAR", "INTEREST RATE", "MONETARY",
            "NUCLEAR", "SANCTION", "HORMUZ",
            // widened with the lexicon above, so those terms can actually fire
            "RUSSIA", "UKRAINE", "CHINA", "TAIWAN", "HAMAS", "HEZBOLLAH",
            "HOUTHI", "NETANYAHU", "KHAMENEI", "PUTIN", "TARIFF", "DEBT CEILING",
            "CREDIT RATING", "BRICS", "OIL", "YIELD", "BULLION", "PRECIOUS METAL",
            "CHINESE", "GOLD COUNCIL", "BULLION BANK"
        };

        private static readonly string[] FalseFriends =
        {
            " FED UP ", " WELL FED ", " FED INTO ", " FED THE "
        };


        private string DescribeFeeds()
        {
            var on = new List<string>();
            if (FeedGold) on.Add("gold/XAU");
            if (FeedMideast) on.Add("Iran/Israel/Mideast");
            if (FeedTrump) on.Add("Trump/US policy");
            if (FeedFed) on.Add("Fed/inflation/dollar");
            var extra = SplitCsv(ExtraFeedUrls).Count;
            if (extra > 0) on.Add(extra + " custom feed(s)");
            on.Add("economic calendar");
            return string.Join(", ", on);
        }

        private string FeedStatus()
        {
            lock (_newsLock) return _feedStatus;
        }


        private static List<string> SplitCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return new List<string>();
            return s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        // Google News RSS search. Free, no key, and each query is its own feed
        // so one failing search cannot silence the others.
        private static string NewsQuery(string q)
        {
            return "https://news.google.com/rss/search?q=" + Uri.EscapeDataString(q + " when:1d") +
                   "&hl=en-US&gl=US&ceid=US:en";
        }

        // Same search, one-hour window: far fewer, far fresher items. Used for
        // the things where being late is the whole problem.
        private static string FastQuery(string q)
        {
            return "https://news.google.com/rss/search?q=" + Uri.EscapeDataString(q + " when:1h") +
                   "&hl=en-US&gl=US&ceid=US:en";
        }

        private List<string> FeedList()
        {
            var urls = new List<string>();
            if (FeedGold) urls.Add(NewsQuery("gold price OR XAUUSD OR bullion"));
            if (FeedMideast) urls.Add(NewsQuery("Iran OR Israel OR \"Middle East\" OR Hormuz"));
            if (FeedTrump) urls.Add(NewsQuery("Trump OR \"White House\" statement OR sanctions"));
            if (FeedFed) urls.Add(NewsQuery("Federal Reserve OR inflation OR interest rates OR dollar"));
            // the wires themselves — fastest, and each is independent
            if (WireAlJazeera) urls.Add("https://www.aljazeera.com/xml/rss/all.xml");
            if (WireAlArabiya) urls.Add("https://english.alarabiya.net/.mrss/en.xml");
            if (WireCnn) urls.Add("http://rss.cnn.com/rss/edition_world.rss");
            if (WireCnnMoney) urls.Add("http://rss.cnn.com/rss/money_news_international.rss");
            if (WireBbc) urls.Add("https://feeds.bbci.co.uk/news/world/rss.xml");
            if (WireCnbc) urls.Add("https://search.cnbc.com/rs/search/combinedcms/view.xml?partnerId=wrss01&id=20910258");
            if (WireTrumpFast) urls.Add(FastQuery("Trump OR \"White House\" OR Netanyahu OR Khamenei"));
            urls.AddRange(SplitCsv(ExtraFeedUrls));
            return urls;
        }


        private void Poll()
        {
            lock (_newsLock)
            {
                if (_pollInFlight) return;
                _pollInFlight = true;
            }
            var urls = FeedList();
            var calUrl = CalendarUrl;

            Task.Run(() =>
            {
                var found = new List<Scored>();
                var ok = 0;
                var failed = 0;
                foreach (var url in urls)
                {
                    try
                    {
                        var xml = Download(url);
                        foreach (var title in ParseFeedTitles(xml))
                        {
                            var s = Score(title);
                            s.Source = ShortSource(url);
                            found.Add(s);
                        }
                        ok++;
                    }
                    catch (Exception) { failed++; }
                }

                List<CalEvent> cal = null;
                try { cal = ParseCalendar(Download(calUrl)); }
                catch (Exception) { }

                lock (_newsLock)
                {
                    if (cal != null && cal.Count > 0) _events = cal;
                    foreach (var s in found)
                    {
                        var key = Normalise(s.Title);
                        if (_seenHeadlines.Contains(key)) continue;
                        _seenHeadlines.Add(key);
                        _seenHeadlineOrder.Enqueue(key);
                        while (_seenHeadlineOrder.Count > 800) _seenHeadlines.Remove(_seenHeadlineOrder.Dequeue());
                        if (s.Impact >= AlertThreshold) _newsPending.Add(s);
                    }
                    _feedStatus = string.Format("{0}/{1} feeds ok{2}, calendar {3} events",
                                                ok, urls.Count,
                                                failed > 0 ? " (" + failed + " failed)" : "",
                                                _events.Count);
                    _pollInFlight = false;
                }
            });
        }

        private static string Download(string url)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; GoldNewsWatch/1.0)");
                return wc.DownloadString(url);
            }
        }

        private static string ShortSource(string url)
        {
            if (url.Contains("aljazeera")) return "Al Jazeera";
            if (url.Contains("alarabiya")) return "Al Arabiya";
            if (url.Contains("money_news")) return "CNN money";
            if (url.Contains("cnn.com")) return "CNN";
            if (url.Contains("bbci.co.uk")) return "BBC";
            if (url.Contains("cnbc.com")) return "CNBC";
            if (url.Contains("news.google.com"))
            {
                if (url.Contains("Khamenei")) return "Trump/statements";
                if (url.Contains("bullion")) return "gold wire";
                if (url.Contains("Hormuz")) return "mideast wire";
                if (url.Contains("Trump")) return "US policy wire";
                if (url.Contains("Federal")) return "macro wire";
                return "news";
            }
            try { return new Uri(url).Host; } catch (Exception) { return "feed"; }
        }


        // ---- feed parsing --------------------------------------------------
        // Deliberately a plain string scan rather than an XML parser: RSS in
        // the wild is frequently malformed, and a parser that throws loses the
        // whole feed. This takes what it can read and ignores the rest.
        public static List<string> ParseFeedTitles(string xml)
        {
            var titles = new List<string>();
            if (string.IsNullOrEmpty(xml)) return titles;
            var i = 0;
            var first = true;
            while (true)
            {
                var a = xml.IndexOf("<title", i, StringComparison.OrdinalIgnoreCase);
                if (a < 0) break;
                var gt = xml.IndexOf('>', a);
                if (gt < 0) break;
                var b = xml.IndexOf("</title>", gt, StringComparison.OrdinalIgnoreCase);
                if (b < 0) break;
                var raw = xml.Substring(gt + 1, b - gt - 1);
                i = b + 8;
                // the first <title> is the channel's own name, not a story
                if (first) { first = false; continue; }
                var t = CleanXml(raw);
                if (t.Length > 0) titles.Add(t);
            }
            return titles;
        }

        public static string CleanXml(string s)
        {
            if (s == null) return "";
            s = s.Replace("<![CDATA[", "").Replace("]]>", "");
            // strip any embedded markup
            var sb = new System.Text.StringBuilder();
            var depth = 0;
            foreach (var ch in s)
            {
                if (ch == '<') { depth++; continue; }
                if (ch == '>') { if (depth > 0) depth--; continue; }
                if (depth == 0) sb.Append(ch);
            }
            var t = sb.ToString();
            t = t.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                 .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&apos;", "'")
                 .Replace("&nbsp;", " ");
            return t.Trim();
        }

        private static string Normalise(string title)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in title.ToUpperInvariant())
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        // ---- scoring -------------------------------------------------------
        public static Scored Score(string title)
        {
            var s = new Scored { Title = title };
            if (string.IsNullOrEmpty(title)) return s;
            var upper = title.ToUpperInvariant();
            var padded = " " + upper + " ";
            // Phrases that contain a relevance token but mean something else.
            // "Voters are fed up with the war on drugs" matched " FED " as a
            // whole word and then scored bullish on "war". Neutralise the
            // phrase before matching rather than dropping the token, which
            // would lose every real "Fed holds rates" headline.
            foreach (var ff in FalseFriends)
                padded = padded.Replace(ff, " ");

            var relevant = 0;
            foreach (var r in Relevance)
                if (padded.Contains(r)) relevant++;

            double dir = 0.0, mag = 0.0;
            foreach (var kv in Lexicon)
            {
                if (!upper.Contains(kv.Key)) continue;
                dir += kv.Value;
                mag += Math.Abs(kv.Value);
                s.Why.Add(kv.Key.ToLowerInvariant() + (kv.Value > 0 ? " (+)" : " (-)"));
            }

            // A loaded word about something unrelated to gold is noise. Impact
            // is the strength of the wording GATED by whether the story is even
            // about gold, the Fed, or a conflict that moves it.
            if (relevant == 0)
            {
                s.Impact = 0.0;
                s.Direction = 0.0;
                return s;
            }
            var relevanceBoost = 1.0 + 0.25 * Math.Min(3, relevant - 1);
            s.Impact = mag * relevanceBoost;
            s.Direction = dir * relevanceBoost;
            return s;
        }


        public static string TelegramUrl(string token, string chatId, string text)
        {
            // Telegram rejects anything over 4096 characters outright.
            if (text != null && text.Length > 3800)
                text = text.Substring(0, 3800) + "\n...(truncated)";
            return "https://api.telegram.org/bot" + Uri.EscapeDataString(token ?? "") +
                   "/sendMessage?chat_id=" + Uri.EscapeDataString(chatId ?? "") +
                   "&disable_web_page_preview=true" +
                   "&text=" + Uri.EscapeDataString(text ?? "");
        }

        // A bot token is a credential: anyone holding it can post as you. It
        // must never reach the cTrader log, which gets pasted into chats and
        // screenshots.
        public static string Redact(string s, string token)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(token)) return s;
            return s.Replace(token, "<token>")
                    .Replace(Uri.EscapeDataString(token), "<token>");
        }

        // The ONLY way this bot reports a Telegram failure. Redaction lives
        // inside it rather than at the call site, so there is one place to get
        // right and it is directly testable without a network.
        public static string TelegramErrorLine(string exType, string exMessage, string token)
        {
            return "telegram failed: " + exType + " — " + Redact(exMessage, token);
        }

        private void Telegram(string text)
        {
            if (string.IsNullOrEmpty(TelegramBotToken) || string.IsNullOrEmpty(TelegramChatId))
                return;
            var url = TelegramUrl(TelegramBotToken, TelegramChatId, text);
            var token = TelegramBotToken;
            // off the trading thread; a slow phone network must never stall the bot
            Task.Run(() =>
            {
                try { Download(url); }
                catch (Exception ex)
                {
                    // One function builds this line and it always redacts, so
                    // the failure path cannot log a token by accident.
                    var line = TelegramErrorLine(ex.GetType().Name, ex.Message, token);
                    BeginInvokeOnMainThread(() => Print(line));
                }
            });
        }


        public static List<CalEventPublic> ParseCalendarPublic(string json)
        {
            return ParseCalendar(json)
                .Select(e => new CalEventPublic { UtcTime = e.UtcTime, Title = e.Title, Currency = e.Currency, Tier = e.Tier })
                .ToList();
        }

        public class CalEventPublic
        {
            public DateTime UtcTime;
            public string Title;
            public string Currency;
            public int Tier;
        }

        private static List<CalEvent> ParseCalendar(string json)
        {
            var list = new List<CalEvent>();
            if (string.IsNullOrEmpty(json)) return list;
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
                if (cur == null) continue;
                cur = cur.Trim().ToUpperInvariant();

                var dateStr = Field(obj, "date");
                if (dateStr == null) continue;
                DateTimeOffset dto;
                if (!DateTimeOffset.TryParse(dateStr, null,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out dto))
                    continue;

                var title = Field(obj, "title") ?? "event";
                var impact = Field(obj, "impact") ?? "";
                var upper = title.ToUpperInvariant();
                var high = impact.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0;
                var critical = upper.Contains("FOMC") || upper.Contains("CPI") ||
                               upper.Contains("NON-FARM") || upper.Contains("NONFARM") ||
                               upper.Contains("POWELL") || upper.Contains("PCE") ||
                               upper.Contains("RATE DECISION") || upper.Contains("RATE STATEMENT");
                int tier;
                if (critical && cur == "USD") tier = 1;
                else if (critical || high) tier = 2;
                else continue;

                list.Add(new CalEvent { UtcTime = dto.UtcDateTime, Title = title, Currency = cur, Tier = tier });
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

        // ---- how the news agent reaches the trading side -------------------
        private void PollNewsIfDue()
        {
            var now = Server.TimeInUtc;
            if ((now - _lastPoll).TotalMinutes < PollMinutes)
                return;
            _lastPoll = now;
            Poll();
            DrainNews(now);
        }

        private void BeginCalendarFetch()
        {
            _lastPoll = Server.TimeInUtc;
            Poll();
        }

        // Everything the poller queued: alert on it, and if it is loud enough,
        // arm the vacuum window. The direction it suggests is printed and sent,
        // and that is ALL it does — no order is opened from a headline.
        private void DrainNews(DateTime now)
        {
            List<Scored> batch;
            lock (_newsLock)
            {
                if (_newsPending.Count == 0)
                    return;
                batch = new List<Scored>(_newsPending);
                _newsPending.Clear();
            }
            batch.Sort((a, b) => b.Impact.CompareTo(a.Impact));
            var loudest = 0.0;
            foreach (var s in batch)
            {
                if (s.Impact > loudest) loudest = s.Impact;
                var lean = Math.Abs(s.Direction) < 0.5
                    ? "no clear direction"
                    : (s.Direction > 0 ? "leans gold UP" : "leans gold DOWN");
                Print("NEWS [{0}] {1:F1} — {2} | {3}", s.Source, s.Impact, s.Title, lean);
                Telegram(string.Format("📰 {0}\n{1}\nimpact {2:F1}, {3}\n(headlines are NOT a " +
                                       "measured edge here — the bot will not trade this on its own)",
                                       s.Source, s.Title, s.Impact, lean));
            }
            if (UseVacuumWindow && loudest >= AlertThreshold)
            {
                _vacuumUntil = now.AddMinutes(VacuumWindowMinutes);
                Print("VACUUM WINDOW armed until {0:HH:mm} UTC — a displacement bar now only " +
                      "needs {1:F1}x ATR to count as a void (normally {2:F1}x). The notes name " +
                      "events as what creates these.",
                      _vacuumUntil, VacuumDisplacementAtr, VoidDisplacementAtr);
            }
        }

        // A scheduled event is close. Tier 1 and 2 only — medium-impact prints
        // are not worth standing aside for.
        private bool InNewsWindow(DateTime nowUtc, out string eventName)
        {
            eventName = null;
            List<CalEvent> snapshot;
            lock (_newsLock)
                snapshot = _events;
            if (snapshot == null || snapshot.Count == 0)
                return false;                    // fail-safe: no data, keep trading
            foreach (var e in snapshot)
            {
                if (e.Tier > 2)
                    continue;
                var mins = (e.UtcTime - nowUtc).TotalMinutes;
                if (mins <= BlockBeforeMinutes && mins >= -BlockAfterMinutes)
                {
                    eventName = e.Title;
                    // an event that just went off is exactly what makes a
                    // vacuum block, so arm the window on the way past
                    if (UseVacuumWindow && mins <= 0 && Server.TimeInUtc > _vacuumUntil)
                        _vacuumUntil = nowUtc.AddMinutes(VacuumWindowMinutes);
                    return true;
                }
            }
            return false;
        }

        // Never CLOSE a trade on news — that measured worse every time it was
        // tried. Protect it instead: a winner's stop goes to breakeven, a loser
        // is left exactly as it was.
        private void ProtectBeforeNews()
        {
            if (!ProtectOnNews)
                return;
            List<CalEvent> snapshot;
            lock (_newsLock)
                snapshot = _events;
            if (snapshot == null || snapshot.Count == 0)
                return;
            string evt = null;
            foreach (var e in snapshot)
            {
                if (e.Tier > 2)
                    continue;
                var mins = (e.UtcTime - Server.TimeInUtc).TotalMinutes;
                if (mins > 0 && mins <= ProtectBeforeMinutes) { evt = e.Title; break; }
            }
            if (evt == null)
                return;
            foreach (var pos in OwnPositions())
            {
                if (pos.NetProfit <= 0)
                    continue;                    // never tighten a losing trade
                var be = pos.EntryPrice;
                var already = pos.StopLoss.HasValue &&
                              ((pos.TradeType == TradeType.Buy && pos.StopLoss.Value >= be) ||
                               (pos.TradeType == TradeType.Sell && pos.StopLoss.Value <= be));
                if (already)
                    continue;
                var r = ModifyPosition(pos, be, pos.TakeProfit);
                if (r.IsSuccessful)
                    Print("NEWS PROTECT: {0} — stop moved to breakeven {1:F2} ahead of {2}.",
                          pos.Id, be, evt);
            }
        }

        protected override void OnStop()
        {
            if (_stopped)
                return;
            Print("GoldICT stopped | equity {0:F2} | {1} trades today | {2} setups waiting | " +
                  "reach rule has learned from {3} closed trades | news: {4}",
                  Account.Equity, _tradesToday, _pending.Count, _reachHistory.Count,
                  UseNews ? FeedStatus() : "off");
        }
    }
}
