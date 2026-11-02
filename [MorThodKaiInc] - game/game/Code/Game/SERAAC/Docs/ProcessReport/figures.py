# Figures for the process report: SVG charts drawn from data (the timeline, the board, the git history)
# and the screenshots / artwork in img/ embedded as base64.
import base64
import datetime as dt
import html
import os
import subprocess
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, *[".."] * 7))
FONT = "Sarabun, 'Leelawadee UI', Tahoma, sans-serif"
ROSE, INK, GREY, LIGHT = "#c2185b", "#222", "#888", "#eee"
COLORS = {"วางแผน": "#5c6bc0", "โปรแกรม": "#c2185b", "ภาพ": "#00897b", "เสียง": "#f9a825", "ทดสอบ": "#6d4c41"}


def esc(s):
    return html.escape(str(s))


def img(name, width="100%"):
    path = os.path.join(HERE, "img", name)
    with open(path, "rb") as f:
        data = base64.b64encode(f.read()).decode()
    mime = "image/png" if name.endswith(".png") else "image/jpeg"
    return f'<img src="data:{mime};base64,{data}" style="width:{width};border:1px solid #ccc">'


def pair(a, b):
    return f'<div style="display:flex;gap:3mm">{img(a, "50%")}{img(b, "50%")}</div>'


def placeholder(text, height_mm=70):
    return (f'<div style="height:{height_mm}mm;border:2px dashed #999;display:flex;align-items:center;justify-content:center;'
            f'color:#666;font-size:12pt;padding:0 10mm;text-align:center">{esc(text)}</div>')


# ---------------------------------------------------------------- timeline (Gantt)

TASKS = [
    # (group, task, start, end)  dates from the sprint plans, commits and the files' history
    ("วางแผน", "Concept, GDD และ Product backlog", "2026-09-01", "2026-09-10"),
    ("วางแผน", "Sprint 1-2 (ต้นแบบแรก: ดันเจี้ยน การต่อสู้ UI)", "2026-09-01", "2026-09-28"),
    ("วางแผน", "ออกแบบเกมใหม่ (SERAAC design)", "2026-09-22", "2026-09-30"),
    ("โปรแกรม", "ต้นแบบ SERAAC: Trial, การต่อสู้, Hub", "2026-09-25", "2026-10-18"),
    ("ทดสอบ", "Playtest ครั้งที่ 1 และสรุปผล", "2026-10-01", "2026-10-02"),
    ("ภาพ", "พื้นหลังฉากต่อสู้ (City Outskirts)", "2026-10-07", "2026-10-10"),
    ("ภาพ", "สไปรต์ตัวละครและศัตรู", "2026-10-11", "2026-10-17"),
    ("โปรแกรม", "ปรับสมดุลเกม (Balance sheet)", "2026-10-16", "2026-10-19"),
    ("เสียง", "รวบรวมเพลงและเสียงประกอบ", "2026-10-15", "2026-11-02"),
    ("ภาพ", "เมนูหลักและวิดีโอเปิดเกม", "2026-10-19", "2026-10-31"),
    ("ภาพ", "ฉาก 3D (Blender) และพื้นหลังบอส", "2026-10-30", "2026-11-02"),
    ("ภาพ", "พอร์ตเทรตตัวละคร และแผนที่ Tiled", "2026-11-01", "2026-11-02"),
    ("โปรแกรม", "บทนำ (Prologue) และ Tutorial", "2026-11-01", "2026-11-03"),
    ("ทดสอบ", "Prototype build v0.1.3 และ Playtest ครั้งที่ 2", "2026-11-03", "2026-11-04"),
]


