"""Gold intraday strategies — long AND short (MT5 margin mode).

Each strategy looks at the rolling minute-price history and may emit a
GoldSignal with a direction; all exits belong to the leverage engine
(risk-based sizing, SL/TP, trailing, time stop, margin stop-out) and the
day guard wraps the session. Set allow_short=False in GoldParams to
restrict to long-only (e.g. for spot/Islamic constraints).
"""
from __future__ import annotations

import math
from dataclasses import dataclass

from .config import GoldParams


@dataclass
class GoldSignal:
    strategy: str
    direction: int          # +1 long, -1 short
    reason: str


def _ema(values: list[float], period: int) -> float:
    """Canonical EMA lives in indicators.ema_series; this is the
    last-value convenience wrapper (review: two copies had drifted risk)."""
    from .indicators import ema_series
    return ema_series(values, period)[-1]


def _mean_std(values: list[float]) -> tuple[float, float]:
    m = sum(values) / len(values)
    var = sum((v - m) ** 2 for v in values) / len(values)
    return m, math.sqrt(var)


def _mtf_agrees(prices: list[float], direction: int) -> bool:
    """15-minute higher-timeframe filter: the 15m EMA(20) must slope with
    the trade. Sampled from minute closes (every 15th)."""
    bars15 = prices[-320::15]
    if len(bars15) < 21:
        return True
    e_now = _ema(bars15, 20)
    e_prev = _ema(bars15[:-1], 20)
    return (e_now - e_prev) * direction >= 0


def trend_signal(prices: list[float], p: GoldParams) -> GoldSignal | None:
    """EMA crossover with a sloping slow EMA: ride macro-flow days."""
    need = p.ema_slow + 5
    if len(prices) < need:
        return None
    window = prices[-need:]
    fast = _ema(window, p.ema_fast)
    slow = _ema(window, p.ema_slow)
    slow_prev = _ema(window[:-5], p.ema_slow)
    slope = (slow - slow_prev) / (5 * slow) if slow else 0.0
    if fast > slow and slope >= p.trend_min_slope and prices[-1] >= fast:
        if p.use_mtf and not _mtf_agrees(prices, +1):
            return None
        return GoldSignal("gold_trend", +1,
                          f"EMA{p.ema_fast}>{p.ema_slow} rising ({slope:.2e})")
    if fast < slow and slope <= -p.trend_min_slope and prices[-1] <= fast:
        if p.use_mtf and not _mtf_agrees(prices, -1):
            return None
        return GoldSignal("gold_trend", -1,
                          f"EMA{p.ema_fast}<{p.ema_slow} falling ({slope:.2e})")
    return None


def meanrev_signal(prices: list[float], p: GoldParams) -> GoldSignal | None:
    """Fade statistically stretched moves in ranging conditions."""
    if len(prices) < 2 * p.mr_window:
        return None
    window = prices[-p.mr_window:]
    m, s = _mean_std(window)
    if s <= 0:
        return None
    z = (prices[-1] - m) / s
    trend_z = (m - _mean_std(prices[-2 * p.mr_window:-p.mr_window])[0]) / s
    if abs(trend_z) > p.mr_max_trend_z:
        return None   # never fade a strong trend
    if z <= p.mr_entry_z:
        return GoldSignal("gold_meanrev", +1,
                          f"dip z={z:.2f} in range")
    if z >= -p.mr_entry_z:
        return GoldSignal("gold_meanrev", -1,
                          f"stretch z={z:.2f} in range")
    return None


def mentor_sweep_signal(prices: list[float], p: GoldParams,
                        now: float | None = None) -> GoldSignal | None:
    """The ICT / PB Blake / TJR liquidity-sweep reversal.

    IF price sweeps beyond the session high/low (runs the stops) and
    snaps back through the level with DISPLACEMENT (a fast rejection),
    THEN enter the reversal — only during kill zones (London/NY opens),
    where these engineered moves actually happen.
    """
    look = p.sweep_lookback
    if len(prices) < look + 6:
        return None
    if now is not None and p.mentor_killzones_only:
        from .mentors import in_kill_zone
        if not in_kill_zone(now):
            return None
    window = prices[-look - 5:-5]         # the levels BEFORE the sweep
    recent = prices[-5:]                  # the sweep + rejection window
    hi, lo = max(window), min(window)
    last = prices[-1]
    # displacement: the rejection leg must be fast relative to normal noise
    from .indicators import atr_proxy
    atr = atr_proxy(prices)
    if atr <= 0:
        return None
    swept_high = max(recent) > hi * (1 + p.sweep_margin)
    swept_low = min(recent) < lo * (1 - p.sweep_margin)
    if swept_high and last < hi and (max(recent) - last) >= p.sweep_atr_mult * atr:
        return GoldSignal("mentor_sweep", -1,
                          f"swept high {hi:.2f}, fast rejection back below")
    if swept_low and last > lo and (last - min(recent)) >= p.sweep_atr_mult * atr:
        return GoldSignal("mentor_sweep", +1,
                          f"swept low {lo:.2f}, fast rejection back above")
    return None


def breakout_signal(prices: list[float], p: GoldParams) -> GoldSignal | None:
    """Rolling high/low break with range expansion (session momentum)."""
    if len(prices) < p.bo_lookback + 10:
        return None
    lookback = prices[-p.bo_lookback - 1:-1]
    hi, lo = max(lookback), min(lookback)
    recent_range = max(prices[-15:]) - min(prices[-15:])
    older_range = (max(lookback[:60]) - min(lookback[:60])) / 4 if len(lookback) >= 60 else 0.0
    expanding = older_range > 0 and recent_range >= p.bo_min_range_expansion * older_range
    if not expanding:
        return None
    if prices[-1] > hi:
        return GoldSignal("gold_breakout", +1,
                          f"broke {p.bo_lookback}m high {hi:.2f}")
    if prices[-1] < lo:
        return GoldSignal("gold_breakout", -1,
                          f"broke {p.bo_lookback}m low {lo:.2f}")
    return None


ALL_STRATEGIES = (trend_signal, meanrev_signal, breakout_signal)
