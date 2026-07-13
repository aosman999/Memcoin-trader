"""Day guard — trade as many times as you like, but protect the day.

  * DAILY LOSS STOP: if the day's equity drops the daily loss limit from
    the day's start, liquidate and stop until tomorrow — a losing day is
    capped at a scratch, never a blowup.
  * PROFIT LOCK (optional; disabled when profit_lock_trigger is huge):
    once the day is up enough, a floor ratchets under the peak so a
    green day cannot round-trip into red.
"""
from __future__ import annotations


class DayGuard:
    def __init__(self, risk, start_equity: float):
        self.risk = risk
        self.start = start_equity
        self.peak = start_equity
        self.halted = False
        self.reason = ""

    @property
    def armed(self) -> bool:
        return self.peak >= self.start * (1.0 + self.risk.profit_lock_trigger)

    def floor(self) -> float:
        if self.armed:
            return self.start + self.risk.profit_lock_keep * (self.peak - self.start)
        return self.start * (1.0 - self.risk.daily_loss_limit)

    def check(self, equity: float) -> bool:
        """Update with current equity; returns True when trading must halt."""
        if self.halted:
            return True
        self.peak = max(self.peak, equity)
        if equity <= self.floor():
            self.halted = True
            self.reason = "profit_lock" if self.armed else "daily_loss_stop"
        return self.halted
