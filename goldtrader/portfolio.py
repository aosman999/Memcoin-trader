"""Portfolio state: cash, closed-trade ledger, equity curve."""
from __future__ import annotations

import json
import os
from dataclasses import asdict

from .models import TradeRecord


class Portfolio:
    # in-memory cap: long live sessions append one point per tick; without
    # a bound this leaks (review finding). Peak/drawdown state is carried
    # in _peak/_max_dd so trimming never loses the true max drawdown.
    CURVE_CAP = 200_000

    def __init__(self, starting_cash: float):
        self.cash = starting_cash
        self.starting_cash = starting_cash
        self.trades: list[TradeRecord] = []
        self.equity_curve: list[float] = [starting_cash]
        self._peak = starting_cash
        self._max_dd = 0.0

    def mark(self, v: float) -> None:
        """Append an equity point with peak/drawdown bookkeeping."""
        self.equity_curve.append(v)
        self._note(v)

    def _note(self, v: float) -> None:
        self._peak = max(self._peak, v)
        if self._peak > 0:
            self._max_dd = max(self._max_dd, 1 - v / self._peak)
        if len(self.equity_curve) > self.CURVE_CAP:
            del self.equity_curve[: self.CURVE_CAP // 2]

    def stats(self) -> dict:
        n = len(self.trades)
        wins = [t for t in self.trades if t.pnl_usd > 0]
        losses = [t for t in self.trades if t.pnl_usd <= 0]
        gross_win = sum(t.pnl_usd for t in wins)
        gross_loss = -sum(t.pnl_usd for t in losses)
        eq = self.equity_curve
        peak, max_dd = self._peak, self._max_dd
        for v in eq:                       # covers curves loaded from disk
            peak = max(peak, v)
            if peak > 0:
                max_dd = max(max_dd, 1 - v / peak)
        return {
            "trades": n,
            "win_rate": len(wins) / n if n else 0.0,
            "profit_factor": (gross_win / gross_loss) if gross_loss > 0
                             else float("inf") if gross_win > 0 else 0.0,
            "total_pnl_usd": sum(t.pnl_usd for t in self.trades),
            "final_equity": eq[-1] if eq else self.starting_cash,
            "return_pct": (eq[-1] / self.starting_cash - 1) * 100 if eq else 0.0,
            "max_drawdown_pct": max_dd * 100,
            "avg_hold_minutes": sum(t.hold_minutes for t in self.trades) / n if n else 0.0,
        }

    def save(self, path: str) -> None:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w") as f:
            json.dump({
                "cash": self.cash,
                "starting_cash": self.starting_cash,
                "trades": [asdict(t) for t in self.trades],
                "equity_curve": self.equity_curve[-5000:],
            }, f, indent=2)

    @classmethod
    def load(cls, path: str) -> "Portfolio | None":
        if not os.path.exists(path):
            return None
        try:
            with open(path) as f:
                d = json.load(f)
            pf = cls(d["starting_cash"])
            pf.cash = d["cash"]
            pf.trades = [TradeRecord(**t) for t in d["trades"]]
            pf.equity_curve = d.get("equity_curve", [pf.starting_cash])
            return pf
        except (json.JSONDecodeError, KeyError, TypeError):
            return None
