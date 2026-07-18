"""Gold trader configuration — fully self-contained.

Every knob the strategies, agents and risk engine use lives here. The
Gold Strategy Lab evolves a GoldParams instance and persists champions
to data/gold_params.json; every mode (campaign, paper, MT5 bridges)
loads that champion automatically.
"""
from __future__ import annotations

import dataclasses
import json
import os
from dataclasses import dataclass, field

DATA_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data")
GOLD_PARAMS_PATH = os.path.join(DATA_DIR, "gold_params.json")


@dataclass
class RiskParams:
    starting_bankroll_usd: float = 3000.0
    risk_per_trade: float = 0.04          # fraction of equity risked per trade;
                                          # the stop distance sets the notional
    stop_loss: float = 0.006              # -0.6% hard stop (price distance)
    tp_multiples: tuple = (1.012,)        # +1.2% target -> exit 100% (binary)
    trailing_stop: float = 0.004          # only used by exit_style="trail"
    max_hold_minutes: float = 600.0       # never hold stale intraday positions
    # day guard (policy — not evolvable; evolution once learned to game it)
    daily_loss_limit: float = 0.15        # stop the day at -15%
    profit_lock_trigger: float = 999.0    # disabled: winning days run unbounded
    profit_lock_keep: float = 0.50


@dataclass
class GoldParams:
    # execution mode
    exit_style: str = "binary"           # "binary" | "trail" | "breakeven"
    use_agents: bool = True              # session + sentinel gates (A/B-validated)
    use_news: bool = False               # news agent (auto-on in live bridges)
    use_indicators: bool = False         # RSI/MACD/Bollinger confluence (A/B first)
    use_mastery: bool = False            # per-strategy performance weighting (A/B first)
    use_analyst: bool = False            # fool-me-twice GATING: measured -60%
                                         # median in sim; available, off
    journal_mistakes: bool = False       # loss journaling (campaign turns on)
    # anti-chop candidates (A/B tested before adoption)
    use_regime_sizing: bool = False      # smaller size AGAINST the regime
    regime_risk_factor: float = 0.6      # counter-regime entries get this scale
    use_atr_stops: bool = False          # stop distance adapts to volatility
    atr_stop_mult: float = 1.3           # stop = mult * 60m ATR (clamped)
    session_min_weight: float = 0.6      # raise to 0.9 to skip Asia entirely
    use_vol_targeting: bool = False      # shrink risk when vol runs hot (A/B)
    vol_boost_only: bool = False         # only upsize in calm, never shrink
    vol_target_ref: float = 3.4e-4       # reference per-minute absolute move
    allow_short: bool = True             # set False for long-only
    max_leverage: float = 200.0          # broker cap (1:200 account). Actual
                                         # leverage used = risk_per_trade/stop_loss

    # trend following (EMA crossover)
    ema_fast: int = 20                   # minutes
    ema_slow: int = 60
    trend_min_slope: float = 1e-4

    # mean reversion (z-score fading in ranging markets)
    mr_window: int = 45
    mr_entry_z: float = -2.2
    mr_max_trend_z: float = 0.8

    # breakout (rolling high/low break with range expansion)
    bo_lookback: int = 120
    bo_min_range_expansion: float = 1.6

    # mentor sweep (ICT/PB Blake/TJR liquidity-sweep reversal).
    # LIVE-ONLY by default: sweeps are real-market stop-hunt microstructure
    # that the statistical simulator cannot contain, so the sim can't judge
    # it — the demo account is its proving ground (mastery records it).
    use_mentors: bool = False            # bridges force-enable at runtime
    use_discipline: bool = False         # Valentini 3-loss daily stop — LIVE-ONLY
                                         # (sim already has the day guard; measured
                                         # as median-up but tail-down in sim)
    sweep_lookback: int = 240            # minutes defining the session high/low
    sweep_margin: float = 0.0003         # how far beyond the level = a sweep
    sweep_atr_mult: float = 1.5          # rejection speed (displacement) filter
    mentor_killzones_only: bool = True   # trade sweeps only at London/NY opens

    risk: RiskParams = field(default_factory=RiskParams)

    # ------------------------------------------------------------------
    def to_dict(self) -> dict:
        d = dataclasses.asdict(self)
        d["risk"]["tp_multiples"] = list(self.risk.tp_multiples)
        return d

    @classmethod
    def from_dict(cls, d: dict) -> "GoldParams":
        d = dict(d)
        risk_d = dict(d.pop("risk", {}))
        if "tp_multiples" in risk_d:
            risk_d["tp_multiples"] = tuple(risk_d["tp_multiples"])
        known_risk = {f.name for f in dataclasses.fields(RiskParams)}
        known = {f.name for f in dataclasses.fields(cls)} - {"risk"}
        return cls(
            **{k: v for k, v in d.items() if k in known},
            risk=RiskParams(**{k: v for k, v in risk_d.items() if k in known_risk}),
        )

    def save(self, path: str = GOLD_PARAMS_PATH) -> None:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w") as f:
            json.dump(self.to_dict(), f, indent=2)

    @classmethod
    def load(cls, path: str = GOLD_PARAMS_PATH) -> "GoldParams":
        if os.path.exists(path):
            with open(path) as f:
                return cls.from_dict(json.load(f))
        return cls()
