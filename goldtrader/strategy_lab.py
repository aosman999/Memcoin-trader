"""Gold Strategy Lab — the AI that improves the other AIs.

Evolutionary search over every gold parameter: a population of mutated
GoldParams each paper-trades multi-day epochs on the simulator across
several seeds; fitness is median-oriented, drawdown-penalized growth.
The champion is saved to data/gold_params.json and every mode (campaign,
paper, both MT5 bridges) picks it up automatically.

Exit policy notes learned the hard way and enforced here:
  * exit_style stays whatever the A/B testing chose (not evolved blindly)
  * day-guard knobs are policy, not evolvable (evolution once learned to
    game the guard in the memecoin lab)
"""
from __future__ import annotations

import random

from .config import GOLD_PARAMS_PATH, GoldParams

# (field, lo, hi, is_int) — entry knobs
GOLD_SPACE = [
    ("ema_fast", 8, 40, True),
    ("ema_slow", 30, 120, True),
    ("trend_min_slope", 2e-5, 3e-4, False),
    ("mr_window", 20, 90, True),
    ("mr_entry_z", -3.0, -1.4, False),
    ("mr_max_trend_z", 0.3, 1.2, False),
    ("bo_lookback", 60, 240, True),
    ("bo_min_range_expansion", 1.2, 2.5, False),
    ("sweep_lookback", 120, 480, True),
    ("sweep_margin", 0.0001, 0.001, False),
    ("sweep_atr_mult", 1.0, 3.0, False),
    ("vol_target_ref", 2.5e-4, 4.5e-4, False),
]
# risk knobs (stop defines sizing; tp expressed as reward:risk ratio)
RISK_SPACE = [
    ("stop_loss", 0.003, 0.010, False),
    ("risk_per_trade", 0.02, 0.06, False),
]
RR_RANGE = (1.5, 3.0)   # tp distance as a multiple of stop distance


def _run_epoch(params: GoldParams, seed: int, days: int) -> dict:
    from .day_guard import DayGuard
    from .portfolio import Portfolio

    from .datafeed.simulator import GoldSim
    from .orchestrator import GoldOrchestrator

    sim = GoldSim(seed=seed, start_price=4100.0)
    pf = Portfolio(params.risk.starting_bankroll_usd)
    orch = GoldOrchestrator(params, portfolio=pf)
    for _ in range(days):
        guard = DayGuard(params.risk, orch.equity(sim.price))
        for _ in range(1440):
            orch.on_price(sim.step(), sim.now_ts)
            if guard.check(pf.equity_curve[-1]):
                orch.liquidate(sim.now_ts, guard.reason)
                for _ in range(1439 - (sim.tick % 1440) % 1440):
                    sim.step()
                break
    orch.liquidate(sim.now_ts, "end")
    s = pf.stats()
    s["multiple"] = pf.cash / params.risk.starting_bankroll_usd
    return s


def fitness_of(multiples: list[float], trades: int) -> float:
    if trades < 8:
        return -1.0
    ms = sorted(multiples)
    median = ms[len(ms) // 2]
    worst = ms[0]
    # median growth, penalized when the worst seed is a bad loss
    return (median - 1.0) + 0.7 * min(0.0, worst - 1.0)


class GoldStrategyLab:
    name = "gold_strategy_lab"

    def __init__(self, base: GoldParams | None = None, seed: int = 11):
        self.rng = random.Random(seed)
        self.base = base or GoldParams.load()

    def _mutate(self, p: GoldParams, rate: float = 0.4) -> GoldParams:
        d = p.to_dict()
        rd = d["risk"]
        for name, lo, hi, is_int in GOLD_SPACE:
            if self.rng.random() < rate:
                span = (hi - lo) * 0.25
                v = d[name] + self.rng.uniform(-span, span)
                d[name] = int(round(min(hi, max(lo, v)))) if is_int else min(hi, max(lo, v))
        for name, lo, hi, is_int in RISK_SPACE:
            if self.rng.random() < rate:
                span = (hi - lo) * 0.25
                v = rd[name] + self.rng.uniform(-span, span)
                rd[name] = min(hi, max(lo, v))
        if self.rng.random() < rate:
            rr = self.rng.uniform(*RR_RANGE)
            rd["tp_multiples"] = [1.0 + rd["stop_loss"] * rr]
        else:
            # keep tp consistent with a possibly-mutated stop
            old_rr = (p.risk.tp_multiples[0] - 1.0) / p.risk.stop_loss
            rd["tp_multiples"] = [1.0 + rd["stop_loss"] * old_rr]
        if d["ema_fast"] >= d["ema_slow"]:
            d["ema_fast"] = max(8, d["ema_slow"] // 3)
        return GoldParams.from_dict(d)

    def evolve(self, generations: int = 4, population: int = 8,
               days: int = 10, eval_seeds: tuple = (501, 502, 503),
               verbose: bool = True,
               save_path: str = GOLD_PARAMS_PATH) -> GoldParams:
        pop = [self.base] + [self._mutate(self.base, 0.6)
                             for _ in range(population - 1)]
        best, best_fit = self.base, float("-inf")
        for gen in range(1, generations + 1):
            scored = []
            for cand in pop:
                mults, trades = [], 0
                for s in eval_seeds:
                    st = _run_epoch(cand, seed=s, days=days)
                    mults.append(st["multiple"])
                    trades += st["trades"]
                scored.append((fitness_of(mults, trades // len(eval_seeds)),
                               cand, mults))
            scored.sort(key=lambda x: x[0], reverse=True)
            top_fit, top_cand, top_mults = scored[0]
            if top_fit > best_fit:
                best_fit, best = top_fit, top_cand
            if verbose:
                print(f"gen {gen}: fitness {top_fit:+.3f} | "
                      f"{days}d multiples {['%.2f' % m for m in sorted(top_mults)]}")
            elite = [c for _, c, _ in scored[: max(2, population // 3)]]
            pop = elite + [self._mutate(self.rng.choice(elite))
                           for _ in range(population - len(elite))]

        # SAVE GUARD: a low-budget run must never overwrite a better
        # champion. The challenger has to beat the incumbent on HOLDOUT
        # seeds neither trained on, else the incumbent stays.
        holdout = (911, 912, 913)
        def holdout_fit(cand: GoldParams) -> float:
            ms, tr = [], 0
            for s in holdout:
                st = _run_epoch(cand, seed=s, days=days)
                ms.append(st["multiple"]); tr += st["trades"]
            return fitness_of(ms, tr // len(holdout))
        challenger, incumbent = holdout_fit(best), holdout_fit(self.base)
        if challenger >= incumbent:
            best.save(save_path)
            if verbose:
                print(f"champion saved -> {save_path} "
                      f"(holdout {challenger:+.3f} vs incumbent {incumbent:+.3f})")
            return best
        if verbose:
            print(f"challenger LOST holdout ({challenger:+.3f} vs "
                  f"{incumbent:+.3f}) — keeping incumbent params")
        return self.base
