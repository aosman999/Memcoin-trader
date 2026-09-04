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
from datetime import datetime, timedelta, timezone

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


class TestNewsGate(unittest.TestCase):
    """The channel got the same airstrike three times, from three wires, at the
    lowest possible impact score. Both halves of that were bugs."""

    # ORDINARY news by default, deliberately below the urgent threshold. A
    # fixture that is urgent by accident exempts itself from the hourly cap,
    # so the tests for that cap silently stop testing anything.
    ORDINARY = 7.0

    def ev(self, headline, impact=None):
        return {"t": "news", "source": "x", "headline": headline,
                "impact": self.ORDINARY if impact is None else impact,
                "lean": "leans gold UP"}

    def test_the_fixture_is_not_accidentally_urgent(self):
        self.assertLess(self.ORDINARY, gs.NEWS_URGENT_IMPACT)
        self.assertGreaterEqual(self.ORDINARY, gs.NEWS_MIN_IMPACT)

    def test_the_same_story_from_two_wires_goes_out_once(self):
        gate = gs.NewsGate()
        a = "Vance: US probing airstrike that Iran says hit a wedding party - Military Times"
        b = "Vance: US probing airstrike that Iran says hit a wedding party - Al-Monitor"
        self.assertTrue(gate.allow(self.ev(a))[0])
        allowed, why = gate.allow(self.ev(b))
        self.assertFalse(allowed)
        self.assertIn("same story", why)

    def test_a_reworded_version_of_the_same_story_is_still_the_same_story(self):
        # The wires rewrite the wording, not the facts.
        gate = gs.NewsGate()
        self.assertTrue(gate.allow(self.ev(
            "US probing airstrike that Iran says hit a wedding party - Reuters"))[0])
        self.assertFalse(gate.allow(self.ev(
            "Iran says US airstrike hit a wedding party, probe under way - AP"))[0])

    def test_a_genuinely_different_story_still_gets_through(self):
        gate = gs.NewsGate()
        self.assertTrue(gate.allow(self.ev(
            "US probing airstrike that Iran says hit a wedding party - Reuters"))[0])
        self.assertTrue(gate.allow(self.ev(
            "South Korea prepares Hormuz military deployment - Reuters"))[0])

    def test_two_short_unrelated_headlines_are_not_merged(self):
        # Short headlines are mostly filler words. "Gold rises after the report"
        # and "Oil falls after the report" share after/the/report -- three of
        # five words, over the similarity threshold -- so without dropping
        # stopwords the second story would be silently swallowed as a repeat of
        # the first. They are opposite stories about different instruments.
        gate = gs.NewsGate(min_impact=1.0)
        self.assertTrue(gate.allow(self.ev("Gold rises after the report"))[0])
        allowed, why = gate.allow(self.ev("Oil falls after the report"))
        self.assertTrue(allowed, "different story suppressed as a duplicate: %s" % why)

    def test_weak_headlines_do_not_interrupt(self):
        gate = gs.NewsGate(min_impact=6.0)
        allowed, why = gate.allow(self.ev("Something mildly gold related", impact=3.0))
        self.assertFalse(allowed)
        self.assertIn("impact", why)
        self.assertTrue(gate.allow(self.ev("A real escalation", impact=7.0))[0])

    def test_major_news_is_not_held_behind_routine_news(self):
        # The cap exists so wire traffic cannot fill a channel. It must not be
        # the reason a war headline waits behind three stories about the oil
        # price -- which is exactly what a single-tier limit would do.
        gate = gs.NewsGate(min_impact=6.0, max_per_hour=2, urgent_impact=9.0)
        self.assertTrue(gate.allow(self.ev("routine one about the fed", 6.5))[0])
        self.assertTrue(gate.allow(self.ev("routine two about opec quotas", 6.5))[0])
        self.assertFalse(gate.allow(self.ev("routine three about tariffs", 6.5))[0])
        allowed, why = gate.allow(self.ev("iran launches missiles at israel", 12.0))
        self.assertTrue(allowed, "a major event was held behind routine news")
        self.assertEqual(why, "urgent")

    def test_major_news_does_not_use_up_the_ordinary_allowance(self):
        gate = gs.NewsGate(min_impact=6.0, max_per_hour=2, urgent_impact=9.0)
        self.assertTrue(gate.allow(self.ev("war breaks out in the strait", 12.0))[0])
        self.assertTrue(gate.allow(self.ev("routine one about the fed", 6.5))[0])
        self.assertTrue(gate.allow(self.ev("routine two about opec quotas", 6.5))[0])

    def test_major_news_is_still_deduplicated(self):
        # Being important does not make a story worth sending three times
        # because three outlets filed it.
        gate = gs.NewsGate(min_impact=6.0, urgent_impact=9.0)
        self.assertTrue(gate.allow(self.ev(
            "Iran launches missiles at Israeli bases - Reuters", 12.0))[0])
        self.assertFalse(gate.allow(self.ev(
            "Iran launches missiles at Israeli bases - CNN", 12.0))[0])

    def test_a_major_headline_is_marked_as_such(self):
        gate = gs.NewsGate(min_impact=6.0, urgent_impact=9.0)
        text = gs.render(self.ev("war breaks out in the strait", 12.0), {"news"}, gate)
        self.assertIn("MAJOR", text)
        gate2 = gs.NewsGate(min_impact=6.0, urgent_impact=9.0)
        ordinary = gs.render(self.ev("routine story about the fed", 6.5), {"news"}, gate2)
        self.assertNotIn("MAJOR", ordinary)

    def test_a_busy_news_hour_cannot_flood_the_channel(self):
        gate = gs.NewsGate(min_impact=1.0, max_per_hour=2)
        self.assertTrue(gate.allow(self.ev("story one about iran nuclear"))[0])
        self.assertTrue(gate.allow(self.ev("story two about fed rate cuts"))[0])
        allowed, why = gate.allow(self.ev("story three about tariffs china"))
        self.assertFalse(allowed)
        self.assertIn("limit", why)

    def test_the_limit_lifts_once_the_hour_has_passed(self):
        clock = [1000.0]
        gate = gs.NewsGate(min_impact=1.0, max_per_hour=1, now=lambda: clock[0])
        self.assertTrue(gate.allow(self.ev("story one about iran nuclear"))[0])
        self.assertFalse(gate.allow(self.ev("story two about fed rate cuts"))[0])
        clock[0] += 3601
        self.assertTrue(gate.allow(self.ev("story two about fed rate cuts"))[0])

    def test_the_publisher_suffix_is_what_gets_stripped(self):
        # Not just any " - ": a genuinely hyphenated short headline must survive.
        self.assertEqual(gs.story_key("Gold - Silver ratio hits a record"),
                         gs.story_key("Gold - Silver ratio hits a record"))
        self.assertTrue(gs.same_story(
            gs.story_key("Gold hits record high on safe haven demand - Reuters"),
            gs.story_key("Gold hits record high on safe haven demand - CNBC")))

    def test_render_applies_the_gate_only_to_news(self):
        gate = gs.NewsGate(min_impact=6.0)
        self.assertIsNone(gs.render(self.ev("weak", impact=2.0), {"news"}, gate))
        # a trade signal is never rate limited, whatever the news is doing
        self.assertIsNotNone(gs.render(ENTRY, {"entry"}, gate))


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


