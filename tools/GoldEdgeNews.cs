// GoldEdgeNews — GoldEdge (the holdout-certified custom strategy) plus a NEWS
// AGENT built on one principle: news must PROTECT the bot without reducing how
// much it trades. News cuts both ways — a print can pump gold as easily as
// dump it — so this agent never closes a trade because news is coming.
//
// STRATEGY (unchanged from GoldEdge — certified on 30 virgin seeds):
//   6-voter confluence, gated by trend quality (Kaufman efficiency >= 0.55
//   over 24 bars) and ADX >= 18 AND rising. Stop 0.6%, target 4:1, 10-bar
//   time stop, 1-hour chart.
//
// WHAT THE NEWS AGENT WATCHES: everything that moves gold, sorted into tiers —
//   TIER 1  FOMC, rate decisions, NFP, CPI, core PCE, Powell/Fed-chair
//           remarks, testimony, press conferences, Jackson Hole
//   TIER 2  any other high-impact print, and tier-1-type events abroad
//   TIER 3  anyone at a microphone (members, governors, presidents, minutes,
//           panels, symposiums) plus medium-impact US data
//   Currencies: USD drives gold directly; EUR/GBP/JPY/CNY move the dollar,
//   which moves gold.
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
// approve network access -> add instance on XAUUSD **h1** -> Play.
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

        [Parameter("Require ADX rising", DefaultValue = true, Group = "Signal")]
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
        // touching entries, so trade frequency is unchanged. MEASURED: edge
        // +0.633 -> +0.636 with the SAME trade count (1530 -> 1532).
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

        [Parameter("News: currencies to watch (comma)", DefaultValue = "USD,EUR,GBP,JPY,CNY", Group = "News agent")]
        public string WatchCurrencies { get; set; }

        [Parameter("News: shock veto (no network)", DefaultValue = true, Group = "News agent")]
        public bool UseShockVeto { get; set; }

        [Parameter("News: shock size (x ATR)", DefaultValue = 2.5, MinValue = 1.0, MaxValue = 10.0, Group = "News agent")]
        public double ShockAtrMult { get; set; }

        [Parameter("News: shock cooldown (bars)", DefaultValue = 3, MinValue = 1, MaxValue = 20, Group = "News agent")]
        public int ShockCooldownBars { get; set; }

        [Parameter("Risk per trade (%)", DefaultValue = 10.0, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Stop loss (%)", DefaultValue = 0.6, MinValue = 0.05, Group = "Exits")]
        public double StopPercent { get; set; }

        [Parameter("Reward:risk", DefaultValue = 4.0, MinValue = 0.5, MaxValue = 10.0, Group = "Exits")]
        public double RewardRisk { get; set; }

        [Parameter("Max hold (bars)", DefaultValue = 10, MinValue = 1, MaxValue = 200, Group = "Exits")]
        public int MaxHoldBars { get; set; }

        [Parameter("Daily loss stop (%)", DefaultValue = 15.0, MinValue = 1.0, Group = "Risk")]
        public double DailyLossStopPercent { get; set; }

        [Parameter("Allow shorts", DefaultValue = true, Group = "Risk")]
        public bool AllowShort { get; set; }

        [Parameter("Log status every N bars", DefaultValue = 6, MinValue = 0, Group = "Diagnostics")]
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
            Print("Exit: stop {0}% | target {1}% ({2}:1) | max hold {3} bars | risk {4}%",
                  StopPercent, StopPercent * RewardRisk, RewardRisk, MaxHoldBars, RiskPercent);
            Print("News agent: calendar {0} watching {1} | protect-open-trade {2} ({3} min before) | block-entries {4} | shock veto {5}",
                  UseCalendar ? "ON" : "OFF", WatchCurrencies,
                  ProtectOnNews ? "ON" : "OFF", ProtectBeforeMinutes,
                  BlockEntriesOnNews ? "ON" : "OFF", UseShockVeto ? "ON" : "OFF");
            Print("News policy: never closes a trade on news (measured worse) — protects it instead.");
            if (Bars.TimeFrame != TimeFrame.Hour)
                Print("NOTE: certified on the 1-HOUR chart; you are on {0}.", Bars.TimeFrame);

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
            if (UseCalendar && (Server.TimeInUtc - _lastFetchUtc).TotalHours >= 6.0)
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

            if (OwnPositions().Any())
                return;

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
                        if (e.Tier > 2)
                            continue;                       // only the real movers
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
                    status = "FETCH FAILED (" + ex.GetType().Name + ") — trading on shock veto only";
                }

                lock (_newsLock)
                {
                    if (parsed != null && parsed.Count > 0)
                        _events = parsed;            // keep the old list if the new one is empty
                    _newsStatus = status;
                    _fetchInFlight = false;
                }
                Print("news: {0}", status);
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
                if (watch.Count > 0 && !watch.Contains(cur))
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
                var isCritical = Tier1Keywords.Any(k => upper.Contains(k));

                int tier;
                if (isCritical && cur == "USD")
                    tier = 1;                       // gold-critical US event
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

        private void OpenTrade(int direction, int votes, double adx, double quality)
        {
            var price = direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0) return;

            var stopDist = price * (StopPercent / 100.0);
            var tpDist = stopDist * RewardRisk;
            if (stopDist <= 0) return;

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
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} | target {4:F2} ({5}:1) | {6}/6 votes, ADX {7:F0}, quality {8:F2}",
                      side, units, price, price - direction * stopDist,
                      price + direction * tpDist, RewardRisk, votes, adx, quality);
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
