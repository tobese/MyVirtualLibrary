#!/usr/bin/env python3
"""
seed_discovery.py — seed the local catalog from NYT bestsellers + OL trending.

For every available NYT bestseller list and both OL trending periods (daily +
weekly), the script collects ISBN-13s and bulk-imports them via POST /api/import.

Idempotent: already-present books are counted but not re-added. Safe to run on a
cron or after any API restart.

No third-party dependencies (stdlib only, Python 3.11+).

Usage:
    python scripts/seed_discovery.py [--api URL] [--persona NAME]

Options:
    --api URL        API base URL        (default: http://localhost:5179)
    --persona NAME   Dev-login persona   (default: superadmin)
"""

import argparse
import json
import sys
import time
import urllib.error
import urllib.request
from collections import defaultdict

API_DEFAULT = "http://localhost:5179"
OL_BASE     = "https://openlibrary.org"
OL_DELAY    = 0.4   # seconds between OL calls — stay well under their rate limit
CHUNK_SIZE  = 500   # max ISBNs per import request


# ── HTTP helpers ──────────────────────────────────────────────────────────────

def _req(url, *, method="GET", body=None, token=None, timeout=60):
    data = json.dumps(body).encode() if body is not None else None
    req  = urllib.request.Request(url, data=data, method=method)
    req.add_header("Accept", "application/json")
    if data:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read())
    except urllib.error.HTTPError as e:
        snippet = e.read().decode(errors="replace")[:300]
        raise RuntimeError(f"{method} {url} → HTTP {e.code}: {snippet}") from e


def api_get(api, path, token):
    return _req(f"{api}{path}", token=token)


def api_post(api, path, body, token, timeout=60):
    return _req(f"{api}{path}", method="POST", body=body, token=token, timeout=timeout)


def ol_get(path):
    return _req(f"{OL_BASE}{path}")


# ── auth ──────────────────────────────────────────────────────────────────────

def dev_login(api, persona):
    try:
        resp = api_post(api, "/api/auth/dev-login", {"persona": persona}, token=None)
    except RuntimeError as e:
        sys.exit(f"ERROR: dev-login failed — is the API running in Debug mode?\n  {e}")
    token = resp.get("token")
    if not token:
        sys.exit(f"ERROR: dev-login returned no token: {resp}")
    print(f"  logged in as '{persona}'")
    return token


# ── bestsellers ───────────────────────────────────────────────────────────────

def collect_bestseller_isbns(api, token):
    """Return {listName: [isbn13, ...]} for every available NYT bestseller list."""
    try:
        list_names = api_get(api, "/api/bestseller", token)
    except RuntimeError as e:
        print(f"  [skip] cannot fetch list names: {e}")
        return {}

    if not isinstance(list_names, list):
        print(f"  [skip] unexpected bestseller response: {list_names!r}")
        return {}

    print(f"  {len(list_names)} NYT lists available")
    by_list = {}
    for name in list_names:
        try:
            data  = api_get(api, f"/api/bestseller/{name}", token)
            isbns = [b["isbn13"] for b in data.get("books", []) if b.get("isbn13")]
            by_list[name] = isbns
            print(f"    {name:<42} {len(isbns):3d} ISBNs")
        except RuntimeError as e:
            print(f"    [skip] {name}: {e}")
    return by_list


# ── trending + OL resolution ──────────────────────────────────────────────────

def _ol_work_to_isbn13(ol_key):
    """
    Resolve an OL work key (e.g. /works/OL1234W) to a single ISBN-13 by
    fetching the work's editions and returning the first ISBN-13 found.
    Returns None if unavailable.
    """
    try:
        data = ol_get(f"{ol_key}/editions.json?limit=20")
    except Exception:
        return None
    for entry in data.get("entries", []):
        for isbn in entry.get("isbn_13", []):
            if isbn and isbn.isdigit() and len(isbn) == 13:
                return isbn
    return None


def collect_trending_isbns(api, token):
    """Return {period: [isbn13, ...]} for daily + weekly OL trending."""
    by_period = {}
    for period in ("weekly", "daily"):
        try:
            data = api_get(api, f"/api/trending?period={period}", token)
        except RuntimeError as e:
            print(f"  [skip] {period} trending unavailable: {e}")
            continue

        works = data.get("works", [])
        print(f"  {period} trending: {len(works)} works — resolving ISBNs via OL…")
        isbns, skipped = [], 0
        for w in works:
            ol_key = w.get("olKey")
            if not ol_key:
                skipped += 1
                continue
            isbn = _ol_work_to_isbn13(ol_key)
            if isbn:
                isbns.append(isbn)
            else:
                skipped += 1
            time.sleep(OL_DELAY)
        print(f"    resolved {len(isbns)}/{len(works)} to ISBN-13"
              + (f" ({skipped} unresolvable)" if skipped else ""))
        by_period[period] = isbns
    return by_period