def gantt():
    d = lambda s: dt.date.fromisoformat(s)
    start, end = d("2026-09-01"), d("2026-11-07")
    days = (end - start).days
    left, top, row, width = 260, 40, 26, 470
    x = lambda s: left + (d(s) - start).days / days * width
    h = top + row * len(TASKS) + 40
    out = [f'<svg viewBox="0 0 760 {h}" xmlns="http://www.w3.org/2000/svg" font-family="{FONT}">']
    # month / week grid
    day = start
    while day <= end:
        if day.weekday() == 0:
            xx = x(day.isoformat())
            out.append(f'<line x1="{xx:.1f}" y1="{top - 6}" x2="{xx:.1f}" y2="{h - 34}" stroke="#e3e3e3"/>')
        if day.day == 1:
            xx = x(day.isoformat())
            out.append(f'<line x1="{xx:.1f}" y1="{top - 18}" x2="{xx:.1f}" y2="{h - 34}" stroke="#999"/>')
            name = {9: "ก.ย. 2569", 10: "ต.ค. 2569", 11: "พ.ย. 2569"}[day.month]
            out.append(f'<text x="{xx + 4:.1f}" y="{top - 8}" font-size="11" fill="{INK}">{name}</text>')
        day += dt.timedelta(days=1)
    for i, (grp, task, s, e) in enumerate(TASKS):
        y = top + i * row
        out.append(f'<text x="{left - 8}" y="{y + 17}" font-size="11" text-anchor="end" fill="{INK}">{esc(task)}</text>')
        x0, x1 = x(s), max(x(e), x(s) + 4)
        out.append(f'<rect x="{x0:.1f}" y="{y + 6}" width="{x1 - x0:.1f}" height="15" rx="3" fill="{COLORS[grp]}"/>')
    # legend
    lx = left
    for grp, col in COLORS.items():
        out.append(f'<rect x="{lx}" y="{h - 22}" width="12" height="12" rx="2" fill="{col}"/>'
                   f'<text x="{lx + 17}" y="{h - 12}" font-size="11" fill="{INK}">{grp}</text>')
        lx += 90
    out.append("</svg>")
    return "".join(out)


# ---------------------------------------------------------------- Kanban board

BOARD = {
    "ต้องทำ (To do)": [
        ("โปรแกรม", "ด่านถัดไป: Drowned Cathedral"),
        ("ภาพ", "วาดตัวละครอื่นใหม่ในสไตล์พิกเซล"),
        ("โปรแกรม", "ปรับสมดุล Naomi หลัง playtest"),
        ("เสียง", "เพลงประกอบเฉพาะของแต่ละด่าน"),
    ],
    "กำลังทำ (Doing)": [
        ("ภาพ", "ท่าโจมตีของ Relle และ Naomi"),
        ("ภาพ", "พื้นหลังฉากต่อสู้ของ Fallen Academy"),
        ("ทดสอบ", "Playtest ครั้งที่ 2: บทนำจนถึง Wyvern"),
    ],
    "เสร็จแล้ว (Done)": [
        ("โปรแกรม", "บทนำ 5 บท และ Tutorial การต่อสู้"),
        ("โปรแกรม", "บทสนทนาแบบ Visual Novel"),
        ("โปรแกรม", "แผนที่ Tiled: ชน, จุดโต้ตอบ, NPC"),
        ("ภาพ", "พอร์ตเทรต Relle และ Naomi"),
        ("ภาพ", "ฉาก Airlift และพื้นหลังบอส (Blender)"),
        ("เสียง", "เพลง 7 เพลง และเสียงประกอบ 34 ชุด"),
        ("โปรแกรม", "Naomi เป็นตัวละครที่เล่นได้"),
        ("ทดสอบ", "Playtest ครั้งที่ 1 และนำผลมาปรับ UI"),
    ],
}


def kanban():
    col_w, card_h, gap, top = 240, 40, 8, 44
    rows = max(len(v) for v in BOARD.values())
    h = top + rows * (card_h + gap) + 16
    out = [f'<svg viewBox="0 0 {3 * col_w + 40} {h}" xmlns="http://www.w3.org/2000/svg" font-family="{FONT}">']
    for c, (title, cards) in enumerate(BOARD.items()):
        x0 = 10 + c * (col_w + 10)
        out.append(f'<rect x="{x0}" y="4" width="{col_w}" height="{h - 8}" rx="6" fill="#f4f4f6" stroke="#ddd"/>')
        out.append(f'<text x="{x0 + 12}" y="28" font-size="13" font-weight="700" fill="{INK}">{esc(title)}  ({len(cards)})</text>')
        for i, (grp, text) in enumerate(cards):
            y = top + i * (card_h + gap)
            out.append(f'<rect x="{x0 + 8}" y="{y}" width="{col_w - 16}" height="{card_h}" rx="4" fill="#fff" stroke="#ccc"/>')
            out.append(f'<rect x="{x0 + 8}" y="{y}" width="5" height="{card_h}" rx="2" fill="{COLORS[grp]}"/>')
            out.append(f'<text x="{x0 + 20}" y="{y + 16}" font-size="9.5" fill="{GREY}">{esc(grp)}</text>')
            out.append(f'<text x="{x0 + 20}" y="{y + 31}" font-size="10.5" fill="{INK}">{esc(text)}</text>')
    out.append("</svg>")
    return "".join(out)


