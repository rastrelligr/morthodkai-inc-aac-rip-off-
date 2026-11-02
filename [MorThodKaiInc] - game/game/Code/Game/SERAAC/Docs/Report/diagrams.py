# SVG diagrams for the data-structure report. Labels use code names so both languages share them.
from collections import deque

INK = "#1f2a44"
ACC = "#8a1c3c"      # rose accent (the game's UI accent)
ACC2 = "#2f5d8a"
MUTE = "#6b7280"
FILL = "#f4f6fa"
FILL2 = "#fbeef2"
FONT = "font-family=\"Consolas, 'Sarabun', monospace\""


def svg(w, h, body):
    return (f'<svg viewBox="0 0 {w} {h}" xmlns="http://www.w3.org/2000/svg" class="fig" {FONT} font-size="13">'
            '<defs><marker id="ar" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
            f'<path d="M0,0 L10,5 L0,10 z" fill="{INK}"/></marker>'
            '<marker id="ara" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
            f'<path d="M0,0 L10,5 L0,10 z" fill="{ACC}"/></marker>'
            '<marker id="dia" viewBox="0 0 12 8" refX="12" refY="4" markerWidth="12" markerHeight="8" orient="auto">'
            f'<path d="M0,4 L6,0 L12,4 L6,8 z" fill="{INK}"/></marker></defs>'
            + body + '</svg>')


def text(x, y, s, size=13, anchor="middle", color=INK, weight="normal", italic=False):
    st = ' font-style="italic"' if italic else ''
    return f'<text x="{x}" y="{y}" font-size="{size}" text-anchor="{anchor}" fill="{color}" font-weight="{weight}"{st}>{esc(s)}</text>'


def esc(s):
    return str(s).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def rect(x, y, w, h, fill=FILL, stroke=INK, sw=1.4, rx=6, dash=False):
    d = ' stroke-dasharray="5 4"' if dash else ''
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{d}/>'


def box(x, y, w, h, title, lines=(), fill=FILL, stroke=INK, size=13, tsize=14):
    out = rect(x, y, w, h, fill, stroke)
    if lines:
        out += text(x + w / 2, y + 20, title, tsize, weight="bold", color=stroke)
        for i, l in enumerate(lines):
            out += text(x + w / 2, y + 40 + i * 17, l, size, color=INK)
    else:
        out += text(x + w / 2, y + h / 2 + 5, title, tsize, weight="bold", color=stroke)
    return out


def arrow(x1, y1, x2, y2, label=None, color=INK, dash=False, lx=None, ly=None, size=12):
    m = "ara" if color == ACC else "ar"
    d = ' stroke-dasharray="5 4"' if dash else ''
    out = f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="1.5" marker-end="url(#{m})"{d}/>'
    if label:
        out += text(lx if lx is not None else (x1 + x2) / 2, ly if ly is not None else (y1 + y2) / 2 - 6, label, size, color=color)
    return out


def path(d, color=INK, dash=False, marker=True, sw=1.5):
    m = ' marker-end="url(#ara)"' if (marker and color == ACC) else (' marker-end="url(#ar)"' if marker else '')
    da = ' stroke-dasharray="5 4"' if dash else ''
    return f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{sw}"{m}{da}/>'


# ------------------------------------------------------------------ 1. architecture
def architecture():
    b = ""
    b += box(330, 10, 240, 44, "Game1  (MonoGame loop)")
    b += arrow(450, 54, 450, 80)
    b += box(250, 80, 400, 60, "ScreenManager", ["_stack : List<Screen>  (used as a Stack)"], fill=FILL2, stroke=ACC)
    scr = ["MainMenu", "Hub", "Exploration", "Combat", "Result"]
    for i, s in enumerate(scr):
        x = 40 + i * 168
        b += box(x, 170, 150, 40, s + "Screen", size=12, tsize=13)
        b += arrow(450, 140, x + 75, 170, color=MUTE)
    layers = [
        ("Trial", ["TrialRun · TrialMap", "Room · RoomLayout · Pack", "Graph · Tile[,] · BFS Queue"]),
        ("Combat", ["Battle · Combatant", "CombatAI", "Combatant[4] · Sort · Queue"]),
        ("Meta", ["Profile · Progression", "Quests", "Dictionary · List → JSON"]),
        ("Data", ["CharacterDb · EnemyDb", "NpcDb · ItemDb ...", "List tables · Dialogue graph"]),
        ("Core / UI", ["Input · Assets · Sprites", "Ui · Particles", "HashSet · cache Dictionary"]),
    ]
    for i, (t, ls) in enumerate(layers):
        x = 40 + i * 168
        b += box(x, 250, 150, 100, t, ls, size=11, tsize=14)
    b += f'<line x1="30" y1="230" x2="870" y2="230" stroke="{MUTE}" stroke-dasharray="3 3"/>'
    b += text(870, 244, "game rules / content / engine services", 11, "end", MUTE, italic=True)
    return svg(900, 365, b)


