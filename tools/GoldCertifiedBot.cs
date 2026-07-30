// GoldCertifiedBot — the SIMULATOR-CERTIFIED strategy as a cTrader cBot.
//
// Runs alongside GoldBot (the indicator-confluence bot) so the two can be
// compared on the same demo account with real fills. Uses a different
// Label, so each bot only ever manages its own positions.
//
// Built from API calls verified working on this machine. Deliberately
// avoids MarketData.GetBars (the likely cause of the earlier
// NullReferenceException): the 15-minute trend filter is derived by
// sampling the 1-minute series instead of loading a second timeframe.
//
// Certified champion logic (data/gold_params.json, 2026-07-21):
//   Strategies, in order: mentor sweep (kill zones) -> trend -> mean
//   reversion -> breakout -> pullback
//   Gates: event sentinel (news-shock veto), session liquidity clock,
//   MACD/RSI confluence, 15m trend filter (trend strategy only)
//   Sizing: 10% risk x session weight x calm-market boost, governor
//   halves risk after 2 straight red days
//   Exits: binary 2:1 (stop -0.6%, target +1.2%), broker-side
//   Safety: demo-only, -15% day stop, 3-loss discipline, max hold
//
// DESIGNED FOR THE 1-MINUTE CHART. Attach it to XAUUSD m1.
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GoldCertifiedBot : Robot
    {
        [Parameter("Risk per trade (%)", DefaultValue = 10.0, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Stop loss (%)", DefaultValue = 0.6, MinValue = 0.05, Group = "Risk")]
        public double StopPercent { get; set; }

        [Parameter("Take profit (%)", DefaultValue = 1.2, MinValue = 0.1, Group = "Risk")]
        public double TakeProfitPercent { get; set; }

        [Parameter("Daily loss stop (%)", DefaultValue = 15.0, MinValue = 1.0, Group = "Risk")]
        public double DailyLossStopPercent { get; set; }

        [Parameter("Max losses per day", DefaultValue = 3, MinValue = 1, Group = "Risk")]
        public int MaxLossesPerDay { get; set; }

        [Parameter("Max hold (minutes)", DefaultValue = 600, MinValue = 5, Group = "Risk")]
        public int MaxHoldMinutes { get; set; }

        [Parameter("Allow shorts", DefaultValue = true, Group = "Risk")]
        public bool AllowShort { get; set; }

        [Parameter("EMA fast", DefaultValue = 20, MinValue = 2, Group = "Strategy")]
        public int EmaFast { get; set; }

        [Parameter("EMA slow", DefaultValue = 75, MinValue = 5, Group = "Strategy")]
        public int EmaSlow { get; set; }

        [Parameter("Trend min slope", DefaultValue = 0.00004971514543176356, Group = "Strategy")]
        public double TrendMinSlope { get; set; }

        [Parameter("Mean-reversion window", DefaultValue = 51, MinValue = 10, Group = "Strategy")]
        public int MrWindow { get; set; }

        [Parameter("Mean-reversion entry z", DefaultValue = 2.2, MinValue = 0.5, Group = "Strategy")]
        public double MrEntryZ { get; set; }

        [Parameter("Mean-reversion max trend z", DefaultValue = 0.756025558832395, Group = "Strategy")]
        public double MrMaxTrendZ { get; set; }

        [Parameter("Breakout lookback", DefaultValue = 120, MinValue = 20, Group = "Strategy")]
        public int BoLookback { get; set; }

        [Parameter("Breakout range expansion", DefaultValue = 1.6, MinValue = 1.0, Group = "Strategy")]
        public double BoRangeExpansion { get; set; }

        [Parameter("Pullback band", DefaultValue = 0.0006, MinValue = 0.0001, Group = "Strategy")]
        public double PullbackBand { get; set; }

        [Parameter("Sweep lookback", DefaultValue = 191, MinValue = 30, Group = "Strategy")]
        public int SweepLookback { get; set; }

        [Parameter("Sweep margin", DefaultValue = 0.000130848675930624, Group = "Strategy")]
        public double SweepMargin { get; set; }

        [Parameter("Sweep ATR multiple", DefaultValue = 1.5, MinValue = 0.2, Group = "Strategy")]
        public double SweepAtrMult { get; set; }

        [Parameter("Session min weight", DefaultValue = 0.6, MinValue = 0.0, MaxValue = 1.2, Group = "Gates")]
        public double SessionMinWeight { get; set; }

        [Parameter("Use indicator confluence", DefaultValue = true, Group = "Gates")]
        public bool UseIndicators { get; set; }

        [Parameter("Use 15m trend filter", DefaultValue = true, Group = "Gates")]
        public bool UseMtf { get; set; }

        [Parameter("Use event sentinel", DefaultValue = true, Group = "Gates")]
        public bool UseSentinel { get; set; }

        [Parameter("Use mentor sweep", DefaultValue = true, Group = "Gates")]
        public bool UseMentorSweep { get; set; }

        [Parameter("Use governor", DefaultValue = true, Group = "Gates")]
        public bool UseGovernor { get; set; }

        [Parameter("Governor cut", DefaultValue = 0.5, MinValue = 0.1, MaxValue = 1.0, Group = "Gates")]
        public double GovernorCut { get; set; }

        [Parameter("Vol-target reference", DefaultValue = 0.00038206155152861575, Group = "Gates")]
        public double VolTargetRef { get; set; }

        [Parameter("Log status every N bars", DefaultValue = 30, MinValue = 0, Group = "Diagnostics")]
        public int StatusEveryBars { get; set; }

        private const string Label = "GoldCertifiedBot";
        private const int MinBars = 260;
        private DateTime _sentinelBlockedUntil = DateTime.MinValue;
        private int _barCount;
        private bool _stopped;

        protected override void OnStart()
        {
            if (Account.IsLive)
            {
                Print("REFUSING TO RUN: live account. This bot is demo-only. No order placed.");
                _stopped = true;
                Stop();
                return;
            }

            Print("GoldCertifiedBot started | {0} | account {1} (DEMO) | balance {2:F2} | bars {3}",
                  SymbolName, Account.Number, Account.Balance, Bars.ClosePrices.Count);
            Print("Certified config: risk {0}% | stop {1}% | target {2}% (2:1) | day stop -{3}%",
                  RiskPercent, StopPercent, TakeProfitPercent, DailyLossStopPercent);
            Print("NOTE: this strategy is designed for the 1-MINUTE chart.");
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

            foreach (var pos in OwnPositions().ToList())
            {
                if ((Server.TimeInUtc - pos.EntryTime).TotalMinutes >= MaxHoldMinutes)
                {
                    Print("Closing {0} — max hold {1} min reached.", pos.Id, MaxHoldMinutes);
                    ClosePosition(pos);
                }
            }

            if (Bars.ClosePrices.Count < MinBars)
                return;

            var now = Server.TimeInUtc;

            // ---- daily loss stop -----------------------------------------
            var dayStart = DayStartEquity();
            if (dayStart > 0 &&
                Account.Equity <= dayStart * (1.0 - DailyLossStopPercent / 100.0))
            {
                foreach (var pos in OwnPositions().ToList())
                    ClosePosition(pos);
                return;
            }

            // ---- 3-loss daily discipline stop ----------------------------
            if (LossesToday() >= MaxLossesPerDay)
                return;

            if (OwnPositions().Any())
                return;

            var prices = LastCloses(MinBars);

            // ---- event sentinel ------------------------------------------
            if (UseSentinel && !SentinelSafe(prices, now))
                return;

            // ---- session liquidity clock ---------------------------------
            var sessionWeight = SessionWeight(now);

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: price {0:F2} | session weight {1:F1} | scanning {2} strategies",
                      prices[prices.Length - 1], sessionWeight,
                      UseMentorSweep ? 5 : 4);

            if (sessionWeight < SessionMinWeight)
                return;

            // ---- certified strategy cascade ------------------------------
            var sig = Evaluate(prices, now);
            if (sig == null)
                return;
            if (sig.Direction < 0 && !AllowShort)
                return;
            if (UseIndicators && !ConfluenceOk(sig.Name, sig.Direction, prices))
                return;

            OpenTrade(sig, sessionWeight, prices);
        }

        // ================= STRATEGIES =================
        private Sig Evaluate(double[] p, DateTime now)
        {
            if (UseMentorSweep)
            {
                var s = MentorSweep(p, now);
                if (s != null) return s;
            }
            var t = Trend(p);
            if (t != null) return t;
            var m = MeanRev(p);
            if (m != null) return m;
            var b = Breakout(p);
            if (b != null) return b;
            return Pullback(p);
        }

        private Sig Trend(double[] p)
        {
            var need = EmaSlow + 5;
            if (p.Length < need) return null;

            var w = Tail(p, need);
            var fast = Ema(w, EmaFast);
            var slow = Ema(w, EmaSlow);
            var slowPrev = Ema(Head(w, w.Length - 5), EmaSlow);
            var slope = slow != 0 ? (slow - slowPrev) / (5 * slow) : 0.0;
            var last = p[p.Length - 1];

            if (fast > slow && slope >= TrendMinSlope && last >= fast)
            {
                if (UseMtf && !MtfAgrees(p, 1)) return null;
                return new Sig("trend", 1);
            }
            if (fast < slow && slope <= -TrendMinSlope && last <= fast)
            {
                if (UseMtf && !MtfAgrees(p, -1)) return null;
                return new Sig("trend", -1);
            }
            return null;
        }

        private Sig MeanRev(double[] p)
        {
            if (p.Length < 2 * MrWindow) return null;

            var w = Tail(p, MrWindow);
            var mean = w.Average();
            var sd = StdDev(w, mean);
            if (sd <= 0) return null;

            var z = (p[p.Length - 1] - mean) / sd;
            var prior = p.Skip(p.Length - 2 * MrWindow).Take(MrWindow).ToArray();
            var trendZ = (mean - prior.Average()) / sd;
            if (Math.Abs(trendZ) > MrMaxTrendZ) return null;

            if (z <= -MrEntryZ) return new Sig("meanrev", 1);
            if (z >= MrEntryZ) return new Sig("meanrev", -1);
            return null;
        }

        private Sig Breakout(double[] p)
        {
            if (p.Length < BoLookback + 10) return null;

            var look = p.Skip(p.Length - BoLookback - 1).Take(BoLookback).ToArray();
            var hi = look.Max();
            var lo = look.Min();
            var recent = Tail(p, 15);
            var recentRange = recent.Max() - recent.Min();
            var older = look.Take(60).ToArray();
            var olderRange = older.Length >= 60 ? (older.Max() - older.Min()) / 4.0 : 0.0;
            if (olderRange <= 0 || recentRange < BoRangeExpansion * olderRange) return null;

            var last = p[p.Length - 1];
            if (last > hi) return new Sig("breakout", 1);
            if (last < lo) return new Sig("breakout", -1);
            return null;
        }

        private Sig Pullback(double[] p)
        {
            var need = EmaSlow + 10;
            if (p.Length < need) return null;

            var w = Tail(p, need);
            var fast = Ema(w, EmaFast);
            var slow = Ema(w, EmaSlow);
            var slowPrev = Ema(Head(w, w.Length - 5), EmaSlow);
            var slope = slow != 0 ? (slow - slowPrev) / (5 * slow) : 0.0;
            var last = p[p.Length - 1];
            var prev = p[p.Length - 2];
            var last4 = Tail(p, 4);
            var band = PullbackBand * last;

            if (fast > slow && slope >= TrendMinSlope &&
                Math.Abs(last4.Min() - fast) <= band && last > prev)
                return new Sig("pullback", 1);
            if (fast < slow && slope <= -TrendMinSlope &&
                Math.Abs(last4.Max() - fast) <= band && last < prev)
                return new Sig("pullback", -1);
            return null;
        }

        private Sig MentorSweep(double[] p, DateTime now)
        {
            if (p.Length < SweepLookback + 6 || !InKillZone(now)) return null;

            var w = p.Skip(p.Length - SweepLookback - 5).Take(SweepLookback).ToArray();
            var recent = Tail(p, 5);
            var hi = w.Max();
            var lo = w.Min();
            var last = p[p.Length - 1];
            var atr = AtrProxy(p, 14);
            if (atr <= 0) return null;

            if (recent.Max() > hi * (1 + SweepMargin) && last < hi &&
                (recent.Max() - last) >= SweepAtrMult * atr)
                return new Sig("sweep", -1);
            if (recent.Min() < lo * (1 - SweepMargin) && last > lo &&
                (last - recent.Min()) >= SweepAtrMult * atr)
                return new Sig("sweep", 1);
            return null;
        }

        // ================= GATES =================
        private bool ConfluenceOk(string name, int direction, double[] p)
        {
            if (name == "trend" || name == "breakout")
            {
                var hist = MacdHist(p);
                return direction > 0 ? hist > 0 : hist < 0;
            }
            if (name == "meanrev")
            {
                var r = Rsi(p, 14);
                return direction > 0 ? r <= 34.0 : r >= 66.0;
            }
            return true;
        }

        // 15-minute trend derived by sampling the 1-minute series (no
        // second-timeframe load: that call caused the earlier crash).
        private bool MtfAgrees(double[] p, int direction)
        {
            if (p.Length < 320) return true;

            var bars = new double[21];
            for (var i = 0; i < 21; i++)
                bars[i] = p[p.Length - 1 - (20 - i) * 15];

            var now = Ema(bars, 20);
            var prev = Ema(Head(bars, 20), 20);
            return (now - prev) * direction >= 0;
        }

        private bool SentinelSafe(double[] p, DateTime now)
        {
            if (now < _sentinelBlockedUntil) return false;
            if (p.Length < 45) return true;

            var prev = p[p.Length - 2];
            var lastMove = prev != 0 ? Math.Abs(p[p.Length - 1] / prev - 1.0) : 0.0;
            var burst = RealizedVol(Tail(p, 6));
            var baseline = RealizedVol(Tail(p, 45));

            if (lastMove > 0.0022 || (baseline > 0 && burst / baseline > 3.5))
            {
                _sentinelBlockedUntil = now.AddMinutes(12);
                Print("Sentinel: news-grade move — entries blocked 12 minutes.");
                return false;
            }
            return true;
        }

        private double SessionWeight(DateTime utc)
        {
            var h = utc.Hour + utc.Minute / 60.0;
            if (h >= 12 && h < 16) return 1.2;
            if (h >= 7 && h < 12) return 1.0;
            if (h >= 16 && h < 21) return 0.9;
            if (h >= 1 && h < 7) return 0.6;
            return 0.4;
        }

        private static bool InKillZone(DateTime utc)
        {
            var h = utc.Hour + utc.Minute / 60.0;
            return (h >= 7.0 && h < 10.0) || (h >= 12.0 && h < 15.0);
        }

        // ================= EXECUTION =================
        private void OpenTrade(Sig sig, double sessionWeight, double[] prices)
        {
            var price = sig.Direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0) return;

            var riskScale = sessionWeight;

            var rv = AtrProxy(prices, 120) / price;
            if (rv > 0)
                riskScale *= Math.Min(1.2, Math.Max(1.0, VolTargetRef / rv));

            if (UseGovernor && RedDayStreak() >= 2)
            {
                riskScale *= GovernorCut;
                Print("Governor active: risk scaled to {0:P0}", GovernorCut);
            }

            riskScale = Math.Max(0.1, Math.Min(1.5, riskScale));

            var stopDist = price * (StopPercent / 100.0);
            var tpDist = price * (TakeProfitPercent / 100.0);
            if (stopDist <= 0) return;

            var riskUsd = Account.Equity * (RiskPercent / 100.0) * riskScale;
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
            var side = sig.Direction > 0 ? TradeType.Buy : TradeType.Sell;

            var result = ExecuteMarketOrder(side, SymbolName, units, Label, stopPips, tpPips);
            if (result.IsSuccessful)
                Print("OPEN [{0}] {1} {2} units @ {3:F2} | stop {4:F2} | target {5:F2} | scale {6:P0}",
                      sig.Name, side, units, price, price - sig.Direction * stopDist,
                      price + sig.Direction * tpDist, riskScale);
            else
                Print("ORDER FAILED [{0}]: {1}", sig.Name, result.Error);
        }

        private System.Collections.Generic.IEnumerable<Position> OwnPositions()
        {
            return Positions.Where(p => p.Label == Label && p.SymbolName == SymbolName);
        }

        private double DayStartEquity()
        {
            var midnight = Server.TimeInUtc.Date;
            var realized = History
                .Where(t => t.Label == Label && t.ClosingTime >= midnight)
                .Sum(t => t.NetProfit);
            return Account.Equity - realized;
        }

        private int LossesToday()
        {
            var midnight = Server.TimeInUtc.Date;
            return History.Count(t => t.Label == Label &&
                                      t.ClosingTime >= midnight &&
                                      t.NetProfit < 0);
        }

        private int RedDayStreak()
        {
            var streak = 0;
            for (var back = 1; back <= 10; back++)
            {
                var dayStart = Server.TimeInUtc.Date.AddDays(-back);
                var dayEnd = dayStart.AddDays(1);
                var trades = History
                    .Where(t => t.Label == Label &&
                                t.ClosingTime >= dayStart && t.ClosingTime < dayEnd)
                    .ToList();
                if (!trades.Any()) continue;
                if (trades.Sum(t => t.NetProfit) < 0) streak++;
                else break;
            }
            return streak;
        }

        // ================= MATH =================
        private double[] LastCloses(int count)
        {
            var n = Math.Min(count, Bars.ClosePrices.Count);
            var o = new double[n];
            for (var i = 0; i < n; i++)
                o[i] = Bars.ClosePrices.Last(n - 1 - i);
            return o;
        }

        private static double[] Tail(double[] v, int c)
        {
            return c >= v.Length ? v : v.Skip(v.Length - c).ToArray();
        }

        private static double[] Head(double[] v, int c)
        {
            return c >= v.Length ? v : v.Take(c).ToArray();
        }

        private static double Ema(double[] v, int period)
        {
            if (v.Length == 0) return 0.0;
            var k = 2.0 / (period + 1);
            var e = v[0];
            for (var i = 1; i < v.Length; i++)
                e = v[i] * k + e * (1 - k);
            return e;
        }

        private static double[] EmaSeries(double[] v, int period)
        {
            var k = 2.0 / (period + 1);
            var o = new double[v.Length];
            if (v.Length == 0) return o;
            o[0] = v[0];
            for (var i = 1; i < v.Length; i++)
                o[i] = v[i] * k + o[i - 1] * (1 - k);
            return o;
        }

        private static double StdDev(double[] v, double mean)
        {
            if (v.Length == 0) return 0.0;
            return Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / v.Length);
        }

        private static double MacdHist(double[] p)
        {
            if (p.Length < 40) return 0.0;
            var w = Tail(p, 104);
            var f = EmaSeries(w, 12);
            var s = EmaSeries(w, 26);
            var line = new double[w.Length];
            for (var i = 0; i < w.Length; i++)
                line[i] = f[i] - s[i];
            var sig = EmaSeries(line, 9);
            return line[line.Length - 1] - sig[sig.Length - 1];
        }

        private static double Rsi(double[] p, int period)
        {
            if (p.Length < period + 1) return 50.0;
            var w = Tail(p, period * 4);
            double gains = 0, losses = 0, avgG = 0, avgL = 0;
            var seeded = false;
            for (var i = 1; i < w.Length; i++)
            {
                var ch = w[i] - w[i - 1];
                var g = Math.Max(ch, 0.0);
                var l = Math.Max(-ch, 0.0);
                if (i < period + 1)
                {
                    gains += g;
                    losses += l;
                    if (i == period)
                    {
                        avgG = gains / period;
                        avgL = losses / period;
                        seeded = true;
                    }
                }
                else
                {
                    avgG = (avgG * (period - 1) + g) / period;
                    avgL = (avgL * (period - 1) + l) / period;
                }
            }
            if (!seeded || avgL == 0) return avgG > 0 ? 100.0 : 50.0;
            return 100.0 - 100.0 / (1.0 + avgG / avgL);
        }

        private static double AtrProxy(double[] p, int period)
        {
            if (p.Length < period + 1) return 0.0;
            var w = Tail(p, period + 1);
            var total = 0.0;
            for (var i = 1; i < w.Length; i++)
                total += Math.Abs(w[i] - w[i - 1]);
            return total / period;
        }

        private static double RealizedVol(double[] w)
        {
            if (w.Length < 2) return 0.0;
            var total = 0.0;
            var n = 0;
            for (var i = 1; i < w.Length; i++)
            {
                if (w[i - 1] <= 0 || w[i] <= 0) continue;
                total += Math.Abs(Math.Log(w[i] / w[i - 1]));
                n++;
            }
            return n > 0 ? total / n : 0.0;
        }

        private class Sig
        {
            public string Name { get; private set; }
            public int Direction { get; private set; }

            public Sig(string name, int direction)
            {
                Name = name;
                Direction = direction;
            }
        }
    }
}
