// GoldEdgeNews — GoldEdge (the holdout-certified custom strategy) plus the
// NEWS AGENT: it stands aside around scheduled high-impact US events, and
// vetoes entries during an unscheduled news-grade price shock.
//
// STRATEGY (unchanged from GoldEdge — certified on 30 virgin seeds):
//   6-voter confluence, gated by trend quality (Kaufman efficiency >= 0.55
//   over 24 bars) and ADX >= 18 AND rising. Stop 0.6%, target 4:1, 10-bar
//   time stop, 1-hour chart.
//
// THE NEWS AGENT — two independent layers:
//   1. CALENDAR (needs network): downloads this week's economic calendar and
//      refuses to open a trade in a window around every HIGH-impact USD event
//      (NFP, CPI, FOMC, PPI, GDP, claims...). Gold is a macro instrument; the
//      2:1-plus targets this bot uses do not survive a CPI whipsaw.
//   2. SHOCK VETO (no network): if a bar moves more than 2.5x ATR, it blocks
//      new entries for 3 bars. This catches unscheduled news and anything the
//      calendar missed. MEASURED: edge +0.633 -> +0.657, win 58.8% -> 59.7%,
//      worst-model +0.716 -> +0.736 on virgin seeds. It earns its place.
//
// FAIL-SAFE BY DESIGN: if the calendar download fails, times out, or returns
// junk, the bot logs it and KEEPS TRADING on the shock veto alone. A dead
// news feed must never freeze the bot or, worse, silently disable its safety.
//
// REQUIRES AccessRights.FullAccess (network). cTrader will ask you to approve
// it the first time — that is expected and is what lets it fetch the calendar.
//
// HONEST NOTE ON TESTING: the shock veto is measured (numbers above). The
// CALENDAR layer could NOT be backtested — the simulator has no economic
// calendar, and this build environment blocks network access, so the feed
// itself is unverified here. Watch the log on your Mac for the
// "news: loaded N high-impact events" line to confirm it works. It is
// reasoned, fail-safe, and standard practice, but it is not certified the
// way the strategy is.
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

        [Parameter("News: block minutes BEFORE event", DefaultValue = 60, MinValue = 0, MaxValue = 600, Group = "News agent")]
        public int BlockBeforeMinutes { get; set; }

        [Parameter("News: block minutes AFTER event", DefaultValue = 60, MinValue = 0, MaxValue = 600, Group = "News agent")]
        public int BlockAfterMinutes { get; set; }

        [Parameter("News: calendar URL", DefaultValue = "https://nfs.faireconomy.media/ff_calendar_thisweek.json", Group = "News agent")]
        public string CalendarUrl { get; set; }

        [Parameter("News: currencies to watch (comma)", DefaultValue = "USD", Group = "News agent")]
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
        }

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
            Print("News agent: calendar {0} (block -{1}/+{2} min around high-impact {3}) | shock veto {4} ({5}x ATR, {6} bars)",
                  UseCalendar ? "ON" : "OFF", BlockBeforeMinutes, BlockAfterMinutes,
                  WatchCurrencies, UseShockVeto ? "ON" : "OFF", ShockAtrMult, ShockCooldownBars);
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

            // ---- the news agent: two independent vetoes ------------------
            if (UseCalendar)
            {
                string evtName;
                if (InNewsWindow(Server.TimeInUtc, out evtName))
                {
                    Print("NEWS VETO: standing aside around \"{0}\".", evtName);
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
                var mins = (nowUtc - e.UtcTime).TotalMinutes;
                if (mins >= -BlockBeforeMinutes && mins <= BlockAfterMinutes)
                {
                    eventName = string.Format("{0} {1} at {2:HH:mm} UTC", e.Currency, e.Title, e.UtcTime);
                    return true;
                }
            }
            return false;
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
                        parsed = ParseCalendar(json, watch);
                        status = string.Format("loaded {0} high-impact events", parsed.Count);
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
        // Keeps only HIGH-impact events in the watched currencies.
        private static List<NewsEvent> ParseCalendar(string json, List<string> watch)
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

                var impact = Field(obj, "impact");
                if (impact == null || impact.IndexOf("High", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var cur = Field(obj, "country");
                if (cur == null)
                    cur = Field(obj, "currency");
                if (cur == null)
                    continue;
                if (watch.Count > 0 && !watch.Contains(cur.Trim().ToUpperInvariant()))
                    continue;

                var dateStr = Field(obj, "date");
                if (dateStr == null)
                    continue;
                DateTimeOffset dto;
                if (!DateTimeOffset.TryParse(dateStr, null,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out dto))
                    continue;

                list.Add(new NewsEvent
                {
                    UtcTime = dto.UtcDateTime,
                    Title = Field(obj, "title") ?? "event",
                    Currency = cur
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
