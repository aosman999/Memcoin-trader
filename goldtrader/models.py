"""Gold trader data models."""
from __future__ import annotations

from dataclasses import dataclass


@dataclass
class TradeRecord:
    symbol: str              # "XAU-L" (long) / "XAU-S" (short)
    strategy: str
    entry_price: float
    exit_price: float
    notional_usd: float      # exposure at entry
    pnl_usd: float
    multiple: float          # 1 + pnl/notional
    hold_minutes: float
    exit_reason: str
    opened_at: float
    closed_at: float
