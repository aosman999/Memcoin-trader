// GoldBot — confluence gold strategy for cTrader (C#).
//
// Built ONLY from API calls verified working on the owner's machine
// (Account, Bars, Print, Indicators on the chart series). Deliberately
// avoids the constructs that caused the earlier NullReferenceException:
//   * no MarketData.GetBars (second timeframe loading)
//   * no Supertrend indicator
//   * no TickVolumes / VWAP
// The 15m trend read is derived from the 1m series instead.
//
// Six voters, each bull or bear on every bar:
//   1. EMA20 > EMA75            4. RSI > 50
//   2. price > EMA75            5. EMA20 rising (vs 3 bars back)
//   3. MACD histogram > 0       6. price > price 20 bars back
// Enters when >= VotesNeeded agree AND ADX >= AdxMin (chop filter).
// Exits: binary 2:1 broker-side (stop -0.6%, target +1.2%).
// Safety: demo-only lock, one position at a time, -15% daily loss stop,
// max hold, small-account guard. Everything wrapped in try/catch so any
// error prints its location instead of killing the bot.
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

        [Parameter("Multi-timeframe alignment (m15 + h1)", DefaultValue = true, Group = "Signal")]
        public bool UseMtfAlignment { get; set; }

        [Parameter("Risk per trade (%)", DefaultValue = 10.0, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Adaptive exits (ATR-based)", DefaultValue = true, Group = "Exits")]
        public bool UseAdaptiveExits { get; set; }

        [Parameter("ATR period", DefaultValue = 14, MinValue = 5, Group = "Exits")]
        public int AtrPeriod { get; set; }

        [Parameter("Stop = ATR x", DefaultValue = 1.5, MinValue = 0.3, Group = "Exits")]
        public double AtrStopMultiple { get; set; }

        [Parameter("Target scales with conviction", DefaultValue = true, Group = "Exits")]
        public bool ScaleTargetByConviction { get; set; }

        [Parameter("Reward:risk MIN (weak setup)", DefaultValue = 1.5, MinValue = 0.5, Group = "Exits")]
        public double RewardRiskMin { get; set; }

        [Parameter("Reward:risk MAX (strong setup)", DefaultValue = 4.0, MinValue = 1.0, Group = "Exits")]
        public double RewardRiskMax { get; set; }

        [Parameter("Early exit on signal flip", DefaultValue = true, Group = "Exits")]
        public bool EarlyExit { get; set; }

        [Parameter("Exit when opposite votes >= (5-6 = only strong flips)", DefaultValue = 6, MinValue = 3, MaxValue = 6, Group = "Exits")]
        public int ExitOppositeVotes { get; set; }

        [Parameter("Early exit only if trade is losing", DefaultValue = true, Group = "Exits")]
        public bool EarlyExitOnlyIfLosing { get; set; }

        [Parameter("Min stop (%)", DefaultValue = 0.25, MinValue = 0.05, Group = "Exits")]
        public double MinStopPercent { get; set; }

        [Parameter("Max stop (%)", DefaultValue = 1.2, MinValue = 0.1, Group = "Exits")]
        public double MaxStopPercent { get; set; }

        [Parameter("Fixed stop (%) when adaptive off", DefaultValue = 0.6, MinValue = 0.05, Group = "Exits")]
        public double StopPercent { get; set; }

        [Parameter("Fixed target (%) when adaptive off", DefaultValue = 1.2, MinValue = 0.1, Group = "Exits")]
        public double TakeProfitPercent { get; set; }

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
        private AverageTrueRange _atr;
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
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Simple);

            Print("GoldBot started | {0} | account {1} (DEMO) | balance {2:F2} | bars {3}",
                  SymbolName, Account.Number, Account.Balance, Bars.ClosePrices.Count);
            Print("Config: {0}/6 votes | ADX>={1} | MTF {2} | adaptive stop {3} | conviction target {4} | early exit {5} | risk {6}%",
                  VotesNeeded, AdxMin,
                  UseMtfAlignment ? "ON" : "OFF",
                  UseAdaptiveExits ? "ON" : "OFF",
                  ScaleTargetByConviction ? "ON" : "OFF",
                  EarlyExit ? "ON" : "OFF", RiskPercent);
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

            if (Bars.ClosePrices.Count < 120)
                return;

            // ---- read the market once ------------------------------------
            var close = Bars.ClosePrices.Last(0);
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

            // ---- higher-timeframe trend bias (resampled from the 1m
            // series, so no risky GetBars). +1 up, -1 down, 0 unknown. -----
            var h1Bias = UseMtfAlignment ? TrendBias(Resample(60, 140)) : 0;
            var m15Bias = UseMtfAlignment ? TrendBias(Resample(15, 260)) : 0;

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: price {0:F2} | m1 {1}b/{2}b | m15 {3} | h1 {4} | ADX {5:F1}",
                      close, bulls, bears, BiasText(m15Bias), BiasText(h1Bias), adx);

            // ---- manage open positions: time stop + early exit -----------
            foreach (var pos in OwnPositions().ToList())
            {
                if ((Server.TimeInUtc - pos.EntryTime).TotalMinutes >= MaxHoldMinutes)
                {
                    Print("Closing {0} — max hold {1} min reached.", pos.Id, MaxHoldMinutes);
                    ClosePosition(pos);
                    continue;
                }
                // early exit: the confluence has flipped against the trade,
                // so close now rather than wait for the stop or target.
                // early exit ONLY IF NEEDED: the signal has strongly flipped
                // AND (optionally) the trade is actually losing. A winning
                // trade heading to target is never cut.
                if (EarlyExit && !(EarlyExitOnlyIfLosing && pos.NetProfit >= 0))
                {
                    var flippedLong = pos.TradeType == TradeType.Buy && bears >= ExitOppositeVotes;
                    var flippedShort = pos.TradeType == TradeType.Sell && bulls >= ExitOppositeVotes;
                    if (flippedLong || flippedShort)
                    {
                        Print("EARLY EXIT {0} — strong reversal ({1} bull / {2} bear) | P&L {3:F2}",
                              pos.Id, bulls, bears, pos.NetProfit);
                        ClosePosition(pos);
                    }
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

            // ---- entry ---------------------------------------------------
            if (OwnPositions().Any())
                return;                              // one position at a time

            if (adx < AdxMin)
                return;                              // chop filter

            // multi-timeframe: only trade WITH the higher timeframes.
            // A long needs m15 and h1 not pointing down; short the reverse.
            var longOk = !UseMtfAlignment || (h1Bias >= 0 && m15Bias >= 0);
            var shortOk = !UseMtfAlignment || (h1Bias <= 0 && m15Bias <= 0);

            if (bulls >= VotesNeeded && longOk)
                OpenTrade(1, bulls, adx);
            else if (bears >= VotesNeeded && AllowShort && shortOk)
                OpenTrade(-1, bears, adx);
            else if (UseMtfAlignment && (bulls >= VotesNeeded || bears >= VotesNeeded)
                     && StatusEveryBars > 0)
                Print("skip: m1 says {0} but m15/h1 disagree (m15 {1}, h1 {2})",
                      bulls >= VotesNeeded ? "BUY" : "SELL",
                      BiasText(m15Bias), BiasText(h1Bias));
        }

        // Conviction 0..1 from vote margin and trend strength → maps the
        // reward:risk between the MIN and MAX so strong setups aim further.
        private double ConvictionRewardRisk(int votes, double adx)
        {
            var voteScore = (votes - VotesNeeded) / (6.0 - VotesNeeded + 1.0); // 0..1
            var adxScore = Math.Max(0.0, Math.Min(1.0, (adx - AdxMin) / 25.0)); // 0..1
            var conviction = Math.Max(0.0, Math.Min(1.0, 0.5 * voteScore + 0.5 * adxScore));
            return RewardRiskMin + conviction * (RewardRiskMax - RewardRiskMin);
        }

        private void OpenTrade(int direction, int votes, double adx)
        {
            var price = direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0)
                return;

            // ---- exits: adaptive to current volatility -------------------
            // The bot sets the distance from what the market is actually
            // doing (ATR), clamped so it can never be absurdly tight or
            // wide. Levels still go to the broker so they survive a crash.
            // reward:risk depends on how strong THIS setup is, so the target
            // varies trade to trade (a strong signal aims further).
            var rr = ScaleTargetByConviction ? ConvictionRewardRisk(votes, adx) : RewardRiskMin;

            double stopDist;
            double tpDist;
            if (UseAdaptiveExits)
            {
                var atr = _atr.Result.Last(0);
                var minDist = price * (MinStopPercent / 100.0);
                var maxDist = price * (MaxStopPercent / 100.0);
                stopDist = atr * AtrStopMultiple;
                if (double.IsNaN(stopDist) || stopDist <= 0)
                    stopDist = price * (StopPercent / 100.0);
                stopDist = Math.Max(minDist, Math.Min(maxDist, stopDist));
                tpDist = stopDist * rr;
            }
            else
            {
                stopDist = price * (StopPercent / 100.0);
                tpDist = stopDist * rr;
            }
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
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} ({4:F2}%) | target {5:F2} ({6:F2}%) | RR {7:F1} | {8}/6 votes, ADX {9:F0}",
                      side, units, price, price - direction * stopDist,
                      stopDist / price * 100.0,
                      price + direction * tpDist,
                      tpDist / price * 100.0, rr, votes, adx);
            else
                Print("ORDER FAILED: {0}", result.Error);
        }

        // Build a higher-timeframe close series by taking every `factor`-th
        // 1-minute close (factor 15 = m15, 60 = h1), newest last.
        private double[] Resample(int factor, int maxBars)
        {
            var have = Bars.ClosePrices.Count;
            var n = Math.Min(maxBars, have / factor);
            if (n < 80)
                return new double[0];
            var o = new double[n];
            for (var i = 0; i < n; i++)
                o[n - 1 - i] = Bars.ClosePrices.Last(i * factor);
            return o;
        }

        // Trend direction of a series: EMA20 vs EMA75. +1 up, -1 down,
        // 0 = not enough data (treated as "don't block").
        private static int TrendBias(double[] closes)
        {
            if (closes.Length < 80)
                return 0;
            var fast = Ema(closes, 20);
            var slow = Ema(closes, 75);
            return fast > slow ? 1 : -1;
        }

        private static double Ema(double[] v, int period)
        {
            if (v.Length == 0)
                return 0.0;
            var k = 2.0 / (period + 1);
            var e = v[0];
            for (var i = 1; i < v.Length; i++)
                e = v[i] * k + e * (1 - k);
            return e;
        }

        private static string BiasText(int b)
        {
            return b > 0 ? "UP" : b < 0 ? "DOWN" : "?";
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
