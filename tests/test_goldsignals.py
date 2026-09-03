"""Tests for the Telegram signal bot.

The bot's whole job is to say what cTrader did, in one exact format, without
ever leaking the bot token. So that is what these check: the shape of the
message, that the feed is read exactly once, and that the credential cannot
escape. tools/verify/signals-negcontrol.py breaks each of these on purpose and
requires this file to notice.
"""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "tools"))
import goldsignals as gs  # noqa: E402


ENTRY = {
    "t": "entry", "symbol": "XAUUSD", "demo": True, "signal": 7,
    "model": "BREAKER", "side": "BUY", "entry": 4301.85, "level": 4301.20,
    "stop": 4289.55, "tps": [4308.02, 4314.19, 4320.36],
    "target_from": "reach 1.67x", "risk_pct": 1.0, "detail": "breaker retest",
}


class TestSignalFormat(unittest.TestCase):
    def test_entry_has_the_requested_shape(self):
        text = gs.format_entry(ENTRY)
        lines = text.split("\n")
        self.assertEqual(lines[0], "\U0001f7e2 Buy gold")
        self.assertTrue(lines[1].startswith("Entry- "), lines[1])
        self.assertEqual(lines[2], "Tp 1: 4308.02")
        self.assertEqual(lines[3], "Tp2: 4314.19")
        self.assertEqual(lines[4], "Tp3: 4320.36")
        self.assertEqual(lines[5], "Sl \U0001f6d1: 4289.55")

    def test_sell_says_sell(self):
        ev = dict(ENTRY, side="SELL")
        self.assertTrue(gs.format_entry(ev).startswith("\U0001f534 Sell gold"))

    def test_buy_and_sell_are_never_rendered_the_same(self):
        # The whole message has to read differently at a glance: a follower
        # scrolling a channel should not have to parse prices to tell which way
        # a signal points.
        buy = gs.format_entry(ENTRY)
        sell = gs.format_entry(dict(ENTRY, side="SELL",
                                    tps=[4295.68, 4289.51, 4283.34],
                                    stop=4314.15))
        self.assertNotEqual(buy.split("\n")[0], sell.split("\n")[0])
        self.assertIn("\U0001f7e2", buy)
        self.assertIn("\U0001f534", sell)
        self.assertNotIn("\U0001f534", buy.split("\n")[0])
        self.assertNotIn("\U0001f7e2", sell.split("\n")[0])

    def test_the_direction_marker_is_on_every_message_about_the_trade(self):
        for fn, ev in ((gs.format_tp, {"rung": 1, "of": 3, "entry": 1.0,
                                       "price": 2.0, "profit": 1.0}),
                       (gs.format_sl, {"entry": 1.0, "price": 0.5, "profit": -1.0}),
                       (gs.format_setup, {"level": 1.0, "stop": 0.5,
                                          "projected_tp": 2.0})):
            self.assertIn("\U0001f7e2", fn(dict(ev, side="BUY")))
            self.assertIn("\U0001f534", fn(dict(ev, side="SELL")))

    def test_entry_zone_is_the_level_and_the_fill(self):
        self.assertEqual(gs.entry_zone(ENTRY), "4301.20 - 4301.85")

    def test_entry_zone_collapses_when_they_are_the_same(self):
        # An invented range would be a lie: followers would place a limit that
        # never fills, or one further from the model's level than the bot's own.
        ev = dict(ENTRY, level=4301.85)
        self.assertEqual(gs.entry_zone(ev), "4301.85")

    def test_take_profit_count_follows_the_feed(self):
        one = gs.format_entry(dict(ENTRY, tps=[4308.02]))
        self.assertIn("Tp 1: 4308.02", one)
        self.assertNotIn("Tp2:", one)

    def test_the_account_type_is_not_mentioned_by_default(self):
        # The owner asked for it out of the channel. Recorded here so the
        # behaviour is deliberate rather than something that quietly regressed.
        text = gs.format_entry(ENTRY)
        self.assertNotIn("Demo", text)
        self.assertNotIn("LIVE", text)

    def test_the_switch_puts_the_account_type_back(self):
        self.assertIn("Demo account",
                      gs.format_entry(ENTRY, show_account=True))
        self.assertIn("LIVE ACCOUNT",
                      gs.format_entry(dict(ENTRY, demo=False), show_account=True))

    def test_tp_message_names_the_rung_and_the_state(self):
        first = gs.format_tp({"side": "BUY", "rung": 1, "of": 3, "entry": 4301.85,
                              "price": 4308.02, "profit": 6.12})
        self.assertIn("TP1 HIT", first)
        self.assertIn("Runners still open", first)
        last = gs.format_tp({"side": "BUY", "rung": 3, "of": 3, "entry": 4301.85,
                             "price": 4320.36, "profit": 18.5})
        self.assertIn("TP3 HIT", last)
        self.assertIn("Full position closed", last)

    def test_stop_message_is_not_dressed_up(self):
        text = gs.format_sl({"side": "SELL", "entry": 4301.85, "price": 4315.0,
                             "profit": -30.4})
        self.assertIn("SL hit", text)
        self.assertIn("-30.40", text)

    def test_a_trailed_exit_in_profit_is_not_called_a_loss(self):
        text = gs.format_close({"side": "BUY", "entry": 4301.85, "price": 4310.2,
                                "profit": 5.4})
        self.assertIn("IN PROFIT", text)

    def test_get_ready_says_nothing_is_open(self):
        text = gs.format_setup({"side": "BUY", "model": "MSS", "level": 4301.2,
                                "stop": 4288.9, "projected_tp": 4325.6})
        self.assertIn("GET READY", text)
        self.assertIn("Nothing is open yet", text)

    def test_news_message_states_it_is_not_traded_on(self):
        text = gs.format_news({"source": "cnn.com", "headline": "x",
                               "impact": 5.0, "lean": "leans gold UP"})
        self.assertIn("No signal is taken from a headline", text)

    def test_render_respects_the_filter(self):
        self.assertIsNone(gs.render(ENTRY, {"tp"}))
        self.assertIsNotNone(gs.render(ENTRY, {"entry"}))

    def test_unknown_event_type_is_silent(self):
        self.assertIsNone(gs.render({"t": "something_new"}, gs.ALL_EVENTS))

    def test_a_malformed_event_does_not_crash_the_bot(self):
        text = gs.render({"t": "tp"}, {"tp"})       # no fields at all
        self.assertIsInstance(text, str)


