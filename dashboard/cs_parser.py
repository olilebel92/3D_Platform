"""
Parse C# enum declarations from Unity script files.
Returns {EnumName: {int_index: "ValueName"}} dicts.
"""

import re
from pathlib import Path


def _strip_comments(text: str) -> str:
    """Remove // line comments and /* block */ comments."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def _parse_enum_body(body: str) -> dict[int, str]:
    """Parse the comma-separated body of an enum block into {index: name}."""
    result: dict[int, str] = {}
    current = 0
    for raw in body.split(","):
        entry = raw.strip()
        if not entry:
            continue
        if "=" in entry:
            name, _, val_str = entry.partition("=")
            name = name.strip()
            try:
                current = int(val_str.strip())
            except ValueError:
                pass
        else:
            name = entry.strip()
        if name:
            result[current] = name
            current += 1
    return result


def parse_cs_file(path: Path) -> dict[str, dict[int, str]]:
    """
    Parse all public enum declarations from a C# file.
    Returns {EnumName: {0: "First", 1: "Second", ...}}.
    """
    try:
        raw = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return {}

    clean = _strip_comments(raw)
    enums: dict[str, dict[int, str]] = {}

    # Match: (optional modifiers) enum EnumName { body }
    for m in re.finditer(
        r"\benum\s+(\w+)\s*\{([^}]*)\}", clean, flags=re.DOTALL
    ):
        name = m.group(1)
        body = m.group(2)
        enums[name] = _parse_enum_body(body)

    return enums
