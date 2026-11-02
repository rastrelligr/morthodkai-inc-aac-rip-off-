# Builds the data-structure report: content_<lang>.html + diagrams -> HTML -> PDF (Edge headless).
# Two passes: the first render finds each heading's page (hidden markers), the second fills the TOC.
#   python build.py th      python build.py en      python build.py all
import html
import os
import re
import subprocess
import sys

import pypdf

import diagrams

HERE = os.path.dirname(os.path.abspath(__file__))
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

L = {
    "th": dict(fig="ภาพที่", tab="ตารางที่", toc="สารบัญ", lof="สารบัญภาพ", lot="สารบัญตาราง", page="หน้า",
               out="SERAAC_DataStructures_Report_TH"),
    "en": dict(fig="Figure", tab="Table", toc="Table of Contents", lof="List of Figures", lot="List of Tables", page="Page",
               out="SERAAC_DataStructures_Report_EN"),
}

CSS = r"""
@import url('https://fonts.googleapis.com/css2?family=Sarabun:ital,wght@0,400;0,600;0,700;1,400&display=block');
@page { size: A4; margin: 25mm 22mm 22mm 28mm;
  @top-right { content: counter(page); font: 13pt 'Sarabun', 'Leelawadee UI', sans-serif; color: #333; } }
@page cover { @top-right { content: none; } }
:root { --ink:#111; --acc:#333; --mute:#666; }
html { font-family: 'Sarabun', 'Leelawadee UI', 'Tahoma', sans-serif; font-size: 13.5pt; line-height: 1.62; color: #111; }
body { margin: 0; background: #fff; }
p { margin: 0 0 .6em; text-align: justify; text-indent: 2.2em; }
p.ni, li p, td p, .cap p { text-indent: 0; }
h1, h2, h3 { font-weight: 700; color: var(--ink); break-after: avoid; }
h1.chap { break-before: page; text-align: center; font-size: 20pt; margin: 0 0 1.2em; line-height: 1.5; }
h1.chap small { display:block; font-size: 17pt; color: var(--ink); font-weight: 600; }
h1.front { break-before: page; text-align: center; font-size: 19pt; margin: 0 0 1em; }
h2 { font-size: 15.5pt; margin: 1.1em 0 .4em; }
h3 { font-size: 13.8pt; margin: .9em 0 .3em; color: var(--ink); }
ul, ol { margin: .2em 0 .7em; padding-left: 2.4em; }
li { margin: .15em 0; }
code, pre { font-family: Consolas, 'Cascadia Mono', monospace; }
code { font-size: 10.5pt; color: #222; }
pre { font-size: 9.2pt; line-height: 1.42; background: #f7f8fb; border: 1px solid #dfe3ea; border-left: 4px solid #8a1c3c;
      padding: 8px 10px; margin: .5em 0 .4em; white-space: pre-wrap; break-inside: avoid; }
pre .c { color: #6a7a5a; font-style: italic; }
pre .k { color: #1f4f9a; font-weight: 600; }
.codecap { font-size: 11pt; color: var(--mute); margin: 0 0 .9em; text-align: left; text-indent: 0; }
figure { margin: .8em 0 1em; text-align: center; break-inside: avoid; }
figure svg { width: 100%; height: auto; max-height: 205mm; }
figcaption, .tcap { font-size: 12.5pt; text-align: center; margin-top: .3em; text-indent: 0; }
figcaption b, .tcap b { color: var(--ink); }
table { border-collapse: collapse; width: 100%; margin: .3em 0 1em; font-size: 11.5pt; line-height: 1.4; break-inside: auto; }
tr { break-inside: avoid; }
th, td { border: 1px solid #999; padding: 4px 7px; vertical-align: top; text-align: left; }
th { background: #eee; color: #000; }
td code { font-size: 9.8pt; }
.tcap { break-after: avoid; margin-bottom: .25em; }
.note { border: 1px solid #bbb; background: #f6f6f6; padding: 8px 12px; margin: .6em 0 1em; break-inside: avoid; }
.note p { text-indent: 0; margin: 0 0 .3em; }
.mk { position: absolute; font-size: 1px; color: #fff; line-height: 1px; }
h1, h2, figcaption, .tcap { position: relative; }
/* cover */
.cover { page: cover; height: 245mm; display: flex; flex-direction: column; justify-content: space-between; text-align: center; }
.cover .t1 { font-size: 20pt; font-weight: 700; color: #000; line-height: 1.55; margin-top: 8mm; }
.cover .t2 { font-size: 15pt; color: #333; margin-top: 4mm; }
.cover .who { font-size: 14pt; line-height: 1.8; }
.cover .logo { width: 32mm; height: 32mm; margin: 0 auto 6mm; display: block; }
/* toc */
.toc { list-style: none; padding: 0; margin: 0; }
.toc li { display: flex; align-items: baseline; margin: .12em 0; }
.toc li .t { flex: 0 1 auto; }
.toc li .d { flex: 1 1 auto; border-bottom: 1.5px dotted #888; margin: 0 6px; transform: translateY(-4px); min-width: 20px; }
.toc li .n { flex: 0 0 auto; min-width: 1.6em; text-align: right; }
.toc li.l1 { font-weight: 700; margin-top: .45em; }
.toc li.l2 { padding-left: 1.8em; }
.tochead { display: flex; justify-content: space-between; font-weight: 700; border-bottom: 1px solid #999; margin-bottom: .3em; }
.unused-appendix-title { break-before: page; display: flex; align-items: center; justify-content: center; height: 220mm; font-size: 24pt; font-weight: 700; color: var(--ink); }
.ref p { text-indent: -2.2em; padding-left: 2.2em; text-align: left; }
"""