# ------------------------------------------------------------------ 2. screen stack
def screen_stack():
    def stack(x, items, caption, top_label=True):
        o = ""
        base = 250
        for i, it in enumerate(items):
            y = base - (i + 1) * 46
            hl = i == len(items) - 1
            o += rect(x, y, 170, 40, FILL2 if hl else FILL, ACC if hl else INK)
            o += text(x + 85, y + 25, it, 13, weight="bold" if hl else "normal", color=ACC if hl else INK)
        o += f'<line x1="{x-8}" y1="{base+4}" x2="{x+178}" y2="{base+4}" stroke="{INK}" stroke-width="2"/>'
        if top_label:
            ty = base - len(items) * 46 + 20
            o += arrow(x + 230, ty, x + 176, ty, color=ACC)
            o += text(x + 236, ty + 4, "Top", 12, "start", ACC, "bold")
        o += text(x + 85, base + 26, caption, 12, color=MUTE)
        return o
    b = ""
    b += stack(20, ["ExplorationScreen"], "(1) in a trial")
    b += arrow(250, 140, 320, 140, "Push(combat)", ly=130)
    b += stack(330, ["ExplorationScreen", "CombatScreen"], "(2) fight: Update() only Top")
    b += arrow(560, 140, 630, 140, "Pop()", ly=130)
    b += stack(640, ["ExplorationScreen"], "(3) OnResume(): state intact", top_label=False)
    b += text(450, 300, "Draw(): from the first non-overlay screen up to Top (bottom → top); Update(): Top only", 12, color=INK)
    return svg(900, 315, b)


# ------------------------------------------------------------------ 3. trial map graph
MAP_ROOMS = {
    "A": ((0, 0), "S"), "B": ((1, 0), ""), "C": ((2, 0), ""), "D": ((2, 1), ""), "E": ((2, 2), "*"),
    "F": ((0, 1), ""), "G": ((0, 2), ""), "H": ((-1, 2), "$"), "I": ((0, 3), "X"), "J": ((-1, 0), ""),
    "K": ((-2, 0), "!"), "L": ((-2, -1), "?"), "M": ((1, -1), "X"), "N": ((1, 1), "+"),
}
MAP_TREE = [("A", "B"), ("B", "C"), ("C", "D"), ("D", "E"), ("A", "F"), ("F", "G"), ("G", "H"), ("G", "I"),
            ("A", "J"), ("J", "K"), ("K", "L"), ("B", "M"), ("B", "N")]
MAP_LOOP = [("N", "D"), ("F", "N")]


def map_depths():
    adj = {k: [] for k in MAP_ROOMS}
    for a, c in MAP_TREE + MAP_LOOP:
        adj[a].append(c)
        adj[c].append(a)
    depth = {"A": 0}
    q = deque(["A"])
    while q:
        r = q.popleft()
        for n in adj[r]:
            if n not in depth:
                depth[n] = depth[r] + 1
                q.append(n)
    return depth


def trial_map():
    depth = map_depths()
    cs = 92
    ox, oy = 40 + 2 * cs + cs / 2, 30 + cs / 2   # grid x -2..2, y -1..3
    def pos(k):
        (gx, gy), _ = MAP_ROOMS[k]
        return ox + gx * cs, oy + (gy + 1) * cs
    b = ""
    for gx in range(-2, 3):
        for gy in range(-1, 4):
            x, y = ox + gx * cs - cs / 2, oy + (gy + 1) * cs - cs / 2
            b += f'<rect x="{x}" y="{y}" width="{cs}" height="{cs}" fill="none" stroke="#d7dbe3" stroke-dasharray="2 3"/>'
            b += text(x + 4, y + 12, f"({gx},{gy})", 9, "start", "#a0a6b2")
    for a, c in MAP_TREE:
        (x1, y1), (x2, y2) = pos(a), pos(c)
        b += f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{INK}" stroke-width="3"/>'
    for a, c in MAP_LOOP:
        (x1, y1), (x2, y2) = pos(a), pos(c)
        b += f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{ACC}" stroke-width="3" stroke-dasharray="6 4"/>'
    for k, ((gx, gy), lab) in MAP_ROOMS.items():
        x, y = pos(k)
        special = lab != ""
        b += f'<circle cx="{x}" cy="{y}" r="22" fill="{FILL2 if special else "#fff"}" stroke="{ACC if special else INK}" stroke-width="2"/>'
        b += text(x, y - 2, lab or k, 15 if special else 13, weight="bold", color=ACC if special else INK)
        b += text(x, y + 13, f"d={depth[k]}", 10, color=MUTE)
    lx = 520
    b += text(lx, 40, "Legend", 14, "start", weight="bold")
    leg = [("S", "Entrance (depth 0)"), ("*", "Objective: deepest dead end"), ("X", "Extraction: depth ≥ ⌈Max/2⌉"),
           ("!", "Elite (depth ≥ 2)"), ("+", "Rest: near Max/2"), ("$", "Treasure"), ("?", "Event"),
           ("d=n", "BFS depth from S")]
    for i, (k, v) in enumerate(leg):
        b += text(lx + 14, 68 + i * 24, k, 13, weight="bold", color=ACC)
        b += text(lx + 44, 68 + i * 24, v, 12, "start")
    y0 = 68 + len(leg) * 24 + 10
    b += f'<line x1="{lx}" y1="{y0}" x2="{lx+36}" y2="{y0}" stroke="{INK}" stroke-width="3"/>'
    b += text(lx + 44, y0 + 4, "tree edge (growth step)", 12, "start")
    b += f'<line x1="{lx}" y1="{y0+24}" x2="{lx+36}" y2="{y0+24}" stroke="{ACC}" stroke-width="3" stroke-dasharray="6 4"/>'
    b += text(lx + 44, y0 + 28, "extra edge → loop (p = 0.18)", 12, "start")
    return svg(860, 500, b)


