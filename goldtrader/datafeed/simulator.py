"""Gold market simulator — minute bars, calibrated to the 2026 regime.

Calibration targets (July 2026, live research — see docs/GOLD_MARKET_STUDY.md):
  * spot ~$4,100 after the Jan-2026 $5,590 all-time high and correction
  * annualized volatility ~24% (2026 runs hot: >50% at the peak, ~30%
    now, vs a 20-year average of 17%) -> per-minute sigma ~4e-4
  * session structure: quiet Asia, active London, most active NY overlap
  * ~35% of days trend (macro flows), the rest mean-revert in a range
  * news jumps (CPI/NFP/FOMC): ~2/day, 0.1-0.6% moves
"""
from __future__ import annotations

import math
import random

TICK_SECONDS = 60.0


class GoldSim:
    """Evolves a spot-gold price path one minute at a time."""

    def __init__(self, seed: int = 7, start_price: float = 4100.0,
                 vol_scale: float = 1.0, trend_prob: float = 0.35,
                 jump_rate: float = 2.0):
        """vol_scale: 1.0 = the 2026 hot regime (~24% ann.); 0.6 ≈ the
        20-year average regime; 1.5 ≈ crisis conditions.
        trend_prob: share of days that trend (0.35 = 2026 calibration).
        jump_rate: news jumps per day."""
        self.rng = random.Random(seed)
        self.vol_scale = vol_scale
        self.trend_prob = trend_prob
        self.jump_rate = jump_rate
        self.price = start_price
        self.tick = 0
        self.price_high = start_price
        self.day_anchor = start_price
        self.trending_day = False
        self.day_drift = 0.0
        self._roll_day()

    def _roll_day(self) -> None:
        rng = self.rng
        self.trending_day = rng.random() < self.trend_prob
        self.day_drift = rng.choice([-1, 1]) * rng.uniform(1e-5, 4e-5) \
            if self.trending_day else 0.0
        self.day_anchor = self.price

    @property
    def now_ts(self) -> float:
        # base is midnight UTC so tick-of-day == UTC time-of-day: the
        # SessionAgent's clock and the sim's session structure agree
        return 1_700_006_400.0 + self.tick * TICK_SECONDS

    def _session_mult(self) -> float:
        minute_of_day = self.tick % 1440
        h = minute_of_day / 60.0
        if 0 <= h < 7:      # Asia
            return 0.6
        if 7 <= h < 12:     # London
            return 1.2
        if 12 <= h < 16:    # London/NY overlap
            return 1.6
        if 16 <= h < 21:    # NY
            return 1.2
        return 0.7

    def step(self) -> float:
        rng = self.rng
        self.tick += 1
        if self.tick % 1440 == 0:
            self._roll_day()

        sigma = 4.0e-4 * self.vol_scale * self._session_mult()
        ret = rng.gauss(self.day_drift, sigma)
        if not self.trending_day:
            # gentle pull back toward the day's anchor (ranging behavior)
            ret += -0.002 * (self.price / self.day_anchor - 1.0) / 60.0
        # news jumps (CPI/NFP/FOMC prints): ~2/day in the 2026 regime
        if rng.random() < self.jump_rate / 1440:
            ret += rng.choice([-1, 1]) * rng.uniform(0.001, 0.006)

        self.price *= math.exp(ret)
        self.price_high = max(self.price_high, self.price)
        return self.price
