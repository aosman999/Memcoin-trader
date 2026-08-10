"""Live core — ONE entry brain for the simulator and both live bridges.

The 2026-07-19 code review found the live bridges re-implementing the
entry chain by hand and silently dropping certified gates (indicator
confluence, session threshold, sizing scalars). This module fixes that
at the root: `EntryPipeline.evaluate()` IS the certified decision chain,
extracted verbatim from the orchestrator, and every runner — sim
orchestrator, live paper, MT5 bridge, MetaApi bridge — must call it.
Parity holds by construction; a gate added here reaches all of them.

Also here:
  * apply_live_profile()  — the single place live-only features turn on
  * LiveDayState          — persistent UTC day baseline for the daily
                            loss stop (survives restarts) + governor
                            red-streak memory
  * market_closed()       — gold's weekly close (Fri 21:30 → Sun 22:00 UTC)
"""
from __future__ import annotations

import datetime as _dt
import json
import os
from dataclasses import dataclass

from .agents import EventSentinel, RegimeAgent, SessionAgent
from .config import DATA_DIR, GoldParams
from .indicators import atr_proxy, confluence_ok
from .mastery import StrategyMastery
from .mentors import MentorDiscipline
from .mistake_analyst import MistakeAnalyst
from .news_agent import NewsAgent
from .strategies import (ALL_STRATEGIES, mentor_sweep_signal,
                         momentum_signal, orb_signal, pullback_signal,
                         six_voter_signal, squeeze_signal)

DAY_STATE_PATH = os.path.join(DATA_DIR, "live_day_state.json")


def utc_day(ts: float) -> str:
    """The ONE day-boundary definition (UTC) — bridges, discipline and
    the mistake journal must all agree on what 'today' means."""
    return _dt.datetime.utcfromtimestamp(ts).strftime("%Y-%m-%d")


def apply_live_profile(params: GoldParams) -> GoldParams:
    """Single chokepoint for live-only features. Every live entry point
    (paper, mt5, mac) calls this; forgetting a flag is no longer possible."""
    params.use_news = True          # headlines + economic calendar
    params.use_mentors = True       # sweep strategy on its live proving ground
    params.use_discipline = True    # Valentini 3-loss daily stop
    return params


def market_closed(now: float) -> bool:
    """Gold's weekly closure: Friday ~21:30 UTC through Sunday 22:00 UTC."""
    t = _dt.datetime.utcfromtimestamp(now)
    wd, h = t.weekday(), t.hour + t.minute / 60.0
    if wd == 5:                       # Saturday
        return True
    if wd == 6 and h < 22.0:          # Sunday before the Asia reopen
        return True
    if wd == 4 and h >= 21.5:         # late Friday
        return True
    return False


@dataclass
class EntryPlan:
    direction: int          # +1 long / -1 short
    strategy: str
    reason: str
    risk_scale: float       # multiply configured risk_per_trade by this
    stop_frac: float | None # ATR-adaptive stop distance (None = fixed)


