"""Check deployed idle art without changing any artwork or game files."""
import json
from pathlib import Path
from PIL import Image

repo = Path(__file__).resolve().parent.parent
assets = repo.parent / 'daggerfall-unity/Assets/Game/Mods/PeopleOfTheIliacBay'
files = json.loads((assets / 'PeopleOfTheIliacBay.dfmod.json').read_text(encoding='utf-8-sig'))['Files']
coverage = json.loads((repo / 'art/standing/mobile/idle-coverage.json').read_text())
assert len(coverage['appearances']) == 24
for item in coverage['appearances']:
    archive = item['archive']
    stem = f'idle-{archive}'
    for suffix in ('.json', '.png.bytes'):
        assert f'Assets/Game/Mods/PeopleOfTheIliacBay/Textures/{stem}{suffix}' in files, (archive, suffix, 'not bundled')
    definition = json.loads((assets / f'Textures/{stem}.json').read_text(encoding='utf-8-sig'))
    atlas = Image.open(assets / f'Textures/{stem}.png.bytes').convert('RGBA')
    cw, ch = definition['cellSize']
    assert definition['archive'] == archive and definition['nativeIdleSize'] == item['native_source_size']
    assert atlas.size == (cw * 4, ch * 2)
    for frame in item['frames']:
        source = Image.open(repo / 'art/standing/mobile' / frame['file']).convert('RGBA')
        i = frame['view']
        x, y = i % 4 * cw + (cw-source.width)//2, i//4*ch
        assert atlas.crop((x,y,x+source.width,y+source.height)).tobytes() == source.tobytes(), (archive, i, 'pixels changed')
print('IDLE_PACKAGE_PASS: 24 bundled atlases/manifests; 192 exact source frames; native dimensions')
