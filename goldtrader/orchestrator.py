"""Gold orchestrator — one instrument, leveraged margin engine (MT5-style).

Keeps the rolling minute-price history, delegates the ENTIRE entry
decision to the shared, certified EntryPipeline (live_core.py — the same
brain the live bridges run), and hands entries/exits to the leverage
engine. One position at a time; the day guard wraps the whole session.
"""
from __future__ import annotations

import datetime as _dt
from collections import deque

from .config import GoldParams
from .leverage import LevEngine
from .live_core import EntryPipeline
from .models import TradeRecord
from .portfolio import Portfolio


class GoldOrchestrator:
    def __init__(self, params: GoldParams, portfolio: Portfolio | None = None,
                 verbose: bool = False, use_agents: bool | None = None):
        self.params = params
        self.verbose = verbose
        self.portfolio = portfolio or Portfolio(params.risk.starting_bankroll_usd)
        self.engine = LevEngine(params.risk, max_leverage=params.max_leverage,
                                exit_style=params.exit_style)
        self.prices: deque[float] = deque(maxlen=1600)
        self.pipeline = EntryPipeline(params, use_agents=use_agents)
        # entry gate for runners that halt a day (live paper's day guard)
        self.entries_enabled = True
        # environment governor: day-level weather memory
        self._gov_day = ""
        self._gov_day_start_eq = None

    # convenience passthroughs (tests and campaign reports use these)
    @property
    def mastery(self):
        return self.pipeline.mastery

    @property
    def analyst(self):
        return self.pipeline.analyst

    @property
    def discipline(self):
        return self.pipeline.discipline

    @property
    def current_regime(self):
        return self.pipeline.current_regime

    def on_price(self, price: float, now: float) -> list[TradeRecord]:
        # realistic spreads: widen with the last minute's violence
        if self.prices:
            last_move = abs(price / self.prices[-1] - 1.0)
            self.engine.spread_mult = 1.0 + 900.0 * last_move  # +0.1% move -> ~1.9x
        self.prices.append(price)
        closed: list[TradeRecord] = []

        # environment governor bookkeeping (day-level weather memory)
        day = _dt.datetime.utcfromtimestamp(now).strftime("%Y-%m-%d")
        if day != self._gov_day:
            eq_now = self.engine.equity(self.portfolio, price)
            if self._gov_day_start_eq is not None:
                if eq_now < self._gov_day_start_eq:
                    self.pipeline.gov_red_streak += 1
                else:
                    self.pipeline.gov_red_streak = 0
            self._gov_day = day
            self._gov_day_start_eq = eq_now

        # exits before entries, always
        rec = self.engine.manage(price, self.portfolio, now)
        if rec:
            closed.append(rec)
            self.pipeline.on_trade_closed(rec, now)
            if self.verbose:
                print(f"  CLOSE [{rec.exit_reason:>13}] {rec.symbol} "
                      f"pnl ${rec.pnl_usd:+.2f} held {rec.hold_minutes:.0f}m")

        if self.engine.pos is None and self.entries_enabled:
            plan = self.pipeline.evaluate(list(self.prices), price, now)
            if plan is not None:
                pos = self.engine.open(plan.direction, price, self.portfolio,
                                       now, plan.strategy,
                                       risk_scale=plan.risk_scale,
                                       stop_frac=plan.stop_frac)
                if pos and self.verbose:
                    side = "LONG" if plan.direction > 0 else "SHORT"
                    lev = pos.notional / max(1e-9, self.portfolio.cash)
                    print(f"  OPEN  [{plan.strategy:>13}] {side} "
                          f"${pos.notional:,.0f} ({lev:.1f}x) @ {price:.2f} "
                          f"— {plan.reason}")

        self.portfolio.equity_curve.append(self.engine.equity(self.portfolio, price))
        return closed

    def equity(self, price: float | None = None) -> float:
        p = price if price is not None else (self.prices[-1] if self.prices else 0.0)
        return self.engine.equity(self.portfolio, p)

    def liquidate(self, now: float, reason: str) -> None:
        if self.engine.pos is not None and self.prices:
            self.engine.close(self.prices[-1], self.portfolio, now, reason)