# ------------------------------------------------------------------ 4. map hash table + adjacency
def map_hash():
    b = ""
    b += text(130, 22, "Rooms : Dictionary<Point, Room>", 13, weight="bold", color=ACC)
    rows = [("(0,0)", "Room#0 S"), ("(1,0)", "Room#1"), ("(0,1)", "Room#5"), ("(-1,0)", "Room#9"), ("…", "…")]
    for i, (k, v) in enumerate(rows):
        y = 36 + i * 34
        b += rect(20, y, 100, 30, FILL, INK, rx=2) + text(70, y + 20, k, 13)
        b += arrow(120, y + 15, 150, y + 15)
        b += rect(150, y, 110, 30, FILL2 if i == 0 else FILL, ACC if i == 0 else INK, rx=2) + text(205, y + 20, v, 13)
    b += text(140, 222, "At(p): TryGetValue → O(1) average", 12, color=MUTE)
    # room object
    b += rect(330, 30, 250, 200, "#fff", INK)
    b += text(455, 52, "Room #0 (Entrance)", 14, weight="bold")
    fields = ["Grid = (0,0)", "Type = Entrance", "Depth = 0", "Layout : RoomLayout", "Links : Dictionary<Dir,Room>",
              "Pickups : List<LootPickup>", "Encounter : List<EnemyDef>", "Visited / Cleared : bool"]
    for i, f in enumerate(fields):
        b += text(345, 76 + i * 19, f, 12, "start", ACC if "Links" in f else INK, "bold" if "Links" in f else "normal")
    b += arrow(260, 51, 330, 60)
    # adjacency
    b += text(740, 22, "Links (adjacency list)", 13, weight="bold", color=ACC)
    adj = [("North", "— (no key)"), ("East", "Room#1"), ("South", "Room#5"), ("West", "Room#9")]
    for i, (k, v) in enumerate(adj):
        y = 36 + i * 38
        b += rect(650, y, 80, 30, FILL, INK, rx=2) + text(690, y + 20, k, 13)
        b += arrow(730, y + 15, 760, y + 15, dash=i == 0)
        b += rect(760, y, 110, 30, FILL, MUTE if i == 0 else INK, rx=2, dash=i == 0) + text(815, y + 20, v, 12, color=MUTE if i == 0 else INK)
    b += path("M580,152 C610,152 620,60 650,52")
    b += text(760, 200, "Link(a,b,d): a.Links[d]=b;", 12)
    b += text(760, 218, "b.Links[Opposite(d)]=a  (undirected)", 12)
    return svg(900, 245, b)


# ------------------------------------------------------------------ 5. tile grid + BFS distance field
PLAZA = [
    "...................",
    ".o.......L.......o.",
    "...,..........F,...",
    "..L...o.....o...L..",
    "...................",
    "...................",
    "..L...o.....o...L..",
    "...,...........,...",
    ".o.......L.......o.",
    "...................",
]


def plaza_grid(doors=("West", "East", "North")):
    cols, rows = 21, 12
    g = [["W"] * rows for _ in range(cols)]
    feature = None
    for iy in range(10):
        for ix in range(19):
            ch = PLAZA[iy][ix]
            x, y = ix + 1, iy + 1
            g[x][y] = {"o": "P", ",": "D"}.get(ch, "F")
            if ch == "L": g[x][y] = "L"
            if ch == "F": feature = (x, y); g[x][y] = "X"
    door = {"North": [(10, 0)], "South": [(10, 11)], "West": [(0, 5), (0, 6)], "East": [(20, 5), (20, 6)]}
    for d in doors:
        for (x, y) in door[d]: g[x][y] = "F"
    return g, feature


