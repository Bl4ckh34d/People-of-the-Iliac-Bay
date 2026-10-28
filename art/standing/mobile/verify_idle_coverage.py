"""Inventory and deterministic verification; never modifies sprite deliverables."""
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib, json

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT.parent / "source" / "native-idle"
IDS = list(range(381,399)) + list(range(451,457))
NAMES = ["front-native","front-left","left-profile","rear-left","back","rear-right","right-profile","front-right"]
ACCEPTED = {385,386,391,394,453,454,455,456}
rows = []
all_frames = {}
checks = {"appearance_count":0,"frame_count":0,"native_front_byte_matches":0,"rgba_frames_with_real_alpha":0,"native_height_matches":0,"atlas_pixel_matches":0}
for archive in IDS:
    folder = ROOT / ("idle-pilot-391" if archive == 391 else f"idle-{archive}")
    source = SOURCE / f"{archive}-0.png"
    original = Image.open(source)
    meta = json.loads((folder/"manifest-v1.json").read_text())
    cw,ch = meta["atlas_cell_size"]
    atlas_path = folder/f"{archive}-idle-native-atlas-v1.png"
    atlas = Image.open(atlas_path).convert("RGBA")
    assert atlas.size == (cw*4,ch*2), archive
    frames = []
    frame_details = []
    for view,name in enumerate(NAMES):
        path = folder/f"{archive}-idle-view-{view}-{name}.png"
        im = Image.open(path)
        assert im.mode == "RGBA", (archive,view,im.mode)
        alpha_min,alpha_max = im.getchannel("A").getextrema()
        assert alpha_min == 0 and alpha_max >= 250, (archive,view,"alpha")
        assert im.height == original.height, (archive,view,"height")
        if view == 0:
            assert path.read_bytes() == source.read_bytes(), (archive,"front changed")
            checks["native_front_byte_matches"] += 1
        x,y = view%4*cw+(cw-im.width)//2, view//4*ch
        assert atlas.crop((x,y,x+im.width,y+im.height)).tobytes() == im.tobytes(), (archive,view,"atlas")
        frames.append(im.copy())
        checks["frame_count"] += 1
        checks["rgba_frames_with_real_alpha"] += 1
        checks["native_height_matches"] += 1
        checks["atlas_pixel_matches"] += 1
        frame_details.append({"view":view,"bearing":name,"file":path.relative_to(ROOT).as_posix(),"size":list(im.size),"alpha_extrema":[alpha_min,alpha_max],"sha256":hashlib.sha256(path.read_bytes()).hexdigest()})
    checks["appearance_count"] += 1
    all_frames[archive] = frames
    rows.append({"archive":archive,"folder":folder.name,"native_source_size":list(original.size),"native_source_sha256":hashlib.sha256(source.read_bytes()).hexdigest(),"status":"user-accepted" if archive in ACCEPTED else "prepared-and-visually-reviewed","atlas":atlas_path.relative_to(ROOT).as_posix(),"atlas_cell_size":[cw,ch],"review":(folder/f"{archive}-idle-review-4x-v1.png").relative_to(ROOT).as_posix(),"manifest":(folder/"manifest-v1.json").relative_to(ROOT).as_posix(),"frames":frame_details})