class FakeTelegram(object):
    """Stands in for api.telegram.org. Records what was asked of it so the
    setup flow can be driven end to end without a network or a real token."""

    def __init__(self, ok=True, sends_fail=False, fail_methods=()):
        self.ok = ok
        self.sends_fail = sends_fail
        # Which single call fails, so a test can isolate ONE broken link
        # instead of breaking the whole chain and proving nothing about which
        # step stopped it.
        self.fail_methods = set(fail_methods)
        self.calls = []

    def __call__(self, req, timeout=None):
        url = req.full_url
        method = url.rsplit("/", 1)[-1]
        self.calls.append(method)
        if not self.ok or method in self.fail_methods:
            raise IOError("HTTP Error 401: Unauthorized")
        if url.endswith("getMe"):
            body = '{"ok":true,"result":{"username":"goldict_bot"}}'
        elif url.endswith("getUpdates"):
            body = ('{"ok":true,"result":[{"channel_post":{"chat":'
                    '{"id":-1001234567890,"title":"Gold Signals","type":"channel"}}}]}')
        elif url.endswith("sendMessage"):
            if self.sends_fail:
                raise IOError("HTTP Error 400: chat not found")
            body = '{"ok":true,"result":{}}'
        else:
            body = '{"ok":true,"result":{}}'

        class R(object):
            def read(self_inner):
                return body.encode("utf-8")

            def close(self_inner):
                pass

        return R()