def _logo():
    import base64
    with open(os.path.join(HERE, "cmu_logo.png"), "rb") as f:
        return '<img class="logo" src="data:image/png;base64,' + base64.b64encode(f.read()).decode() + '">'


LOGO = _logo()

KW = r"\b(public|private|static|readonly|new|var|foreach|while|for|if|else|return|void|int|bool|string|byte|class|struct|sealed|out|in|null|true|false|switch|continue|break)\b"


def highlight(code):
    code = html.escape(code)
    out = []
    for line in code.split("\n"):
        m = line.find("//")
        head, tail = (line[:m], line[m:]) if m >= 0 else (line, "")
        head = re.sub(KW, r'<span class="k">\1</span>', head)
        out.append(head + (f'<span class="c">{tail}</span>' if tail else ""))
    return "\n".join(out)


def build_html(lang, pages=None):
    lab = L[lang]
    src = open(os.path.join(HERE, f"content_{lang}.html"), encoding="utf-8").read()
    figs = diagrams.render_all()
    src = src.replace("{{PATH_LEN}}", str(figs["_path_len"])).replace("{{REACH}}", str(figs["_reach"]))
    # `inline code` shorthand (escaped), outside {{CODE}} blocks only
    parts = re.split(r"(\{\{CODE\|.*?\{\{/CODE\}\})", src, flags=re.S)
    src = "".join(p if p.startswith("{{CODE|") else re.sub(r"`([^`]+)`", lambda m: "<code>" + html.escape(m.group(1)) + "</code>", p)
                  for p in parts)

    heads, figlist, tablist = [], [], []
    chap = {"n": "0", "f": 0, "t": 0}
    counter = [0]

    def mark():
        counter[0] += 1
        return f"M{counter[0]:03d}"

    # Walk the document in order: chapter headings reset figure/table numbering.
    token = re.compile(r'<h1 class="(chap|front|appx)"([^>]*)>(.*?)</h1>|<h2>(.*?)</h2>|\{\{FIG:(\w+)\|(.*?)\}\}|\{\{TAB\|(.*?)\}\}|\{\{CODE\|(.*?)\}\}\n(.*?)\n\{\{/CODE\}\}', re.S)

    def repl(m):
        if m.group(1):
            kind, attrs, inner = m.group(1), m.group(2), m.group(3)
            num = re.search(r'data-n="([^"]+)"', attrs)
            toc_title = re.search(r'data-toc="([^"]+)"', attrs)
            if num: chap.update(n=num.group(1), f=0, t=0)
            mk = mark()
            title = toc_title.group(1) if toc_title else re.sub("<.*?>", " ", inner).strip()
            if kind != "appx" or True:
                heads.append((1, title, mk))
            cls = "chap" if kind in ("chap", "appx") else "front"
            return f'<h1 class="{cls}"><span class="mk">#{mk}#</span>{inner}</h1>'
        if m.group(4) is not None:
            mk = mark()
            heads.append((2, re.sub("<.*?>", "", m.group(4)), mk))
            return f'<h2><span class="mk">#{mk}#</span>{m.group(4)}</h2>'
        if m.group(5):
            chap["f"] += 1
            n = f'{chap["n"]}.{chap["f"]}'
            mk = mark()
            figlist.append((f'{lab["fig"]} {n} {m.group(6)}', mk))
            return (f'<figure>{figs[m.group(5)]}<figcaption><span class="mk">#{mk}#</span><b>{lab["fig"]} {n}</b> {m.group(6)}</figcaption></figure>')
        if m.group(7):
            chap["t"] += 1
            n = f'{chap["n"]}.{chap["t"]}'
            mk = mark()
            tablist.append((f'{lab["tab"]} {n} {m.group(7)}', mk))
            return f'<div class="tcap"><span class="mk">#{mk}#</span><b>{lab["tab"]} {n}</b> {m.group(7)}</div>'
        if m.group(8):
            long = m.group(9).count("\n") > 30   # long listings may split across pages
            pre = '<pre style="break-inside:auto">' if long else '<pre>'
            return f'{pre}{highlight(m.group(9))}</pre><p class="codecap">{m.group(8)}</p>'
        return m.group(0)

    body = token.sub(repl, src)

    def pg(mk):
        return str(pages.get(mk, "")) if pages else "0"

    def toc_list(items, levels=True):
        rows = []
        for it in items:
            lvl, title, mk = it if levels else (2, it[0], it[1])
            cls = "l1" if lvl == 1 and levels else ("l2" if levels else "")
            rows.append(f'<li class="{cls}"><span class="t">{title}</span><span class="d"></span><span class="n">{pg(mk)}</span></li>')
        return '<ul class="toc">' + "".join(rows) + "</ul>"

    toc_head = f'<div class="tochead"><span></span><span>{lab["page"]}</span></div>'
    body = body.replace("{{TOC}}", toc_head + toc_list(heads))
    body = body.replace("{{LOF}}", toc_head + toc_list(figlist, False))
    body = body.replace("{{LOT}}", toc_head + toc_list(tablist, False))
    body = body.replace("{{LOGO}}", LOGO)
    return f'<!doctype html><html lang="{lang}"><head><meta charset="utf-8"><title>SERAAC Data Structures</title><style>{CSS}</style></head><body>{body}</body></html>'


