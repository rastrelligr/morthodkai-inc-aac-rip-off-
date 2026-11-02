# Builds the work-process report: content_th.html + figures -> HTML -> PDF (Edge headless), using the same
# page style and two-pass table of contents as Docs/Report/build.py.
#   python build.py
import os
import re
import sys

import pypdf

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "Report"))
import build as base   # noqa: E402  (Docs/Report/build.py: CSS, Edge render, page finding)
import figures         # noqa: E402

LAB = dict(fig="ภาพที่", tab="ตารางที่", page="หน้า", out="SERAAC_Process_Report_TH")
EXTRA_CSS = """
figure img { max-height: 200mm; object-fit: contain; }
.chip { display:inline-block; text-indent:0; padding:0 6px; border-radius:3px; background:#eee; font-size:11pt; }
"""


def build_html(pages=None):
    src = open(os.path.join(HERE, "content_th.html"), encoding="utf-8").read()
    figs = figures.render_all()
    src = src.replace("{{CONTRIBUTORS}}", figures.contributors_rows())
    src = re.sub(r"`([^`]+)`", lambda m: "<code>" + m.group(1) + "</code>", src)

    heads, figlist, tablist = [], [], []
    chap = {"n": "0", "f": 0, "t": 0}
    counter = [0]

    def mark():
        counter[0] += 1
        return f"M{counter[0]:03d}"

    token = re.compile(r'<h1 class="(chap|front)"([^>]*)>(.*?)</h1>|<h2>(.*?)</h2>|\{\{FIG:(\w+)\|(.*?)\}\}|\{\{TAB\|(.*?)\}\}', re.S)

    def repl(m):
        if m.group(1):
            kind, attrs, inner = m.group(1), m.group(2), m.group(3)
            num = re.search(r'data-n="([^"]+)"', attrs)
            toc_title = re.search(r'data-toc="([^"]+)"', attrs)
            if num:
                chap.update(n=num.group(1), f=0, t=0)
            mk = mark()
            heads.append((1, toc_title.group(1) if toc_title else re.sub("<.*?>", " ", inner).strip(), mk))
            return f'<h1 class="{kind}"><span class="mk">#{mk}#</span>{inner}</h1>'
        if m.group(4) is not None:
            mk = mark()
            heads.append((2, re.sub("<.*?>", "", m.group(4)), mk))
            return f'<h2><span class="mk">#{mk}#</span>{m.group(4)}</h2>'
        if m.group(5):
            chap["f"] += 1
            n = f'{chap["n"]}.{chap["f"]}'
            mk = mark()
            figlist.append((f'{LAB["fig"]} {n} {m.group(6)}', mk))
            return f'<figure>{figs[m.group(5)]}<figcaption><span class="mk">#{mk}#</span><b>{LAB["fig"]} {n}</b> {m.group(6)}</figcaption></figure>'
        chap["t"] += 1
        n = f'{chap["n"]}.{chap["t"]}'
        mk = mark()
        tablist.append((f'{LAB["tab"]} {n} {m.group(7)}', mk))
        return f'<div class="tcap"><span class="mk">#{mk}#</span><b>{LAB["tab"]} {n}</b> {m.group(7)}</div>'

    body = token.sub(repl, src)

    def pg(mk):
        return str(pages.get(mk, "")) if pages else "0"

    def toc(items, levels=True):
        rows = []
        for it in items:
            lvl, title, mk = it if levels else (2, it[0], it[1])
            cls = "l1" if lvl == 1 and levels else ("l2" if levels else "")
            rows.append(f'<li class="{cls}"><span class="t">{title}</span><span class="d"></span><span class="n">{pg(mk)}</span></li>')
        return '<ul class="toc">' + "".join(rows) + "</ul>"

    head = f'<div class="tochead"><span></span><span>{LAB["page"]}</span></div>'
    body = body.replace("{{TOC}}", head + toc(heads)).replace("{{LOF}}", head + toc(figlist, False))
    body = body.replace("{{LOT}}", head + toc(tablist, False)).replace("{{LOGO}}", base.LOGO)
    return (f'<!doctype html><html lang="th"><head><meta charset="utf-8"><title>Aetheris Process Report</title>'
            f'<style>{base.CSS}{EXTRA_CSS}</style></head><body>{body}</body></html>')


def build():
    tmp = os.path.join(HERE, "_build")
    os.makedirs(tmp, exist_ok=True)
    hp = os.path.join(tmp, LAB["out"] + ".html")
    pdf = os.path.join(HERE, LAB["out"] + ".pdf")
    pages = None
    for _ in range(2):
        open(hp, "w", encoding="utf-8").write(build_html(pages))
        base.render(hp, pdf)
        pages = base.find_pages(pdf)
    base.clean_bottom_margin(pdf)
    print(f"{pdf}  ({len(pypdf.PdfReader(pdf).pages)} pages, {len(pages)} markers found)")


if __name__ == "__main__":
    build()
