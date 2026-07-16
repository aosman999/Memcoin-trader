"""Mistake Analyst — the AI that studies every loss and learns from it.

Every losing trade gets an AUTOPSY: which strategy died, in which market
regime, in which session, how (stopped? guard? margin?), after how long.
Autopsies accumulate in data/gold_mistakes.json and the analyst does two
things with them:

  1. STUDY — aggregates the journal into named patterns ("breakout longs
     die on ranging days", "Asia entries underperform") and writes a
     human-readable mistake report (`python3 -m goldtrader mistakes`).

  2. ACT — the fool-me-twice rule: when the SAME strategy loses TWICE in
     the SAME regime on the SAME day, that (strategy, regime) pair is
     benched until tomorrow. One loss is tuition; the second identical
     loss on the same tape is a lesson; there is no third.

The acting rule is A/B tested like every other feature before it gates
real entries.
"""
from __future__ import annotations

import datetime as _dt
import json
import os
from collections import defaultdict

from .config import DATA_DIR

MISTAKES_PATH = os.path.join(DATA_DIR, "gold_mistakes.json")
JOURNAL_LIMIT = 500
FOOL_ME_LIMIT = 2      # same strategy + same regime + same day


def _day(ts: float) -> str:
    return _dt.datetime.utcfromtimestamp(ts).strftime("%Y-%m-%d")


class MistakeAnalyst:
    name = "mistake_analyst"

    def __init__(self, persist: bool = False):
        self.persist = persist
        self.journal: list[dict] = []
        if persist and os.path.exists(MISTAKES_PATH):
            try:
                with open(MISTAKES_PATH) as f:
                    self.journal = list(json.load(f))[-JOURNAL_LIMIT:]
            except (json.JSONDecodeError, OSError):
                pass

    # ------------------------------------------------------------------
    def on_trade_closed(self, rec, regime: str, session_weight: float,
                        now: float) -> None:
        if rec.pnl_usd >= 0:
            return                       # the journal is for losses only
        self.journal.append({
            "day": _day(now),
            "strategy": rec.strategy,
            "side": "short" if rec.symbol.endswith("-S") else "long",
            "regime": regime,
            "session_weight": round(session_weight, 2),
            "exit_reason": rec.exit_reason,
            "hold_minutes": round(rec.hold_minutes, 1),
            "pnl_usd": round(rec.pnl_usd, 2),
        })
        del self.journal[:-JOURNAL_LIMIT]
        if self.persist:
            try:
                os.makedirs(DATA_DIR, exist_ok=True)
                with open(MISTAKES_PATH, "w") as f:
                    json.dump(self.journal, f, indent=2)
            except OSError:
                pass

    # ------------------------------------------------------------------
    def blocked(self, strategy: str, regime: str, now: float) -> bool:
        """Fool-me-twice: bench (strategy, regime) after 2 same-day losses."""
        day = _day(now)
        losses = sum(1 for m in self.journal
                     if m["day"] == day and m["strategy"] == strategy
                     and m["regime"] == regime)
        return losses >= FOOL_ME_LIMIT

    # ------------------------------------------------------------------
    def report(self) -> str:
        if not self.journal:
            return "mistake journal is empty — no losses recorded yet"
        by_pair: dict = defaultdict(lambda: [0, 0.0])
        by_reason: dict = defaultdict(int)
        for m in self.journal:
            k = f"{m['strategy']} in {m['regime']} markets"
            by_pair[k][0] += 1
            by_pair[k][1] += m["pnl_usd"]
            by_reason[m["exit_reason"]] += 1
        lines = [f"MISTAKE JOURNAL — {len(self.journal)} losses studied",
                 "", "loss patterns (worst first):"]
        for k, (n, pnl) in sorted(by_pair.items(), key=lambda kv: kv[1][1]):
            lines.append(f"  {k}: {n} losses, ${pnl:+,.2f}")
        lines.append("")
        lines.append("how losses end: " + ", ".join(
            f"{r} x{n}" for r, n in sorted(by_reason.items(),
                                           key=lambda kv: -kv[1])))
        fooled = defaultdict(int)
        for m in self.journal:
            fooled[(m["day"], m["strategy"], m["regime"])] += 1
        repeats = sum(1 for v in fooled.values() if v >= FOOL_ME_LIMIT)
        lines.append(f"repeat-mistake days (same strategy+regime 2+ losses): {repeats}")
        return "\n".join(lines)