def tile_grid():
    g, feature = plaza_grid()
    cols, rows = 21, 12
    solid = lambda x, y: x < 0 or y < 0 or x >= cols or y >= rows or g[x][y] in ("W", "P") or (x, y) == feature
    start = (1, 5)
    goal = (feature[0], feature[1] + 1)
    dist, prev = {start: 0}, {start: start}
    q = deque([start])
    while q:
        p = q.popleft()
        for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):    # North, East, South, West (Dir order)
            n = (p[0] + dx, p[1] + dy)
            if n in dist or solid(*n): continue
            dist[n] = dist[p] + 1
            prev[n] = p
            q.append(n)
    path_tiles = []
    c = goal
    while c != start:
        path_tiles.append(c)
        c = prev[c]
    path_tiles.append(start)
    path_tiles.reverse()
    cs = 34
    ox, oy = 20, 20
    maxd = max(dist.values())
    b = ""
    for x in range(cols):
        for y in range(rows):
            t = g[x][y]
            X, Y = ox + x * cs, oy + y * cs
            if t == "W": fill = "#2b2f3a"
            elif t == "P": fill = "#7c8292"
            elif (x, y) == feature: fill = ACC
            else:
                d = dist.get((x, y))
                k = 0 if d is None else d / maxd
                r, gg, bb = int(240 - 70 * k), int(244 - 60 * k), int(250 - 20 * k)
                fill = f"rgb({r},{gg},{bb})"
            b += f'<rect x="{X}" y="{Y}" width="{cs}" height="{cs}" fill="{fill}" stroke="#ffffff" stroke-width="1"/>'
            if t not in ("W", "P") and (x, y) != feature and (x, y) in dist:
                b += text(X + cs / 2, Y + cs / 2 + 4, dist[(x, y)], 10, color=INK)
            if t == "L":
                b += f'<circle cx="{X+cs-7}" cy="{Y+7}" r="4" fill="#d9a400"/>'
            if t == "D":
                b += f'<rect x="{X+3}" y="{Y+3}" width="6" height="6" fill="#9bb0c9"/>'
    b += text(ox + feature[0] * cs + cs / 2, oy + feature[1] * cs + cs / 2 + 5, "F", 14, color="#fff", weight="bold")
    pts = " ".join(f"{ox + x * cs + cs / 2},{oy + y * cs + cs / 2}" for x, y in path_tiles)
    b += f'<polyline points="{pts}" fill="none" stroke="{ACC}" stroke-width="3.5" stroke-linejoin="round" opacity="0.85"/>'
    sx, sy = ox + start[0] * cs + cs / 2, oy + start[1] * cs + cs / 2
    b += f'<circle cx="{sx}" cy="{sy}" r="9" fill="none" stroke="{ACC}" stroke-width="3"/>'
    ly = oy + rows * cs + 22
    leg = [("#2b2f3a", "Wall (solid)"), ("#7c8292", "Pillar (solid)"), ("rgb(205,214,240)", "Floor: number = BFS distance"),
           ("#d9a400", "L = LootSpots"), (ACC, "F = Feature")]
    for i, (c, s) in enumerate(leg):
        x = ox + i * 150
        b += f'<rect x="{x}" y="{ly-11}" width="14" height="14" fill="{c}"/>' + text(x + 20, ly + 1, s, 11, "start")
    b += f'<line x1="{ox+5*150-10}" y1="{ly-4}" x2="{ox+5*150+16}" y2="{ly-4}" stroke="{ACC}" stroke-width="3.5"/>'
    b += text(ox + 5 * 150 + 22, ly + 1, "BFS path", 11, "start")
    return svg(ox * 2 + cols * cs + 60, oy + rows * cs + 40, b), len(path_tiles) - 1, len(dist)


# ------------------------------------------------------------------ 6. BFS mechanics
def bfs_mechanics():
    b = ""
    b += text(150, 22, "q : Queue<Point>  (FIFO frontier)", 13, weight="bold", color=ACC)
    cells = ["(3,5)", "(2,4)", "(2,6)", "(2,5)", ""]
    for i, c in enumerate(cells):
        x = 30 + i * 60
        b += rect(x, 36, 60, 34, FILL2 if i == 0 else FILL, ACC if i == 0 else INK, rx=0) + text(x + 30, 58, c, 12)
    b += arrow(30, 95, 30, 72, color=ACC) + text(30, 110, "Dequeue()", 11, color=ACC)
    b += arrow(300, 95, 300, 72) + text(300, 110, "Enqueue(n)", 11)
    b += text(150, 145, "prev : Dictionary<Point, Point>", 13, weight="bold", color=ACC)
    pv = [("(1,5)", "(1,5)  ← start"), ("(2,5)", "(1,5)"), ("(1,4)", "(1,5)"), ("(3,5)", "(2,5)"), ("(4,5)", "(3,5)")]
    for i, (k, v) in enumerate(pv):
        y = 158 + i * 28
        b += rect(40, y, 80, 26, FILL, INK, rx=0) + text(80, y + 18, k, 12)
        b += rect(120, y, 150, 26, "#fff", INK, rx=0) + text(195, y + 18, v, 12)
    b += text(155, 312, "visited-set + parent pointer in one hash table", 11, color=MUTE)
    # reconstruction
    b += text(640, 22, "Path reconstruction", 13, weight="bold", color=ACC)
    chain = ["to = (4,5)", "(3,5)", "(2,5)", "from = (1,5)"]
    for i, c in enumerate(chain):
        y = 40 + i * 52
        b += rect(560, y, 160, 32, FILL2 if i in (0, 3) else FILL, ACC if i in (0, 3) else INK) + text(640, y + 21, c, 12)
        if i < 3: b += arrow(640, y + 32, 640, y + 52, "prev[c]", lx=690, ly=y + 46, size=11)
    b += text(640, 268, "path.Add(c) while c ≠ from", 12)
    b += text(640, 288, "then path.Reverse()  → from … to", 12, color=ACC, weight="bold")
    b += f'<line x1="460" y1="20" x2="460" y2="300" stroke="#d7dbe3"/>'
    return svg(900, 320, b)


