#!/usr/bin/env python3
"""Inventory exact central face-region matches in the native portrait catalog.

This is read-only with respect to source PNGs. Region matches are candidates,
not proof that pixels outside the compared regions can be reused.
"""
from __future__ import annotations

import argparse
import hashlib
import itertools
import json
from collections import defaultdict
from pathlib import Path

from PIL import Image


REGIONS = {
    # Fixed native 64x64 discovery windows. These intentionally avoid most
    # clothing/background pixels, but may still include occlusion boundaries.
    "eyes_brows": (15, 17, 50, 27),
    "nose": (22, 26, 43, 39),
    "mouth": (18, 37, 47, 51),
}

# Separate visual notes only. They are tentative contact-sheet impressions,
# never used by the exact-pixel matcher or as hood/unhood ground truth.
TENTATIVE_HEAD_COVERING = {
    "common-037": "possible-head-covering",
    "common-367": "possible-head-covering",
    "common-099": "possible-head-covering",
    "common-107": "possible-head-covering",
    "common-120": "possible-head-covering",
    "common-121": "possible-head-covering",
    "common-280": "possible-head-covering",
    "common-281": "possible-head-covering",
    "common-282": "possible-head-covering",
    "common-283": "possible-head-covering",
    "common-327": "possible-head-covering",
    "common-351": "possible-head-covering",
}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def relative_posix(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def load_records(root: Path, catalog_path: Path) -> list[dict]:
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    if catalog.get("schemaVersion") != 1 or not isinstance(catalog.get("entries"), list):
        raise ValueError(f"Unsupported portrait catalog schema: {catalog_path}")
    catalog_dir = catalog_path.parent
    records = []
    for entry in catalog["entries"]:
        original = entry.get("original")
        if original is None:
            continue
        image_path = (catalog_dir / original).resolve()
        if not image_path.is_file():
            raise FileNotFoundError(f"Catalog original missing for {entry.get('id')}: {image_path}")
        with Image.open(image_path) as opened:
            image = opened.convert("RGBA")
            pixels = image.tobytes()
            regions = {}
            for name, box in REGIONS.items():
                crop = image.crop(box)
                crop_pixels = crop.tobytes()
                regions[name] = {
                    "boxXYXY": list(box),
                    "width": crop.width,
                    "height": crop.height,
                    "pixelSha256": sha256(crop_pixels),
                    "pixels": crop_pixels,
                }
            records.append({
                "id": entry["id"],
                "label": entry.get("label"),
                "bank": entry.get("bank"),
                "record": entry.get("record"),
                "sourcePath": relative_posix(image_path, root),
                "originalFileSha256": sha256(image_path.read_bytes()),
                "originalRgbaSha256": sha256(pixels),
                "width": image.width,
                "height": image.height,
                "regions": regions,
            })
    return records


def differing_pixel_count(left: bytes, right: bytes) -> int:
    return sum(left[i:i + 4] != right[i:i + 4] for i in range(0, len(left), 4))


def record_view(record: dict) -> dict:
    tentative = TENTATIVE_HEAD_COVERING.get(record["id"])
    return {key: record[key] for key in (
        "id", "label", "bank", "record", "sourcePath", "originalFileSha256",
        "originalRgbaSha256", "width", "height",
    )} | {
        "visualReview": {
            "tentativeHeadCovering": tentative if tentative else "not-visually-reviewed",
            "basis": (
                "temporary contact-sheet visual impression; tentative and unverified"
                if tentative else "no visual head-covering classification was made"
            ),
        }
    }


def analyze(records: list[dict], catalog_rel: str) -> dict:
    by_id = {record["id"]: record for record in records}
    feature_names = list(REGIONS)
    candidate_pairs = []
    for left, right in itertools.combinations(records, 2):
        exact_regions = [
            name for name in feature_names
            if left["regions"][name]["pixelSha256"] == right["regions"][name]["pixelSha256"]
        ]
        if len(exact_regions) < 2:
            continue
        comparisons = {}
        for name in feature_names:
            lreg = left["regions"][name]
            rreg = right["regions"][name]
            comparisons[name] = {
                "boxXYXY": lreg["boxXYXY"],
                "pixelCountCompared": lreg["width"] * lreg["height"],
                "exact": lreg["pixelSha256"] == rreg["pixelSha256"],
                "leftPixelSha256": lreg["pixelSha256"],
                "rightPixelSha256": rreg["pixelSha256"],
                "differentPixels": differing_pixel_count(lreg["pixels"], rreg["pixels"]),
            }
        candidate_pairs.append({
            "leftId": left["id"],
            "rightId": right["id"],
            "sharedExactRegions": exact_regions,
            "differentRegionCount": len(feature_names) - len(exact_regions),
            "roiComparisons": comparisons,
        })

    # Candidate groups require identical hashes for the same two-or-more
    # named features. Groups may overlap and are not canonicalization decisions.
    groups_by_key = {}
    for feature_count in (2, 3):
        for names in itertools.combinations(feature_names, feature_count):
            buckets = defaultdict(list)
            for record in records:
                key = tuple(record["regions"][name]["pixelSha256"] for name in names)
                buckets[key].append(record["id"])
            for ids in buckets.values():
                if len(ids) < 2:
                    continue
                ids = sorted(ids)
                group_key = (names, tuple(ids))
                groups_by_key[group_key] = {
                    "candidateType": "exact-feature-signature",
                    "exactSharedRegions": list(names),
                    "members": ids,
                    "pairwiseExactAcrossNamedRegions": True,
                    "proofBoundary": "only these named fixed ROIs were compared; no inference outside their bounds",
                }
    candidate_groups = sorted(
        groups_by_key.values(),
        key=lambda g: (-len(g["exactSharedRegions"]), g["exactSharedRegions"], g["members"]),
    )

    public_records = [record_view(record) for record in records]
    return {
        "schemaVersion": 1,
        "purpose": "Read-only exact-pixel candidate inventory for possible portrait feature reuse.",
        "sourceCatalog": catalog_rel,
        "sourceRecordCount": len(records),
        "geometry": {
            "imageDimensionsExpected": [64, 64],
            "regions": {name: {"boxXYXY": list(box), "width": box[2] - box[0], "height": box[3] - box[1]} for name, box in REGIONS.items()},
            "method": "PNG decoded to RGBA at native size; SHA-256 over crop bytes; exact candidates also compared pixel-by-pixel.",
            "limitations": [
                "Fixed geometry is used only for candidate discovery.",
                "Exact ROI equality is not proof of equivalence outside the compared ROI.",
                "No hood/unhood, canonical-face, or production-mask decision is inferred from record number or these signatures.",
                "Distinct face, palette, geometry, or occlusion must be verified pairwise against the actual intended feature mask before production reuse.",
            ],
        },
        "visualReview": {
            "method": "Human inspection of a temporary side-by-side contact sheet for three-region exact candidates, made only from native catalog originals.",
            "reviewedCandidateMemberIds": sorted(TENTATIVE_HEAD_COVERING),
            "hoodFlagsAreTentative": True,
            "canonicalAssignments": "none",
        },
        "records": public_records,
        "candidateGroups": candidate_groups,
        "candidateGroupsMayOverlap": True,
        "candidatePairwiseVerification": candidate_pairs,
        "summary": {
            "candidateGroupCount": len(candidate_groups),
            "candidatePairCount": len(candidate_pairs),
            "pairsExactInAllThreeRegions": sum(len(pair["sharedExactRegions"]) == 3 for pair in candidate_pairs),
            "pairsWithAtLeastTwoExactRegions": sum(len(pair["sharedExactRegions"]) >= 2 for pair in candidate_pairs),
            "productionCanonicalAssignments": 0,
        },
    }


def markdown(report: dict) -> str:
    geo = report["geometry"]["regions"]
    lines = [
        "# Native portrait face-family inventory",
        "",
        "Read-only exact-pixel discovery over catalog originals. Candidate groups are not canonical assignments and do not authorize mask reuse.",
        "",
        f"- Records read: **{report['sourceRecordCount']}**",
        f"- Candidate groups (exact named-region signatures): **{report['summary']['candidateGroupCount']}**",
        f"- Candidate pair comparisons (at least two exact regions): **{report['summary']['candidatePairCount']}**",
        f"- Pairs exact in all three regions: **{report['summary']['pairsExactInAllThreeRegions']}**",
        "- Canonical assignments: **0**",
        "- Candidate group entries may overlap because the same members can share multiple feature subsets.",
        "",
        "## Compared regions",
        "",
        "All crops are native 64×64 RGBA coordinates; no resizing or color normalization was used.",
        "",
        "| Region | Native box (left, top, right, bottom) | Size |",
        "|---|---:|---:|",
    ]
    for name, info in geo.items():
        lines.append(f"| {name} | {tuple(info['boxXYXY'])} | {info['width']}×{info['height']} |")
    lines += ["", "## Candidate groups", ""]
    for group in report["candidateGroups"]:
        lines.append(f"- **Exact {', '.join(group['exactSharedRegions'])}:** {', '.join(group['members'])}")
    lines += [
        "",
        "The JSON contains each catalog entry’s bank, record, path, dimensions, file SHA-256, decoded RGBA SHA-256, and a separate tentative visual head-covering field. Only listed IDs were visually assessed; all other records are marked not reviewed. Candidate group entries may overlap because one member set can share multiple feature subsets. The JSON also contains every candidate pair’s exact-match regions and per-region differing-pixel counts across all three ROIs.",
        "",
        "## Interpretation and limits",
        "",
        "Exact signature matches show that the listed feature crops have identical decoded RGBA pixels. They do not show that the portraits are identical outside those crops, nor that a crop remains visible under a hood or other occluder. The tentative head-covering flags came from visual inspection of a temporary side-by-side contact sheet; they are separate from the matcher and are not ground truth. No canonical unhooded face was selected. Before production reuse, verify each pair against the actual eye/mouth feature mask, palette, alignment, and occlusion boundaries.",
        "",
        "The source catalog and original PNGs were read only. No image generation, image edits, catalog changes, or asset changes were made.",
        "",
    ]
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1], help="LivingCities repository root")
    parser.add_argument("--catalog", type=Path, default=None, help="catalog JSON path (default: art/.../portrait-catalog.json)")
    parser.add_argument("--json-out", type=Path, default=None, help="output JSON path (default: work/portrait-face-families.json)")
    parser.add_argument("--md-out", type=Path, default=None, help="output Markdown path (default: work/portrait-face-families.md)")
    args = parser.parse_args()
    root = args.root.resolve()
    catalog_path = (args.catalog or root / "art/portraits/mobile-mouths/split-mask-prototype/portrait-catalog.json").resolve()
    json_out = (args.json_out or root / "work/portrait-face-families.json").resolve()
    md_out = (args.md_out or root / "work/portrait-face-families.md").resolve()
    report = analyze(load_records(root, catalog_path), relative_posix(catalog_path, root))
    json_out.parent.mkdir(parents=True, exist_ok=True)
    md_out.parent.mkdir(parents=True, exist_ok=True)
    json_out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    md_out.write_text(markdown(report), encoding="utf-8")
    print(json.dumps({"records": report["sourceRecordCount"], **report["summary"], "json": str(json_out), "markdown": str(md_out)}, indent=2))


if __name__ == "__main__":
    main()
