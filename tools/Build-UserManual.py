from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    KeepTogether,
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
)


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "QHH-Desktop-Storage-Box-User-Manual.md"
OUTPUT = ROOT / "docs" / "QHH-Desktop-Storage-Box-User-Manual.pdf"
FONT_PATH = Path(r"C:\Windows\Fonts\simhei.ttf")


def register_font() -> None:
    if not FONT_PATH.exists():
        raise FileNotFoundError(f"Missing Chinese font: {FONT_PATH}")
    pdfmetrics.registerFont(TTFont("QHHChinese", str(FONT_PATH)))


def build_styles():
    base = getSampleStyleSheet()
    return {
        "title": ParagraphStyle(
            "QHHTitle",
            parent=base["Title"],
            fontName="QHHChinese",
            fontSize=27,
            leading=34,
            textColor=colors.HexColor("#17202A"),
            alignment=TA_CENTER,
            spaceAfter=8 * mm,
        ),
        "subtitle": ParagraphStyle(
            "QHHSubtitle",
            parent=base["Normal"],
            fontName="QHHChinese",
            fontSize=11,
            leading=18,
            textColor=colors.HexColor("#5C6875"),
            alignment=TA_CENTER,
            spaceAfter=4 * mm,
        ),
        "h1": ParagraphStyle(
            "QHHH1",
            parent=base["Heading1"],
            fontName="QHHChinese",
            fontSize=18,
            leading=25,
            textColor=colors.HexColor("#1677FF"),
            spaceBefore=5 * mm,
            spaceAfter=3 * mm,
        ),
        "h2": ParagraphStyle(
            "QHHH2",
            parent=base["Heading2"],
            fontName="QHHChinese",
            fontSize=13,
            leading=19,
            textColor=colors.HexColor("#263442"),
            spaceBefore=4 * mm,
            spaceAfter=2 * mm,
        ),
        "body": ParagraphStyle(
            "QHHBody",
            parent=base["BodyText"],
            fontName="QHHChinese",
            fontSize=10,
            leading=16,
            textColor=colors.HexColor("#263442"),
            alignment=TA_LEFT,
            spaceAfter=2.5 * mm,
        ),
        "bullet": ParagraphStyle(
            "QHHBullet",
            parent=base["BodyText"],
            fontName="QHHChinese",
            fontSize=10,
            leading=16,
            leftIndent=6 * mm,
            firstLineIndent=-3 * mm,
            textColor=colors.HexColor("#263442"),
            spaceAfter=1.4 * mm,
        ),
        "code": ParagraphStyle(
            "QHHCode",
            parent=base["Code"],
            fontName="QHHChinese",
            fontSize=9,
            leading=14,
            leftIndent=4 * mm,
            rightIndent=4 * mm,
            borderColor=colors.HexColor("#D3DBE4"),
            borderWidth=0.7,
            borderPadding=4,
            backColor=colors.HexColor("#F3F5F8"),
            textColor=colors.HexColor("#263442"),
            spaceBefore=1.5 * mm,
            spaceAfter=3 * mm,
        ),
    }


def add_page_number(canvas, document) -> None:
    canvas.saveState()
    canvas.setStrokeColor(colors.HexColor("#D3DBE4"))
    canvas.line(18 * mm, 15 * mm, A4[0] - 18 * mm, 15 * mm)
    canvas.setFont("QHHChinese", 8.5)
    canvas.setFillColor(colors.HexColor("#718096"))
    canvas.drawString(18 * mm, 10 * mm, "QHH Desktop Storage Box 用户说明书")
    canvas.drawRightString(A4[0] - 18 * mm, 10 * mm, f"{document.page}")
    canvas.restoreState()


def parse_markdown(styles):
    story = []
    code_lines = []
    in_code = False

    def flush_code():
        nonlocal code_lines
        if code_lines:
            code = "<br/>".join(escape(line) for line in code_lines)
            story.append(Paragraph(code, styles["code"]))
            code_lines = []

    for raw_line in SOURCE.read_text(encoding="utf-8").splitlines():
        line = raw_line.rstrip()
        if line.startswith("```"):
            if in_code:
                flush_code()
                in_code = False
            else:
                flush_code()
                in_code = True
            continue

        if in_code:
            code_lines.append(line)
            continue

        if not line:
            continue

        if line.startswith("# "):
            story.append(Spacer(1, 24 * mm))
            story.append(Paragraph(escape(line[2:]), styles["title"]))
            story.append(Spacer(1, 8 * mm))
            continue

        if line.startswith("## "):
            story.append(Paragraph(escape(line[3:]), styles["h1"]))
            continue

        if line.startswith("### "):
            story.append(Paragraph(escape(line[4:]), styles["h2"]))
            continue

        if line.startswith("- "):
            story.append(Paragraph("• " + escape(line[2:]), styles["bullet"]))
            continue

        if line.startswith("版本：") or line.startswith("适用平台："):
            story.append(Paragraph(escape(line), styles["subtitle"]))
            continue

        story.append(Paragraph(escape(line), styles["body"]))

    flush_code()
    return story


def main() -> None:
    register_font()
    styles = build_styles()
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    document = SimpleDocTemplate(
        str(OUTPUT),
        pagesize=A4,
        rightMargin=18 * mm,
        leftMargin=18 * mm,
        topMargin=18 * mm,
        bottomMargin=22 * mm,
        title="QHH Desktop Storage Box 用户说明书",
        author="QHH",
    )
    story = parse_markdown(styles)
    document.build(story, onFirstPage=add_page_number, onLaterPages=add_page_number)
    print(OUTPUT)


if __name__ == "__main__":
    main()
