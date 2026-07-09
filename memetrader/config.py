"""All tunable parameters live here. The Strategy Lab agent evolves a
`StrategyParams` instance and persists winners to data/best_params.json,
so every knob a strategy uses MUST be declared here to be tunable."""
from __future__ import annotations

import dataclasses
import json
import os
from dataclasses import dataclass, field

DATA_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "data")
BEST_PARAMS_PATH = os.path.join(DATA_DIR, "best_params.json")


@dataclass
class RiskParams:
    starting_bankroll_usd: float = 1_000.0
    risk_per_trade: float = 0.02          # fraction of equity per position
    max_concurrent_positions: int = 8
    max_per_strategy: int = 3            # no single strategy may hog the book
    max_position_usd: float = 200.0
    stop_loss: float = 0.45               # exit if price falls this fraction from entry
    trailing_stop: float = 0.35           # after first TP, trail the high by this
    tp_multiples: tuple = (2.0, 4.0, 10.0)   # take-profit ladder (price multiples)
    tp_fractions: tuple = (0.50, 0.25, 0.15) # fraction of ORIGINAL size sold at each rung
    max_hold_minutes: float = 720.0       # time-stop: memecoins that go nowhere bleed
    stale_exit_multiple: float = 1.15     # if below this at time-stop, dump it


@dataclass
class StrategyParams:
    # ---- shared entry filters (every coin is entered at LOW market cap) ----
    min_mcap: float = 15_000.0
    max_mcap: float = 400_000.0           # low-cap only: no chasing pumped charts
    min_liquidity: float = 8_000.0
    max_age_minutes: float = 2_880.0      # only coins younger than 48h (dip buyer exempt)
    min_safety_score: int = 60

    # ---- graduation sniper ----
    grad_min_bonding: float = 0.85        # enter when bonding curve is nearly complete
    grad_min_holders: int = 60
    grad_max_top10_pct: float = 0.32

    # ---- momentum breakout ----
    mom_vol_spike: float = 3.0            # 5m volume must be X times the hourly baseline
    mom_min_holders: int = 100
    mom_min_volume_5m: float = 6_000.0

    # ---- copy trade ----
    copy_min_smart_buys: int = 2          # distinct profitable wallets in 30 min
    copy_max_mcap: float = 800_000.0      # whales sometimes enter a bit later; allow more room

    # ---- dip buyer (mature survivors only) ----
    dip_min_age_minutes: float = 2_880.0  # >48h old = survived the rug window
    dip_min_mcap: float = 300_000.0
    dip_drawdown_low: float = 0.30
    dip_drawdown_high: float = 0.65

    # ---- news / narrative ----
    news_boost: float = 0.15              # confidence bonus when token matches a hot narrative

    risk: RiskParams = field(default_factory=RiskParams)

    # ------------------------------------------------------------------
    def to_dict(self) -> dict:
        d = dataclasses.asdict(self)
        d["risk"]["tp_multiples"] = list(self.risk.tp_multiples)
        d["risk"]["tp_fractions"] = list(self.risk.tp_fractions)
        return d

    @classmethod
    def from_dict(cls, d: dict) -> "StrategyParams":
        d = dict(d)
        risk_d = dict(d.pop("risk", {}))
        if "tp_multiples" in risk_d:
            risk_d["tp_multiples"] = tuple(risk_d["tp_multiples"])
        if "tp_fractions" in risk_d:
            risk_d["tp_fractions"] = tuple(risk_d["tp_fractions"])
        known_risk = {f.name for f in dataclasses.fields(RiskParams)}
        known = {f.name for f in dataclasses.fields(cls)} - {"risk"}
        return cls(
            **{k: v for k, v in d.items() if k in known},
            risk=RiskParams(**{k: v for k, v in risk_d.items() if k in known_risk}),
        )

    def save(self, path: str = BEST_PARAMS_PATH) -> None:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w") as f:
            json.dump(self.to_dict(), f, indent=2)

    @classmethod
    def load(cls, path: str = BEST_PARAMS_PATH) -> "StrategyParams":
        if os.path.exists(path):
            with open(path) as f:
                return cls.from_dict(json.load(f))
        return cls()
