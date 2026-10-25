"""REJECTED EXPERIMENT: dark-rim erosion damaged faces and calves.

Retained for diagnosis only. Do not apply this to production artwork. Protecting
an eroded interior does not protect the original anatomical silhouette: dark skin
and facial contours can occupy the outer two pixels too. Original assets were
restored on 2026-10-18 after user review. Candidate generation is disabled.
"""
from pathlib import Path
import argparse
import hashlib
import json
import shutil

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "art/standing/mobile"


def erode(mask):
    padded = np.pad(mask, 1)
    result = mask.copy()
    for y in range(3):
        for x in range(3):
            result &= padded[y:y + mask.shape[0], x:x + mask.shape[1]]
    return result


def clean(image):
    source = np.array(image.convert("RGBA"))
    result = source.copy()
    original_mask = source[:, :, 3] >= 128
    protected = erode(erode(original_mask))
    # Alpha below the native cutout threshold is invisible in-game; clear its
    # generated matte too so preview compositing cannot resurrect a dark halo.
    result[:, :, 3][~original_mask] = 0
    for _ in range(2):
        mask = result[:, :, 3] >= 128
        edge = mask & ~erode(mask) & ~protected
        brightness = result[:, :, :3].max(axis=2)
        remove = np.zeros(mask.shape, dtype=bool)
        for y, x in zip(*np.nonzero(edge & (brightness < 56))):
            # Require a brighter opaque neighbour on the body side. This keeps
            # uniformly dark hair/shoes and isolated thin details intact.
            y0, y1 = max(0, y - 2), min(mask.shape[0], y + 3)
            x0, x1 = max(0, x - 2), min(mask.shape[1], x + 3)
            neighbours = brightness[y0:y1, x0:x1][mask[y0:y1, x0:x1]]
            if neighbours.size and int(neighbours.max()) >= max(48, int(brightness[y, x]) + 28):
                remove[y, x] = True
        result[:, :, 3][remove] = 0
    # Discard detached dark flecks left outside the body, never a body component
    # or an interior feature. All removals stay inside the original rim band.
    remaining = result[:, :, 3] >= 128
    visited = np.zeros(remaining.shape, dtype=bool)
    for start_y, start_x in zip(*np.nonzero(remaining)):
        if visited[start_y, start_x]:
            continue
        stack = [(start_y, start_x)]
        component = []
        visited[start_y, start_x] = True
        while stack:
            y, x = stack.pop()
            component.append((y, x))
            for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < remaining.shape[0] and 0 <= nx < remaining.shape[1] and remaining[ny, nx] and not visited[ny, nx]:
                    visited[ny, nx] = True
                    stack.append((ny, nx))
        if len(component) <= 8 and all(not protected[y, x] and source[y, x, :3].max() < 64 for y, x in component):
            for y, x in component:
                result[y, x, 3] = 0
    assert np.array_equal(source[protected], result[protected])
    assert np.array_equal(source[:, :, :3], result[:, :, :3])
    return Image.fromarray(result), {
        "visible_pixels_before": int(original_mask.sum()),
        "visible_pixels_removed": int((original_mask & (result[:, :, 3] == 0)).sum()),
        "protected_interior_unchanged": True,
        "rgb_unchanged": True,
    }


def prepare(archive, output):
    raise RuntimeError("Cleanup withdrawn: alpha erosion removes anatomy. Use original standing assets.")
    folder = ART / ("idle-pilot-391" if archive == 391 else f"idle-{archive}")
    metadata = json.loads((folder / "manifest-v1.json").read_text(encoding="utf-8"))
    if "edge_cleanup" in metadata:
        raise ValueError(f"Archive {archive} already cleaned; use preserved originals for another review")
    destination = output / str(archive)
    destination.mkdir(parents=True, exist_ok=True)
    cell_width, cell_height = metadata["atlas_cell_size"]
    atlas = Image.new("RGBA", (cell_width * 4, cell_height * 2))
    rows = []
    report = []
    for frame in metadata["frames"]:
        view = frame["view"]
        # The original 391 pilot predates the per-frame file field.
        frame.setdefault("file", f"{archive}-idle-view-{view}-{frame['bearing']}.png")
        source_path = folder / frame["file"]
        source = Image.open(source_path).convert("RGBA")
        if view == 0:
            candidate = source.copy()
            checks = {"native_front_unchanged": True}
            shutil.copyfile(source_path, destination / frame["file"])
        else:
            candidate, checks = clean(source)
            candidate.save(destination / frame["file"])
        assert candidate.size == source.size
        x = view % 4 * cell_width + (cell_width - candidate.width) // 2
        y = view // 4 * cell_height
        atlas.paste(candidate, (x, y))
        rows.append((view, source, candidate))
        report.append({"view": view, "source_sha256": hashlib.sha256(source_path.read_bytes()).hexdigest(), **checks})
    atlas.save(destination / "atlas.png")
    metadata["edge_cleanup"] = {"method": "selective dark outer-rim alpha removal, max two native pixels", "frames": report}
    (destination / "manifest.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    # Same-scale native walking reference beside original and cleaned standing.
    scale = 3
    column = 340
    height = cell_height * scale + 40
    review = Image.new("RGB", (column * 3, height * len(rows)), "#858779")
    draw = ImageDraw.Draw(review)
    walking_root = ROOT / "art/run/pilot-385/source"
    for row, (view, source, candidate) in enumerate(rows):
        top = row * height
        draw.text((8, top + 8), f"{archive} view {view} | Original walk", fill="black")
        draw.text((column + 8, top + 8), "Standing before", fill="black")
        draw.text((column * 2 + 8, top + 8), "Standing cleaned", fill="black")
        # The native files contain five stored bearings; the other three use
        # DFU's normal mirrored display. Reference only, never altered artwork.
        record = view if view <= 4 else 8 - view
        native_path = walking_root / f"{archive}-{record}-0.png"
        native = Image.open(native_path).convert("RGBA") if native_path.exists() else None
        if native is not None:
            if view > 4:
                native = native.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
            native = native.resize((round(native.width * cell_height / native.height), cell_height), Image.Resampling.NEAREST)
        for col, item in enumerate((native, source, candidate)):
            if item is None:
                continue
            enlarged = item.resize((item.width * scale, item.height * scale), Image.Resampling.NEAREST)
            review.paste(enlarged, (col * column + (column - enlarged.width) // 2, top + 30), enlarged)
        review.crop((0, top, column * 3, top + height)).save(destination / f"comparison-view-{view}.png")
    review.save(destination / "comparison.png")
    print(json.dumps({"archive": archive, "directory": str(destination), "frames": report}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive", type=int, default=385)
    parser.add_argument("--output", type=Path, default=ROOT / "work/standing-edge-review")
    args = parser.parse_args()
    prepare(args.archive, args.output)