class TestSetupFlow(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.cfg = os.path.join(self.dir, "sub", "telegram_config.json")
        self.out = []

    def answers(self, *values):
        it = iter(values)

        def ask(_prompt):
            return next(it, "")
        return ask

    def test_happy_path_writes_a_usable_config(self):
        fake = FakeTelegram()
        rc = gs.cmd_setup(self.cfg, feed_default="~/GoldICT/signals.jsonl",
                          ask=self.answers("123:ABC", "", "1", ""),
                          out=self.out.append, opener=fake)
        self.assertEqual(rc, 0, "\n".join(self.out))
        saved = json.load(open(self.cfg))
        self.assertEqual(saved["bot_token"], "123:ABC")
        self.assertEqual(saved["chat_id"], "-1001234567890")
        self.assertEqual(saved["feed"], "~/GoldICT/signals.jsonl")
        self.assertIn("sendMessage", fake.calls)

    def test_the_config_is_not_readable_by_other_users(self):
        gs.cmd_setup(self.cfg, ask=self.answers("123:ABC", "", "1", ""),
                     out=self.out.append, opener=FakeTelegram())
        mode = os.stat(self.cfg).st_mode & 0o777
        self.assertEqual(mode, 0o600, oct(mode))

    def test_a_bad_token_saves_nothing(self):
        # Half-written config files are how people end up debugging the wrong
        # thing. Nothing is saved until every step has passed.
        rc = gs.cmd_setup(self.cfg, ask=self.answers("nonsense"),
                          out=self.out.append, opener=FakeTelegram(ok=False))
        self.assertEqual(rc, 1)
        self.assertFalse(os.path.exists(self.cfg))

    def test_a_channel_it_cannot_post_to_saves_nothing(self):
        rc = gs.cmd_setup(self.cfg, ask=self.answers("123:ABC", "", "1", ""),
                          out=self.out.append,
                          opener=FakeTelegram(sends_fail=True))
        self.assertEqual(rc, 1)
        self.assertFalse(os.path.exists(self.cfg))
        self.assertTrue(any("ADMIN" in line for line in self.out),
                        "the message should name the usual cause")

    def test_only_the_token_failing_is_enough_to_stop_the_flow(self):
        # Deliberately the ONLY broken step: everything downstream would work.
        # Without this, a control that removes the early return still looks
        # caught, because the next call happens to fail too.
        fake = FakeTelegram(fail_methods={"getMe"})
        rc = gs.cmd_setup(self.cfg, ask=self.answers("123:ABC", "", "1", ""),
                          out=self.out.append, opener=fake)
        self.assertEqual(rc, 1)
        self.assertFalse(os.path.exists(self.cfg))
        self.assertEqual(fake.calls, ["getMe"],
                         "a rejected token should stop before anything else")

    def test_a_certificate_failure_is_not_blamed_on_the_token(self):
        # The owner hit this: Python could not verify Telegram's certificate and
        # the wizard said "that token did not work", sending him to re-copy a
        # credential that was fine. A transport failure and an auth failure are
        # different problems with different fixes.
        class TlsFake(FakeTelegram):
            def __call__(self, req, timeout=None):
                raise IOError("[SSL: CERTIFICATE_VERIFY_FAILED] certificate verify "
                              "failed: self-signed certificate in certificate chain")

        rc = gs.cmd_setup(self.cfg, ask=self.answers("123:ABC"),
                          out=self.out.append, opener=TlsFake())
        text = "\n".join(self.out)
        self.assertEqual(rc, 1)
        self.assertNotIn("did not work", text)
        self.assertIn("certificate", text.lower())
        self.assertIn("Install Certificates", text)
        self.assertFalse(os.path.exists(self.cfg))

    def test_the_token_is_never_echoed_back(self):
        gs.cmd_setup(self.cfg, ask=self.answers("8123456789:AAsecret", "", "1", ""),
                     out=self.out.append, opener=FakeTelegram())
        self.assertNotIn("AAsecret", "\n".join(self.out))

    def test_channel_ids_are_read_out_of_the_update_feed(self):
        result = [{"channel_post": {"chat": {"id": -100111, "title": "Gold"}}},
                  {"my_chat_member": {"chat": {"id": -100222, "title": "Other"}}}]
        found = gs.chat_ids_from_updates(result)
        self.assertEqual(found[0], ("-100222", "Other"))
        self.assertIn(("-100111", "Gold"), found)

    def test_an_empty_update_feed_is_not_a_crash(self):
        self.assertEqual(gs.chat_ids_from_updates([]), [])
        self.assertEqual(gs.chat_ids_from_updates(None), [])

    def test_check_names_the_broken_link(self):
        gs.write_config(self.cfg, {"bot_token": "123:ABC", "chat_id": "-1",
                                   "feed": os.path.join(self.dir, "missing.jsonl")})
        rc = gs.cmd_check(self.cfg, out=self.out.append, opener=FakeTelegram())
        text = "\n".join(self.out)
        self.assertEqual(rc, 1)
        self.assertIn("no feed file", text)
        self.assertIn("Write the signal feed", text)

    def test_check_passes_when_everything_is_wired(self):
        feed = os.path.join(self.dir, "signals.jsonl")
        open(feed, "w").write('{"t":"start"}\n')
        gs.write_config(self.cfg, {"bot_token": "123:ABC", "chat_id": "-1",
                                   "feed": feed})
        rc = gs.cmd_check(self.cfg, out=self.out.append, opener=FakeTelegram())
        self.assertEqual(rc, 0, "\n".join(self.out))


class TestRateLimiting(unittest.TestCase):
    """The owner's channel hit HTTP 429 and the sender made it worse: it backed
    off on a fixed 2/4/8 schedule, ignored the number Telegram actually gave it,
    burned its retries and dropped the message."""

    TOKEN = "8123456789:AAsecret"

    class Rate(object):
        """Fails with 429 for the first `n` calls, then succeeds."""

        def __init__(self, n, retry_after=17):
            self.left = n
            self.retry_after = retry_after
            self.calls = 0

        def __call__(self, req, timeout=None):
            self.calls += 1
            if self.left > 0:
                self.left -= 1
                err = IOError("HTTP Error 429: Too Many Requests")
                body = ('{"ok":false,"error_code":429,"parameters":'
                        '{"retry_after":%d}}' % self.retry_after).encode()
                err.read = lambda: body
                raise err

            class R(object):
                def read(self_inner):
                    return b"{}"

                def close(self_inner):
                    pass
            return R()

    def make(self, opener, slept, min_gap=0.0):
        return gs.Telegram(self.TOKEN, "-100", opener=opener, log=lambda m: None,
                           sleep=slept.append, clock=lambda: 0.0, min_gap=min_gap)

    def test_it_waits_the_number_telegram_gave_it(self):
        slept = []
        fake = self.Rate(1, retry_after=17)
        self.assertTrue(self.make(fake, slept).send("hello"))
        self.assertTrue(any(x >= 17 for x in slept),
                        "should have waited the 17s Telegram asked for, slept %s" % slept)

    def test_being_told_to_wait_does_not_use_up_a_retry(self):
        # Four 429s then success. On the old fixed-backoff logic those four
        # would have exhausted the attempts and the message would be lost.
        slept = []
        fake = self.Rate(4, retry_after=3)
        self.assertTrue(self.make(fake, slept).send("hello", attempts=2))

    def test_a_message_that_is_dropped_says_so_loudly(self):
        lines = []

        def boom(req, timeout=None):
            raise IOError("HTTP Error 500: Server Error")

        tg = gs.Telegram(self.TOKEN, "-100", opener=boom, log=lines.append,
                         sleep=lambda s: None, clock=lambda: 0.0, min_gap=0.0)
        self.assertFalse(tg.send("Buy gold\nEntry- 1", attempts=2))
        self.assertEqual(tg.dropped, 1)
        self.assertTrue(any("DROPPED" in l for l in lines), lines)
        self.assertTrue(any("Buy gold" in l for l in lines),
                        "the dropped message should be identifiable")

    def test_it_paces_itself_before_being_told_to(self):
        slept = []
        clock = [0.0]

        class Ok(object):
            def __call__(self, req, timeout=None):
                class R(object):
                    def read(self_inner):
                        return b"{}"

                    def close(self_inner):
                        pass
                return R()

        tg = gs.Telegram(self.TOKEN, "-100", opener=Ok(), log=lambda m: None,
                         sleep=slept.append, clock=lambda: clock[0], min_gap=3.5)
        tg.send("one")
        tg.send("two")           # immediately after, so it must pause
        self.assertTrue(any(x >= 3.0 for x in slept),
                        "second send should have been paced, slept %s" % slept)

    def test_retry_after_is_read_out_of_the_body(self):
        err = IOError("boom")
        err.read = lambda: b'{"parameters":{"retry_after":42}}'
        self.assertEqual(gs.Telegram.retry_after(err), 42)

    def test_a_silly_retry_after_is_capped(self):
        err = IOError("boom")
        err.read = lambda: b'{"parameters":{"retry_after":99999}}'
        self.assertEqual(gs.Telegram.retry_after(err), 300)

    def test_no_retry_after_returns_none(self):
        self.assertIsNone(gs.Telegram.retry_after(IOError("plain failure")))


class TestStaleEvents(unittest.TestCase):
    """The owner's terminal bot was stopped while cTrader took three trades.
    Resuming from the saved offset is right for not LOSING them and wrong for
    POSTING them: a "Buy gold" whose entry price is three hours old invites a
    follower into a price that no longer exists."""

    def ev(self, kind, minutes_ago):
        when = datetime.now(timezone.utc) - timedelta(minutes=minutes_ago)
        return {"t": kind, "utc": when.strftime("%Y-%m-%dT%H:%M:%SZ")}

    def test_a_fresh_trade_is_posted(self):
        self.assertFalse(gs.too_stale(self.ev("entry", 2), 30))

    def test_a_stale_trade_is_not_posted(self):
        self.assertTrue(gs.too_stale(self.ev("entry", 180), 30))

    def test_every_actionable_kind_is_covered(self):
        # A stale stop or take profit is just as misleading as a stale entry.
        for kind in ("entry", "setup", "tp", "sl", "close"):
            self.assertTrue(gs.too_stale(self.ev(kind, 180), 30),
                            "%s should be suppressed when stale" % kind)

    def test_dull_events_are_not_suppressed(self):
        # A late heartbeat is merely dull. Suppressing it would hide the fact
        # that the service had been down, which is the opposite of the point.
        for kind in ("heartbeat", "start", "stop", "news", "guard"):
            self.assertFalse(gs.too_stale(self.ev(kind, 180), 30))

    def test_an_event_with_no_timestamp_is_never_suppressed(self):
        # Fail towards posting. Losing a real signal is worse than a late one.
        self.assertFalse(gs.too_stale({"t": "entry"}, 30))
        self.assertFalse(gs.too_stale({"t": "entry", "utc": "nonsense"}, 30))

    def test_zero_disables_the_check(self):
        self.assertFalse(gs.too_stale(self.ev("entry", 9999), 0))

    def test_the_channel_is_told_what_it_missed(self):
        sent = []

        class Fake(object):
            def send(self_inner, text):
                sent.append(text)
                return True

        gs.report_missed([self.ev("entry", 200), self.ev("entry", 200),
                          self.ev("tp", 190)], Fake())
        self.assertEqual(len(sent), 1, "one summary, not one message per event")
        self.assertIn("2 trade(s)", sent[0])
        self.assertIn("offline", sent[0])

    def test_no_summary_when_nothing_tradeable_was_missed(self):
        sent = []

        class Fake(object):
            def send(self_inner, text):
                sent.append(text)
                return True

        gs.report_missed([self.ev("heartbeat", 200)], Fake())
        self.assertEqual(sent, [])


class TestStaleEventsEndToEnd(unittest.TestCase):
    """too_stale() being correct is not the same as the loop CALLING it. This
    drives the real main loop over a real feed file, because a negative control
    that deletes the call has to fail something."""

    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.feed = os.path.join(self.dir, "signals.jsonl")
        self.state = os.path.join(self.dir, "state.json")

    def write(self, rows):
        with open(self.feed, "w", encoding="utf-8") as fh:
            for r in rows:
                fh.write(json.dumps(r) + "\n")

    def entry(self, minutes_ago, price):
        when = datetime.now(timezone.utc) - timedelta(minutes=minutes_ago)
        return {"t": "entry", "utc": when.strftime("%Y-%m-%dT%H:%M:%SZ"),
                "symbol": "XAUUSD", "demo": True, "signal": 1, "model": "M",
                "side": "BUY", "entry": price, "level": price, "stop": price - 10,
                "tps": [price + 5, price + 10, price + 15], "risk_pct": 1.0,
                "detail": "d"}

    def run_once(self, extra=None):
        import contextlib
        import io
        buf = io.StringIO()
        argv = ["--feed", self.feed, "--state", self.state, "--once",
                "--dry-run", "--from-start",
                "--config", os.path.join(self.dir, "no-such-config.json")]
        with contextlib.redirect_stdout(buf):
            gs.main(argv + (extra or []))
        return buf.getvalue()

    def test_a_stale_entry_is_not_posted_but_a_fresh_one_is(self):
        self.write([self.entry(240, 4000.0), self.entry(1, 4500.0)])
        out = self.run_once()
        self.assertIn("4500.00", out, "the fresh signal should have been posted")
        self.assertNotIn("4000.00", out,
                         "a four-hour-old entry was posted as a live signal")

    def test_the_console_says_what_it_skipped(self):
        self.write([self.entry(240, 4000.0)])
        out = self.run_once()
        self.assertIn("skipped", out.lower())

    def test_disabling_the_age_check_posts_everything(self):
        self.write([self.entry(240, 4000.0)])
        out = self.run_once(["--max-event-age", "0"])
        self.assertIn("4000.00", out)


class TestOnlyOneCopy(unittest.TestCase):
    """The owner got every message twice. Not a bug in the messages -- two
    copies of the program were watching the same feed, each posting everything
    once. From the channel it is indistinguishable from a duplication bug."""

    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.lock = os.path.join(self.dir, "sub", "signals.jsonl.posted.lock")

    def test_the_first_copy_takes_the_lock(self):
        self.assertIsNone(gs.claim_lock(self.lock))
        self.assertTrue(os.path.exists(self.lock))
        with open(self.lock) as fh:
            self.assertEqual(int(fh.read()), os.getpid())

    def test_a_second_copy_is_told_who_holds_it(self):
        # A pid that is definitely alive but is not us: our own parent.
        other = os.getppid()
        with open_lock(self.lock) as fh:
            fh.write(str(other))
        held = gs.claim_lock(self.lock)
        self.assertEqual(held, other)

    def test_a_lock_left_by_a_dead_process_is_taken_over(self):
        # A crash must not lock the user out forever. What matters is whether
        # that process is still running, not whether it tidied up.
        with open_lock(self.lock) as fh:
            fh.write("999999")            # a pid that cannot be alive
        self.assertIsNone(gs.claim_lock(self.lock))

    def test_a_corrupt_lock_file_is_taken_over(self):
        with open_lock(self.lock) as fh:
            fh.write("not a number")
        self.assertIsNone(gs.claim_lock(self.lock))

    def test_our_own_lock_does_not_block_us(self):
        gs.claim_lock(self.lock)
        self.assertIsNone(gs.claim_lock(self.lock))

    def test_releasing_removes_it(self):
        gs.claim_lock(self.lock)
        gs.release_lock(self.lock)
        self.assertFalse(os.path.exists(self.lock))

    def test_releasing_never_removes_somebody_elses_lock(self):
        with open_lock(self.lock) as fh:
            fh.write(str(os.getppid()))
        gs.release_lock(self.lock)
        self.assertTrue(os.path.exists(self.lock),
                        "released a lock this process never held")


def open_lock(path):
    d = os.path.dirname(path)
    if d and not os.path.isdir(d):
        os.makedirs(d)
    return open(path, "w", encoding="utf-8")


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
