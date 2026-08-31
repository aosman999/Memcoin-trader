// Minimal cAlgo.API surface — enough to COMPILE a cBot offline and catch real
// errors (typos, bad overloads, missing usings, type mistakes) before the user
// spends a build cycle in cTrader. Behaviour is irrelevant here; only the
// signatures matter, and they mirror the documented API the bot actually uses.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace cAlgo.API
{
    public enum TimeZones { UTC }
    public enum AccessRights { None, FullAccess }
    public enum TradeType { Buy, Sell }
    public enum MovingAverageType { Simple, Exponential, Weighted }
    public enum RoundingMode { Down, Up, ToNearest }

    [AttributeUsage(AttributeTargets.Class)]
    public class RobotAttribute : Attribute
    {
        public TimeZones TimeZone { get; set; }
        public AccessRights AccessRights { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ParameterAttribute : Attribute
    {
        public ParameterAttribute() { }
        public ParameterAttribute(string name) { }
        public object DefaultValue { get; set; }
        public object MinValue { get; set; }
        public object MaxValue { get; set; }
        public string Group { get; set; }
        public double Step { get; set; }
    }

    public class TimeFrame
    {
        private readonly string _n;
        private TimeFrame(string n) { _n = n; }
        public static readonly TimeFrame Minute = new TimeFrame("m1");
        public static readonly TimeFrame Minute2 = new TimeFrame("m2");
        public static readonly TimeFrame Minute3 = new TimeFrame("m3");
        public static readonly TimeFrame Minute4 = new TimeFrame("m4");
        public static readonly TimeFrame Minute5 = new TimeFrame("m5");
        public static readonly TimeFrame Minute6 = new TimeFrame("m6");
        public static readonly TimeFrame Minute10 = new TimeFrame("m10");
        public static readonly TimeFrame Minute15 = new TimeFrame("m15");
        public static readonly TimeFrame Minute20 = new TimeFrame("m20");
        public static readonly TimeFrame Minute30 = new TimeFrame("m30");
        public static readonly TimeFrame Minute45 = new TimeFrame("m45");
        public static readonly TimeFrame Hour = new TimeFrame("h1");
        public static readonly TimeFrame Hour2 = new TimeFrame("h2");
        public static readonly TimeFrame Hour3 = new TimeFrame("h3");
        public static readonly TimeFrame Hour4 = new TimeFrame("h4");
        public static readonly TimeFrame Hour6 = new TimeFrame("h6");
        public static readonly TimeFrame Hour8 = new TimeFrame("h8");
        public static readonly TimeFrame Hour12 = new TimeFrame("h12");
        public static readonly TimeFrame Daily = new TimeFrame("d1");
        public static bool operator ==(TimeFrame a, TimeFrame b) { return ReferenceEquals(a, b); }
        public static bool operator !=(TimeFrame a, TimeFrame b) { return !ReferenceEquals(a, b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return _n.GetHashCode(); }
        public override string ToString() { return _n; }
    }

    public class DataSeries
    {
        public double Last(int index) { return 0.0; }
        public int Count { get; set; }
        public double this[int i] { get { return 0.0; } }
    }

    public class Bars
    {
        public DataSeries ClosePrices { get; set; }
        public DataSeries HighPrices { get; set; }
        public DataSeries LowPrices { get; set; }
        public DataSeries OpenPrices { get; set; }
        public DataSeries TickVolumes { get; set; }
        public TimeFrame TimeFrame { get; set; }
        public int Count { get; set; }
    }

    public class Position
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string SymbolName { get; set; }
        public TradeType TradeType { get; set; }
        public DateTime EntryTime { get; set; }
        public double EntryPrice { get; set; }
        public double NetProfit { get; set; }
        public double? StopLoss { get; set; }
        public double? TakeProfit { get; set; }
        public double VolumeInUnits { get; set; }
    }

    public class HistoricalTrade
    {
        public string Label { get; set; }
        public string SymbolName { get; set; }
        public DateTime ClosingTime { get; set; }
        public double NetProfit { get; set; }
    }

    public class Positions : IEnumerable<Position>
    {
        private readonly List<Position> _l = new List<Position>();
        public IEnumerator<Position> GetEnumerator() { return _l.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _l.GetEnumerator(); }
    }

    public class History : IEnumerable<HistoricalTrade>
    {
        private readonly List<HistoricalTrade> _l = new List<HistoricalTrade>();
        public IEnumerator<HistoricalTrade> GetEnumerator() { return _l.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _l.GetEnumerator(); }
    }

    public class TradeResult
    {
        public bool IsSuccessful { get; set; }
        public object Error { get; set; }
        public Position Position { get; set; }
    }

    namespace Indicators
    {
        public class ExponentialMovingAverage { public DataSeries Result { get; set; } }
        public class RelativeStrengthIndex { public DataSeries Result { get; set; } }
        public class MacdHistogram { public DataSeries Histogram { get; set; } public DataSeries Signal { get; set; } }
        public class DirectionalMovementSystem { public DataSeries ADX { get; set; } public DataSeries DIPlus { get; set; } public DataSeries DIMinus { get; set; } }
        public class AverageTrueRange { public DataSeries Result { get; set; } }
    }

    namespace Internals
    {
        public class Symbol
        {
            public double Ask { get; set; }
            public double Bid { get; set; }
            public double PipSize { get; set; }
            public double VolumeInUnitsMin { get; set; }
            public double NormalizeVolumeInUnits(double v, RoundingMode m) { return v; }
        }

        public class Account
        {
            public double Balance { get; set; }
            public double Equity { get; set; }
            public int Number { get; set; }
            public bool IsLive { get; set; }
            public string Currency { get; set; }
        }

        public class Server { public DateTime TimeInUtc { get; set; } }
    }

    public class IndicatorFactory
    {
        public cAlgo.API.Indicators.ExponentialMovingAverage ExponentialMovingAverage(DataSeries s, int p) { return new cAlgo.API.Indicators.ExponentialMovingAverage(); }
        public cAlgo.API.Indicators.RelativeStrengthIndex RelativeStrengthIndex(DataSeries s, int p) { return new cAlgo.API.Indicators.RelativeStrengthIndex(); }
        public cAlgo.API.Indicators.MacdHistogram MacdHistogram(DataSeries s, int a, int b, int c) { return new cAlgo.API.Indicators.MacdHistogram(); }
        public cAlgo.API.Indicators.DirectionalMovementSystem DirectionalMovementSystem(int p) { return new cAlgo.API.Indicators.DirectionalMovementSystem(); }
        public cAlgo.API.Indicators.AverageTrueRange AverageTrueRange(int p, MovingAverageType t) { return new cAlgo.API.Indicators.AverageTrueRange(); }
    }

    public class Notifications
    {
        public void SendEmail(string from, string to, string subject, string body) { }
    }

    public abstract class Robot
    {
        public Notifications Notifications { get; set; }
        public Bars Bars { get; set; }
        public cAlgo.API.Internals.Symbol Symbol { get; set; }
        public cAlgo.API.Internals.Account Account { get; set; }
        public cAlgo.API.Internals.Server Server { get; set; }
        public IndicatorFactory Indicators { get; set; }
        public Positions Positions { get; set; }
        public History History { get; set; }
        public string SymbolName { get; set; }

        protected virtual void OnStart() { }
        protected virtual void OnBar() { }
        protected virtual void OnTick() { }
        protected virtual void OnStop() { }

        public void Print(string msg) { }
        public void Print(string fmt, params object[] args) { }
        public void Stop() { }
        public void BeginInvokeOnMainThread(Action a) { }
        public TradeResult ExecuteMarketOrder(TradeType t, string sym, double units, string label, double? sl, double? tp) { return new TradeResult(); }
        public TradeResult ModifyPosition(Position p, double? sl, double? tp) { return new TradeResult(); }
        public TradeResult ClosePosition(Position p) { return new TradeResult(); }
        public TradeResult ClosePosition(Position p, double volume) { return new TradeResult(); }
    }
}
