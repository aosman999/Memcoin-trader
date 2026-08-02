// GoldBot — confluence gold strategy for cTrader (C#). Single timeframe:
// runs on whatever chart you attach it to (use m1), no multi-timeframe.
//
// Six voters, each bull or bear on every bar:
//   1. EMA20 > EMA75            4. RSI > 50
//   2. price > EMA75            5. EMA20 rising (vs 3 bars back)
//   3. MACD histogram > 0       6. price > price 20 bars back
// Enters when >= VotesNeeded agree AND ADX >= AdxMin (chop filter).
// Exits: adaptive ATR stop, target scaled by conviction, plus an early
// exit only on a strong reversal while losing. Levels go broker-side.
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
            Print("Config: {0}/6 votes | ADX>={1} | adaptive stop {2} | conviction target {3} | early exit {4} | risk {5}%",
                  VotesNeeded, AdxMin,
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

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: price {0:F2} | {1} bull / {2} bear | ADX {3:F1} | ATR {4:F2}",
                      close, bulls, bears, adx, _atr.Result.Last(0));

            foreach (var pos in OwnPositions().ToList())
            {
                if ((Server.TimeInUtc - pos.EntryTime).TotalMinutes >= MaxHoldMinutes)
                {
                    Print("Closing {0} — max hold {1} min reached.", pos.Id, MaxHoldMinutes);
                    ClosePosition(pos);
                    continue;
                }
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

            var dayStart = DayStartEquity();
            if (dayStart > 0 &&
                Account.Equity <= dayStart * (1.0 - DailyLossStopPercent / 100.0))
            {
                foreach (var pos in OwnPositions().ToList())
                    ClosePosition(pos);
                return;
            }

            if (OwnPositions().Any())
                return;

            if (adx < AdxMin)
                return;

            if (bulls >= VotesNeeded)
                OpenTrade(1, bulls, adx);
            else if (bears >= VotesNeeded && AllowShort)
                OpenTrade(-1, bears, adx);
        }

        private double ConvictionRewardRisk(int votes, double adx)
        {
            var voteScore = (votes - VotesNeeded) / (6.0 - VotesNeeded + 1.0);
            var adxScore = Math.Max(0.0, Math.Min(1.0, (adx - AdxMin) / 25.0));
            var conviction = Math.Max(0.0, Math.Min(1.0, 0.5 * voteScore + 0.5 * adxScore));
            return RewardRiskMin + conviction * (RewardRiskMax - RewardRiskMin);
        }

        private void OpenTrade(int direction, int votes, double adx)
        {
            var price = direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0)
                return;

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
