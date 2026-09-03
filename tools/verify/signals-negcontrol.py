#!/usr/bin/env python3
"""NEGATIVE CONTROL for tests/test_goldsignals.py.

Breaks the signal bot on purpose, one fault at a time, and requires the test
suite to notice each break. A test suite that cannot fail is decoration, and
this project has shipped a decorative check before -- a Telegram leak test that
passed with the redaction deleted, because the token's colon was
percent-encoded and the raw token never appeared in the first place.

Every fault below is one that would actually reach the channel: a format the
followers cannot read, a side flipped, an invented entry zone, a credential in
a log line, or a feed reader that silently stops reading.
"""
import os
import shutil
import subprocess
import sys
import tempfile

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BOT = os.path.join(REPO, "tools", "goldsignals.py")

FAULTS = [
    ("take profit labels no longer match the requested format",
     '        lines.append("Tp%s%d: %s" % (" " if i == 1 else "", i, _f(tp)))',
     '        lines.append("TP%d = %s" % (i, _f(tp)))'),

    ("an entry zone invented when there is only one price",
     "    if hi - lo < 0.01:\n        return _f(fill)",
     "    if False:\n        return _f(fill)"),

    ("stop loss line dropped from the signal",
     '    lines.append("Sl \\U0001f6d1: %s" % _f(ev.get("stop")))',
     '    pass'),

    ("the account-type switch stops working",
     '    if show_account:',
     '    if False:'),

    ("buy and sell swapped in the signal",
     '    buy = side == "BUY"',
     '    buy = side != "BUY"'),

    ("the green/red cue is the same colour whichever way the trade points",
     '    return "\\U0001f7e2" if (side or "").upper() == "BUY" else "\\U0001f534"',
     '    return "\\U0001f7e2"'),

    ("token redaction removed (secret half left in the log)",
     "        if self.token and self.token in text:\n"
     "            text = text.replace(self.token, \"<token redacted>\")",
     "        if False:\n            pass"),

    ("bot-id redaction removed",
     "        if head and len(head) >= 6 and head in text:\n"
     "            text = text.replace(head, \"<token redacted>\")",
     "        if False:\n            pass"),

    ("dry run posts for real anyway",
     "        if self.dry_run or not self.token or not self.chat_id:",
     "        if not self.token or not self.chat_id:"),

    ("the message body is sent without the text",
     '            "text": text,',
     '            "text": "",'),

    ("feed offset ignored -- every event reposted forever",
     "        fh.seek(offset)",
     "        fh.seek(0)"),

    ("a truncated feed is never noticed, so the bot goes silent",
     "    if size < offset:\n        offset = 0",
     "    if False:\n        offset = 0"),

    ("half-written lines posted as if complete",
     "    if not chunk.endswith(\"\\n\") and chunk:",
     "    if False:"),

    ("every take profit claims the position is fully closed",
     '            lines.append("Runners still open, stop is trailing.")',
     '            lines.append("Full position closed.")'),

    ("the model name put back into the signal",
     '    lines.append(RISK_LINE)',
     '    lines.append("%s · %s" % (ev.get("model", ""), ev.get("detail", "")))'),

    ("the day guard echoes free text from the feed again",
     '        detail = "Down %s%% on the day." % _f(down, 1)',
     '        detail = ev.get("detail", "")'),

    ("the leak detector made vacuous, so nothing can ever be reported",
     "    return [w for w in PRIVATE_WORDS if w.lower() in low]",
     "    return []"),

    ("setup saves a config even when the token was rejected",
     "        out(\"  That token did not work: %s\" % exc)\n"
     "        out(\"  Check you copied all of it, including the part after the colon.\")\n"
     "        return 1",
     "        out(\"  That token did not work: %s\" % exc)\n"
     "        me = {}"),

    ("setup saves a config it could not post to",
     "        out(\"  The usual cause is the bot not being an ADMIN of the channel.\")\n"
     "        return 1",
     "        out(\"  The usual cause is the bot not being an ADMIN of the channel.\")"),

    ("the saved config is left world-readable",
     "        os.chmod(path, 0o600)",
     "        os.chmod(path, 0o644)"),

    ("--check reports success whatever the state of the feed",
     '        out("  [X] no feed file at %s" % feed)',
     '        ok = True'),

    ("the token echoed back into the setup transcript",
     '    out("  OK — the token belongs to @%s." % me.get("username", "?"))',
     '    out("  OK — token %s belongs to @%s." % (token, me.get("username", "?")))'),

    ("a certificate failure blamed on the token again",
     '        if "CERTIFICATE_VERIFY_FAILED" in msg or "SSLCertVerification" in msg:\n'
     '            raise TlsError(msg)',
     '        pass'),

    # NOTE: the python-side publisher-suffix strip deliberately has NO control
    # here. It is defensive only -- the word-overlap check catches wire
    # duplicates without it, so breaking it does not fail the suite and a
    # control claiming otherwise would be decoration. Where the strip IS
    # load-bearing is the cBots' Normalise(), which compares whole strings;
    # that one is controlled in tools/verify/news-test.sh.
    ("stopwords no longer stripped, so unrelated headlines look alike",
     "        if len(w) > 2 and w not in _STOP:",
     "        if len(w) > 2:"),

    ("the same-story check made vacuous",
     "    return shared / float(min(len(a), len(b))) >= overlap",
     "    return False"),

    ("the impact threshold ignored",
     "        if impact < self.min_impact:\n            return False, \"below the impact threshold\"",
     "        pass"),

    ("the hourly ceiling removed",
     "        if self.max_per_hour > 0 and len(self.recent) >= self.max_per_hour:\n"
     "            return False, \"hourly news limit reached\"",
     "        pass"),

    ("the gate skipped entirely when rendering news",
     "    if kind == \"news\" and gate is not None:",
     "    if False:"),

    ("Telegram's retry-after ignored, fixed backoff again",
     "                if wait is not None and waits < 5:",
     "                if False:"),

    ("waiting on a rate limit burns a retry, so messages get dropped",
     "                    waits += 1",
     "                    attempt += 1"),

    ("the retry-after number is never parsed out of the body",
     "        marker = '\"retry_after\"'",
     "        return None\n        marker = '\"retry_after\"'"),

    ("self-pacing removed, so it floods until told to stop",
     "        gap = self.clock() - self._last_send\n"
     "        if gap < self.min_gap:\n"
     "            self.sleep(self.min_gap - gap)",
     "        pass"),

    ("a dropped message vanishes without a word",
     '                    self.log("TELEGRAM FAILED after %d tries — %s\\n"\n'
     '                             "  DROPPED this message: %s"\n'
     '                             % (attempts, msg, text.split("\\n")[0][:70]))',
     '                    pass'),

    ("a second copy is allowed to run, so everything posts twice",
     "            if other and other != os.getpid() and _alive(other):\n"
     "                return other",
     "            pass"),

    ("a crashed run locks the user out forever",
     "    except OSError:\n        return False\n    except Exception:\n"
     "        return False\n    return True",
     "    return True"),

    ("release deletes whichever lock it finds, including someone else's",
     "            if int((fh.read() or \"0\").strip()) != os.getpid():\n"
     "                return                  # someone else's lock, leave it alone",
     "        pass"),

    ("the event filter is ignored, so --only does nothing",
     "    if kind not in want:\n        return None",
     "    if False:\n        return None"),
]


