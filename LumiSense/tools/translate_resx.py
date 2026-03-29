#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


@dataclass(frozen=True)
class ResxEntry:
    name: str
    value: str
    comment: str | None


def _parse_resx(path: Path) -> tuple[list[ET.Element], list[ResxEntry]]:
    tree = ET.parse(path)
    root = tree.getroot()

    # Preserve headers (resheader nodes) exactly as-is.
    headers = list(root.findall("resheader"))

    entries: list[ResxEntry] = []
    for data in root.findall("data"):
        name = data.get("name")
        if not name:
            continue
        value_el = data.find("value")
        value = (value_el.text or "") if value_el is not None else ""
        comment_el = data.find("comment")
        comment = (comment_el.text or "") if comment_el is not None else None
        entries.append(ResxEntry(name=name, value=value, comment=comment))

    return headers, entries


def _ensure_argos_model(from_code: str, to_code: str) -> None:
    try:
        from argostranslate import package  # type: ignore
    except Exception as e:  # pragma: no cover
        raise RuntimeError(
            "argostranslate is not installed. Run: pip install -r tools/requirements.txt"
        ) from e

    package.update_package_index()
    available = package.get_available_packages()
    match = next(
        (p for p in available if p.from_code == from_code and p.to_code == to_code),
        None,
    )
    if match is None:
        raise RuntimeError(
            f"No Argos translation package available for {from_code}->{to_code}. "
            "You may need a different provider for that pair."
        )

    # If not already installed, download+install it.
    installed = {(p.from_code, p.to_code) for p in package.get_installed_packages()}
    if (from_code, to_code) in installed:
        return

    download_path = match.download()
    package.install_from_path(download_path)


def _translate_text(text: str, from_code: str, to_code: str) -> str:
    if not text.strip():
        return text

    from argostranslate import translate  # type: ignore

    return translate.translate(text, from_code, to_code)


def _write_resx(
    out_path: Path,
    headers: list[ET.Element],
    entries: Iterable[ResxEntry],
) -> None:
    root = ET.Element("root")
    for header in headers:
        root.append(_clone_element(header))

    for entry in entries:
        data_el = ET.SubElement(root, "data", {"name": entry.name, "xml:space": "preserve"})
        value_el = ET.SubElement(data_el, "value")
        value_el.text = entry.value
        if entry.comment is not None and entry.comment.strip():
            comment_el = ET.SubElement(data_el, "comment")
            comment_el.text = entry.comment

    _indent_xml(root)
    tree = ET.ElementTree(root)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    tree.write(out_path, encoding="utf-8", xml_declaration=True)


def _clone_element(el: ET.Element) -> ET.Element:
    new_el = ET.Element(el.tag, el.attrib)
    new_el.text = el.text
    new_el.tail = el.tail
    for child in list(el):
        new_el.append(_clone_element(child))
    return new_el


def _indent_xml(elem: ET.Element, level: int = 0) -> None:
    i = "\n" + level * "  "
    if len(elem):
        if not elem.text or not elem.text.strip():
            elem.text = i + "  "
        for child in elem:
            _indent_xml(child, level + 1)
        if not elem.tail or not elem.tail.strip():
            elem.tail = i
    else:
        if level and (not elem.tail or not elem.tail.strip()):
            elem.tail = i


def _default_out_path(input_path: Path, culture: str, out_dir: Path | None) -> Path:
    # SharedResource.resx -> SharedResource.tr.resx
    stem = input_path.stem  # "SharedResource" for "SharedResource.resx"
    filename = f"{stem}.{culture}{input_path.suffix}"
    return (out_dir or input_path.parent) / filename


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Translate a .resx file into multiple cultures (generates SharedResource.<culture>.resx)."
    )
    parser.add_argument("--input", required=True, help="Path to source .resx file (e.g. Resources/SharedResource.resx).")
    parser.add_argument("--from", dest="from_code", default="en", help="Source language code (default: en).")
    parser.add_argument("--to", dest="to_codes", nargs="+", default=["tr", "bg", "ar"], help="Target language codes.")
    parser.add_argument("--out-dir", default=None, help="Optional output directory. Defaults to input file directory.")
    parser.add_argument("--overwrite", action="store_true", help="Overwrite existing translated .resx files.")
    args = parser.parse_args(argv)

    input_path = Path(args.input).resolve()
    if not input_path.exists():
        print(f"Input file not found: {input_path}", file=sys.stderr)
        return 2

    out_dir = Path(args.out_dir).resolve() if args.out_dir else None

    headers, entries = _parse_resx(input_path)

    for to_code in args.to_codes:
        out_path = _default_out_path(input_path, to_code, out_dir)
        if out_path.exists() and not args.overwrite:
            print(f"Skip (exists): {out_path}")
            continue

        _ensure_argos_model(args.from_code, to_code)

        translated_entries: list[ResxEntry] = []
        for e in entries:
            translated_value = _translate_text(e.value, args.from_code, to_code)
            translated_entries.append(ResxEntry(name=e.name, value=translated_value, comment=e.comment))

        _write_resx(out_path, headers=headers, entries=translated_entries)
        print(f"Wrote: {out_path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

