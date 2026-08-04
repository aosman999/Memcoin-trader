// GoldBot — the 6-voter confluence strategy (UNCHANGED) plus the support
// AIs that surround it in the original design. Runs on whatever chart you
// attach it to (single timeframe). Fixed 0.6% / 1.2% (2:1) exits.
//
// STRATEGY (same as before): 6 voters — EMA20>EMA75, price>EMA75, MACD>0,
// RSI>50, EMA20 rising, price>20 bars ago — enter when >= VotesNeeded
// agree AND ADX >= AdxMin.
//
// THE ADDED AIs (each a toggle, all from price/time — no network):
//   * Session AI  — gold's liquidity clock; stands aside in thin hours.
//   * Sentinel AI — vetoes entries during a news-grade price shock
//                   (12-min cooldown) — the "rug check" equivalent.
//   * Regime AI   — classifies trending/ranging/chaotic; can skip chaos.
//   * Discipline  — Valentini's rule: stop after 3 losing trades in a day.
//
// Safety: demo-only lock, one position at a time, -15% daily loss stop,
// max hold, small-account guard. OnBar wrapped in try/catch.
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GoldBot : Robot
    {
        [Parameter("Votes needed (of 6)", DefaultValue = 5, MinValue = 3, MaxValue = 6, Group = "Signal")]
        public int VotesNeeded { get; set; }

        [Parameter("Minimum ADX", DefaultValue = 18.0, MinValue = 0, MaxValue = 50, Group = "Signal")]
        public double AdxMin { get; set; }

        [Parameter("Risk per trade (%)", DefaultValue = 13.5, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Stop loss (%)", DefaultValue = 0.6, MinValue = 0.05, Group = "Exits")]
        public double StopPercent { get; set; }

        [Parameter("Take profit (%)", DefaultValue = 1.2, MinValue = 0.1, Group = "Exits")]
        public double TakeProfitPercent { get; set; }

        [Parameter("Session AI (skip thin hours)", DefaultValue = true, Group = "Support AIs")]
        public bool UseSession { get; set; }

        [Parameter("Session min weight", DefaultValue = 0.6, MinValue = 0.0, MaxValue = 1.2, Group = "Support AIs")]
        public double SessionMinWeight { get; set; }

        [Parameter("Sentinel AI (veto news shocks)", DefaultValue = true, Group = "Support AIs")]
        public bool UseSentinel { get; set; }

        [Parameter("Regime AI (skip chaotic tape)", DefaultValue = true, Group = "Support AIs")]
        public bool UseRegimeFilter { get; set; }

        [Parameter("Discipline (stop after N daily losses)", DefaultValue = true, Group = "Support AIs")]
        public bool UseDiscipline { get; set; }

        [Parameter("Max losses per day", DefaultValue = 3, MinValue = 1, Group = "Support AIs")]
        public int MaxLossesPerDay { get; set; }

        [Parameter("Daily loss stop (%)", DefaultValue = 15.0, MinValue = 1.0, Group = "Risk")]
        public double DailyLossStopPercent { get; set; }

        [Parameter("Max hold (minutes)", DefaultValue = 600, MinValue = 5, Group = "Risk")]
        public int MaxHoldMinutes { get; set; }

        [Parameter("Allow shorts", DefaultValue = true, Group = "Risk")]
        public bool AllowShort { get; set; }

        [Parameter("Log status every N bars", DefaultValue = 15, MinValue = 0, Group = "Diagnostics")]
        public int StatusEveryBars { get; set; }

        private const string Label = "GoldBot";
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private RelativeStrengthIndex _rsi;
        private MacdHistogram _macd;
        private DirectionalMovementSystem _dms;
        private DateTime _sentinelBlockedUntil = DateTime.MinValue;
        private int _barCount;
        private bool _stopped;

        protected override void OnStart()
        {
            if (Account.IsLive)
            {
                Print("REFUSING TO RUN: live account. GoldBot is demo-only. No order placed.");
                _stopped = true;
                Stop();
                return;
            }

            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 75);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _macd = Indicators.MacdHistogram(Bars.ClosePrices, 26, 12, 9);
            _dms = Indicators.DirectionalMovementSystem(14);

            Print("GoldBot started | {0} | account {1} (DEMO) | balance {2:F2} | bars {3}",
                  SymbolName, Account.Number, Account.Balance, Bars.ClosePrices.Count);
            Print("Config: {0}/6 votes | ADX>={1} | stop {2}% | target {3}% | risk {4}%",
                  VotesNeeded, AdxMin, StopPercent, TakeProfitPercent, RiskPercent);
            Print("Support AIs: session {0} | sentinel {1} | regime {2} | discipline {3}",
                  UseSession ? "ON" : "OFF", UseSentinel ? "ON" : "OFF",
                  UseRegimeFilter ? "ON" : "OFF", UseDiscipline ? "ON" : "OFF");
        }

        protected override void OnBar()
        {
            if (_stopped)
                return;

            try
            {
                Evaluate();
            }
            catch (Exception ex)
            {
                Print("ERROR in OnBar: {0} — {1}", ex.GetType().Name, ex.Message);
            }
        }

        private void Evaluate()
        {
            _barCount++;

            if (Bars.ClosePrices.Count < 260)
                return;

            var now = Server.TimeInUtc;
            var close = Bars.ClosePrices.Last(0);

            // ---- the 6 voters (unchanged strategy) -----------------------
            var emaFast = _emaFast.Result.Last(0);
            var emaFastPrev = _emaFast.Result.Last(3);
            var emaSlow = _emaSlow.Result.Last(0);
            var rsi = _rsi.Result.Last(0);
            var macdHist = _macd.Histogram.Last(0);
            var adx = _dms.ADX.Last(0);
            var past = Bars.ClosePrices.Last(20);

            var bulls = 0;
            if (emaFast > emaSlow) bulls++;
            if (close > emaSlow) bulls++;
            if (macdHist > 0) bulls++;
            if (rsi > 50.0) bulls++;
            if (emaFast > emaFastPrev) bulls++;
            if (close > past) bulls++;
            var bears = 6 - bulls;

            // ---- the support AIs -----------------------------------------
            var sessionWeight = SessionWeight(now);
            var regime = Regime();

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: price {0:F2} | {1}b/{2}b | ADX {3:F1} | session {4:F1} | regime {5}",
                      close, bulls, bears, adx, sessionWeight, regime);

            // ---- manage the open position (time stop only, strat unchanged)
            foreach (var pos in OwnPositions().ToList())
            {
                if ((Server.TimeInUtc - pos.EntryTime).TotalMinutes >= MaxHoldMinutes)
                {
                    Print("Closing {0} — max hold {1} min reached.", pos.Id, MaxHoldMinutes);
                    ClosePosition(pos);
                }
            }

            // ---- daily loss stop -----------------------------------------
            var dayStart = DayStartEquity();
            if (dayStart > 0 &&
                Account.Equity <= dayStart * (1.0 - DailyLossStopPercent / 100.0))
            {
                foreach (var pos in OwnPositions().ToList())
                    ClosePosition(pos);
                return;
            }

            // ---- entry gates ---------------------------------------------
            if (OwnPositions().Any())
                return;                               // one position at a time

            // Discipline AI: 3 losing trades today -> done for the day
            if (UseDiscipline && LossesToday() >= MaxLossesPerDay)
                return;

            // Sentinel AI: a news-grade shock is in progress -> stand aside
            if (UseSentinel && !SentinelSafe(now))
                return;

            // Session AI: skip thin-liquidity hours
            if (UseSession && sessionWeight < SessionMinWeight)
                return;

            // Regime AI: don't fight a chaotic tape
            if (UseRegimeFilter && regime == "chaotic")
                return;

            if (adx < AdxMin)
                return;                               // chop filter

            if (bulls >= VotesNeeded)
                OpenTrade(1, bulls, adx);
            else if (bears >= VotesNeeded && AllowShort)
                OpenTrade(-1, bears, adx);
        }

        // ================= SUPPORT AIs =================
        private double SessionWeight(DateTime utc)
        {
            var h = utc.Hour + utc.Minute / 60.0;
            if (h >= 12 && h < 16) return 1.2;        // London/NY overlap
            if (h >= 7 && h < 12) return 1.0;         // London
            if (h >= 16 && h < 21) return 0.9;        // NY afternoon
            if (h >= 1 && h < 7) return 0.6;          // Asia
            return 0.4;                               // around the daily close
        }

        private bool SentinelSafe(DateTime now)
        {
            if (now < _sentinelBlockedUntil)
                return false;

            var p = LastCloses(46);
            if (p.Length < 45)
                return true;

            var prev = p[p.Length - 2];
            var lastMove = prev != 0 ? Math.Abs(p[p.Length - 1] / prev - 1.0) : 0.0;
            var burst = RealizedVol(p, 6);
            var baseline = RealizedVol(p, 45);

            if (lastMove > 0.0022 || (baseline > 0 && burst / baseline > 3.5))
            {
                _sentinelBlockedUntil = now.AddMinutes(12);
                Print("Sentinel AI: news-grade move — entries blocked 12 minutes.");
                return false;
            }
            return true;
        }

        private string Regime()
        {
            var w = LastCloses(240);
            if (w.Length < 120)
                return "ranging";
            var net = Math.Abs(w[w.Length - 1] - w[0]);
            var path = 0.0;
            for (var i = 1; i < w.Length; i++)
                path += Math.Abs(w[i] - w[i - 1]);
            var efficiency = path > 0 ? net / path : 0.0;
            var rvShort = RealizedVol(w, 30);
            var rvLong = RealizedVol(w, w.Length);
            if (rvLong > 0 && rvShort / rvLong > 2.4)
                return "chaotic";
            return efficiency >= 0.30 ? "trending" : "ranging";
        }

        private int LossesToday()
        {
            var midnight = Server.TimeInUtc.Date;
            return History.Count(t => t.Label == Label &&
                                      t.ClosingTime >= midnight &&
                                      t.NetProfit < 0);
        }

        // ================= EXECUTION (strategy unchanged) =================
        private void OpenTrade(int direction, int votes, double adx)
        {
            var price = direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0)
                return;

            var stopDist = price * (StopPercent / 100.0);
            var tpDist = price * (TakeProfitPercent / 100.0);
            if (stopDist <= 0)
                return;

            var riskUsd = Account.Equity * (RiskPercent / 100.0);
            var units = Symbol.NormalizeVolumeInUnits(riskUsd / stopDist, RoundingMode.Down);

            var minRisk = Symbol.VolumeInUnitsMin * stopDist;
            if (minRisk > riskUsd * 2.0)
            {
                Print("SKIP: account too small — smallest trade risks {0:F2}, budget {1:F2}.",
                      minRisk, riskUsd);
                return;
            }
            if (units < Symbol.VolumeInUnitsMin)
                units = Symbol.VolumeInUnitsMin;

            var stopPips = stopDist / Symbol.PipSize;
            var tpPips = tpDist / Symbol.PipSize;
            var side = direction > 0 ? TradeType.Buy : TradeType.Sell;

            var result = ExecuteMarketOrder(side, SymbolName, units, Label, stopPips, tpPips);
            if (result.IsSuccessful)
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} | target {4:F2} | {5}/6 votes, ADX {6:F0}",
                      side, units, price, price - direction * stopDist,
                      price + direction * tpDist, votes, adx);
            else
                Print("ORDER FAILED: {0}", result.Error);
        }

        // ================= HELPERS =================
        private double[] LastCloses(int count)
        {
            var n = Math.Min(count, Bars.ClosePrices.Count);
            var o = new double[n];
            for (var i = 0; i < n; i++)
                o[n - 1 - i] = Bars.ClosePrices.Last(i);
            return o;
        }

        // mean absolute log-return over the last `window` values of p
        private static double RealizedVol(double[] p, int window)
        {
            var start = Math.Max(1, p.Length - window);
            var total = 0.0;
            var n = 0;
            for (var i = start; i < p.Length; i++)
            {
                if (p[i - 1] <= 0 || p[i] <= 0) continue;
                total += Math.Abs(Math.Log(p[i] / p[i - 1]));
                n++;
            }
            return n > 0 ? total / n : 0.0;
        }

        private System.Collections.Generic.IEnumerable<Position> OwnPositions()
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
            Print("GoldBot stopped. Open positions keep their broker-side SL/TP.");
        }
    }
}
