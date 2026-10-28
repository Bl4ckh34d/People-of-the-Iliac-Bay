"""Create contact sheets from preserved originals and exported poses."""
from pathlib import Path
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parent
archives=[a for a in list(range(381,399))+list(range(451,457)) if (ROOT/str(a)/'atlas.png').exists()]
overview=Image.new('RGB',(800,((len(archives)+3)//4)*210),(60,60,60))
draw=ImageDraw.Draw(overview)
angles=Image.new('RGB',(1152,((len(archives)+2)//3)*380),(50,50,50))
angle_draw=ImageDraw.Draw(angles)
for i,a in enumerate(archives):
    x,y=(i%4)*200,(i//4)*210
    draw.text((x+4,y+4),f'{a} native idle | sitting',fill='white')
    for name,offset in [('source-idle.png',0),('view-0-front.png',100)]:
        image=Image.open(ROOT/str(a)/name)
        overview.paste(image,(x+offset+(100-image.width)//2,y+205-image.height),image)
    x,y=(i%3)*384,(i//3)*380
    angle_draw.text((x+4,y+4),str(a),fill='white')
    angles.paste(Image.open(ROOT/str(a)/'preview-native.png'),(x,y+20))
overview.save(ROOT/'all-appearances-source-comparison.png')
angles.save(ROOT/'all-eight-views-preview.png')
print(f'Previewed {len(archives)} appearances')
