"""
Convert Unity-flavored PGN files (with TextMeshPro rich-text tags) into clean
standard PGN that chess.com / lichess analysis boards accept.

Reads every *.pgn under ../arena/Games/ and writes a stripped copy with the
same filename into ../arena/pgn_raw/.
"""

from __future__ import annotations

import re
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
ARENA_DIR = SCRIPT_DIR.parent / "arena"
GAMES_DIR = ARENA_DIR / "Games"
OUT_DIR = ARENA_DIR / "pgn_raw"

TAG_RE = re.compile(r"<[^>]+>")
MOVENUM_RE = re.compile(r"\s+(\d+)\.\s+")


def strip_richtext(text: str) -> str:
    return TAG_RE.sub("", text)


def format_pgn(raw: str) -> str:
    lines = raw.splitlines()
    headers: list[str] = []
    move_lines: list[str] = []
    result = "*"

    in_movetext = False
    for line in lines:
        stripped = line.strip()
        if not in_movetext:
            if stripped.startswith("[") and stripped.endswith("]"):
                headers.append(stripped)
                if stripped.startswith("[Result "):
                    m = re.search(r'"([^"]+)"', stripped)
                    if m:
                        result = m.group(1)
            elif stripped == "":
                in_movetext = True
            else:
                in_movetext = True
                move_lines.append(strip_richtext(line))
        else:
            move_lines.append(strip_richtext(line))

    movetext = " ".join(part.strip() for part in move_lines if part.strip())
    movetext = re.sub(r"\s+", " ", movetext).strip()

    if not movetext.endswith(result):
        movetext = f"{movetext} {result}".strip()

    wrapped = wrap_movetext(movetext, width=80)

    return "\n".join(headers) + "\n\n" + wrapped + "\n"


def wrap_movetext(text: str, width: int = 80) -> str:
    words = text.split(" ")
    lines: list[str] = []
    current = ""
    for word in words:
        if not current:
            current = word
        elif len(current) + 1 + len(word) <= width:
            current += " " + word
        else:
            lines.append(current)
            current = word
    if current:
        lines.append(current)
    return "\n".join(lines)


def main() -> None:
    if not GAMES_DIR.is_dir():
        raise SystemExit(f"Games directory not found: {GAMES_DIR}")

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    pgn_files = sorted(GAMES_DIR.glob("*.pgn"))
    if not pgn_files:
        print(f"No .pgn files found in {GAMES_DIR}")
        return

    for src in pgn_files:
        raw = src.read_text(encoding="utf-8")
        cleaned = format_pgn(raw)
        dst = OUT_DIR / src.name
        dst.write_text(cleaned, encoding="utf-8")
        print(f"  {src.name} -> {dst.relative_to(ARENA_DIR.parent)}")

    print(f"\nWrote {len(pgn_files)} file(s) to {OUT_DIR}")


if __name__ == "__main__":
    main()
