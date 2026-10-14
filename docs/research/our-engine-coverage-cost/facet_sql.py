"""Write a SQL query that reads the enters-tapped facet of the sample's
facet-only lands (lands whose only gaps are tapped entry, shock payment,
or self-damage).

Usage: python facet_sql.py <audit_dir>, then pipe facet.sql into psql
against the mtg-archive compose stack's Postgres."""

import json
import sys
from collections import Counter

HERE = sys.path[0]
r = json.load(open(f"{sys.argv[1]}/results.json", encoding="utf-8"))
cards = r["cards"]
nb = [s for s in r["slots"] if not cards[s[2]]["basic"]]
w = Counter(s[2] for s in nb)
FACET = {"etb_tapped", "enters_with", "pay_life", "damage", "life_loss"}
COVERED = {"defined", "inert_exact", "fetch_facet", "expressible"}
rows = []
for n, c in cards.items():
    if c["basic"] or c["engine"] in COVERED:
        continue
    if "Land" in c["types"].split(" // ")[0] and set(c["missing"]) <= FACET:
        rows.append((n, w[n], " ".join(sorted(c["missing"]))))
vals = ",\n".join("('" + n.replace("'", "''") + f"', {k}, '{t}')" for n, k, t in rows)
sql = f"""with s(name, slots, tags) as (values
{vals})
select coalesce(p.enters_tapped, 'none') as facet, s.tags, count(*) as cards, sum(s.slots) as slots,
       string_agg(s.name, '; ' order by s.slots desc) as names
from s
left join card c on c.name = s.name
left join source_profile p on p.card_id = c.id
group by 1, 2 order by 4 desc;
"""
open(f"{HERE}/facet.sql", "w", encoding="utf-8").write(sql)
print(len(rows), "lands")
