"""Gold agents — the cooperating AIs around the strategies.

  * SessionAgent  — gold's liquidity clock (Asia/London/NY): entries are
                    gated and risk-scaled by how alive the market is
  * RegimeAgent   — classifies the tape (trending / ranging / chaotic)
                    and decides WHICH strategies may trade right now
  * EventSentinel — the safety veto: abnormal short-term volatility
                    (a news print in progress) blocks new entries; this
                    is gold's equivalent of the memecoin rug check

All three read only the price history — they work identically on the
simulator, the live feed, and both MT5 bridges.
"""
from __future__ import annotations

import math
import time as _time


class SessionAgent:
    name = "session"

    @staticmethod
    def weight(ts: float) -> float:
        """0..1.2 — how much of normal size this hour deserves (UTC)."""
        h = (_time.gmtime(int(ts)).tm_hour + _time.gmtime(int(ts)).tm_min / 60.0)
        if 12 <= h < 16:     # London/NY overlap: the prime hours
            return 1.2
        if 7 <= h < 12:      # London
            return 1.0
        if 16 <= h < 21:     # NY afternoon
            return 0.9
        if 1 <= h < 7:       # Asia
            return 0.6
        return 0.4           # the graveyard around the daily close

    def tradeable(self, ts: float) -> bool:
        return self.weight(ts) >= 0.6


class RegimeAgent:
    name = "regime"

    def classify(self, prices: list[float]) -> str:
        """"trending" | "ranging" | "chaotic" from the last ~4 hours."""
        if len(prices) < 120:
            return "ranging"
        window = prices[-240:] if len(prices) >= 240 else prices[-120:]
        net = abs(window[-1] - window[0])
        path = sum(abs(window[i] - window[i - 1]) for i in range(1, len(window)))
        efficiency = net / path if path > 0 else 0.0

        # short-term vs long-term realized vol (chaos detector)
        rv_short = _rv(prices[-30:])
        rv_long = _rv(window)
        if rv_long > 0 and rv_short / rv_long > 2.4:
            return "chaotic"
        return "trending" if efficiency >= 0.30 else "ranging"



class EventSentinel:
    name = "sentinel"
    COOLDOWN_MIN = 12

    def __init__(self):
        self._blocked_until: float = 0.0

    def check(self, prices: list[float], now: float) -> bool:
        """True = safe to enter. False = news-grade move in progress."""
        if now < self._blocked_until:
            return False
        if len(prices) < 45:
            return True
        # single-minute shock: gold "gapping" on a print
        last_move = abs(prices[-1] / prices[-2] - 1.0) if prices[-2] else 0.0
        # 5-minute burst vs the trailing baseline
        burst = _rv(prices[-6:])
        base = _rv(prices[-45:])
        if last_move > 0.0022 or (base > 0 and burst / base > 3.5):
            self._blocked_until = now + self.COOLDOWN_MIN * 60
            return False
        return True


def _rv(window: list[float]) -> float:
    """Mean absolute 1-minute log return of a price window."""
    if len(window) < 2:
        return 0.0
    rets = [abs(math.log(window[i] / window[i - 1]))
            for i in range(1, len(window)) if window[i - 1] > 0]
    return sum(rets) / len(rets) if rets else 0.0
