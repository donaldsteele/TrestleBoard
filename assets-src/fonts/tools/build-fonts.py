#!/usr/bin/env python3
"""Build the bundled font faces described by ``assets-src/fonts/fonts.json`` (PLAN.md M14).

    python -m venv .venv
    .venv/Scripts/pip install -r requirements.txt      # or .venv/bin/pip on Linux/macOS
    .venv/Scripts/python build-fonts.py --write-hashes

Pipeline, per face:  fetch (pinned commit or release tag)  ->  varLib.instancer to a static
instance at the manifest's axis pins  ->  pyftsubset to a fixed unicode range and exactly the
layout features HarfBuzzShaper can enable  ->  pin head.created/head.modified to a fixed epoch
->  verify.  Without the last normalisation every run emits different bytes and the manifest's
hashes would be theatre.

Faces marked ``"frozen": true`` are upstream-verbatim and are never rewritten: subsetting
renumbers glyph ids, WidgetDrawListDump records glyph ids, and re-subsetting the three original
families would re-bake all 42 snapshot baselines and every widget golden. ``--verify`` checks
their bytes against the manifest but leaves them alone.

CI does not run Python. The standing guarantee is the managed test
``tests/Layout.Tests/FontManifestTests`` — this script is how a maintainer regenerates the
inputs that test then polices.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import shutil
import sys
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

FONTS_DIR = Path(__file__).resolve().parent.parent
MANIFEST = FONTS_DIR / "fonts.json"
CACHE = Path(__file__).resolve().parent / ".cache"
UA = {"User-Agent": "TrestleBoard-build-fonts/1.0"}


# ── fetching ────────────────────────────────────────────────────────────────────────────
def _download(url: str) -> bytes:
    request = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def _cached(key: str, url: str) -> bytes:
    CACHE.mkdir(parents=True, exist_ok=True)
    path = CACHE / hashlib.sha256(key.encode("utf-8")).hexdigest()
    if path.exists():
        return path.read_bytes()
    print(f"  fetch {url}")
    data = _download(url)
    path.write_bytes(data)
    return data


def _release_zip(source: dict) -> zipfile.ZipFile:
    owner_repo = source["repo"].removeprefix("https://github.com/")
    url = f"https://github.com/{owner_repo}/releases/download/{source['tag']}/{source['asset']}"
    return zipfile.ZipFile(io.BytesIO(_cached(url, url)))


def fetch_upstream(sources: dict, face: dict) -> bytes:
    """Returns the upstream bytes for one face, from a pinned commit or a pinned release."""
    source = sources[face["source"]]
    if source["kind"] == "git-raw":
        owner_repo = source["repo"].removeprefix("https://github.com/")
        quoted = urllib.parse.quote(face["upstreamPath"])
        url = f"https://raw.githubusercontent.com/{owner_repo}/{source['commit']}/{quoted}"
        return _cached(url, url)

    if source["kind"] == "release-zip":
        archive = _release_zip(source)
        wanted = face["upstreamPath"]
        for name in archive.namelist():
            if name == wanted or name.endswith("/" + wanted):
                return archive.read(name)
        raise KeyError(f"{wanted} not found in {source['asset']}")

    raise ValueError(f"unknown source kind {source['kind']!r}")


def fetch_licence(sources: dict, family: dict) -> bytes | None:
    """Returns the upstream licence text for a family, or None if it is not fetchable."""
    source = sources[family["source"]]
    if source["kind"] != "git-raw" or not family.get("upstreamDir"):
        return None
    owner_repo = source["repo"].removeprefix("https://github.com/")
    name = Path(family["licenceFile"]).name
    url = (f"https://raw.githubusercontent.com/{owner_repo}/{source['commit']}"
           f"/ofl/{family['upstreamDir']}/{name}")
    return _cached(url, url)


# ── building ────────────────────────────────────────────────────────────────────────────
def build_face(raw: bytes, face: dict, subsets: dict, head_epoch: int) -> bytes:
    from fontTools import subset as ft_subset
    from fontTools.ttLib import TTFont
    from fontTools.varLib import instancer

    # recalcTimestamp=False is load-bearing: table__h_e_a_d.compile() otherwise overwrites
    # head.modified with the wall clock on save, silently undoing the pin below.
    font = TTFont(io.BytesIO(raw), recalcTimestamp=False)

    pins = face.get("axisPins")
    if pins:
        if "fvar" not in font:
            raise ValueError(f"{face['file']}: axisPins given but upstream is not variable")
        instancer.instantiateVariableFont(
            font, {k: float(v) for k, v in pins.items()}, inplace=True, updateFontNames=True)
    elif "fvar" in font:
        raise ValueError(f"{face['file']}: upstream is variable but no axisPins given "
                         f"(PLAN.md §1 requires static instances)")

    spec = subsets[face["subset"]]
    options = ft_subset.Options()
    options.layout_features = spec["layoutFeatures"].split(",")
    options.name_IDs = [0, 1, 2, 3, 4, 5, 6]
    options.name_legacy = False
    options.notdef_outline = True
    options.recalc_bounds = True
    options.drop_tables += ["DSIG"]
    unicodes = ft_subset.parse_unicodes(spec["unicodes"])
    subsetter = ft_subset.Subsetter(options=options)
    subsetter.populate(unicodes=unicodes)
    subsetter.subset(font)

    # Without this, fonttools stamps head.modified with the wall clock and no two runs of this
    # script agree byte-for-byte — which would make every sha256 below meaningless.
    font["head"].created = head_epoch
    font["head"].modified = head_epoch

    out = io.BytesIO()
    font.save(out)
    font.close()
    return out.getvalue()


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


# ── licence bundle and docs ─────────────────────────────────────────────────────────────
# The exact composition rules below are mirrored in tests/Layout.Tests/FontManifestTests, which
# recomposes both files from fonts.json and fails if the checked-in copies have drifted. Change
# the format here and that test tells the next maintainer to re-run this script.
RULE = "=" * 78

BUNDLE_PREAMBLE = """TrestleBoard — third-party font licences

