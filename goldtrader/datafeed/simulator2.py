"""Alternate gold market model — structurally DIFFERENT from GoldSim.

Purpose: cross-model certification. A configuration tuned on one market
model can secretly overfit that model's quirks; profiting on two
independent models is much stronger evidence of a real edge.

Differences from GoldSim (deliberate):
  * fat-tailed returns (Student-t, df=4) instead of Gaussian
  * volatility clustering (GARCH-style): quiet spells and storm spells
    emerge organically instead of a fixed per-session sigma
  * persistent drift via a slow AR(1): trends build and decay over days
    instead of being coin-flipped each midnight
  * jumps with t-distributed sizes

Shares only the session liquidity clock (a real feature of gold) and
the tick interface (step/now_ts/price), so every harness runs unchanged.
"""
from __future__ import annotations

import math
import random

TICK_SECONDS = 60.0


class GoldSim2:
    def __init__(self, seed: int = 7, start_price: float = 4100.0):
        self.rng = random.Random(seed)
        self.price = start_price
        self.price_high = start_price
        self.tick = 0
        # GARCH-ish state, targeting ~24% annualized like the base regime
        self._var = (2.6e-4) ** 2
        self._prev_ret = 0.0
        # slow AR(1) drift: persistent multi-day trends
        self._mu = 0.0

    @property
    def now_ts(self) -> float:
        return 1_700_006_400.0 + self.tick * TICK_SECONDS

    def _session_mult(self) -> float:
        h = (self.tick % 1440) / 60.0
        if 0 <= h < 7:
            return 0.6
        if 7 <= h < 12:
            return 1.2
        if 12 <= h < 16:
            return 1.6
        if 16 <= h < 21:
            return 1.2
        return 0.7

    def _student_t(self, df: float = 4.0) -> float:
        # t-variate via normal / sqrt(chi2/df)
        rng = self.rng
        z = rng.gauss(0, 1)
        chi2 = sum(rng.gauss(0, 1) ** 2 for _ in range(int(df)))
        return z / math.sqrt(chi2 / df)

    def step(self) -> float:
        rng = self.rng
        self.tick += 1

        # volatility clustering: the BASE shock (session-mult excluded)
        # feeds the recursion, and both tails and variance are clamped —
        # otherwise a fat-tail draw compounds into a runaway
        w = (2.6e-4) ** 2 * 0.05
        self._var = w + 0.10 * self._prev_ret ** 2 + 0.85 * self._var
        self._var = min(self._var, (1.3e-3) ** 2)          # vol ceiling ~5x base
        sigma_base = math.sqrt(self._var)

        # persistent drift builds and decays over ~days
        self._mu = 0.999 * self._mu + rng.gauss(0, 6e-7)

        t_draw = max(-8.0, min(8.0, self._student_t() * 0.82))
        shock = sigma_base * t_draw
        ret = self._mu + shock * self._session_mult()
        if rng.random() < 2.0 / 1440:                      # news jumps
            ret += max(-0.01, min(0.01, self._student_t() * 0.002))

        self._prev_ret = shock
        self.price *= math.exp(ret)
        self.price_high = max(self.price_high, self.price)
        return self.price
