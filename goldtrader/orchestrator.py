"""Gold orchestrator — one instrument, leveraged margin engine (MT5-style).

Keeps the rolling minute-price history, asks each strategy for a signal,
and hands entries/exits to the leverage engine. One position at a time;
the day guard wraps the whole session. allow_short/max_leverage come
from GoldParams.
"""
from __future__ import annotations

from collections import deque

from .portfolio import Portfolio
from .models import TradeRecord

from .agents import EventSentinel, RegimeAgent, SessionAgent
from .config import GoldParams
from .leverage import LevEngine
from .news_agent import NewsAgent
from .strategies import ALL_STRATEGIES


class GoldOrchestrator:
    def __init__(self, params: GoldParams, portfolio: Portfolio | None = None,
                 verbose: bool = False, use_agents: bool | None = None):
        self.params = params
        self.verbose = verbose
        self.portfolio = portfolio or Portfolio(params.risk.starting_bankroll_usd)
        self.engine = LevEngine(params.risk, max_leverage=params.max_leverage,
                                exit_style=getattr(params, "exit_style", "trail"))
        self.prices: deque[float] = deque(maxlen=1600)
        self.use_agents = params.use_agents if use_agents is None else use_agents
        self.session = SessionAgent()
        self.regime = RegimeAgent()
        self.sentinel = EventSentinel()
        # news agent is off in simulation (no headlines to read there);
        # live paper mode and both MT5 bridges switch it on
        self.news = NewsAgent(enabled=params.use_news)
        self.current_regime = "ranging"

    def on_price(self, price: float, now: float) -> list[TradeRecord]:
        self.prices.append(price)
        closed: list[TradeRecord] = []

        # exits before entries, always
        rec = self.engine.manage(price, self.portfolio, now)
        if rec:
            closed.append(rec)
            if self.verbose:
                print(f"  CLOSE [{rec.exit_reason:>13}] {rec.symbol} "
                      f"pnl ${rec.pnl_usd:+.2f} held {rec.hold_minutes:.0f}m")

        if self.engine.pos is None:
            history = list(self.prices)
            risk_scale = 1.0
            if self.use_agents:
                # A/B-validated gates: sentinel (news shock veto) + session
                # (liquidity clock). Regime is ADVISORY only — gating by it
                # measurably starved the strategies, which carry their own
                # internal regime filters.
                if (not self.sentinel.check(history, now)
                        or not self.session.tradeable(now)):
                    self.portfolio.equity_curve.append(
                        self.engine.equity(self.portfolio, price))
                    return closed
                risk_scale = self.session.weight(now)
                self.current_regime = self.regime.classify(history)
            self.news.update(now)
            for strat in ALL_STRATEGIES:
                sig = strat(history, self.params)
                if sig is None:
                    continue
                if sig.direction < 0 and not self.params.allow_short:
                    continue
                if not self.news.entry_allowed(sig.direction, now):
                    continue
                pos = self.engine.open(sig.direction, price, self.portfolio,
                                       now, sig.strategy, risk_scale=risk_scale)
                if pos:
                    if self.verbose:
                        side = "LONG" if sig.direction > 0 else "SHORT"
                        lev = pos.notional / max(1e-9, self.portfolio.cash)
                        print(f"  OPEN  [{sig.strategy:>13}] {side} "
                              f"${pos.notional:,.0f} ({lev:.1f}x) @ {price:.2f} "
                              f"— {sig.reason}")
                    break

        self.portfolio.equity_curve.append(self.engine.equity(self.portfolio, price))
        return closed

    def equity(self, price: float | None = None) -> float:
        p = price if price is not None else (self.prices[-1] if self.prices else 0.0)
        return self.engine.equity(self.portfolio, p)

    def liquidate(self, now: float, reason: str) -> None:
        if self.engine.pos is not None and self.prices:
            self.engine.close(self.prices[-1], self.portfolio, now, reason)