TrestleBoard bundles the typefaces listed below and renders with those only. It never
loads a font from your computer, which is what makes a page look the same on every
machine. Each family is used under the licence reproduced in full beneath its heading.
"""


def _upstream_ref(sources: dict, family: dict) -> str:
    source = sources[family["source"]]
    pin = source.get("tag") or source["commit"]
    return f"{source['repo']} @ {pin}"


def compose_licence_bundle(manifest: dict) -> str:
    parts = [BUNDLE_PREAMBLE]
    for family in sorted(manifest["families"], key=lambda f: f["sortOrder"]):
        text = (FONTS_DIR / family["licenceFile"]).read_text(encoding="utf-8-sig")
        parts.append(
            f"{RULE}\n"
            f"{family['family']} — {family['designer']}\n"
            f"{family['licence']}\n"
            f"Upstream: {_upstream_ref(manifest['sources'], family)}\n"
            f"{RULE}\n\n"
            f"{text.replace(chr(13), '').rstrip()}\n")
    return "\n".join(parts)


def compose_fonts_doc(manifest: dict) -> str:
    faces = {}
    for face in manifest["faces"]:
        faces.setdefault(face["family"], []).append(face)
    rows = ["# Bundled fonts",
            "",
            "Generated by `assets-src/fonts/tools/build-fonts.py`; do not edit by hand.",
            "`tests/Layout.Tests/FontManifestTests` fails if this file and",
            "`assets-src/fonts/fonts.json` disagree.",
            "",
            "TrestleBoard renders with these faces and no others — never a font installed on",
            "your computer. That is what makes a page paginate identically on Windows, Linux",
            "and macOS. The full licence text ships with the app: Help → About → *Fonts and",
            "licences*.",
            "",
            "| Family | Faces | Designer | Licence | Upstream |",
            "| --- | --- | --- | --- | --- |"]
    for family in sorted(manifest["families"], key=lambda f: f["sortOrder"]):
        rows.append(f"| {family['family']} | {len(faces[family['family']])} | "
                    f"{family['designer']} | {family['licence']} | "
                    f"{_upstream_ref(manifest['sources'], family)} |")
    rows += ["",
             f"{len(manifest['families'])} families, {len(manifest['faces'])} faces."]
    return "\n".join(rows) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-hashes", action="store_true",
                        help="write the produced SHA-256 of every face back into fonts.json")
    parser.add_argument("--verify", action="store_true",
                        help="build nothing; only check on-disk bytes against the manifest")
    parser.add_argument("--clean-cache", action="store_true",
                        help="drop the upstream download cache first")
    parser.add_argument("--family", action="append", default=None,
                        help="limit to one family (repeatable)")
    args = parser.parse_args()

    if args.clean_cache and CACHE.exists():
        shutil.rmtree(CACHE)

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    sources = manifest["sources"]
    subsets = manifest["subsets"]
    head_epoch = manifest["headEpoch"]
    families = {f["family"]: f for f in manifest["families"]}
    wanted = set(args.family) if args.family else None

    failures: list[str] = []
    for face in manifest["faces"]:
        if wanted and face["family"] not in wanted:
            continue
        target = FONTS_DIR / face["file"]
        target.parent.mkdir(parents=True, exist_ok=True)

        if face["frozen"] or args.verify:
            if not target.exists():
                failures.append(f"{face['file']}: missing on disk")
                continue
            actual = sha256(target.read_bytes())
            if face["sha256"] and actual != face["sha256"]:
                failures.append(f"{face['file']}: sha256 {actual} != manifest {face['sha256']}")
            elif not face["sha256"] and args.write_hashes:
                face["sha256"] = actual
                print(f"  hash {face['file']} {actual[:16]}…")
            continue

        print(f"build {face['file']}")
        produced = build_face(fetch_upstream(sources, face), face, subsets, head_epoch)
        digest = sha256(produced)
        if face["sha256"] and digest != face["sha256"]:
            failures.append(f"{face['file']}: rebuild produced {digest}, "
                            f"manifest says {face['sha256']}")
        target.write_bytes(produced)
        if args.write_hashes:
            face["sha256"] = digest

    for family in manifest["families"]:
        if wanted and family["family"] not in wanted:
            continue
        licence = FONTS_DIR / family["licenceFile"]
        if licence.exists() or args.verify:
            if not licence.exists():
                failures.append(f"{family['licenceFile']}: missing")
            continue
        text = fetch_licence(sources, family)
        if text is None:
            failures.append(f"{family['licenceFile']}: missing and not fetchable "
                            f"(copy it out of the upstream release by hand)")
            continue
        licence.parent.mkdir(parents=True, exist_ok=True)
        licence.write_bytes(text)
        print(f"  licence {family['licenceFile']}")

    if not wanted and not failures:
        bundle = FONTS_DIR / "THIRD-PARTY-FONTS.txt"
        bundle.write_text(compose_licence_bundle(manifest), encoding="utf-8", newline="\n")
        doc = FONTS_DIR.parent.parent / "docs" / "FONTS.md"
        doc.write_text(compose_fonts_doc(manifest), encoding="utf-8", newline="\n")
        print(f"wrote {bundle} and {doc}")

    if args.write_hashes and not failures:
        MANIFEST.write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8", newline="\n")
        print(f"wrote {MANIFEST}")

    for message in failures:
        print(f"FAIL {message}", file=sys.stderr)
    total = sum(1 for f in manifest["faces"] if not wanted or f["family"] in wanted)
    print(f"{total} faces, {len(families)} families, {len(failures)} failures")
    return 1 if failures else 0


if __name__ == "__main__":
    os.environ.setdefault("PYTHONIOENCODING", "utf-8")
    sys.exit(main())
