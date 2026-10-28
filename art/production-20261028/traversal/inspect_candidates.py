"""Read-only pixel inspection of traversal candidates; never repairs source art."""
from pathlib import Path
from collections import Counter
import hashlib
import json
import statistics
from PIL import Image

ROOT = Path(__file__).resolve().parent
ART = ROOT.parents[1]
NATIVE = ART / 'standing' / 'source' / 'native-idle' / '381-0.png'

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

source = Image.open(NATIVE).convert('RGBA')
palette = set(p[:3] for p in source.getdata() if p[3])
report = {'source': {'path': str(NATIVE), 'sha256': sha(NATIVE), 'size': list(source.size), 'opaqueColors': len(palette)}, 'candidates': []}
for path in [ROOT/name for name in ['381-jump-generated-v1.png', '381-jump-generated-v2.png', '381-climb-generated-v1.png', '381-swim-generated-v1.png']]:
    im = Image.open(path).convert('RGBA')
    pixels = list(im.getdata())
    alpha = Counter(p[3] for p in pixels)
    visible = [p for p in pixels if p[3]]
    cells = []
    for phase in range(4):
        for bearing in range(8):
            rect = [round(bearing*im.width/8), round(phase*im.height/4), round((bearing+1)*im.width/8), round((phase+1)*im.height/4)]
            cell = im.crop(rect)
            a = cell.getchannel('A')
            bbox = a.point(lambda n: 255 if n >= 128 else 0).getbbox()
            touching = []
            if bbox:
                for side, condition in [('left', bbox[0] == 0), ('top', bbox[1] == 0), ('right', bbox[2] == cell.width), ('bottom', bbox[3] == cell.height)]:
                    if condition:
                        touching.append(side)
            cells.append({'bearing': bearing, 'phase': phase, 'sourceRect': rect, 'alpha128Bounds': bbox, 'touchesCellBoundary': touching, 'rgbaDigest': hashlib.sha256(cell.tobytes()).hexdigest()})
    report['candidates'].append({'file': path.name, 'sha256': sha(path), 'size': list(im.size), 'mode': im.mode, 'bytes': path.stat().st_size, 'alpha0': alpha[0], 'alpha255': alpha[255], 'alphaPartial': sum(n for a, n in alpha.items() if 0 < a < 255), 'alphaHistogram': dict(sorted(alpha.items())), 'visiblePixels': len(visible), 'visiblePixelsOutsideNativeSourcePalette': sum(p[:3] not in palette for p in visible), 'uniqueVisibleColors': len(set(p[:3] for p in visible)), 'cells': cells, 'distinctCellImages': len(set(c['rgbaDigest'] for c in cells)), 'status': 'generated-visually-rejected-not-production', 'integrated': False, 'userApproved': False})
    report['candidates'][-1]['medianCutoutAlpha'] = statistics.median([p[3] for p in pixels if p[3] >= 128])
    report['candidates'][-1]['partialAlphaInterpretation'] = 'Partial alpha alone is not a failure; alpha>=128 cutout foreground has near-opaque median. Geometry and directional consistency determine rejection.'
(ROOT / 'technical-checks.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
print(json.dumps({'native': report['source'], 'candidates': [{k:v for k,v in r.items() if k not in ['cells','alphaHistogram']} for r in report['candidates']]}, indent=2))