# ------------------------------------------------------------------ 7. formation arrays
def formation():
    b = ""
    labels = ["[0] Front 1", "[1] Front 2", "[2] Back 1", "[3] Back 2"]
    def arr(x, y, title, vals, hl=()):
        o = text(x, y - 8, title, 13, "start", ACC, "bold")
        for i, v in enumerate(vals):
            X = x + i * 100
            empty = v is None
            o += rect(X, y, 100, 44, FILL2 if i in hl else ("#fff" if empty else FILL), ACC if i in hl else INK, rx=0, dash=empty)
            o += text(X + 50, y + 20, "null" if empty else v, 13, color=MUTE if empty else INK, weight="bold" if not empty else "normal")
            o += text(X + 50, y + 37, labels[i], 10, color=MUTE)
        return o
    b += arr(20, 40, "Party : Combatant[4]", ["Relle", "Warden", "Hunter", "Priest"])
    b += arr(470, 40, "Enemies : Combatant[4]", ["Gnawer A", "Gnawer B", "Husk", "Wraith"])
    b += arrow(220, 100, 220, 140, "SwapTo(Priest, slot 1)", lx=330, ly=124)
    b += arr(20, 160, "after Position (swap)", ["Relle", "Priest", "Hunter", "Warden"], hl=(1, 3))
    b += arrow(670, 100, 670, 140, "Kill(Gnawer B)", lx=740, ly=124)
    b += arr(470, 160, "after a death: slot stays empty", ["Gnawer A", None, "Husk", "Wraith"], hl=(1,))
    b += text(450, 240, "Index = position: Has(slot), Allies(c)[c.Slot] == c and neighbour checks |a.Slot − b.Slot| ≤ 1 are all O(1)", 12)
    return svg(900, 255, b)


# ------------------------------------------------------------------ 8. Ranks bitmask
def ranks():
    b = ""
    def bits(x, y, title, val, hl=None):
        o = text(x, y - 10, title, 13, "start", ACC, "bold")
        for i in range(8):
            bit = 7 - i
            on = (val >> bit) & 1
            unused = bit >= 4
            o += rect(x + i * 40, y, 40, 36, "#eceef2" if unused else (FILL2 if on else "#fff"), MUTE if unused else (ACC if on else INK), rx=0)
            o += text(x + i * 40 + 20, y + 24, on, 15, color=MUTE if unused else (ACC if on else INK), weight="bold")
            if not unused:
                o += text(x + i * 40 + 20, y + 52, f"slot {bit}", 10, color=MUTE)
        return o
    b += bits(20, 40, 'Ranks.Of("12")  → front row: _bits = 0b0011', 0b0011)
    b += bits(470, 40, 'Ranks.Of("234") → _bits = 0b1110', 0b1110)
    b += text(20, 130, "Has(slot)     = (_bits & (1 << slot)) != 0", 13, "start")
    b += text(20, 152, "Within(other) = _bits != 0 && (_bits & ~other._bits) == 0", 13, "start")
    b += text(20, 174, "Ranks.All = 0b1111,  Ranks.None = 0", 13, "start")
    b += text(470, 130, "Example: Has(2) on 0b1110", 13, "start", weight="bold")
    b += text(470, 152, "1 << 2      = 0b0100", 13, "start")
    b += text(470, 174, "0b1110 & 0b0100 = 0b0100 ≠ 0  → true", 13, "start", ACC)
    b += text(450, 205, "readonly struct holding one byte: no allocation, copied by value, O(1) for every test", 12, color=MUTE)
    return svg(900, 220, b)


# ------------------------------------------------------------------ 9. turn pipeline
def turn_pipeline():
    b = ""
    b += box(20, 30, 200, 92, "Choose phase", ["_choosers : List<Combatant>", "_chooserIndex (cursor)", "Undo(): index-- (LIFO)"], size=11)
    b += arrow(220, 76, 260, 76)
    b += box(260, 30, 230, 92, "Battle.ResolveOrder()", ["sort by: Priority ↓, Speed ↓,", "party first, Slot ↑, Order ↑", "→ List<Combatant>"], size=11)
    b += arrow(490, 76, 530, 76, "Enqueue", ly=68)
    b += box(530, 30, 200, 92, "_resolveQueue", ["Queue<Combatant>", "FIFO: fastest first"], fill=FILL2, stroke=ACC, size=11)
    b += arrow(630, 122, 630, 170, "Dequeue()", lx=680, ly=150)
    b += box(530, 170, 200, 80, "Battle.Execute(actor)", ["pure rules, no drawing", "→ List<BattleEvent>"], size=11)
    b += arrow(530, 210, 490, 210, "Enqueue each", ly=200)
    b += box(260, 170, 230, 80, "_events", ["Queue<BattleEvent>", "Damage, Move, Death, ..."], fill=FILL2, stroke=ACC, size=11)
    b += arrow(260, 210, 220, 210, "Dequeue()", ly=200)
    b += box(20, 170, 200, 80, "PlayEvent(e)", ["animate one event", "then wait _timer"], size=11)
    b += path("M120,250 C120,300 630,300 630,252", dash=True)
    b += text(375, 296, "both queues empty → Battle.EndTurn() events → next turn (Turn + 1)", 12, color=MUTE)
    return svg(760, 310, b)