# ---------------------------------------------------------------- git history

ALIASES = {"jirayu phothong": "Jirayu Phothong", "PongsapakThai": "Pongsapak Thai", "LittleOnyx": "Onyx"}


def git(*args):
    return subprocess.run(["git", "-C", REPO, *args], capture_output=True, text=True, encoding="utf-8").stdout


def commits():
    rows = []
    for line in git("log", "--all", "--format=%ad|%an", "--date=short").splitlines():
        day, who = line.split("|", 1)
        rows.append((dt.date.fromisoformat(day), ALIASES.get(who, who)))
    return rows


def commit_chart():
    rows = commits()
    weeks = defaultdict(Counter)
    for day, who in rows:
        monday = day - dt.timedelta(days=day.weekday())
        weeks[monday][who] += 1
    authors = [a for a, _ in Counter(w for _, w in rows).most_common()]
    palette = ["#c2185b", "#5c6bc0", "#00897b", "#f9a825", "#6d4c41", "#8e24aa", "#546e7a"]
    color = {a: palette[i % len(palette)] for i, a in enumerate(authors)}
    keys = sorted(weeks)
    peak = max(sum(c.values()) for c in weeks.values())
    left, top, bw, gap, ch = 40, 16, 42, 14, 200
    w = left + len(keys) * (bw + gap) + 20
    out = [f'<svg viewBox="0 0 {max(w, 720)} {top + ch + 90}" xmlns="http://www.w3.org/2000/svg" font-family="{FONT}">']
    for v in range(0, peak + 1, 5):
        y = top + ch - v / peak * ch
        out.append(f'<line x1="{left}" y1="{y:.1f}" x2="{w}" y2="{y:.1f}" stroke="#eee"/><text x="{left - 6}" y="{y + 4:.1f}" font-size="10" text-anchor="end" fill="{GREY}">{v}</text>')
    for i, k in enumerate(keys):
        x = left + 10 + i * (bw + gap)
        y = top + ch
        for a in authors:
            n = weeks[k][a]
            if not n:
                continue
            hh = n / peak * ch
            y -= hh
            out.append(f'<rect x="{x}" y="{y:.1f}" width="{bw}" height="{hh:.1f}" fill="{color[a]}"/>')
        out.append(f'<text x="{x + bw / 2}" y="{top + ch + 15}" font-size="10" text-anchor="middle" fill="{INK}">{k.day}/{k.month}</text>')
        out.append(f'<text x="{x + bw / 2}" y="{y - 4:.1f}" font-size="10" text-anchor="middle" fill="{INK}">{sum(weeks[k].values())}</text>')
    lx, ly = left, top + ch + 40
    for a in authors:
        out.append(f'<rect x="{lx}" y="{ly}" width="11" height="11" fill="{color[a]}"/><text x="{lx + 15}" y="{ly + 10}" font-size="10.5" fill="{INK}">{esc(a)}</text>')
        lx += 7 * len(a) + 40
        if lx > 640:
            lx, ly = left, ly + 18
    out.append("</svg>")
    return "".join(out)


def contributors_rows():
    c = Counter(w for _, w in commits())
    return "".join(f"<tr><td>{esc(a)}</td><td style='text-align:center'>{n}</td></tr>" for a, n in c.most_common())