# Confirm user-requested 390 reorder used exactly the opposite original grid cells.
raw = Image.open(ROOT/"idle-390/generated-sheet-v1.png").convert("RGBA")
swap_checks = {}
for dest,src in ((1,7),(7,1)):
    box = (round(src%4*raw.width/4),round(src//4*raw.height/2),round((src%4+1)*raw.width/4),round((src//4+1)*raw.height/2))
    cell = raw.crop(box)
    bounds = cell.getchannel("A").point(lambda a:255 if a>=128 else 0).getbbox()
    cut = cell.crop(bounds)
    target = all_frames[390][dest]
    expected = cut.resize(target.size,Image.Resampling.NEAREST)
    color_repair = json.loads((ROOT/"idle-390/manifest-v1.json").read_text()).get("outline_color_repair")
    if color_repair:
        # The user-requested original cell swap remains authoritative. Only the
        # reviewed material-color correction may differ, never alpha or geometry.
        import sys
        sys.path.insert(0, str(ROOT.parents[2] / "tools"))
        from soften_standing_outline import soften
        expected, _ = soften(expected)
    assert expected.tobytes() == target.tobytes(), (390,dest,"not exact swap")
    swap_checks[str(dest)] = {"original_cell":src,"exact_pixel_match_after_native_resize_and_color_repair":True}
checks["390_swap"] = swap_checks
# Native-size catalog pages, 8 appearances per page. No sprite scaling.
for page in range(3):
    subset = IDS[page*8:(page+1)*8]
    sheet = Image.new("RGB",(920,8*204+28),"#303238")
    draw = ImageDraw.Draw(sheet)
    draw.text((8,8),f"Mobile idle catalog {page+1}/3 | native pixels | views 0-7 left to right",fill="white")
    for row,archive in enumerate(subset):
        top = 28+row*204
        draw.text((8,top+8),str(archive),fill="white")
        for view,frame in enumerate(all_frames[archive]):
            x = 48+view*108+(108-frame.width)//2
            y = top+22+(179-frame.height)
            sheet.paste(frame,(x,y),frame)
            draw.text((48+view*108+4,top+5),str(view),fill="#bbbbbb")
    sheet.save(ROOT/f"idle-review-page-{page+1}.png")
coverage = {"appearance_ids":IDS,"appearance_count":24,"view_count":192,"new_generated_angle_count":168,"bearing_order":NAMES,"runtime_integration":False,"status_note":"Acceptance recorded from parent/user report for pilot391 and Daggerfall pool. Remaining sets prepared and visually reviewed. Coverage status supersedes initial historical manifest status text.","appearances":rows}
(ROOT/"idle-coverage.json").write_text(json.dumps(coverage,indent=2),encoding="utf-8")
(ROOT/"idle-verification.json").write_text(json.dumps(checks,indent=2),encoding="utf-8")
lines = ["# Mobile standing idle art","", "24 appearances, 192 views: 24 unchanged native fronts + 168 generated angles. Art only; runtime integration is outside this batch.","","Bearing order, left to right: 0 front, 1 front-left, 2 left profile, 3 rear-left, 4 back, 5 rear-right, 6 right profile, 7 front-right. Left/right describes the nose direction on the image.","","Each appearance has native-height RGBA crops, a padded 4x2 atlas, a 4x nearest-neighbor review sheet, generation prompt, and crop manifest. Original front PNG bytes are preserved. Wider silhouettes use transparent cell padding rather than horizontal squeezing. Generated alpha is retained.","","Native-size catalog: [page 1](idle-review-page-1.png), [page 2](idle-review-page-2.png), [page 3](idle-review-page-3.png). Full paths, dimensions and hashes: [coverage](idle-coverage.json). Checks: [verification](idle-verification.json).","", "The accepted pilot remains in idle-pilot-391. Accepted Daggerfall pool assets are preserved. Initial manifest status fields are historical; current acceptance status is recorded in coverage.","","390 view1 and view7 are an exact reorder of the two existing generated cells, with no regeneration, mirroring or repainting. Native reduction remains unchanged. Correction overrides in manifests are authoritative; some raw sheets intentionally retain rejected cells for provenance.","","| Archive | Original size | Review | Status |","| --- | --- | --- | --- |"]
for row in rows:
    w,h = row["native_source_size"]
    lines.append(f'| {row["archive"]} | {w}x{h} | [8 views]({row["review"]}) | {row["status"]} |')
(ROOT/"IDLE-COVERAGE.md").write_text("\n".join(lines)+"\n",encoding="utf-8")
print(json.dumps(checks,indent=2))