class TestTheMethodStaysPrivate(unittest.TestCase):
    """The channel gets the trade and a risk reminder. It does not get the
    model name or the reasoning: nobody following a signal needs the method,
    and a signal that explains itself invites arguing with it. The feed still
    carries both — the cTrader log and the ledger need them — so this is a
    rendering rule, and rendering rules rot the moment someone edits a
    formatter without thinking about it. Hence a test."""

    LOADED = {
        "model": "BREAKER", "detail": "old low taken, price back through the "
        "swing high; buying its breaker", "models": "MSS BREAKER VOID",
    }

    def test_no_strategy_word_reaches_a_signal(self):
        text = gs.format_entry(dict(ENTRY, **self.LOADED))
        self.assertEqual(gs.leaks_method(text), [], text)

    def test_no_strategy_word_reaches_a_get_ready(self):
        ev = dict(self.LOADED, side="BUY", level=4301.2, stop=4288.9,
                  projected_tp=4325.6)
        self.assertEqual(gs.leaks_method(gs.format_setup(ev)), [])

    def test_no_strategy_word_reaches_the_start_message(self):
        ev = dict(self.LOADED, symbol="XAUUSD", timeframe="Minute5")
        self.assertEqual(gs.leaks_method(gs.format_start(ev)), [])

    def test_every_formatter_is_clean_on_loaded_input(self):
        loaded = dict(ENTRY, **self.LOADED)
        loaded.update(rung=1, of=3, price=4308.02, profit=6.1, impact=7.0,
                      lean="leans gold UP", source="cnn.com", headline="x",
                      until="2026-09-03T14:01:00Z", equity=2540.0,
                      start_equity=3000.0,
                      trades_today=2, open_signals=1, waiting_setups=1)
        for kind in gs.ALL_EVENTS:
            text = gs.render(dict(loaded, t=kind), gs.ALL_EVENTS)
            self.assertEqual(gs.leaks_method(text), [],
                             "%s leaked: %s" % (kind, text))

    def test_the_risk_line_is_on_the_signal(self):
        self.assertIn("Utilize risk management techniques to protect capital.",
                      gs.format_entry(ENTRY))

    def test_the_detector_itself_is_not_vacuous(self):
        # If leaks_method() could never return anything, every test above would
        # pass with the model name printed in full.
        self.assertEqual(gs.leaks_method("entered on the BREAKER"), ["BREAKER"])


