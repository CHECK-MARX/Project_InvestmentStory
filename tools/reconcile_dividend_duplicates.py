"""Audit and reversibly retire duplicate dividend actuals from legacy CSV imports.

Run without --apply to write a review report. Applying requires an explicit
expected count and creates a consistent SQLite backup before any updates.
"""

from __future__ import annotations

import argparse
import json
import os
import sqlite3
import unicodedata
from collections import defaultdict
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DB = Path(os.environ["LOCALAPPDATA"]) / "InvestmentStory" / "investment_story.db"


def normalized(value: str | None) -> str:
    return unicodedata.normalize("NFKC", value or "").replace("證", "証").strip().upper()


def amount(row: sqlite3.Row) -> float:
    return float(row["NetAmountJpy"] or row["JpyAmount"] or row["NetAmount"] or 0)


def close_in_yen(left: sqlite3.Row, right: sqlite3.Row) -> bool:
    a, b = amount(left), amount(right)
    return a > 0 and b > 0 and abs(a - b) <= max(10.0, max(a, b) * 0.01)


def same_event_key(row: sqlite3.Row) -> tuple[str, str, str, str, float]:
    return (
        row["PaymentDate"][:10], normalized(row["Ticker"]),
        normalized(row["Broker"]), normalized(row["AccountType"]),
        float(row["Quantity"] or 0),
    )


def collect(conn: sqlite3.Connection) -> list[dict]:
    rows = conn.execute("""
        SELECT d.Id, d.PaymentDate, s.Ticker, d.Broker, d.AccountType,
               d.Quantity, d.Currency, d.GrossAmount, d.NetAmount,
               d.NetAmountJpy, d.JpyAmount, d.Source, d.SourceFile,
               d.SourceRowNumber
          FROM DividendPayments AS d JOIN Stocks AS s ON s.Id = d.StockId
         WHERE d.DividendStatus = 'Actual' AND d.ReplacedByDividendId IS NULL
    """).fetchall()
    csv_groups: dict[tuple, list[sqlite3.Row]] = defaultdict(list)
    csv_rows: list[sqlite3.Row] = []
    for row in rows:
        if normalized(row["Source"]) == "CSV":
            csv_groups[same_event_key(row)].append(row)
            csv_rows.append(row)

    replacement: dict[int, int] = {}
    reason: dict[int, str] = {}
    by_id = {row["Id"]: row for row in rows}

    # A source without a row number is the legacy representation of a later
    # broker-CSV row. Require exactly one of each and the same positive quantity.
    for group in csv_groups.values():
        if len(group) != 2 or same_event_key(group[0])[-1] <= 0:
            continue
        old = next((row for row in group if row["SourceRowNumber"] == 0), None)
        new = next((row for row in group if row["SourceRowNumber"] > 0), None)
        if old is None or new is None or not close_in_yen(old, new):
            continue
        if not old["SourceFile"] or not new["SourceFile"]:
            continue
        replacement[old["Id"]] = new["Id"]
        reason[old["Id"]] = "legacy_csv_and_broker_row"

    # Older manual rows copied from a CSV sometimes have zero quantity and a
    # different stock ID / old kanji spelling for the same broker. Match only
    # exact native-currency gross, net, and converted yen against one CSV row.
    for manual in rows:
        if normalized(manual["Source"]) != "MANUAL" or manual["Quantity"] != 0:
            continue
        candidates = [
            csv for csv in csv_rows
            if manual["PaymentDate"][:10] == csv["PaymentDate"][:10]
            and normalized(manual["Ticker"]) == normalized(csv["Ticker"])
            and normalized(manual["Broker"]) == normalized(csv["Broker"])
            and normalized(manual["AccountType"]) == normalized(csv["AccountType"])
            and normalized(manual["Currency"]) == normalized(csv["Currency"])
            and abs(float(manual["GrossAmount"] or 0) - float(csv["GrossAmount"] or 0)) < 0.001
            and abs(float(manual["NetAmount"] or 0) - float(csv["NetAmount"] or 0)) < 0.001
            and abs(amount(manual) - amount(csv)) < 0.01
        ]
        if len(candidates) != 1:
            continue
        target = candidates[0]["Id"]
        while target in replacement:
            target = replacement[target]
        replacement[manual["Id"]] = target
        reason[manual["Id"]] = "manual_copy_of_csv"

    result = []
    for old_id, target_id in sorted(replacement.items()):
        old, target = by_id[old_id], by_id[target_id]
        if old_id == target_id or normalized(target["Source"]) != "CSV":
            raise RuntimeError(f"Invalid replacement {old_id} -> {target_id}")
        result.append({
            "old_id": old_id,
            "keep_id": target_id,
            "date": old["PaymentDate"][:10],
            "ticker": old["Ticker"],
            "account": old["AccountType"],
            "old_net_jpy": amount(old),
            "kept_net_jpy": amount(target),
            "reason": reason[old_id],
        })
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", type=Path, default=DEFAULT_DB)
    parser.add_argument("--report", type=Path,
                        default=ROOT / "artifacts" / "dividend_duplicate_audit.json")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--expected-count", type=int)
    args = parser.parse_args()
    if args.apply and args.expected_count is None:
        parser.error("--apply requires --expected-count")

    uri = args.database.resolve().as_uri() + ("?mode=rw" if args.apply else "?mode=ro")
    conn = sqlite3.connect(uri, uri=True)
    conn.row_factory = sqlite3.Row
    try:
        pairs = collect(conn)
        totals: dict[str, float] = defaultdict(float)
        for pair in pairs:
            totals[pair["date"][:4]] += pair["old_net_jpy"]
        report = {
            "database": str(args.database),
            "candidate_count": len(pairs),
            "possible_overcount_jpy_by_year": dict(sorted(totals.items())),
            "candidates": pairs,
        }
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"candidates={len(pairs)} overcount_by_year={dict(sorted(totals.items()))}")
        print(f"report={args.report}")
        if not args.apply:
            return
        if len(pairs) != args.expected_count:
            raise RuntimeError(f"Expected {args.expected_count} candidates, found {len(pairs)}")

        backup = ROOT / "artifacts" / "db_backups" / (
            "investment_story_before_dividend_dedupe_" + datetime.now().strftime("%Y%m%d_%H%M%S") + ".db")
        backup.parent.mkdir(parents=True, exist_ok=True)
        with sqlite3.connect(backup) as destination:
            conn.backup(destination)
        print(f"backup={backup}")

        conn.execute("BEGIN IMMEDIATE")
        if collect(conn) != pairs:
            raise RuntimeError("Dividend rows changed since audit; no updates applied")
        stamp = datetime.now().isoformat(timespec="seconds")
        for pair in pairs:
            updated = conn.execute("""
                UPDATE DividendPayments
                   SET DividendStatus = 'Replaced',
                       ReplacedByDividendId = ?, MatchedActualDividendId = ?,
                       UpdatedAt = ?,
                       Memo = COALESCE(Memo, '') || ?
                 WHERE Id = ? AND DividendStatus = 'Actual'
                   AND ReplacedByDividendId IS NULL
            """, (pair["keep_id"], pair["keep_id"], stamp,
                  f" / duplicate actual consolidated into ID {pair['keep_id']}",
                  pair["old_id"])).rowcount
            if updated != 1:
                raise RuntimeError(f"Could not update dividend ID {pair['old_id']}")
        conn.commit()
        print(f"applied={len(pairs)} remaining_candidates={len(collect(conn))}")
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()


if __name__ == "__main__":
    main()
