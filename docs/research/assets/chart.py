"""Generates docs/research/assets/value-functionality.svg and positioning.json. Run: python3 chart.py
Value × functionality positioning chart for the Ambysto Pro Suite research.
Functionality = categories covered of 15 (✓ = 1, partial = 0.5).
Value = functionality points per $10 of effective annual price (one-time licenses amortized over 3 years)."""
import json

# name, platform, functionality points, effective $/yr, price basis, label offset (dx, dy)
P = [
    ("CleanMyMac", "mac", 10.5, 39.99, "Mac App Store yearly $39.99", (10, -8)),
    ("Parallels Toolbox", "both", 8.0, 24.99, "$24.99/yr (VAT-incl., VN view)", (10, -8)),
    ("Nektony MacCleaner Pro", "mac", 7.5, 39.95, "$39.95/yr, 1 Mac", (-10, 18, "end")),
    ("iStat Menus", "mac", 4.0, 11.99 / 3, "$11.99 one-time ÷ 3 yrs", (8, 4)),
    ("DaisyDisk", "mac", 1.5, 9.99 / 3, "$9.99 one-time ÷ 3 yrs", (8, 4)),
    ("BuhoCleaner", "mac", 7.5, 17.99, "$17.99/yr ('sale', no end date)", (10, -8)),
    ("MacKeeper", "mac", 7.5, 71.40, "$71.40/yr, 1 Mac", (10, 18)),
    ("Setapp (catalog)", "catalog", 13.0, 107.88, "$8.99/mo billed yearly; coverage assumed", (-10, -10, "end")),
    ("Little Snitch", "mac", 2.5, 59 * 1.08 / 3, "€59 one-time × 1.08 ÷ 3 yrs", (8, 4)),
    ("Gemini 2", "mac", 1.0, 19.99, "$19.99/yr", (8, 4)),
    ("IObit ASC Pro", "win", 7.0, 16.77, "$16.77/yr, 1 PC ('sale', permanent)", (-10, 4, "end")),
    ("IObit 3-in-1", "win", 8.5, 29.99, "$29.99/yr, 3 PCs", (10, -6)),
    ("CCleaner Pro Plus", "win", 7.5, 64.95, "renewal $64.95/yr, 3 devices", (10, 4)),
    ("Avast Ultimate", "win", 11.0, 109.99, "renewal $109.99/yr, 1 PC", (10, 4)),
    ("AVG TuneUp", "win", 9.0, 65.99, "renewal $65.99/yr, 1 PC", (10, 4)),
    ("Ashampoo WinOptimizer", "win", 7.5, 25 / 3, "$25 one-time ÷ 3 yrs, 1 PC", (8, 4)),
    ("Glary Utilities Pro", "win", 9.5, 39.95, "$39.95/yr, 3 PCs", (10, 16)),
    ("iolo Ultimate Defense", "win", 7.5, 67.93, "$67.93/yr sale, 10 devices", (-10, 18, "end")),
    ("Wise Care 365 Pro", "win", 6.0, 29.96, "$29.96/yr, 3 PCs", (-10, 4, "end")),
    ("Auslogics BoostSpeed", "win", 7.0, 56.62, "$56.62/yr, 3 PCs", (-10, -8, "end")),
]
# Ambysto Pro Suite (Optimer + Steady + Space) at candidate prices — hypotheses, not decisions.
AMB = [("Suite $39/yr", 7.5, 39.0), ("Suite $59 one-time", 7.5, 59 / 3), ("Suite $39 one-time", 7.5, 39 / 3)]

def value(f, price): return f / (price / 10)

W, H = 1040, 820
L, R, T, B = 90, 30, 70, 90
XMAX, YMAX = 15, 11
def sx(f): return L + f / XMAX * (W - L - R)
def sy(v): return H - B - min(v, YMAX) / YMAX * (H - T - B)

COL = {"mac": "#6B5CE8", "win": "#2F7BD6", "both": "#8A6FD1", "catalog": "#8A8F98"}
out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" font-family="-apple-system, Segoe UI, Helvetica, Arial, sans-serif">',
       f'<rect width="{W}" height="{H}" fill="#ffffff"/>',
       f'<text x="{L}" y="34" font-size="20" font-weight="600" fill="#1C1A33">Value × functionality: utility suites (read 2026-10-09)</text>',
       f'<text x="{L}" y="54" font-size="12" fill="#5b5f6a">X: categories covered of 15 (✓=1, partial=0.5) · Y: functionality points per $10 of effective yearly price (one-time ÷ 3 yrs)</text>']
