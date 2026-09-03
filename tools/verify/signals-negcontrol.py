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
