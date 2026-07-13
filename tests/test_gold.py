"""Gold trader tests: python3 -m unittest discover -s tests -v"""
import unittest

from goldtrader.broker import GoldBroker
from goldtrader.config import GoldParams
from goldtrader.datafeed.simulator import GoldSim, gold_snapshot
from goldtrader.orchestrator import GoldOrchestrator
from memetrader.engine.portfolio import Portfolio


class TestGoldBroker(unittest.TestCase):
    def test_no_leverage_possible(self):
        """The broker can only spend cash it is given — nothing more."""
        b = GoldBroker()
        snap = gold_snapshot(3350.0, 0.0, 3350.0, [3350.0])
        oz, spent = b.buy(snap, 10.0)
        self.assertEqual(spent, 10.0)
        self.assertLessEqual(oz * 3350.0, 10.0)   # never worth more than paid

    def test_round_trip_costs_spread(self):
        b = GoldBroker()
        snap = gold_snapshot(3350.0, 0.0, 3350.0, [3350.0])
        oz, spent = b.buy(snap, 100.0)
        back = b.sell(snap, oz)
        self.assertLess(back, spent)
        self.assertGreater(back, spent * 0.998)  # ~0.04% round trip


class TestAgents(unittest.TestCase):
    def test_sentinel_blocks_news_shock(self):
        from goldtrader.agents import EventSentinel
        s = EventSentinel()
        calm = [4100 + 0.05 * i for i in range(50)]
        self.assertTrue(s.check(calm, now=1000.0))
        shock = calm + [calm[-1] * 1.004]     # +0.4% in one minute
        self.assertFalse(s.check(shock, now=2000.0))
        # cooldown holds even after the tape calms
        self.assertFalse(s.check(calm, now=2000.0 + 60))

    def test_session_clock(self):
        from goldtrader.agents import SessionAgent
        a = SessionAgent()
        overlap = 14 * 3600.0      # 14:00 UTC — London/NY overlap
        graveyard = 22 * 3600.0    # 22:00 UTC
        self.assertTrue(a.tradeable(overlap))
        self.assertFalse(a.tradeable(graveyard))
        self.assertGreater(a.weight(overlap), a.weight(graveyard))

    def test_regime_labels(self):
        from goldtrader.agents import RegimeAgent
        r = RegimeAgent()
        trend = [4000 + 0.5 * i for i in range(300)]
        self.assertEqual(r.classify(trend), "trending")
        rng = [4000 + (3 if i % 2 else -3) for i in range(300)]
        self.assertEqual(r.classify(rng), "ranging")


class TestGoldEndToEnd(unittest.TestCase):
    def test_solvent_and_leverage_capped(self):
        params = GoldParams()
        params.risk.starting_bankroll_usd = 1000.0
        sim = GoldSim(seed=3)
        pf = Portfolio(1000.0)
        orch = GoldOrchestrator(params, portfolio=pf)
        for _ in range(1440 * 5):   # 5 sim days
            orch.on_price(sim.step(), sim.now_ts)
            pos = orch.engine.pos
            if pos is not None:
                self.assertLessEqual(     # leverage hard cap holds
                    pos.notional, pf.cash * params.max_leverage * 1.01)
        orch.liquidate(sim.now_ts, "test_end")
        self.assertGreater(pf.cash, 500.0)  # intact account (no blowups)

    def test_long_only_mode_respected(self):
        params = GoldParams()
        params.allow_short = False
        sim = GoldSim(seed=9)
        pf = Portfolio(1000.0)
        orch = GoldOrchestrator(params, portfolio=pf)
        for _ in range(1440 * 3):
            orch.on_price(sim.step(), sim.now_ts)
        orch.liquidate(sim.now_ts, "test_end")
        for t in pf.trades:
            self.assertEqual(t.symbol, "XAU-L")   # no shorts ever opened

    def test_gold_moves_are_gold_sized(self):
        sim = GoldSim(seed=11)
        start = sim.price
        for _ in range(1440):
            sim.step()
        day_move = abs(sim.price / start - 1)
        self.assertLess(day_move, 0.05)    # gold does not 2x in a day


if __name__ == "__main__":
    unittest.main()