# ------------------------------------------------------------------ 10. dialogue graph
def dialogue():
    b = ""
    nodes = {"Entry()": (80, 150), "offer": (260, 80), "progress": (260, 220), "reward": (260, 300),
             "info": (470, 40), "accepted": (470, 150), "end": (700, 150)}
    def n(k, special=False):
        x, y = nodes[k]
        w = 110
        return rect(x - w / 2, y - 18, w, 36, FILL2 if special else FILL, ACC if special else INK, rx=18) + text(x, y + 5, k, 13, weight="bold", color=ACC if special else INK)
    edges = [("Entry()", "offer", "quest not active"), ("Entry()", "progress", "active"), ("Entry()", "reward", "done"),
             ("offer", "info", '"What\'s in it…"'), ("offer", "accepted", '"Deal."'), ("info", "accepted", '"Deal."'),
             ("offer", "end", '"Not interested."'), ("info", "end", ""), ("accepted", "end", "Continue")]
    for a, c, lab in edges:
        (x1, y1), (x2, y2) = nodes[a], nodes[c]
        dx, dy = x2 - x1, y2 - y1
        L = (dx * dx + dy * dy) ** .5
        sx, sy = x1 + dx / L * 58, y1 + dy / L * 20
        ex, ey = x2 - dx / L * 58, y2 - dy / L * 22
        dash = a == "Entry()"
        f = {("offer", "end"): 0.72, ("info", "end"): 0.5}.get((a, c), 0.5)
        lx, ly = sx + (ex - sx) * f, sy + (ey - sy) * f - 6
        if (a, c) == ("info", "accepted"): lx += 26
        if (a, c) == ("offer", "accepted"): lx -= 22; ly += 4
        b += arrow(sx, sy, ex, ey, lab, dash=dash, lx=lx, ly=ly, size=11)
    for k in nodes:
        b += n(k, k in ("Entry()", "end"))
    b += text(620, 270, "Nodes : Dictionary<string, DialogueNode>", 12, color=ACC, weight="bold")
    b += text(620, 290, "edge = DialogueChoice { Label, Next, If }", 12)
    b += text(620, 310, "OnEnter: side effect (start quest, give item)", 12)
    return svg(820, 330, b)


# ------------------------------------------------------------------ 11. cache
def cache():
    b = ""
    b += box(20, 60, 150, 50, "Sprites.Get(id)")
    b += arrow(170, 85, 220, 85)
    b += f'<polygon points="300,45 380,85 300,125 220,85" fill="{FILL2}" stroke="{ACC}" stroke-width="1.5"/>'
    b += text(300, 82, "_cache.", 12, color=ACC) + text(300, 98, "TryGetValue?", 12, color=ACC)
    b += arrow(380, 85, 470, 85, "hit: O(1)", ly=76)
    b += box(470, 60, 170, 50, "return cached", fill="#fff")
    b += arrow(300, 125, 300, 170, "miss", lx=325, ly=150)
    b += box(200, 170, 200, 50, "load file / build", ["(slow: disk + GPU upload)"], size=11)
    b += arrow(400, 195, 470, 195)
    b += box(470, 170, 170, 50, "_cache[id] = set")
    b += arrow(555, 170, 555, 110)
    b += text(780, 60, "Same pattern (memoization):", 12, weight="bold")
    for i, s in enumerate(["Assets._cache : <string, ArtSprite>", "Sprites._scaled : <int, Texture2D>",
                           "Sprites._recolored : <(string,uint), …>", "Particles._emitters : <string, …>",
                           "Audio : <string, SoundEffect>", "Icons : <string, Texture2D>"]):
        b += text(780, 82 + i * 20, s, 11)
    return svg(940, 235, b)


