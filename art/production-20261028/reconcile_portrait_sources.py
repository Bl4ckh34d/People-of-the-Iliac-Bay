"""Inspect every installed mod TFAC texture and reuse exact existing portrait sources."""
import hashlib
import json
from collections import Counter, defaultdict
from pathlib import Path

import UnityPy
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
CATALOG = HERE.parents[1] / "art/portraits/mobile-mouths/split-mask-prototype/portrait-catalog.json"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def pixel_key(picture):
    picture = picture.convert("RGBA")
    return (picture.size, hashlib.sha256(picture.tobytes()).hexdigest())


def main():
    report_path = HERE / "portraits/mod-portrait-source-reconciliation.json"
    previous = json.loads(report_path.read_text()) if report_path.exists() else {}
    previous_rows = {row["id"]: row for row in previous.get("entries", [])}
    catalog = json.loads(CATALOG.read_text())
    index = defaultdict(list)
    for row in catalog["entries"]:
        path = CATALOG.parent / row["original"]
        index[pixel_key(Image.open(path))].append({"id": row["id"], "path": str(path), "animationManifest": row.get("animationManifest")})
    det = json.loads((HERE / "portraits/det-portrait-sources.json").read_text())
    existing_det = {row["asset"]: row for row in det["entries"]}
    for row in det["entries"]:
        index[pixel_key(Image.open(row["original"]))].append({"id": "det:"+row["fileName"], "path": row["original"], "animationManifest": None})

    bundles = json.loads((HERE / "mod-container-index.json").read_text())["bundles"]
    enabled = {row["FileName"].lower() for row in json.loads((HERE / "source-inventory.json").read_text())["enabledMods"] if row["Enabled"]}
    destination = HERE / "portraits/additional-mod-originals"
    destination.mkdir(exist_ok=True)
    rows = []
    for bundle in bundles:
        assets = [row["path"] for row in bundle["assets"] if row["type"] == "Texture2D" and "tfac" in Path(row["path"]).name.lower()]
        if not assets:
            continue
        path = Path(bundle["bundle"])
        assert digest(path) == bundle["sha256"]
        environment = UnityPy.load(str(path))
        for asset in assets:
            picture = environment.container[asset].read().image.convert("RGBA")
            key = pixel_key(picture)
            matches = list(index.get(key, []))
            if asset in existing_det:
                original = Path(existing_det[asset]["original"])
                assert pixel_key(Image.open(original)) == key
            elif matches:
                original = Path(matches[0]["path"])
            else:
                provider_directory = destination / path.stem
                provider_directory.mkdir(exist_ok=True)
                original = provider_directory / Path(asset).name
                if original.exists():
                    assert pixel_key(Image.open(original)) == key
                else:
                    picture.save(original)
            identity = path.stem+":"+Path(asset).name
            old = previous_rows.get(identity, {})
            visual_review = old.get("visualReview", "pending") if old.get("rgbaSha256") == key[1] else "pending"
            rows.append({"id": identity, "provider": path.stem, "enabled": path.stem.lower() in enabled,
                         "bundle": str(path), "bundleSha256": bundle["sha256"], "asset": asset,
                         "dimensions": list(picture.size), "rgbaSha256": key[1], "original": str(original),
                         "fileSha256": digest(original), "exactCatalogMatches": [m for m in matches if not m["id"].startswith("det:") and m["id"].startswith(("common-", "special-"))],
                         "exactEarlierSourceMatches": [m["id"] for m in matches],
                         "visualReview": visual_review, "newAnimationProduced": False,
                         "bank": Path(asset).name.split("_", 1)[0].upper(),
                         "recordFromFilename": int(Path(asset).name.split("_", 1)[1].split("-", 1)[0])})
            index[key].append({"id": identity, "path": str(original), "animationManifest": None})
        del environment

    contacts = []
    for page_index, start in enumerate(range(0, len(rows), 48)):
        page_rows = rows[start:start+48]
        sheet = Image.new("RGB", (8*160, 6*160), (35, 39, 43))
        draw = ImageDraw.Draw(sheet)
        for position, row in enumerate(page_rows):
            x, y = position % 8 * 160, position // 8 * 160
            picture = Image.open(row["original"]).convert("RGBA")
            picture.thumbnail((128, 128), Image.Resampling.NEAREST)
            if picture.size == (64, 64):
                picture = picture.resize((128, 128), Image.Resampling.NEAREST)
            sheet.paste(picture, (x+16, y+20), picture)
            label = Path(row["asset"]).name.split("_")[-1].removesuffix("-0.png")
            draw.text((x+3, y+3), f"{start+position}: {label}", fill="white")
            row["contactIndex"] = start+position
        output = HERE / f"portraits/mod-portrait-source-contact-{page_index}.png"
        sheet.save(output)
        contacts.append(str(output))
    unique = {(tuple(row["dimensions"]), row["rgbaSha256"]) for row in rows}
    result = {"scope": "Installed mod TFAC Texture2D resources, joined with enabled config; runtime portrait assignment is not inferred from filenames.",
              "catalog": str(CATALOG), "catalogSha256": digest(CATALOG), "counts": {"resources": len(rows), "uniquePixels": len(unique),
              "byProvider": dict(Counter(row["provider"] for row in rows)), "exactCatalogMatches": sum(bool(row["exactCatalogMatches"]) for row in rows)},
              "entries": rows, "contacts": contacts,
              "limitations": ["Catalog source match does not prove animation quality or active runtime assignment.", "No generated portrait animation is created by this source extraction."]}
    result["verification"] = {"allSourceFileAndRgbaHashesMatch": True, "dimensions64x64": sum(row["dimensions"] == [64,64] for row in rows),
                              "parentSourceContactsInspected": 4 if all(row["visualReview"] == "parent-inspected-source-contact-2026-10-28" for row in rows) else 0,
                              "runtimeAssignmentVerified": False}
    report_path.write_text(json.dumps(result, indent=2)+"\n")
    print(json.dumps(result["counts"]))


if __name__ == "__main__":
    main()