class TestTokenSafety(unittest.TestCase):
    TOKEN = "8123456789:AAFtest_token_value_that_must_never_leak"

    def test_token_is_stripped_from_error_text(self):
        tg = gs.Telegram(self.TOKEN, "-100123")
        leaked = "HTTPError: 401 at %s" % tg.url()
        self.assertNotIn(self.TOKEN, tg.redact(leaked))

    def test_the_numeric_half_alone_is_also_stripped(self):
        tg = gs.Telegram(self.TOKEN, "-100123")
        self.assertNotIn("8123456789", tg.redact("bot 8123456789 failed"))

    def test_the_SECRET_half_is_stripped_on_its_own(self):
        # The reason this test exists as well as the two above: redacting the
        # numeric prefix alone makes the FULL token stop appearing verbatim, so
        # a test that only looks for the whole token passes with the real
        # secret still in the log. That exact vacuous check has shipped in this
        # project once already. This one looks for the half that is the secret.
        tg = gs.Telegram(self.TOKEN, "-100123")
        secret = self.TOKEN.split(":", 1)[1]
        self.assertNotIn(secret, tg.redact("HTTPError 401 at %s" % tg.url()))

    def test_dry_run_never_builds_a_request(self):
        sent = []

        def must_not_be_called(*a, **k):
            raise AssertionError("a dry run reached the network")

        tg = gs.Telegram(self.TOKEN, "-100123", dry_run=True,
                         log=lambda m: sent.append(m), opener=must_not_be_called)
        self.assertTrue(tg.send("hello"))
        self.assertEqual(len(sent), 1)
        self.assertNotIn(self.TOKEN, sent[0])

    def test_a_real_send_posts_the_text_to_the_channel(self):
        seen = {}

        class Resp(object):
            def read(self):
                return b"{}"

            def close(self):
                pass

        def fake(req, timeout=None):
            seen["url"] = req.full_url
            seen["body"] = req.data.decode("utf-8")
            return Resp()

        tg = gs.Telegram(self.TOKEN, "-100123", opener=fake)
        self.assertTrue(tg.send("Buy gold"))
        self.assertIn("sendMessage", seen["url"])
        self.assertIn("chat_id=-100123", seen["body"])
        self.assertIn("Buy+gold", seen["body"])


class TestFeedReading(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.path = os.path.join(self.dir, "signals.jsonl")

    def write(self, *rows, **kw):
        with open(self.path, "a" if kw.get("append") else "w", encoding="utf-8") as fh:
            for r in rows:
                fh.write(json.dumps(r) + "\n")

    def test_reads_only_what_is_new(self):
        self.write({"t": "start"}, {"t": "stop"})
        lines, off = gs.read_new_lines(self.path, 0)
        self.assertEqual(len(lines), 2)
        again, off2 = gs.read_new_lines(self.path, off)
        self.assertEqual(again, [])
        self.write({"t": "entry"}, append=True)
        more, _ = gs.read_new_lines(self.path, off2)
        self.assertEqual(len(more), 1)

    def test_a_half_written_line_is_left_for_next_time(self):
        # cTrader appends; a read can land mid-write. Posting half a JSON object
        # would drop the event entirely, because the next read starts after it.
        with open(self.path, "w", encoding="utf-8") as fh:
            fh.write('{"t":"entry","side":"BUY"}\n{"t":"tp","ru')
        lines, off = gs.read_new_lines(self.path, 0)
        self.assertEqual(len(lines), 1)
        with open(self.path, "a", encoding="utf-8") as fh:
            fh.write('ng":1}\n')
        rest, _ = gs.read_new_lines(self.path, off)
        self.assertEqual(len(rest), 1)
        self.assertEqual(json.loads(rest[0])["rung"], 1)

    def test_a_truncated_feed_restarts_instead_of_going_silent(self):
        self.write({"t": "start"}, {"t": "stop"})
        _, off = gs.read_new_lines(self.path, 0)
        self.write({"t": "entry"})                  # rewrites the file, shorter
        lines, _ = gs.read_new_lines(self.path, off)
        self.assertEqual(len(lines), 1)

    def test_missing_feed_is_not_an_error(self):
        lines, off = gs.read_new_lines(os.path.join(self.dir, "nope"), 0)
        self.assertEqual((lines, off), ([], 0))


if __name__ == "__main__":
    unittest.main()
