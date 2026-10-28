#!/usr/bin/env python3
"""Turn data/events.csv into data/events.json.

The CSV is the community spreadsheet "Spreadsheet with Events, Conditions and
Consequences of the Game" by Brian Sirith (see docs/BRANCHES.md for the link and
credit). Layout, no header row:

    event, condition, category, (option, requires, outcome) x 5

Rows with only a first column are group headers ("Childhood (Year 1118)",
"20 (Year 1138)", "The Revolt (Year 1146)", ...).

Usage:  python3 tools/csv_to_events.py
"""

import csv
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "data" / "events.csv"
DST = ROOT / "data" / "events.json"

OPTIONS = 5  # columns per option: text, requires, outcome


def clean(value):
    value = (value or "").strip()
    return "" if value in ("-", "—") else value


def main():
    groups = []
    total = 0
    with SRC.open(newline="", encoding="utf-8") as fh:
        for row in csv.reader(fh):
            if not row or not row[0].strip():
                continue
            event = row[0].strip()
            rest = [c for c in row[1:]]
            # group header: nothing but the first column set
            if all(not c.strip() for c in rest):
                groups.append({"group": event, "events": []})
                continue

            condition = clean(row[1] if len(row) > 1 else "")
            category = clean(row[2] if len(row) > 2 else "")
            options = []
            for i in range(OPTIONS):
                base = 3 + i * 3
                if base >= len(row):
                    break
                text = clean(row[base])
                requires = clean(row[base + 1] if base + 1 < len(row) else "")
                outcome = clean(row[base + 2] if base + 2 < len(row) else "")
                if not text and not outcome:
                    continue
                options.append({
                    "text": text,
                    "requires": requires,
                    "outcome": outcome,
                })
            if not groups:
                groups.append({"group": "", "events": []})
            groups[-1]["events"].append({
                "title": event,
                "condition": condition,
                "category": category,
                "options": options,
            })
            total += 1

    data = {
        "schema": 1,
        "source": {
            "name": "Spreadsheet with Events, Conditions and Consequences of the Game",
            "author": "Brian Sirith",
            "guide": "https://steamcommunity.com/sharedfiles/filedetails/?id=2577499714",
            "credit": "Event list and option texts transcribed by Brian Sirith, with corrections from Gowther32 and others.",
        },
        "note": "Options are per event; the sheet groups events by life stage/year and by path (Family/General/Judge/Priest/Publisher). It does not say which event follows which option - that has to come from traces (see docs/BRANCHES.md).",
        "eventCount": total,
        "groups": groups,
    }
    DST.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
    print("wrote %s: %d groups, %d events" % (DST.relative_to(ROOT), len(groups), total))


if __name__ == "__main__":
    main()