def run_tests(cwd):
    return subprocess.run(
        [sys.executable, "-m", "unittest", "tests.test_goldsignals"],
        cwd=cwd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode


def main():
    clean = run_tests(REPO)
    if clean != 0:
        print("  SETUP FAIL — the suite does not pass on the UNMODIFIED bot.")
        return 1

    bad = 0
    original = open(BOT, encoding="utf-8").read()
    for name, old, new in FAULTS:
        if old not in original:
            print("  SETUP FAIL  %s (patch target gone — control protects nothing)" % name)
            bad += 1
            continue
        work = tempfile.mkdtemp()
        try:
            shutil.copytree(os.path.join(REPO, "tests"), os.path.join(work, "tests"))
            os.makedirs(os.path.join(work, "tools"))
            with open(os.path.join(work, "tools", "goldsignals.py"), "w",
                      encoding="utf-8") as fh:
                fh.write(original.replace(old, new, 1))
            if run_tests(work) == 0:
                print("  MISSED  %s — the broken bot PASSED" % name)
                bad += 1
            else:
                print("  CAUGHT  %s" % name)
        finally:
            shutil.rmtree(work, ignore_errors=True)

    if bad:
        print("\n%d of %d controls did not hold." % (bad, len(FAULTS)))
        return 1
    print("\nall %d controls held — the signal tests have teeth." % len(FAULTS))
    return 0


if __name__ == "__main__":
    sys.exit(main())
