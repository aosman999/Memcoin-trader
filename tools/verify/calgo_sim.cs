// A WORKING cAlgo.API, not a compile-only stub. Enough of the real API to
// actually RUN the cBot bar by bar against a simulated broker: indicators
// that compute, positions that fill and get stopped out, an account whose
// equity moves. Compiling proves the file parses; this proves it behaves.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace cAlgo.API
{
    public enum AccessRights { None, FullAccess }
    public enum TradeType { Buy, Sell }
    public enum MovingAverageType { Simple, Exponential, Weighted }
    public enum RoundingMode { Down, Up, ToNearest }

    public class TimeZones { public const string UTC = "UTC"; }

    public class RobotAttribute : Attribute
    {
        public string TimeZone { get; set; }
        public AccessRights AccessRights { get; set; }
    }

    public class ParameterAttribute : Attribute
    {
        public ParameterAttribute(string name) { }
        public object DefaultValue { get; set; }
        public object MinValue { get; set; }
        public object MaxValue { get; set; }
        public string Group { get; set; }
    }

    public class TimeFrame
    {
        private readonly string _n; private readonly int _m;
        private TimeFrame(string n, int m) { _n = n; _m = m; }
        public int Minutes { get { return _m; } }
        public static readonly TimeFrame Minute = new TimeFrame("m1", 1);
        public static readonly TimeFrame Minute2 = new TimeFrame("m2", 2);
        public static readonly TimeFrame Minute3 = new TimeFrame("m3", 3);
        public static readonly TimeFrame Minute4 = new TimeFrame("m4", 4);
        public static readonly TimeFrame Minute5 = new TimeFrame("m5", 5);
        public static readonly TimeFrame Minute6 = new TimeFrame("m6", 6);
        public static readonly TimeFrame Minute10 = new TimeFrame("m10", 10);
        public static readonly TimeFrame Minute15 = new TimeFrame("m15", 15);
        public static readonly TimeFrame Minute20 = new TimeFrame("m20", 20);
        public static readonly TimeFrame Minute30 = new TimeFrame("m30", 30);
        public static readonly TimeFrame Minute45 = new TimeFrame("m45", 45);
        public static readonly TimeFrame Hour = new TimeFrame("h1", 60);
        public static readonly TimeFrame Hour2 = new TimeFrame("h2", 120);
        public static readonly TimeFrame Hour3 = new TimeFrame("h3", 180);
        public static readonly TimeFrame Hour4 = new TimeFrame("h4", 240);
        public static readonly TimeFrame Hour6 = new TimeFrame("h6", 360);
        public static readonly TimeFrame Hour8 = new TimeFrame("h8", 480);
        public static readonly TimeFrame Hour12 = new TimeFrame("h12", 720);
        public static readonly TimeFrame Daily = new TimeFrame("d1", 1440);
        public static bool operator ==(TimeFrame a, TimeFrame b) { return ReferenceEquals(a, b); }
        public static bool operator !=(TimeFrame a, TimeFrame b) { return !ReferenceEquals(a, b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return _n.GetHashCode(); }
        public override string ToString() { return _n; }
    }

    // Series indexed backwards from the current bar, as cTrader does.
    public class DataSeries
    {
        private readonly List<double> _v = new List<double>();
        private readonly Sim _sim;
        public DataSeries(Sim s) { _sim = s; }
        public void Push(double x) { _v.Add(x); }
        public double Last(int index)
        {
            var i = _sim.Cursor - index;
            if (i < 0 || i >= _v.Count)
                throw new IndexOutOfRangeException(
                    "DataSeries.Last(" + index + ") off the end — the bot read past available history.");
            return _v[i];
        }
        public int Count { get { return _sim.Cursor + 1; } }
        public double this[int i] { get { return _v[i]; } }
    }

    public class Sim { public int Cursor; }

    public class Bars
    {
        public DataSeries ClosePrices, HighPrices, LowPrices, OpenPrices, TickVolumes;
        public TimeFrame TimeFrame;
        public int Count { get { return ClosePrices.Count; } }
    }

    public class IndicatorDataSeries : DataSeries
    {
        public IndicatorDataSeries(Sim s) : base(s) { }
    }

    public class ExponentialMovingAverage { public IndicatorDataSeries Result; }
    public class RelativeStrengthIndex { public IndicatorDataSeries Result; }
    public class MacdHistogram { public IndicatorDataSeries Histogram; }
    public class DirectionalMovementSystem { public IndicatorDataSeries ADX, DIPlus, DIMinus; }
    public class AverageTrueRange { public IndicatorDataSeries Result; }

    public class Position
    {
        public int Id;
        public string Label, SymbolName;
        public TradeType TradeType;
        public double EntryPrice, VolumeInUnits, NetProfit;
        public double? StopLoss, TakeProfit;
        public DateTime EntryTime;
    }

    public class HistoricalTrade
    {
        public int PositionId;
        public string Label, SymbolName;
        public TradeType TradeType;
        public DateTime EntryTime, ClosingTime;
        public double EntryPrice, ClosingPrice, NetProfit;
    }

    public class TradeResult
    {
        public bool IsSuccessful;
        public string Error;
        public Position Position;
    }

    public class Positions : IEnumerable<Position>
    {
        public readonly List<Position> Items = new List<Position>();
        public IEnumerator<Position> GetEnumerator() { return Items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return Items.GetEnumerator(); }
    }

    public class History : IEnumerable<HistoricalTrade>
    {
        public readonly List<HistoricalTrade> Items = new List<HistoricalTrade>();
        public IEnumerator<HistoricalTrade> GetEnumerator() { return Items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return Items.GetEnumerator(); }
    }
}

namespace cAlgo.API.Internals
{
    public class Symbol
    {
        public double Ask, Bid, PipSize = 0.01, TickSize = 0.01,
                      VolumeInUnitsMin = 1.0, VolumeInUnitsStep = 1.0;
        public double NormalizeVolumeInUnits(double units, RoundingMode m)
        {
            if (double.IsNaN(units) || double.IsInfinity(units)) return VolumeInUnitsMin;
            var n = Math.Floor(units / VolumeInUnitsStep) * VolumeInUnitsStep;
            return n < 0 ? 0 : n;
        }
    }

    public class Account
    {
        public double Balance = 3000.0, Equity = 3000.0;
        public int Number = 12345;
        public bool IsLive = false;
        public string Currency = "USD";
    }

    public class Server { public DateTime TimeInUtc; }

    // Higher-timeframe bars, so the top-down model can be exercised by the
    // behaviour tests. A bot that asks for h4 bars and gets null is a bot whose
    // top-down path is never tested.
    public class MarketDataStub
    {
        public Func<cAlgo.API.TimeFrame, string, cAlgo.API.Bars> Provider;
        public cAlgo.API.Bars GetBars(cAlgo.API.TimeFrame tf, string symbolName)
        {
            return Provider == null ? null : Provider(tf, symbolName);
        }
        public cAlgo.API.Bars GetBars(cAlgo.API.TimeFrame tf) { return GetBars(tf, null); }
    }
}

namespace cAlgo.API.Indicators
{
    using cAlgo.API;
    public class IndicatorFactory
    {
        public Func<DataSeries, int, ExponentialMovingAverage> MakeEma;
        public Func<DataSeries, int, RelativeStrengthIndex> MakeRsi;
        public Func<DataSeries, int, int, int, MacdHistogram> MakeMacd;
        public Func<int, DirectionalMovementSystem> MakeDms;
        public Func<int, MovingAverageType, AverageTrueRange> MakeAtr;
        public ExponentialMovingAverage ExponentialMovingAverage(DataSeries s, int p) { return MakeEma(s, p); }
        public RelativeStrengthIndex RelativeStrengthIndex(DataSeries s, int p) { return MakeRsi(s, p); }
        public MacdHistogram MacdHistogram(DataSeries s, int a, int b, int c) { return MakeMacd(s, a, b, c); }
        public DirectionalMovementSystem DirectionalMovementSystem(int p) { return MakeDms(p); }
        public AverageTrueRange AverageTrueRange(int p, MovingAverageType t) { return MakeAtr(p, t); }
    }
}

namespace cAlgo.API
{
    using cAlgo.API.Internals;
    using cAlgo.API.Indicators;

    public abstract class Robot
    {
        public Bars Bars;
        public Symbol Symbol;
        public Account Account;
        public Server Server;
        public IndicatorFactory Indicators;
        public cAlgo.API.Internals.MarketDataStub MarketData =
            new cAlgo.API.Internals.MarketDataStub();
        public Positions Positions = new Positions();
        public History History = new History();
        public string SymbolName = "XAUUSD";

        public bool Stopped;
        public readonly List<string> Log = new List<string>();
        public Func<TradeType, string, double, string, double, double, TradeResult> OnOrder;
        public Func<Position, double?, double?, TradeResult> OnModify;
        public Func<Position, TradeResult> OnClose;

        protected virtual void OnStart() { }
        protected virtual void OnBar() { }
        protected virtual void OnTick() { }
        protected virtual void OnStop() { }

        // Drivers — the test harness lives outside the class, so it needs a
        // public way in without changing the bot's own access modifiers.
        public void DriveStart() { OnStart(); }
        public void DriveBar() { OnBar(); }
        public void DriveTick() { OnTick(); }
        public void DriveStop() { OnStop(); }

        public void Print(string format, params object[] args)
        {
            Log.Add(args.Length == 0 ? format : string.Format(format, args));
        }
        public void Print(object o) { Log.Add(o == null ? "" : o.ToString()); }
        public void Stop() { Stopped = true; }
        public void BeginInvokeOnMainThread(Action a) { a(); }

        public TradeResult ExecuteMarketOrder(TradeType side, string sym, double units,
                                              string label, double stopPips, double tpPips)
        {
            return OnOrder(side, sym, units, label, stopPips, tpPips);
        }
        public TradeResult ModifyPosition(Position p, double? sl, double? tp) { return OnModify(p, sl, tp); }
        public TradeResult ClosePosition(Position p) { return OnClose(p); }
        public TradeResult ClosePosition(Position p, double volume) { return OnClose(p); }
    }
}
