"""Classic technical indicators (the TradingView staples), stdlib-only.

Computed from the rolling minute-close history the bot already keeps:
  RSI(14)         — overbought/oversold confluence
  MACD(12,26,9)   — trend direction/momentum confluence
  Bollinger(20,2) — mean-reversion band context
  ATR-proxy(14)   — volatility (from close-to-close moves; no OHLC bars)

Used as CONFLUENCE for trade setup, never as standalone signals — each
strategy demands the indicator agree before an entry is allowed.
"""
from __future__ import annotations

import math


def ema_series(values: list[float], period: int) -> list[float]:
    k = 2.0 / (period + 1)
    out = [values[0]]
    for v in values[1:]:
        out.append(v * k + out[-1] * (1 - k))
    return out


def rsi(prices: list[float], period: int = 14) -> float:
    """Wilder's RSI on minute closes. 50.0 when not enough data."""
    if len(prices) < period + 1:
        return 50.0
    window = prices[-(period * 4):]
    gains = losses = 0.0
    avg_gain = avg_loss = None
    for i in range(1, len(window)):
        change = window[i] - window[i - 1]
        g, l = max(change, 0.0), max(-change, 0.0)
        if i < period + 1:
            gains += g
            losses += l
            if i == period:
                avg_gain, avg_loss = gains / period, losses / period
        else:
            avg_gain = (avg_gain * (period - 1) + g) / period
            avg_loss = (avg_loss * (period - 1) + l) / period
    if avg_loss is None or avg_loss == 0:
        return 100.0 if (avg_gain or 0) > 0 else 50.0
    rs = (avg_gain or 0) / avg_loss
    return 100.0 - 100.0 / (1.0 + rs)


def macd_histogram(prices: list[float], fast: int = 12, slow: int = 26,
                   signal: int = 9) -> float:
    """MACD histogram (macd - signal). Positive = bullish momentum."""
    if len(prices) < slow + signal + 5:
        return 0.0
    window = prices[-(slow * 4):]
    ef = ema_series(window, fast)
    es = ema_series(window, slow)
    macd_line = [f - s for f, s in zip(ef, es)]
    sig = ema_series(macd_line, signal)
    return macd_line[-1] - sig[-1]


def bollinger(prices: list[float], period: int = 20,
              num_std: float = 2.0) -> tuple[float, float, float]:
    """(lower, middle, upper). Falls back to price when data is short."""
    if len(prices) < period:
        p = prices[-1] if prices else 0.0
        return p, p, p
    window = prices[-period:]
    mid = sum(window) / period
    var = sum((v - mid) ** 2 for v in window) / period
    sd = math.sqrt(var)
    return mid - num_std * sd, mid, mid + num_std * sd


def atr_proxy(prices: list[float], period: int = 14) -> float:
    """Average absolute close-to-close move — the minute-bar ATR stand-in."""
    if len(prices) < period + 1:
        return 0.0
    window = prices[-(period + 1):]
    return sum(abs(window[i] - window[i - 1])
               for i in range(1, len(window))) / period


def confluence_ok(strategy: str, direction: int, prices: list[float]) -> bool:
    """Does the indicator picture agree with this entry?

    trend/breakout: MACD histogram must point the same way, and RSI must
    not already be exhausted in that direction.
    meanrev: RSI must be genuinely oversold/overbought AND price outside
    the Bollinger band (the textbook band-fade setup).
    """
    if strategy in ("gold_trend", "gold_breakout"):
        # MACD must agree with the trade direction. No RSI exhaustion cap:
        # minute-RSI saturates in strong trends — capping it blocked the
        # best entries (caught by A/B testing and a saturation probe).
        hist = macd_histogram(prices)
        return hist > 0 if direction > 0 else hist < 0
    if strategy == "gold_meanrev":
        # RSI must confirm the stretch. (No Bollinger condition: the
        # strategy's z-score entry IS band position — it was redundant.)
        r = rsi(prices)
        return r <= 34.0 if direction > 0 else r >= 66.0
    return True
