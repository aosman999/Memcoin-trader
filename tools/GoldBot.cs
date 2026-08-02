// GoldBot — confluence gold strategy for cTrader (C#).
//
// Runs on m1 for entries/exits, but reads the real m15, h1 and h4
// timeframes for trend alignment (loaded via MarketData.GetBars, each
// wrapped so a load failure degrades to neutral instead of crashing).
// Avoids Supertrend and TickVolumes/VWAP (other earlier crash suspects).
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
        private ExponentialMovingAverage _m15Fast, _m15Slow;
        private ExponentialMovingAverage _h1Fast, _h1Slow;
        private ExponentialMovingAverage _h4Fast, _h4Slow;
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

            // Load the real higher timeframes for alignment (m15/h1/h4).
            // Protected: if a timeframe fails to load, it's treated as
            // neutral and the bot keeps running.
            LoadTf(TimeFrame.Minute15, "m15", ref _m15Fast, ref _m15Slow);
            LoadTf(TimeFrame.Hour, "h1", ref _h1Fast, ref _h1Slow);
            LoadTf(TimeFrame.Hour4, "h4", ref _h4Fast, ref _h4Slow);

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

            // ---- higher-timeframe trend bias (real m15/h1/h4 series).
            // +1 up, -1 down, 0 unknown/unavailable (treated as neutral). --
            var m15Bias = UseMtfAlignment ? TfBias(_m15Fast, _m15Slow) : 0;
            var h1Bias = UseMtfAlignment ? TfBias(_h1Fast, _h1Slow) : 0;
            var h4Bias = UseMtfAlignment ? TfBias(_h4Fast, _h4Slow) : 0;

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: price {0:F2} | m1 {1}b/{2}b | m15 {3} | h1 {4} | h4 {5} | ADX {6:F1}",
                      close, bulls, bears, BiasText(m15Bias), BiasText(h1Bias),
                      BiasText(h4Bias), adx);

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

            // multi-timeframe: only trade WITH all higher timeframes.
            // A long needs m15/h1/h4 not pointing down; short the reverse.
            var longOk = !UseMtfAlignment || (m15Bias >= 0 && h1Bias >= 0 && h4Bias >= 0);
            var shortOk = !UseMtfAlignment || (m15Bias <= 0 && h1Bias <= 0 && h4Bias <= 0);

            if (bulls >= VotesNeeded && longOk)
                OpenTrade(1, bulls, adx);
            else if (bears >= VotesNeeded && AllowShort && shortOk)
                OpenTrade(-1, bears, adx);
            else if (UseMtfAlignment && (bulls >= VotesNeeded || bears >= VotesNeeded)
                     && StatusEveryBars > 0)
                Print("skip: m1 says {0} but higher TFs disagree (m15 {1}, h1 {2}, h4 {3})",
                      bulls >= VotesNeeded ? "BUY" : "SELL",
                      BiasText(m15Bias), BiasText(h1Bias), BiasText(h4Bias));
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

        // Load a higher timeframe and build its EMA20/EMA75. Protected:
        // any failure leaves the EMAs null (treated as neutral) and the
        // bot keeps running rather than crashing.
        private void LoadTf(TimeFrame tf, string name,
                            ref ExponentialMovingAverage fast,
                            ref ExponentialMovingAverage slow)
        {
            try
            {
                var bars = MarketData.GetBars(tf);
                fast = Indicators.ExponentialMovingAverage(bars.ClosePrices, 20);
                slow = Indicators.ExponentialMovingAverage(bars.ClosePrices, 75);
                Print("Loaded {0} for alignment ({1} bars).", name, bars.ClosePrices.Count);
            }
            catch (Exception ex)
            {
                Print("Could not load {0} ({1}) — treated as neutral.", name, ex.GetType().Name);
                fast = null;
                slow = null;
            }
        }

        // Trend direction of a loaded timeframe: EMA20 vs EMA75.
        // +1 up, -1 down, 0 = unavailable / not enough data.
        private static int TfBias(ExponentialMovingAverage fast, ExponentialMovingAverage slow)
        {
            if (fast == null || slow == null)
                return 0;
            try
            {
                if (fast.Result.Count < 76)
                    return 0;
                return fast.Result.Last(0) > slow.Result.Last(0) ? 1 : -1;
            }
            catch
            {
                return 0;
            }
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