def branches():
    names = sorted({b.strip().replace("remotes/origin/", "") for b in git("branch", "-a").splitlines()
                    if "HEAD" not in b} - {"* dev"})
    names = sorted({n.lstrip("* ") for n in names})
    main_y, dev_y = 40, 100
    out = [f'<svg viewBox="0 0 760 {150 + 26 * len(names)}" xmlns="http://www.w3.org/2000/svg" font-family="{FONT}">']
    out.append(f'<line x1="40" y1="{main_y}" x2="720" y2="{main_y}" stroke="{INK}" stroke-width="4"/><text x="40" y="{main_y - 10}" font-size="12" font-weight="700">main</text>')
    out.append(f'<line x1="80" y1="{dev_y}" x2="720" y2="{dev_y}" stroke="{ROSE}" stroke-width="4"/><text x="80" y="{dev_y - 10}" font-size="12" font-weight="700" fill="{ROSE}">dev</text>')
    out.append(f'<path d="M60 {main_y} C 70 {main_y} 70 {dev_y} 80 {dev_y}" stroke="{ROSE}" stroke-width="3" fill="none"/>')
    out.append(f'<circle cx="700" cy="{dev_y}" r="7" fill="{ROSE}"/><text x="690" y="{dev_y + 24}" font-size="11" text-anchor="end" fill="{ROSE}">tag v0.1.3</text>')
    y = dev_y + 40
    feats = [n for n in names if n not in ("main", "dev")]
    for i, n in enumerate(feats):
        x0 = 120 + i * 18
        out.append(f'<path d="M{x0} {dev_y} L{x0} {y + i * 26}" stroke="#bbb" stroke-width="2"/>'
                   f'<line x1="{x0}" y1="{y + i * 26}" x2="{x0 + 170}" y2="{y + i * 26}" stroke="#5c6bc0" stroke-width="3"/>'
                   f'<text x="{x0 + 180}" y="{y + i * 26 + 4}" font-size="11" fill="{INK}">{esc(n)}</text>')
    out.append("</svg>")
    return "".join(out)


def file_tree():
    rows = [
        ("morthodkai-inc-aac-rip-off-/", 0, "รากของ repository"),
        ("README.md", 1, "วิธี build / run / เล่น"),
        ("[MorThodKaiInc] - game/docs/", 1, "GDD, backlog, sprint plan, meeting notes"),
        ("[MorThodKaiInc] - game/game/Code/Game/SERAAC/", 1, "โปรเจกต์เกม (C#)"),
        ("Art/", 2, "ภาพ แผนที่ Tiled สไปรต์ พื้นหลัง (โหลดตอนรัน)"),
        ("Audio/", 2, "เพลงและเสียงประกอบ"),
        ("Content/", 2, "ฟอนต์ (MonoGame Content Pipeline)"),
        ("Combat/  Core/  Data/  Screens/  Story/  UI/  World/", 2, "ซอร์สโค้ด"),
        ("Docs/", 2, "รายงาน แบบทดสอบ Balance sheet"),
    ]
    h = 20 + 26 * len(rows)
    out = [f'<svg viewBox="0 0 760 {h}" xmlns="http://www.w3.org/2000/svg" font-family="{FONT}">']
    for i, (name, depth, note) in enumerate(rows):
        y = 20 + i * 26
        x = 20 + depth * 26
        if depth:
            out.append(f'<path d="M{x - 14} {y - 18} L{x - 14} {y - 4} L{x - 4} {y - 4}" stroke="#aaa" fill="none"/>')
        out.append(f'<text x="{x}" y="{y}" font-size="12" font-family="Consolas, monospace" fill="{INK}">{esc(name)}</text>')
        out.append(f'<text x="470" y="{y}" font-size="11.5" fill="{GREY}">{esc(note)}</text>')
    out.append("</svg>")
    return "".join(out)


def render_all():
    return {
        "gantt": gantt(),
        "kanban": kanban(),
        "commits": commit_chart(),
        "branches": branches(),
        "filetree": file_tree(),
        "portraits": img("art_portraits.jpg", "88%"),
        "sprites": img("art_sprites.jpg"),
        "enemies": img("art_enemies.jpg"),
        "stages": pair("art_stage_city.jpg", "art_stage_academy.jpg"),
        "mainmenu": img("art_mainmenu.jpg"),
        "blender": img("art_blender.jpg", "80%"),
        "maps": pair("map_dorm.jpg", "map_roof.jpg"),
        "shots_story": pair("shot_vn.jpg", "shot_dorm.jpg") + '<div style="height:3mm"></div>' + pair("shot_spar.jpg", "shot_airlift.jpg"),
        "shots_game": pair("shot_hub.jpg", "shot_loading.jpg") + '<div style="height:3mm"></div>' + pair("shot_explore.jpg", "shot_wyvern.jpg"),
        "discord_channels": img("discord_channels.png", "92%"),
        "discord_files": img("discord_feedback.png", "85%") + '<div style="height:3mm"></div>' + img("discord_files.png", "62%"),
        "github_release": img("github_tag.png", "55%"),
    }