# ------------------------------------------------------------------ 12. UI hit regions
def ui_regions():
    b = ""
    b += text(150, 22, "Frame N: Draw() → Ui.Hit(...) appends", 13, weight="bold", color=ACC)
    items = [("0", "actions[0]"), ("1", "actions[1]"), ("2", "Block(modal)"), ("3", "modal.ok")]
    for i, (k, v) in enumerate(items):
        y = 36 + i * 32
        b += rect(30, y, 40, 28, FILL, INK, rx=0) + text(50, y + 19, k, 12)
        b += rect(70, y, 180, 28, FILL2 if i == 3 else "#fff", ACC if i == 3 else INK, rx=0) + text(160, y + 19, v, 12)
    b += text(150, 182, "_current : List<Region>", 12)
    b += arrow(260, 90, 330, 90, "swap at frame end", ly=80)
    b += text(150, 205, "Frame N+1: Hover() scans _last from i = Count−1 down to 0", 12, color=ACC)
    b += text(150, 223, "→ the first rect containing the mouse wins (drawn last = on top)", 12)
    # screen mock
    b += rect(360, 30, 360, 170, "#fff", INK)
    b += rect(380, 60, 150, 30, FILL, INK) + text(455, 80, "actions[0]", 12)
    b += rect(380, 100, 150, 30, FILL, INK) + text(455, 120, "actions[1]", 12)
    b += rect(470, 70, 220, 115, "rgba(138,28,60,0.08)", ACC, dash=True) + text(580, 92, "modal (Block)", 12, color=ACC)
    b += rect(540, 140, 110, 30, FILL2, ACC) + text(595, 160, "modal.ok", 12, color=ACC)
    b += f'<path d="M500,112 l0,16 l4,-4 l4,8 l3,-1 l-4,-8 l6,0 z" fill="{INK}"/>'
    b += text(505, 150, "click here → Block hit: -1", 10, "start", MUTE)
    return svg(740, 235, b)


# ------------------------------------------------------------------ 13. weighted pick
def weighted():
    b = ""
    items = [("Relle", 1.2, "Front 1"), ("Warden", 1.0, "Front 2"), ("Hunter", 2.9, "Back 1 · Marked, hurt"), ("Priest", 1.4, "Back 2")]
    total = sum(w for _, w, _ in items)
    x = 40
    W = 700
    cols = [FILL, "#e6eaf2", FILL2, "#dfe7f0"]
    for i, (n, w, note) in enumerate(items):
        ww = W * w / total
        b += rect(x, 40, ww, 50, cols[i], ACC if i == 2 else INK, rx=0)
        b += text(x + ww / 2, 62, n, 13, weight="bold", color=ACC if i == 2 else INK)
        b += text(x + ww / 2, 80, f"w = {w}", 11)
        b += text(x + ww / 2, 108, note, 10, color=MUTE)
        x += ww
    rpos = 40 + W * 3.1 / total
    b += f'<line x1="{rpos}" y1="28" x2="{rpos}" y2="98" stroke="{ACC}" stroke-width="2.5"/>'
    b += text(rpos, 22, f"r = NextDouble() × total = 3.1", 12, color=ACC, weight="bold")
    b += text(40, 135, f"total = Σ w = {total:.1f}.  Walk the list subtracting w until r ≤ 0 → Hunter is chosen (P = 2.9 / {total:.1f} ≈ {2.9/total:.0%}).", 12, "start")
    b += text(40, 155, "w = (1 + (1 − hp/maxHp)·WoundedBias + Mark 1.5 − Guard 0.5) × Type.SlotPreference[slot]", 12, "start", MUTE)
    return svg(780, 170, b)