for i in range(0, XMAX + 1, 3):
    out.append(f'<line x1="{sx(i)}" y1="{T}" x2="{sx(i)}" y2="{H-B}" stroke="#eceef2"/>')
    out.append(f'<text x="{sx(i)}" y="{H-B+20}" font-size="12" text-anchor="middle" fill="#5b5f6a">{i}</text>')
for v in range(0, YMAX + 1, 1):
    out.append(f'<line x1="{L}" y1="{sy(v)}" x2="{W-R}" y2="{sy(v)}" stroke="#eceef2"/>')
    out.append(f'<text x="{L-10}" y="{sy(v)+4}" font-size="12" text-anchor="end" fill="#5b5f6a">{v}</text>')
out.append(f'<text x="{(L+W-R)/2}" y="{H-40}" font-size="13" text-anchor="middle" fill="#1C1A33">Functionality →</text>')
out.append(f'<text transform="translate(28 {(T+H-B)/2}) rotate(-90)" font-size="13" text-anchor="middle" fill="#1C1A33">Value for money →</text>')
# quadrant hint: suites with real breadth and good value
out.append(f'<rect x="{sx(7)}" y="{sy(YMAX)}" width="{sx(15)-sx(7)}" height="{sy(3)-sy(YMAX)}" fill="#ED708F" opacity="0.06"/>')
out.append(f'<text x="{sx(14.8)}" y="{sy(YMAX)+18}" font-size="12" text-anchor="end" fill="#B8476A">broad + good value: open space for a suite</text>')
rows = []
for name, plat, f, price, basis, off in P:
    v = value(f, price)
    x, y = sx(f), sy(v)
    out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="6" fill="{COL[plat]}" opacity="0.9"/>')
    anchor = off[2] if len(off) > 2 else "start"
    out.append(f'<text x="{x+off[0]:.1f}" y="{y+off[1]:.1f}" font-size="11.5" fill="#1C1A33" text-anchor="{anchor}">{name}</text>')
    rows.append({"name": name, "platform": plat, "functionality": f, "effective_per_year": round(price, 2), "value": round(v, 2), "basis": basis})
pts = []
for i, (name, f, price) in enumerate(AMB):
    v = value(f, price); x, y = sx(f), sy(v); pts.append((x, y))
    out.append(f'<path d="M{x:.1f},{y-8:.1f} L{x+8:.1f},{y:.1f} L{x:.1f},{y+8:.1f} L{x-8:.1f},{y:.1f} Z" fill="#ffffff" stroke="#ED708F" stroke-width="2.5"/>')
    out.append(f'<text x="{x+12:.1f}" y="{y+4:.1f}" font-size="11.5" font-weight="600" fill="#B8476A">Ambysto {name}</text>')
    rows.append({"name": f"Ambysto {name}", "platform": "ambysto", "functionality": f, "effective_per_year": round(price, 2), "value": round(v, 2), "basis": "candidate price (hypothesis)"})
out.append(f'<polyline points="{" ".join(f"{x:.1f},{y:.1f}" for x, y in pts)}" fill="none" stroke="#ED708F" stroke-dasharray="4 4"/>')
# legend
lx, ly = L + 24, sy(8.4)
out.append(f'<rect x="{lx-12}" y="{ly-18}" width="236" height="96" rx="8" fill="#ffffff" stroke="#eceef2"/>')
for i, (label, c) in enumerate([("Mac vendor", COL["mac"]), ("Windows vendor", COL["win"]), ("Mac + Windows", COL["both"]), ("Subscription catalog", COL["catalog"])]):
    out.append(f'<circle cx="{lx}" cy="{ly+i*18}" r="5" fill="{c}"/><text x="{lx+12}" y="{ly+i*18+4}" font-size="11.5" fill="#1C1A33">{label}</text>')
out.append(f'<path d="M{lx},{ly+72-6} L{lx+6},{ly+72} L{lx},{ly+72+6} L{lx-6},{ly+72} Z" fill="#fff" stroke="#ED708F" stroke-width="2"/><text x="{lx+12}" y="{ly+76}" font-size="11.5" fill="#B8476A">Ambysto suite, candidate prices</text>')
out.append('</svg>')
open("value-functionality.svg", "w").write("\n".join(out))
json.dump(rows, open("positioning.json", "w"), indent=1)
for r in sorted(rows, key=lambda r: -r["value"]): print(f'{r["value"]:>6}  F={r["functionality"]:<5} ${r["effective_per_year"]:<7} {r["name"]}')