# ── import ────────────────────────────────────────────────────────────────────

# ImportRowStatus enum values (integer — default System.Text.Json serialization)
STATUS_ERROR = 3


def import_isbns(api, token, isbns):
    """Chunk-import all ISBNs; return (aggregated_summary_dict, error_list)."""
    totals = defaultdict(int)
    errors = []

    for i in range(0, len(isbns), CHUNK_SIZE):
        chunk = isbns[i : i + CHUNK_SIZE]
        label = f"chunk {i // CHUNK_SIZE + 1} ({len(chunk)} ISBNs)"
        print(f"  importing {label}…", end=" ", flush=True)
        try:
            resp = api_post(api, "/api/import", {
                "isbns":          chunk,
                "defaultStatus":  0,      # WantToRead
                "defaultIsOwned": False,
            }, token, timeout=600)
        except RuntimeError as e:
            print(f"FAILED: {e}")
            totals["errors"] += len(chunk)
            continue

        s = resp.get("summary", {})
        totals["total"]            += s.get("total", 0)
        totals["added"]            += s.get("added", 0)
        totals["alreadyInLibrary"] += s.get("alreadyInLibrary", 0)
        totals["dataRefreshed"]    += s.get("dataRefreshed", 0)
        totals["notFound"]         += s.get("notFound", 0)
        totals["errors"]           += s.get("errors", 0)

        for row in resp.get("results", []):
            if row.get("status") == STATUS_ERROR:
                errors.append(f"  {row['isbn']}: {row.get('message', '?')}")

        print(f"added {s.get('added', 0)}, "
              f"skipped {s.get('alreadyInLibrary', 0)}, "
              f"not found {s.get('notFound', 0)}")

    return dict(totals), errors


# ── main ──────────────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--api",     default=API_DEFAULT,
                    help=f"API base URL (default: {API_DEFAULT})")
    ap.add_argument("--persona", default="superadmin",
                    help="Dev-login persona (default: superadmin)")
    args = ap.parse_args()
    api  = args.api.rstrip("/")

    print(f"API: {api}\n")

    # 1 — auth
    print("── Auth " + "─" * 40)
    token = dev_login(api, args.persona)

    # 2 — bestsellers
    print("\n── NYT Bestsellers " + "─" * 28)
    bs = collect_bestseller_isbns(api, token)

    # 3 — trending
    print("\n── OL Trending " + "─" * 33)
    tr = collect_trending_isbns(api, token)

    # 4 — deduplicate across sources
    seen         = set()
    unique_isbns = []
    isbn_sources = defaultdict(list)  # isbn → [source, ...]

    def add(isbns, source):
        for isbn in isbns:
            isbn_sources[isbn].append(source)
            if isbn not in seen:
                seen.add(isbn)
                unique_isbns.append(isbn)

    for list_name, isbns in bs.items():
        add(isbns, f"nyt:{list_name}")
    for period, isbns in tr.items():
        add(isbns, f"trending:{period}")

    bs_total  = sum(len(v) for v in bs.values())
    tr_total  = sum(len(v) for v in tr.values())
    dupes     = bs_total + tr_total - len(unique_isbns)

    print(f"\n── Collection summary " + "─" * 26)
    print(f"  NYT bestsellers  {bs_total:5d} ISBNs  ({len(bs)} lists)")
    print(f"  OL trending      {tr_total:5d} ISBNs  ({len(tr)} periods)")
    print(f"  Duplicates       {dupes:5d}")
    print(f"  Unique total     {len(unique_isbns):5d}")

    if not unique_isbns:
        print("\nNothing to import — check that the NYT API key is configured and "
              "the API is reachable.")
        return

    # 5 — import
    print(f"\n── Importing " + "─" * 35)
    totals, errors = import_isbns(api, token, unique_isbns)

    # 6 — final report
    w = 6
    print(f"""
┌────────────────────────────────────────┐
│  Import report                         │
├────────────────────────────────────────┤
│  ISBNs submitted    {totals.get('total',            0):{w}}               │
│  New books added    {totals.get('added',            0):{w}}               │
│  Already present    {totals.get('alreadyInLibrary', 0):{w}}               │
│  Metadata refreshed {totals.get('dataRefreshed',   0):{w}}               │
│  Not found on OL    {totals.get('notFound',         0):{w}}               │
│  Errors             {totals.get('errors',           0):{w}}               │
└────────────────────────────────────────┘""")

    if errors:
        print(f"\nError details ({len(errors)} rows):")
        for e in errors[:20]:
            print(e)
        if len(errors) > 20:
            print(f"  … {len(errors) - 20} more errors omitted")

    print()


if __name__ == "__main__":
    main()
