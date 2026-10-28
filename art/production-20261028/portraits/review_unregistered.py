"""Full-face saved contacts and structural evidence for unregistered194..199."""
import json
from pathlib import Path
from PIL import Image,ImageDraw
from candidate_gate import gate

HERE=Path(__file__).resolve().parent
BASE=HERE.parents[2]/'art/portraits/mobile-mouths'
MOODS=('neutral','happy','angry','sad','afraid','guarded')

def main():
    inventory=[]
    for record in range(194,200):
        name=f'npc{record}-mouth-position-v2' if record==194 else f'npc{record}-keyframe-v1'
        source=BASE/name
        out=HERE/f'npc{record}-existing-review';out.mkdir(exist_ok=True)
        if not (source/'manifest.json').is_file():
            files=[str(p.relative_to(source)) for p in source.rglob('*') if p.is_file()]
            inventory.append({'record':record,'source':str(source),'structuralPass':False,'issues':['No animation manifest or animation PNGs.'], 'existingFiles':files,'publication':'not registered in current catalog'})
            continue
        result=gate(source/'manifest.json')
        (out/'structural-gate.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        manifest=json.loads((source/'manifest.json').read_text(encoding='utf-8-sig'))
        original=Image.open(source/manifest['original']).convert('RGB')
        for kind in ('eyes','mouth'):
            canvas=Image.new('RGB',(9*128,6*152),'#252525');draw=ImageDraw.Draw(canvas)
            for row,mood in enumerate(MOODS):
                if kind=='eyes':
                    states=[(gaze,phase) for gaze in ('front','left','right') for phase in ('open','half','closed')]
                    lower=Image.open(source/f'mouth/{mood}/X.png')
                    for col,(gaze,phase) in enumerate(states):
                        full=original.copy();full.paste(Image.open(source/f'eyes/{mood}/{gaze}/{phase}.png'),(0,0));full.paste(lower,(0,35))
                        canvas.paste(full.resize((128,128),Image.Resampling.NEAREST),(col*128,row*152+24));draw.text((col*128+2,row*152+2),f'{mood} {gaze} {phase}',fill='white')
                else:
                    upper=Image.open(source/f'eyes/{mood}/front/open.png')
                    for col,shape in enumerate('ABCDEFGHX'):
                        full=original.copy();full.paste(upper,(0,0));full.paste(Image.open(source/f'mouth/{mood}/{shape}.png'),(0,35))
                        canvas.paste(full.resize((128,128),Image.Resampling.NEAREST),(col*128,row*152+24));draw.text((col*128+2,row*152+2),f'{mood} {shape}',fill='white')
            canvas.save(out/f'{kind}-full-contact.png')
        inventory.append({'record':record,'source':str(source),'structuralPass':result['structuralPass'],'issues':result['issues'],'manifestSha256':result['manifestSha256'],'reviewDirectory':str(out),'publication':'not registered in current catalog'})
    (HERE/'unregistered-194-199.json').write_text(json.dumps(inventory,indent=2),encoding='utf-8')
    print(json.dumps(inventory,indent=2))

if __name__=='__main__':main()
