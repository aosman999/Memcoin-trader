// GoldTraderBot — the certified gold strategy as a NATIVE cTrader cBot.
//
// Runs entirely inside cTrader (Automate tab). No Open API, no API keys,
// no webhooks, no external executor, no network access at all
// (AccessRights.None enforces that).
//
// DEMO-ONLY LOCK: refuses to run on a live account. Checked in OnStart
// AND on every bar; if the account is live the bot stops immediately
// without ever placing an order.
//
// Ported 1:1 from the certified Python champion (data/gold_params.json,
// 2026-07-21) — same strategies, same gates, same sizing, same exits:
//   Strategies : trend (EMA20/75 + slope), mean-reversion (z<=-2.2 in
//                range), breakout (120m high/low + range expansion),
//                pullback (retrace-and-turn at EMA20), mentor sweep
//                (ICT/TJR liquidity sweep + displacement, kill zones)
//   Gates      : event sentinel (news-shock veto, 12m cooldown),
//                session liquidity clock (min weight 0.6), MACD/RSI
//                confluence, 15-minute higher-timeframe filter
//   Sizing     : 10% risk per trade x session weight x calm-market boost
//                (boost-only vol targeting), governor halves risk after
//                2 straight red days
//   Exits      : BINARY 2:1 — stop -0.6%, target +1.2%, broker-side
//   Safety     : -15% daily loss stop, 3-loss daily discipline stop,
//                600-minute max hold, one position at a time
//
// Install: cTrader -> Automate -> New cBot -> paste this over everything
// -> Build -> attach to an XAUUSD chart -> Play.
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GoldTraderBot : Robot
    {
        // ---------------- certified champion parameters ----------------
        [Parameter("Risk per trade (%)", DefaultValue = 10.0, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Stop loss (%)", DefaultValue = 0.6, MinValue = 0.1, Group = "Risk")]
        public double StopPercent { get; set; }

        [Parameter("Take profit (%)", DefaultValue = 1.2, MinValue = 0.2, Group = "Risk")]
        public double TakeProfitPercent { get; set; }

        [Parameter("Daily loss stop (%)", DefaultValue = 15.0, MinValue = 1.0, Group = "Risk")]
        public double DailyLossStopPercent { get; set; }

        [Parameter("Max losses per day", DefaultValue = 3, MinValue = 1, Group = "Risk")]
        public int MaxLossesPerDay { get; set; }

        [Parameter("Max hold (minutes)", DefaultValue = 600, MinValue = 10, Group = "Risk")]
        public int MaxHoldMinutes { get; set; }

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

        [Parameter("Use governor (2 red days -> half risk)", DefaultValue = true, Group = "Gates")]
        public bool UseGovernor { get; set; }

        [Parameter("Governor cut", DefaultValue = 0.5, MinValue = 0.1, MaxValue = 1.0, Group = "Gates")]
        public double GovernorCut { get; set; }

        [Parameter("Vol-target reference", DefaultValue = 0.00038206155152861575, Group = "Gates")]
        public double VolTargetRef { get; set; }

        [Parameter("Allow shorts", DefaultValue = true, Group = "Gates")]
        public bool AllowShort { get; set; }

        // ---------------- internals ----------------
        private const string Label = "GoldTraderBot";
        private const int MinBars = 260;          // deepest lookback + margin
        private Bars _m1;                          // always 1-minute, whatever the chart shows
        private Bars _m15;                         // higher-timeframe filter
        private DateTime _sentinelBlockedUntil = DateTime.MinValue;
        private bool _locked;                      // demo-lock tripped

        protected override void OnStart()
        {
            if (!EnforceDemoOnly())
                return;

            _m1 = MarketData.GetBars(TimeFrame.Minute);
            _m15 = MarketData.GetBars(TimeFrame.Minute15);

            // drive off 1-MINUTE bars regardless of the chart's timeframe:
            // the certified strategy is a 1-minute system.
            _m1.BarOpened += OnMinuteBar;

            Print("GoldTraderBot started on {0} | account {1} ({2}) | balance {3:F2} {4}",
                  SymbolName, Account.Number, Account.IsLive ? "LIVE" : "DEMO",
                  Account.Balance, Account.Currency);
            Print("Certified config: risk {0}% | stop {1}% | target {2}% (2:1) | day stop -{3}%",
                  RiskPercent, StopPercent, TakeProfitPercent, DailyLossStopPercent);
        }

        private void OnMinuteBar(BarOpenedEventArgs args)
        {
            if (_locked || !EnforceDemoOnly())
                return;

            ManageOpenPosition();

            if (_m1 == null || _m1.ClosePrices.Count < MinBars)
                return;

            var now = Server.TimeInUtc;

            // ---- daily loss stop (-15%), restart-proof --------------------
            var dayStartEquity = ReconstructDayStartEquity();
            if (dayStartEquity > 0 &&
                Account.Equity <= dayStartEquity * (1.0 - DailyLossStopPercent / 100.0))
            {
                CloseAll("daily loss stop");
                return;
            }

            // ---- Valentini 3-loss daily discipline stop ------------------
            if (LossesToday() >= MaxLossesPerDay)
                return;

            // ---- one position at a time ---------------------------------
            if (OwnPositions().Any())
                return;

            // ---- event sentinel: news-grade move in progress -------------
            if (UseSentinel && !SentinelSafe(now))
                return;

            // ---- session liquidity clock --------------------------------
            var sessionWeight = SessionWeight(now);
            if (sessionWeight < SessionMinWeight)
                return;

            // ---- strategy cascade (certified order) ----------------------
            var signal = Evaluate(now);
            if (signal == null)
                return;

            OpenTrade(signal, sessionWeight);
        }

        protected override void OnStop()
        {
            Print("GoldTraderBot stopped. Open positions keep their broker-side SL/TP.");
        }

        // ================= DEMO-ONLY LOCK =================
        private bool EnforceDemoOnly()
        {
            if (!Account.IsLive)
                return true;

            _locked = true;
            Print("REFUSING TO RUN: this is a LIVE account. GoldTraderBot is demo-only by design.");
            Print("No order was placed. Attach the bot to a DEMO account instead.");
            Stop();
            return false;
        }

        // ================= STRATEGIES =================
        private Signal Evaluate(DateTime now)
        {
            var closes = LastCloses(MinBars);

            // mentor sweep runs first (kill-zone gated), then the certified trio
            var candidates = new Func<double[], DateTime, Signal>[]
            {
                UseMentorSweep ? (Func<double[], DateTime, Signal>)MentorSweepSignal : null,
                (p, t) => TrendSignal(p),
                (p, t) => MeanRevSignal(p),
                (p, t) => BreakoutSignal(p),
                (p, t) => PullbackSignal(p),
            };

            foreach (var strategy in candidates)
            {
                if (strategy == null)
                    continue;

                var sig = strategy(closes, now);
                if (sig == null)
                    continue;
                if (sig.Direction < 0 && !AllowShort)
                    continue;
                if (UseIndicators && !ConfluenceOk(sig.Strategy, sig.Direction, closes))
                    continue;

                return sig;
            }
            return null;
        }

        private Signal TrendSignal(double[] p)
        {
            var need = EmaSlow + 5;
            if (p.Length < need)
                return null;

            var window = Tail(p, need);
            var fast = Ema(window, EmaFast);
            var slow = Ema(window, EmaSlow);
            var slowPrev = Ema(Head(window, window.Length - 5), EmaSlow);
            var slope = slow != 0 ? (slow - slowPrev) / (5 * slow) : 0.0;
            var last = p[p.Length - 1];

            // NOTE: the 15-minute filter gates the TREND strategy only —
            // that is exactly how the certified Python champion works.
            if (fast > slow && slope >= TrendMinSlope && last >= fast)
            {
                if (UseMtf && !MtfAgrees(1))
                    return null;
                return new Signal("trend", 1, string.Format("EMA{0}>{1} rising", EmaFast, EmaSlow));
            }
            if (fast < slow && slope <= -TrendMinSlope && last <= fast)
            {
                if (UseMtf && !MtfAgrees(-1))
                    return null;
                return new Signal("trend", -1, string.Format("EMA{0}<{1} falling", EmaFast, EmaSlow));
            }
            return null;
        }

        private Signal MeanRevSignal(double[] p)
        {
            if (p.Length < 2 * MrWindow)
                return null;

            var window = Tail(p, MrWindow);
            var mean = window.Average();
            var sd = StdDev(window, mean);
            if (sd <= 0)
                return null;

            var z = (p[p.Length - 1] - mean) / sd;
            var prior = p.Skip(p.Length - 2 * MrWindow).Take(MrWindow).ToArray();
            var trendZ = (mean - prior.Average()) / sd;
            if (Math.Abs(trendZ) > MrMaxTrendZ)
                return null;                       // never fade a strong trend

            if (z <= -MrEntryZ)
                return new Signal("meanrev", 1, string.Format("dip z={0:F2} in range", z));
            if (z >= MrEntryZ)
                return new Signal("meanrev", -1, string.Format("stretch z={0:F2} in range", z));
            return null;
        }

        private Signal BreakoutSignal(double[] p)
        {
            if (p.Length < BoLookback + 10)
                return null;

            var lookback = p.Skip(p.Length - BoLookback - 1).Take(BoLookback).ToArray();
            var hi = lookback.Max();
            var lo = lookback.Min();
            var recent = Tail(p, 15);
            var recentRange = recent.Max() - recent.Min();
            var older = lookback.Take(60).ToArray();
            var olderRange = older.Length >= 60 ? (older.Max() - older.Min()) / 4.0 : 0.0;
            if (olderRange <= 0 || recentRange < BoRangeExpansion * olderRange)
                return null;                       // no range expansion

            var last = p[p.Length - 1];
            if (last > hi)
                return new Signal("breakout", 1, string.Format("broke {0}m high {1:F2}", BoLookback, hi));
            if (last < lo)
                return new Signal("breakout", -1, string.Format("broke {0}m low {1:F2}", BoLookback, lo));
            return null;
        }

        private Signal PullbackSignal(double[] p)
        {
            var need = EmaSlow + 10;
            if (p.Length < need)
                return null;

            var window = Tail(p, need);
            var fast = Ema(window, EmaFast);
            var slow = Ema(window, EmaSlow);
            var slowPrev = Ema(Head(window, window.Length - 5), EmaSlow);
            var slope = slow != 0 ? (slow - slowPrev) / (5 * slow) : 0.0;
            var last = p[p.Length - 1];
            var prev = p[p.Length - 2];
            var last4 = Tail(p, 4);
            var band = PullbackBand * last;

            if (fast > slow && slope >= TrendMinSlope &&
                Math.Abs(last4.Min() - fast) <= band && last > prev)
                return new Signal("pullback", 1, "pullback to fast EMA in uptrend");
            if (fast < slow && slope <= -TrendMinSlope &&
                Math.Abs(last4.Max() - fast) <= band && last < prev)
                return new Signal("pullback", -1, "pullback to fast EMA in downtrend");
            return null;
        }

        private Signal MentorSweepSignal(double[] p, DateTime now)
        {
            if (p.Length < SweepLookback + 6 || !InKillZone(now))
                return null;

            // levels BEFORE the sweep, then the sweep + rejection window
            var window = p.Skip(p.Length - SweepLookback - 5).Take(SweepLookback).ToArray();
            var recent = Tail(p, 5);
            var hi = window.Max();
            var lo = window.Min();
            var last = p[p.Length - 1];
            var atr = AtrProxy(p, 14);
            if (atr <= 0)
                return null;

            var sweptHigh = recent.Max() > hi * (1 + SweepMargin);
            var sweptLow = recent.Min() < lo * (1 - SweepMargin);

            if (sweptHigh && last < hi && (recent.Max() - last) >= SweepAtrMult * atr)
                return new Signal("mentor_sweep", -1, string.Format("swept high {0:F2}, fast rejection", hi));
            if (sweptLow && last > lo && (last - recent.Min()) >= SweepAtrMult * atr)
                return new Signal("mentor_sweep", 1, string.Format("swept low {0:F2}, fast rejection", lo));
            return null;
        }

        // ================= GATES =================
        private bool ConfluenceOk(string strategy, int direction, double[] p)
        {
            // Certified confluence: MACD agreement for trend/breakout, RSI
            // stretch for mean-reversion. Pullback and mentor-sweep carry
            // their own structural confirmation and are NOT gated here —
            // that is how the champion was A/B-certified.
            if (strategy == "trend" || strategy == "breakout")
            {
                var hist = MacdHistogram(p);
                return direction > 0 ? hist > 0 : hist < 0;
            }
            if (strategy == "meanrev")
            {
                var r = Rsi(p, 14);
                return direction > 0 ? r <= 34.0 : r >= 66.0;
            }
            return true;
        }

        private bool MtfAgrees(int direction)
        {
            if (_m15 == null || _m15.ClosePrices.Count < 25)
                return true;

            var bars = new double[21];
            for (var i = 0; i < 21; i++)
                bars[i] = _m15.ClosePrices.Last(20 - i);

            var now = Ema(bars, 20);
            var prev = Ema(Head(bars, 20), 20);
            return (now - prev) * direction >= 0;
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
            var burst = RealizedVol(Tail(p, 6));
            var baseline = RealizedVol(Tail(p, 45));

            if (lastMove > 0.0022 || (baseline > 0 && burst / baseline > 3.5))
            {
                _sentinelBlockedUntil = now.AddMinutes(12);
                Print("Sentinel: news-grade move detected — entries blocked for 12 minutes.");
                return false;
            }
            return true;
        }

        private double SessionWeight(DateTime utc)
        {
            var h = utc.Hour + utc.Minute / 60.0;
            if (h >= 12 && h < 16) return 1.2;     // London/NY overlap
            if (h >= 7 && h < 12) return 1.0;      // London
            if (h >= 16 && h < 21) return 0.9;     // NY afternoon
            if (h >= 1 && h < 7) return 0.6;       // Asia
            return 0.4;                            // around the daily close
        }

        private static bool InKillZone(DateTime utc)
        {
            var h = utc.Hour + utc.Minute / 60.0;
            return (h >= 7.0 && h < 10.0) || (h >= 12.0 && h < 15.0);
        }

        // ================= SIZING & EXECUTION =================
        private void OpenTrade(Signal signal, double sessionWeight)
        {
            var price = signal.Direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0)
                return;

            var riskScale = sessionWeight;

            // boost-only volatility targeting: upsize in calm tape, never shrink
            var rv = AtrProxy(LastCloses(121), 120) / price;
            if (rv > 0)
                riskScale *= Math.Min(1.2, Math.Max(1.0, VolTargetRef / rv));

            // hostile-weather governor: 2 straight red days -> trade smaller
            if (UseGovernor && RedDayStreak() >= 2)
            {
                riskScale *= GovernorCut;
                Print("Governor active (2+ red days): risk scaled to {0:P0}", GovernorCut);
            }

            riskScale = Math.Max(0.1, Math.Min(1.5, riskScale));

            var stopDistance = price * (StopPercent / 100.0);
            var tpDistance = price * (TakeProfitPercent / 100.0);
            if (stopDistance <= 0)
                return;

            // risk-based sizing: 1 unit of gold moves $1 per $1 of price
            var riskUsd = Account.Equity * (RiskPercent / 100.0) * riskScale;
            var rawUnits = riskUsd / stopDistance;
            var units = Symbol.NormalizeVolumeInUnits(rawUnits, RoundingMode.Down);

            // account-too-small guard: the minimum trade must not force >2x risk
            var minRisk = Symbol.VolumeInUnitsMin * stopDistance;
            if (minRisk > Account.Equity * (RiskPercent / 100.0) * 2.0)
            {
                Print("SKIP: account too small — minimum {0} units risks {1:F2}. Fund to ~{2:F0}+.",
                      Symbol.VolumeInUnitsMin, minRisk, minRisk / (RiskPercent / 100.0));
                return;
            }
            if (units < Symbol.VolumeInUnitsMin)
                units = Symbol.VolumeInUnitsMin;

            var stopPips = stopDistance / Symbol.PipSize;
            var tpPips = tpDistance / Symbol.PipSize;
            var side = signal.Direction > 0 ? TradeType.Buy : TradeType.Sell;

            var result = ExecuteMarketOrder(side, SymbolName, units, Label, stopPips, tpPips,
                                            signal.Strategy);
            if (result.IsSuccessful)
                Print("OPEN [{0}] {1} {2} units @ {3:F2} | stop {4:F2} target {5:F2} | risk {6:F2} ({7}) — {8}",
                      signal.Strategy, side, units, price,
                      price - signal.Direction * stopDistance,
                      price + signal.Direction * tpDistance,
                      riskUsd, string.Format("{0:P0} scale", riskScale), signal.Reason);
            else
                Print("ORDER FAILED [{0}]: {1}", signal.Strategy, result.Error);
        }

        private void ManageOpenPosition()
        {
            foreach (var position in OwnPositions())
            {
                var held = (Server.TimeInUtc - position.EntryTime).TotalMinutes;
                if (held >= MaxHoldMinutes)
                {
                    Print("Closing {0} — max hold {1} minutes reached.", position.Id, MaxHoldMinutes);
                    ClosePosition(position);
                }
            }
        }

        private void CloseAll(string reason)
        {
            var open = OwnPositions().ToList();
            if (!open.Any())
                return;

            Print("DAY GUARD [{0}] — closing {1} position(s); flat until tomorrow (UTC).",
                  reason, open.Count);
            foreach (var position in open)
                ClosePosition(position);
        }

        private System.Collections.Generic.IEnumerable<Position> OwnPositions()
        {
            return Positions.Where(p => p.Label == Label && p.SymbolName == SymbolName);
        }

        // ================= DAILY STATE (reconstructed, restart-proof) =====
        private double ReconstructDayStartEquity()
        {
            // equity now minus everything this bot realized today = today's baseline
            var midnight = Server.TimeInUtc.Date;
            var realizedToday = History
                .Where(t => t.Label == Label && t.ClosingTime >= midnight)
                .Sum(t => t.NetProfit);
            return Account.Equity - realizedToday;
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

                if (!trades.Any())
                    continue;                      // no trading that day: skip it
                if (trades.Sum(t => t.NetProfit) < 0)
                    streak++;
                else
                    break;                         // a green day breaks the streak
            }
            return streak;
        }

        // ================= MATH (ported verbatim from the Python) =========
        private double[] LastCloses(int count)
        {
            var available = Math.Min(count, _m1.ClosePrices.Count);
            var output = new double[available];
            for (var i = 0; i < available; i++)
                output[i] = _m1.ClosePrices.Last(available - 1 - i);
            return output;
        }

        private static double[] Tail(double[] values, int count)
        {
            return count >= values.Length ? values : values.Skip(values.Length - count).ToArray();
        }

        private static double[] Head(double[] values, int count)
        {
            return count >= values.Length ? values : values.Take(count).ToArray();
        }

        private static double Ema(double[] values, int period)
        {
            if (values.Length == 0)
                return 0.0;

            var k = 2.0 / (period + 1);
            var ema = values[0];
            for (var i = 1; i < values.Length; i++)
                ema = values[i] * k + ema * (1 - k);
            return ema;
        }

        private static double[] EmaSeries(double[] values, int period)
        {
            var k = 2.0 / (period + 1);
            var output = new double[values.Length];
            if (values.Length == 0)
                return output;

            output[0] = values[0];
            for (var i = 1; i < values.Length; i++)
                output[i] = values[i] * k + output[i - 1] * (1 - k);
            return output;
        }

        private static double StdDev(double[] values, double mean)
        {
            if (values.Length == 0)
                return 0.0;

            var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Length;
            return Math.Sqrt(variance);
        }

        private static double MacdHistogram(double[] prices, int fast = 12, int slow = 26, int signal = 9)
        {
            if (prices.Length < slow + signal + 5)
                return 0.0;

            var window = Tail(prices, slow * 4);
            var emaFast = EmaSeries(window, fast);
            var emaSlow = EmaSeries(window, slow);
            var macdLine = new double[window.Length];
            for (var i = 0; i < window.Length; i++)
                macdLine[i] = emaFast[i] - emaSlow[i];

            var signalLine = EmaSeries(macdLine, signal);
            return macdLine[macdLine.Length - 1] - signalLine[signalLine.Length - 1];
        }

        private static double Rsi(double[] prices, int period)
        {
            if (prices.Length < period + 1)
                return 50.0;

            var window = Tail(prices, period * 4);
            double gains = 0, losses = 0, avgGain = 0, avgLoss = 0;
            var seeded = false;

            for (var i = 1; i < window.Length; i++)
            {
                var change = window[i] - window[i - 1];
                var g = Math.Max(change, 0.0);
                var l = Math.Max(-change, 0.0);

                if (i < period + 1)
                {
                    gains += g;
                    losses += l;
                    if (i == period)
                    {
                        avgGain = gains / period;
                        avgLoss = losses / period;
                        seeded = true;
                    }
                }
                else
                {
                    avgGain = (avgGain * (period - 1) + g) / period;
                    avgLoss = (avgLoss * (period - 1) + l) / period;
                }
            }

            if (!seeded || avgLoss == 0)
                return avgGain > 0 ? 100.0 : 50.0;

            var rs = avgGain / avgLoss;
            return 100.0 - 100.0 / (1.0 + rs);
        }

        private static double AtrProxy(double[] prices, int period)
        {
            if (prices.Length < period + 1)
                return 0.0;

            var window = Tail(prices, period + 1);
            var total = 0.0;
            for (var i = 1; i < window.Length; i++)
                total += Math.Abs(window[i] - window[i - 1]);
            return total / period;
        }

        private static double RealizedVol(double[] window)
        {
            if (window.Length < 2)
                return 0.0;

            var total = 0.0;
            var n = 0;
            for (var i = 1; i < window.Length; i++)
            {
                if (window[i - 1] <= 0 || window[i] <= 0)
                    continue;
                total += Math.Abs(Math.Log(window[i] / window[i - 1]));
                n++;
            }
            return n > 0 ? total / n : 0.0;
        }

        private class Signal
        {
            public string Strategy { get; private set; }
            public int Direction { get; private set; }
            public string Reason { get; private set; }

            public Signal(string strategy, int direction, string reason)
            {
                Strategy = strategy;
                Direction = direction;
                Reason = reason;
            }
        }
    }
}