# ------------------------------------------------------------------ 14. UML class diagram
def uml():
    b = ""
    def cls(x, y, w, name, attrs, ops=(), stereo=None, hl=False):
        h = 26 + (16 if stereo else 0) + len(attrs) * 16 + 10 + (len(ops) * 16 + 8 if ops else 0)
        o = rect(x, y, w, h, "#fff", ACC if hl else INK, rx=0)
        yy = y + 18
        if stereo:
            o += text(x + w / 2, yy, f"«{stereo}»", 10, color=MUTE); yy += 16
        o += text(x + w / 2, yy, name, 13, weight="bold", color=ACC if hl else INK)
        yy += 8
        o += f'<line x1="{x}" y1="{yy}" x2="{x+w}" y2="{yy}" stroke="{INK}"/>'
        for a in attrs:
            yy += 16; o += text(x + 6, yy, a, 10.5, "start")
        if ops:
            yy += 8
            o += f'<line x1="{x}" y1="{yy}" x2="{x+w}" y2="{yy}" stroke="{INK}"/>'
            for p in ops:
                yy += 16; o += text(x + 6, yy, p, 10.5, "start")
        return o, h
    def comp(x1, y1, x2, y2, label=None, lx=None, ly=None):
        o = f'<line x1="{x2}" y1="{y2}" x2="{x1}" y2="{y1}" stroke="{INK}" stroke-width="1.2" marker-end="url(#dia)"/>'
        if label: o += text(lx or (x1 + x2) / 2, ly or (y1 + y2) / 2 - 4, label, 10, color=MUTE)
        return o
    def assoc(x1, y1, x2, y2, label=None, lx=None, ly=None, dash=False):
        return arrow(x1, y1, x2, y2, label, dash=dash, lx=lx, ly=ly, size=10)
    parts = []
    o, h = cls(20, 20, 230, "ScreenManager", ["- _stack : List<Screen>", "+ Top : Screen"], ["+ Push(s) / Pop() / Replace(s)"]); parts.append(o)
    o, h = cls(20, 160, 230, "Screen", ["+ IsOverlay : bool"], ["+ Update() / Draw()", "+ OnEnter/OnExit/OnResume()"], "abstract"); parts.append(o)
    o, h = cls(20, 320, 230, "CombatScreen", ["- _resolveQueue : Queue<Combatant>", "- _events : Queue<BattleEvent>",
                                              "- _vis : Dictionary<Combatant,Visual>", "- _battle : Battle"]); parts.append(o)
    o, h = cls(20, 470, 230, "ExplorationScreen", ["- _path : List<Vector2>", "- Run : TrialRun"], ["- FindPath(from,to) : List<Point>"]); parts.append(o)
    o, h = cls(320, 20, 250, "TrialRun", ["+ Party : List<PartyMember>", "+ Consumables : List<string>", "+ FoundEquipment : List<string>",
                                          "+ Flags : HashSet<string>", "+ Map : TrialMap", "+ Current : Room"], hl=True); parts.append(o)
    o, h = cls(320, 200, 250, "TrialMap", ["+ Rooms : Dictionary<Point,Room>", "+ Entrance : Room", "+ MaxDepth : int"],
               ["+ Generate(...) : TrialMap", "- ComputeDepths()  (BFS)"], hl=True); parts.append(o)
    o, h = cls(320, 360, 250, "Room", ["+ Links : Dictionary<Dir,Room>", "+ Pickups : List<LootPickup>", "+ Encounter : List<EnemyDef>",
                                       "+ Depth : int", "+ Layout : RoomLayout", "+ Pack : Pack"], hl=True); parts.append(o)
    o, h = cls(320, 540, 250, "RoomLayout", ["+ Tiles : Tile[,]", "+ LootSpots : List<Point>", "+ Doors : Dictionary<Dir,Point[]>",
                                             "+ Templates : string[][]"], ["+ Reachable(p) : HashSet<Point>"]); parts.append(o)
    o, h = cls(640, 20, 250, "Battle", ["+ Party : Combatant[4]", "+ Enemies : Combatant[4]", "+ Fallen : List<Combatant>", "+ Items : List<string>"],
               ["+ ResolveOrder() : List<Combatant>", "+ Execute(c) : List<BattleEvent>"], hl=True); parts.append(o)
    o, h = cls(640, 220, 250, "Combatant", ["+ Slot : int", "+ Statuses : List<StatusInstance>", "+ Actions : List<ActionDef>", "+ Plan : PlannedAction"],
               ["+ AddStatus(kind, turns, power)"]); parts.append(o)
    o, h = cls(640, 400, 250, "ActionDef", ["+ UsableFrom : Ranks", "+ TargetRanks : Ranks", "+ Priority : bool"]); parts.append(o)
    o, h = cls(640, 510, 250, "Ranks", ["- _bits : byte"], ["+ Has(slot) / Within(other)"], "struct"); parts.append(o)
    o, h = cls(600, 620, 300, "Profile", ["+ Characters :", "     Dictionary<string,CharacterProgress>", "+ Quests : Dictionary<string,QuestState>",
                                          "+ PartyOrder / Stash : List<string>"]); parts.append(o)
    o, h = cls(20, 600, 230, "Pack", ["+ Members : List<Hunter>"], ["- FindPath(from,to) : List<Vector2>"]); parts.append(o)
    b += "".join(parts)
    b += comp(135, 112, 135, 160, "1 ◆— 0..*", 180, 140)
    b += f'<line x1="135" y1="284" x2="135" y2="320" stroke="{INK}"/><polygon points="135,268 127,284 143,284" fill="#fff" stroke="{INK}"/>'
    b += f'<polyline points="135,300 10,300 10,516 20,516" fill="none" stroke="{INK}"/>'
    b += path("M250,334 L605,334 L605,90 L640,90", dash=True) + text(612, 200, "uses", 10, "start", MUTE)
    b += path("M250,520 L290,520 L290,86 L320,86", dash=True) + text(296, 300, "uses", 10, "start", MUTE)
    b += comp(445, 152, 445, 200, "1", 455, 180)
    b += comp(445, 324, 445, 360, "1..n", 468, 354)
    b += comp(445, 492, 445, 540, "1", 455, 520)
    b += f'<path d="M570,400 C610,400 610,440 570,440" fill="none" stroke="{INK}"/>' + text(578, 455, "Links (self)", 10, "start", MUTE)
    b += comp(320, 470, 250, 620, "0..1", 300, 560)
    b += comp(765, 160, 765, 220, "◆— ≤4 + fallen", 830, 192)
    b += arrow(765, 344, 765, 400, "0..*", 785, 376, size=10)
    b += comp(765, 484, 765, 510, "2", 778, 500)
    return svg(910, 735, b)


ALL = {
    "architecture": architecture, "screen_stack": screen_stack, "trial_map": trial_map, "map_hash": map_hash,
    "bfs_mechanics": bfs_mechanics, "formation": formation, "ranks": ranks, "turn_pipeline": turn_pipeline,
    "dialogue": dialogue, "cache": cache, "ui_regions": ui_regions, "weighted": weighted, "uml": uml,
}


def render_all():
    out = {k: f() for k, f in ALL.items()}
    tg, plen, reach = tile_grid()
    out["tile_grid"] = tg
    out["_path_len"] = plen
    out["_reach"] = reach
    return out