def render(html_path, pdf_path):
    if os.path.exists(pdf_path): os.remove(pdf_path)
    subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--no-pdf-header-footer", "--run-all-compositor-stages-before-draw",
                    "--virtual-time-budget=20000", f"--print-to-pdf={pdf_path}", "file:///" + html_path.replace("\\", "/")],
                   check=True, capture_output=True, timeout=180)


def find_pages(pdf_path):
    pages = {}
    for i, p in enumerate(pypdf.PdfReader(pdf_path).pages, 1):
        for mk in re.findall(r"#(M\d{3})#", (p.extract_text() or "").replace(" ", "")):
            pages.setdefault(mk, i)
    return pages


def clean_bottom_margin(pdf):
    # Chromium leaves zero-width notdef glyphs in the (empty) bottom margin before some page breaks; paint over them.
    import pymupdf
    doc = pymupdf.open(pdf)
    for p in doc:
        for t in p.get_texttrace():
            x0, y0, x1, y1 = t["bbox"]
            if y0 > p.rect.height - 70 and all(c[0] == 0xFFFD for c in t["chars"]):
                p.draw_rect(pymupdf.Rect(x0 - 6, y0 - 16, x1 + 6, y1), color=None, fill=(1, 1, 1), overlay=True)
    doc.save(pdf + ".tmp", deflate=True)
    doc.close()
    os.replace(pdf + ".tmp", pdf)


def build(lang):
    out = L[lang]["out"]
    tmp = os.path.join(HERE, "_build")
    os.makedirs(tmp, exist_ok=True)
    hp = os.path.join(tmp, out + ".html")
    pdf = os.path.join(HERE, out + ".pdf")
    pages = None
    for _ in range(2):
        open(hp, "w", encoding="utf-8").write(build_html(lang, pages))
        render(hp, pdf)
        pages = find_pages(pdf)
    clean_bottom_margin(pdf)
    total = len(pypdf.PdfReader(pdf).pages)
    print(f"{lang}: {pdf}  ({total} pages, {len(pages)} markers found)")


if __name__ == "__main__":
    for lg in (["th", "en"] if sys.argv[1:] in ([], ["all"]) else sys.argv[1:]):
        build(lg)
