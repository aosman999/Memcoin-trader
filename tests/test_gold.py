"""Gold trader tests: python3 -m unittest discover -s tests -v"""
import unittest

from goldtrader.config import GoldParams
from goldtrader.datafeed.simulator import GoldSim
from goldtrader.orchestrator import GoldOrchestrator
from goldtrader.portfolio import Portfolio


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


class TestNewsAgent(unittest.TestCase):
    def test_bias_blocks_counter_trades(self):
        from goldtrader.news_agent import NewsAgent
        n = NewsAgent(enabled=True)
        n.bias = 4                              # strongly gold-bullish news
        self.assertTrue(n.entry_allowed(+1, now=0.0))
        self.assertFalse(n.entry_allowed(-1, now=0.0))
        n.bias = -4                             # strongly gold-bearish news
        self.assertFalse(n.entry_allowed(+1, now=0.0))
        self.assertTrue(n.entry_allowed(-1, now=0.0))

    def test_impact_window_blocks_everything(self):
        from goldtrader.news_agent import NewsAgent
        n = NewsAgent(enabled=True)
        n._impact_until = 1000.0
        self.assertFalse(n.entry_allowed(+1, now=500.0))
        self.assertFalse(n.entry_allowed(-1, now=500.0))
        self.assertTrue(n.entry_allowed(+1, now=1500.0))

    def test_offline_stand_down(self):
        from goldtrader.news_agent import NewsAgent
        n = NewsAgent(enabled=True)
        for i in range(3):                       # no network here: 3 failures
            n._next_poll = 0.0
            n.update(now=float(i))
        self.assertFalse(n.enabled)              # agent stands down silently
        self.assertTrue(n.entry_allowed(+1, now=99.0))

    def test_headline_scoring(self):
        from goldtrader import news_agent as na
        titles = ["Missile strike escalates war in region",
                  "Fed signals rate cut amid crisis"]
        bull = sum(1 for t in titles for k in na.GOLD_BULLISH if k in t.lower())
        bear = sum(1 for t in titles for k in na.GOLD_BEARISH if k in t.lower())
        self.assertGreater(bull - bear, 2)       # clearly gold-bullish set


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
        self.assertGreater(pf.cash, 400.0)  # intact account (no blowups)

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
        self.assertLess(day_move, 0.06)    # gold does not 2x in a day

    def test_params_roundtrip(self):
        p = GoldParams()
        q = GoldParams.from_dict(p.to_dict())
        self.assertEqual(p.to_dict(), q.to_dict())


class TestDayGuard(unittest.TestCase):
    def test_loss_stop(self):
        from goldtrader.config import RiskParams
        from goldtrader.day_guard import DayGuard
        g = DayGuard(RiskParams(), 100.0)
        self.assertFalse(g.check(90.0))       # -10%: keep trading (limit 15%)
        self.assertTrue(g.check(84.0))        # -16% -> daily loss stop
        self.assertEqual(g.reason, "daily_loss_stop")
