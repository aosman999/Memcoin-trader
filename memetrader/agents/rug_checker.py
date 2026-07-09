"""Rug Checker agent — the background-check AI.

Every candidate passes through here BEFORE any strategy may buy it.
Scores 0-100 from on-chain safety signals; anything below the configured
threshold is vetoed. In live mode the fields are populated from RugCheck +
DexScreener; in sim mode from the simulator's observable features.

The single biggest edge in memecoins is not picking winners —
it is refusing to hold things that go to zero by design.
"""
from __future__ import annotations

from ..config import StrategyParams
from ..models import SafetyReport, TokenSnapshot


class RugChecker:
    name = "rug_checker"

    def __init__(self, params: StrategyParams):
        self.params = params
        self._cache: dict[str, SafetyReport] = {}

    def check(self, t: TokenSnapshot) -> SafetyReport:
        score = 100
        flags: list[str] = []

        # --- hard authority checks: these are how most hard rugs happen ---
        if not t.mint_revoked:
            score -= 35
            flags.append("mint authority NOT revoked (infinite supply risk)")
        if not t.freeze_revoked:
            score -= 20
            flags.append("freeze authority NOT revoked (can block your sells)")
        if not t.lp_locked_or_burned and t.graduated:
            score -= 35
            flags.append("LP not locked/burned (classic liquidity pull setup)")

        # --- holder distribution: concentrated supply = exit liquidity ---
        if t.top10_holder_pct > 0.45:
            score -= 30
            flags.append(f"top10 hold {t.top10_holder_pct:.0%} of supply")
        elif t.top10_holder_pct > 0.30:
            score -= 15
            flags.append(f"top10 hold {t.top10_holder_pct:.0%} (elevated)")

        if t.deployer_holds_pct > 0.10:
            score -= 25
            flags.append(f"deployer still holds {t.deployer_holds_pct:.0%}")
        elif t.deployer_holds_pct > 0.05:
            score -= 10
            flags.append(f"deployer holds {t.deployer_holds_pct:.0%}")

        # --- softer signals ---
        if not t.has_socials:
            score -= 10
            flags.append("no socials/website")
        if t.liquidity < self.params.min_liquidity:
            score -= 15
            flags.append(f"thin liquidity ${t.liquidity:,.0f}")
        if t.holders < 20 and t.age_minutes > 30:
            score -= 10
            flags.append("holder count not growing")

        score = max(0, min(100, score))
        report = SafetyReport(
            address=t.address, score=score, flags=flags,
            passed=score >= self.params.min_safety_score,
        )
        self._cache[t.address] = report
        return report

    def last(self, address: str) -> SafetyReport | None:
        return self._cache.get(address)
