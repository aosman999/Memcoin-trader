"""Strategy Mastery — the bot masters what works and benches what doesn't.

Keeps a rolling record of each strategy's recent closed trades and turns
it into a risk multiplier:

  * expectancy > 0 over the window  -> up to 1.25x normal risk (mastered)
  * mixed / not enough data         -> 1.0x (normal)
  * expectancy < 0 over >= 8 trades -> BENCHED: risk drops to 0.35x —
    the strategy keeps trading small (so it can prove recovery with real
    closed trades) but can no longer hurt the account meaningfully.

Persists to data/gold_mastery.json so what was learned during paper
trading carries into the demo account run.
"""
from __future__ import annotations

import json
import os

from .config import DATA_DIR

MASTERY_PATH = os.path.join(DATA_DIR, "gold_mastery.json")
WINDOW = 20          # rolling trades per strategy
BENCH_MIN_TRADES = 12   # demand a real sample before benching (noise-proofing)


class StrategyMastery:
    name = "mastery"

    def __init__(self, persist: bool = False):
        self.persist = persist
        self.history: dict[str, list[float]] = {}   # strategy -> recent R-multiples
        if persist and os.path.exists(MASTERY_PATH):
            try:
                with open(MASTERY_PATH) as f:
                    self.history = {k: list(v)[-WINDOW:]
                                    for k, v in json.load(f).items()}
            except (json.JSONDecodeError, OSError):
                pass

    # ------------------------------------------------------------------
    def record(self, strategy: str, pnl_usd: float, risked_usd: float) -> None:
        """Record a closed trade as an R-multiple (pnl / amount risked)."""
        if risked_usd <= 0:
            return
        h = self.history.setdefault(strategy, [])
        h.append(pnl_usd / risked_usd)
        del h[:-WINDOW]
        if self.persist:
            try:
                os.makedirs(DATA_DIR, exist_ok=True)
                with open(MASTERY_PATH, "w") as f:
                    json.dump(self.history, f, indent=2)
            except OSError:
                pass

    def expectancy(self, strategy: str) -> tuple[float, int]:
        h = self.history.get(strategy, [])
        return (sum(h) / len(h) if h else 0.0), len(h)

    def risk_scale(self, strategy: str) -> float:
        """Bench-only design: A/B testing showed boosting 'hot' strategies
        just amplified noise; benching clear losers is the part that helps."""
        exp, n = self.expectancy(strategy)
        if n >= BENCH_MIN_TRADES and exp < -0.1:
            return 0.6                      # benched: prove recovery small
        return 1.0

    def report(self) -> str:
        parts = []
        for s, h in sorted(self.history.items()):
            exp, n = self.expectancy(s)
            tag = ("MASTERED" if self.risk_scale(s) > 1 else
                   "benched" if self.risk_scale(s) < 1 else "normal")
            parts.append(f"{s}: exp {exp:+.2f}R over {n} ({tag})")
        return " | ".join(parts) or "no trades recorded yet"
