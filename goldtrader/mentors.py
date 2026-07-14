"""Mentor AI — studies trading mentors and encodes what they teach.

A mentor's edge only helps a bot if it can be written as exact rules.
This module keeps the mentor playbook registry (what each mentor
teaches, what of it is encoded where) and implements the cross-mentor
DISCIPLINE layer. Mentor-derived ENTRY logic lives in strategies.py
(mentor_sweep) so the Strategy Lab can evolve it like everything else.

Encoded so far
--------------
ICT (Inner Circle Trader):
  * liquidity sweeps — price runs stops beyond a prior high/low, then
    reverses; trade the reversal            -> strategies.mentor_sweep
  * kill zones — London open (07-10 UTC) and NY open (12-15 UTC) are
    when the real moves start               -> mentor_sweep time gate
  * displacement — the reversal must be FAST to count
                                            -> mentor_sweep ATR filter
PB Blake (PB Trading — simplified ICT):
  * if-then conditional setups on displacement at key levels; react to
    confirmed behavior, never predict       -> mentor_sweep is exactly
    an if-then: IF sweep AND displacement THEN enter
TJR:
  * sweep of session highs/lows + break of structure in the NY session
                                            -> mentor_sweep + kill zone
Fabio Valentini (auction-market scalper):
  * min 2:1 reward:risk                     -> already the exit policy
  * small fixed risk per trade              -> already the sizing rule
  * HARD STOP after 3 losing trades in a day -> MentorDiscipline below
MunYun:
  * PENDING — no reliable public documentation found to encode; send
    source material (videos/notes) and it gets distilled like the rest.

Add future mentors by appending to MENTOR_PLAYBOOKS and encoding their
mechanizable rules; anything non-mechanizable ("trust your gut") is
recorded but marked not-encodable.
"""
from __future__ import annotations

import datetime as _dt

MENTOR_PLAYBOOKS: dict[str, dict] = {
    "ICT": {
        "status": "encoded",
        "concepts": ["liquidity sweeps", "kill zones", "displacement",
                     "session highs/lows as magnets"],
        "encoded_in": ["strategies.mentor_sweep"],
    },
    "PB Blake": {
        "status": "encoded",
        "concepts": ["simplified ICT", "if-then conditional setups",
                     "displacement confirmation at key levels"],
        "encoded_in": ["strategies.mentor_sweep"],
    },
    "TJR": {
        "status": "encoded",
        "concepts": ["SMC liquidity sweeps", "break of structure",
                     "NY session focus"],
        "encoded_in": ["strategies.mentor_sweep"],
    },
    "Fabio Valentini": {
        "status": "encoded",
        "concepts": ["auction market theory", "min 2:1 RR",
                     "0.25-1% fixed risk", "3-loss daily hard stop"],
        "encoded_in": ["config.RiskParams (RR & sizing)",
                       "mentors.MentorDiscipline (3-loss stop)"],
    },
    "MunYun": {
        "status": "pending",
        "concepts": [],
        "encoded_in": [],
        "note": "no reliable public documentation found — owner to supply",
    },
}


def in_kill_zone(ts: float) -> bool:
    """ICT kill zones: London open 07-10 UTC, NY open 12-15 UTC."""
    t = _dt.datetime.utcfromtimestamp(ts)
    h = t.hour + t.minute / 60.0
    return (7.0 <= h < 10.0) or (12.0 <= h < 15.0)


class MentorDiscipline:
    """Valentini's rule: three losing trades in a day means the market
    isn't giving you your setup today — stop, come back tomorrow."""
    name = "mentor_discipline"
    MAX_LOSSES_PER_DAY = 3

    def __init__(self):
        self._day = ""
        self._losses = 0

    def on_trade_closed(self, pnl_usd: float, now: float) -> None:
        day = _dt.datetime.utcfromtimestamp(now).strftime("%Y-%m-%d")
        if day != self._day:
            self._day, self._losses = day, 0
        if pnl_usd < 0:
            self._losses += 1

    def entries_allowed(self, now: float) -> bool:
        day = _dt.datetime.utcfromtimestamp(now).strftime("%Y-%m-%d")
        if day != self._day:
            return True
        return self._losses < self.MAX_LOSSES_PER_DAY

    def report(self) -> str:
        return f"losses today: {self._losses}/{self.MAX_LOSSES_PER_DAY}"