class EntryPipeline:
    """The certified entry decision chain. evaluate() returns an
    EntryPlan or None; it never sends orders and never touches exits."""

    def __init__(self, params: GoldParams, use_agents: bool | None = None):
        self.params = params
        self.use_agents = params.use_agents if use_agents is None else use_agents
        self.session = SessionAgent()
        self.regime = RegimeAgent()
        self.sentinel = EventSentinel()
        self.news = NewsAgent(enabled=params.use_news)
        self.mastery = StrategyMastery(persist=getattr(params, "use_mastery", False))
        self.discipline = MentorDiscipline()
        self.analyst = MistakeAnalyst(
            persist=getattr(params, "journal_mistakes", False)
            or getattr(params, "use_analyst", False))
        self.current_regime = "ranging"
        self.session_weight = 1.0
        self.gov_red_streak = 0     # maintained by the runner (day-level)

    # ------------------------------------------------------------------
    def on_trade_closed(self, rec, now: float) -> None:
        """Feed a closed trade back to the learning/discipline agents."""
        self.mastery.record(rec.strategy, rec.pnl_usd,
                            rec.notional_usd * self.params.risk.stop_loss)
        self.discipline.on_trade_closed(rec.pnl_usd, now)
        self.analyst.on_trade_closed(rec, self.current_regime,
                                     self.session_weight, now)

    # ------------------------------------------------------------------
    def evaluate(self, history: list, price: float, now: float,
                 losses_today: int | None = None) -> EntryPlan | None:
        """The certified chain, gate by gate. `losses_today` lets live
        bridges supply the broker-measured loss count; None uses the
        in-memory discipline counter."""
        p = self.params
        risk_scale = 1.0
        if self.use_agents:
            # A/B-validated gates: sentinel (news shock veto) + session
            # (liquidity clock). Regime is ADVISORY only — hard-gating by
            # it measurably starved the strategies.
            if (not self.sentinel.check(history, now)
                    or self.session.weight(now) < p.session_min_weight):
                return None
            risk_scale = self.session.weight(now)
            self.session_weight = risk_scale
            self.current_regime = self.regime.classify(history)
        self.news.update(now)
        if p.use_discipline:
            if losses_today is not None:
                if losses_today >= MentorDiscipline.MAX_LOSSES_PER_DAY:
                    return None
            elif not self.discipline.entries_allowed(now):
                return None
        # 6-voter mode: run ONLY the confluence strategy (mirrors the
        # cTrader cBot). Every agent gate in the loop below still wraps it.
        if getattr(p, "use_sixvoter", False):
            candidates = [six_voter_signal]
        else:
            candidates = list(ALL_STRATEGIES)
            if p.use_mentors:
                candidates.insert(0, lambda h, pp: mentor_sweep_signal(h, pp, now))
            # candidate strategies (A/B-gated; appended AFTER the certified
            # trio so flags-off behavior is unchanged)
            if getattr(p, "use_momentum", False):
                candidates.append(momentum_signal)
            if getattr(p, "use_orb", False):
                candidates.append(lambda h, pp: orb_signal(h, pp, now))
            if getattr(p, "use_pullback", False):
                candidates.append(pullback_signal)
            if getattr(p, "use_squeeze", False):
                candidates.append(squeeze_signal)
            if getattr(p, "use_htf", False):
                from .strategies import htf_signal
                candidates.append(htf_signal)
        for strat in candidates:
            sig = strat(history, p)
            if sig is None:
                continue
            if sig.direction < 0 and not p.allow_short:
                continue
            if not self.news.entry_allowed(sig.direction, now):
                continue
            if (p.use_analyst
                    and self.analyst.blocked(sig.strategy,
                                             self.current_regime, now)):
                continue   # fool-me-twice: benched for today
            if p.use_indicators and not confluence_ok(
                    sig.strategy, sig.direction, history,
                    strict=getattr(p, "confluence_strict", False)):
                continue
            # candidate indicator gates (A/B-gated, default off)
            if (getattr(p, "use_adx_filter", False)
                    and sig.strategy in ("gold_trend", "gold_breakout")):
                from .indicators import adx_proxy as _adx
                if _adx(history) < p.adx_min:
                    continue
            if (getattr(p, "use_stochrsi", False)
                    and sig.strategy == "gold_meanrev"):
                from .indicators import stoch_rsi as _srsi
                sr = _srsi(history)
                if not (sr <= p.stochrsi_lo if sig.direction > 0
                        else sr >= p.stochrsi_hi):
                    continue
            strat_scale = (self.mastery.risk_scale(sig.strategy)
                           if p.use_mastery else 1.0)
            # hostile-weather governor: after 2+ straight red days,
            # trade smaller until a green day breaks the streak
            if p.use_governor and self.gov_red_streak >= 2:
                strat_scale *= p.governor_cut
            # anti-chop: counter-regime entries trade smaller, not never
            if p.use_regime_sizing:
                against = ((self.current_regime == "ranging"
                            and sig.strategy in ("gold_trend", "gold_breakout"))
                           or (self.current_regime == "trending"
                               and sig.strategy == "gold_meanrev"))
                if against:
                    strat_scale *= p.regime_risk_factor
            # vol targeting: when the tape runs hotter than reference,
            # risk shrinks proportionally — crisis exposure control
            if p.use_vol_targeting:
                rv = atr_proxy(history, period=120) / price if price else 0.0
                if rv > 0:
                    floor = 1.0 if p.vol_boost_only else 0.4
                    strat_scale *= min(1.2, max(floor, p.vol_target_ref / rv))
            stop_frac = None
            if p.use_atr_stops:
                atr = atr_proxy(history, period=60)
                if atr > 0 and price > 0:
                    stop_frac = min(0.010, max(0.004,
                                    p.atr_stop_mult * atr / price))
            return EntryPlan(direction=sig.direction, strategy=sig.strategy,
                             reason=sig.reason,
                             risk_scale=risk_scale * strat_scale,
                             stop_frac=stop_frac)
        return None


class LiveDayState:
    """Persistent day baseline for live bridges: the daily loss stop's
    reference equity survives restarts, the governor's red streak
    survives sessions, and day boundaries are UTC everywhere."""

    def __init__(self, equity_now: float, now: float,
                 path: str = DAY_STATE_PATH):
        self.path = path
        d = {}
        if os.path.exists(path):
            try:
                with open(path) as f:
                    d = json.load(f)
            except (json.JSONDecodeError, OSError):
                d = {}
        today = utc_day(now)
        if d.get("day") == today:
            # same trading day: keep the ORIGINAL baseline (restart-proof)
            self.day = today
            self.day_start_equity = float(d.get("day_start_equity", equity_now)) or equity_now
            self.halted = bool(d.get("halted", False))
            self.red_streak = int(d.get("red_streak", 0))
        else:
            # new day: settle yesterday's color into the governor streak
            prev_start = float(d.get("day_start_equity", 0) or 0)
            prev_last = float(d.get("last_equity", 0) or 0)
            streak = int(d.get("red_streak", 0))
            if prev_start > 0 and prev_last > 0:
                streak = streak + 1 if prev_last < prev_start else 0
            self.day = today
            self.day_start_equity = equity_now
            self.halted = False
            self.red_streak = streak
        self._save(equity_now)

    def roll_if_new_day(self, equity_now: float, now: float) -> bool:
        """Returns True when a new UTC day just started."""
        today = utc_day(now)
        if today == self.day:
            return False
        if self.day_start_equity > 0:
            self.red_streak = (self.red_streak + 1
                               if equity_now < self.day_start_equity else 0)
        self.day = today
        self.day_start_equity = equity_now
        self.halted = False
        self._save(equity_now)
        return True

    def note_equity(self, equity_now: float) -> None:
        self._save(equity_now)

    def halt(self, equity_now: float) -> None:
        self.halted = True
        self._save(equity_now)

    def loss_stop_hit(self, equity_now: float, daily_loss_limit: float) -> bool:
        return (self.day_start_equity > 0
                and equity_now <= self.day_start_equity * (1 - daily_loss_limit))

    def _save(self, last_equity: float) -> None:
        try:
            os.makedirs(os.path.dirname(self.path), exist_ok=True)
            with open(self.path, "w") as f:
                json.dump({"day": self.day,
                           "day_start_equity": self.day_start_equity,
                           "last_equity": last_equity,
                           "halted": self.halted,
                           "red_streak": self.red_streak}, f)
        except OSError:
            pass
