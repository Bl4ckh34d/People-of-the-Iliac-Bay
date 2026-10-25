using System;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Import generated sheets onto the native 76-pixel character grid. The front
    // remains DFU's untouched original texture and animation.
    public sealed class KeeperStandingArt : IDisposable
    {
        public const int Cell=96,Height=76,Frames=12;
        private readonly Func<string,byte[]> read;
        private readonly bool readable;
        private Texture2D atlas;
        public KeeperStandingArt(Mod mod) : this(name=>{var asset=mod.GetAsset<TextAsset>(name);return asset?asset.bytes:null;}) { }
        public KeeperStandingArt(Func<string,byte[]> read,bool readable=false) {this.read=read;this.readable=readable;}
        public Texture2D Atlas
        {
            get
            {
                if(atlas) return atlas;
                var output=new Color32[Cell*Frames*Cell*7];
                for(int view=1;view<=7;view++)
                {
                    string name="keeper-182-0-view-"+view+".png.bytes";
                    var source=read(name);
                    if(source==null) throw new InvalidOperationException("Keeper direction missing: "+name);
                    var raw=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    try
                    {
                        if(!raw.LoadImage(source)) throw new InvalidOperationException("Keeper direction cannot decode: "+name);
                        var pixels=raw.GetPixels32();
                        for(int frame=0;frame<Frames;frame++)
                        {
                            int col=frame%4,row=frame/4;
                            int left=Mathf.RoundToInt(col*raw.width/4f),right=Mathf.RoundToInt((col+1)*raw.width/4f);
                            int bottom=raw.height-Mathf.RoundToInt((row+1)*raw.height/3f),top=raw.height-Mathf.RoundToInt(row*raw.height/3f);
                            int x0=right,y0=top,x1=left,y1=bottom;
                            for(int y=bottom;y<top;y++) for(int x=left;x<right;x++) if(pixels[y*raw.width+x].a>=128)
                            {x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}
                            if(x1<x0 || y1<=y0) throw new InvalidOperationException("Empty keeper direction/frame: "+view+"/"+frame);
                            // Feet anchor body alignment independently of the arm's extent.
                            int footLeft=right,footRight=left;
                            for(int y=y0;y<=y0+(y1-y0)/5;y++) for(int x=x0;x<=x1;x++) if(pixels[y*raw.width+x].a>=128)
                            {footLeft=Math.Min(footLeft,x);footRight=Math.Max(footRight,x);}
                            float foot=(footLeft+footRight)*0.5f,scale=Height/(float)(y1-y0+1);
                            for(int y=10;y<10+Height;y++) for(int x=0;x<Cell;x++)
                            {
                                int sx=Mathf.FloorToInt(foot+(x-Cell/2+0.5f)/scale);
                                int sy=Mathf.Min(y1,y0+Mathf.FloorToInt((y-10+0.5f)/scale));
                                if(sx<left || sx>=right) continue;
                                var color=pixels[sy*raw.width+sx];if(color.a<128) continue;
                                color.a=255;
                                output[((7-view)*Cell+y)*(Cell*Frames)+frame*Cell+x]=color;
                            }
                        }
                    }
                    finally {Release(raw);}
                }
                atlas=new Texture2D(Cell*Frames,Cell*7,TextureFormat.RGBA32,false);
                atlas.SetPixels32(output);atlas.Apply(false,!readable);atlas.filterMode=FilterMode.Point;atlas.wrapMode=TextureWrapMode.Clamp;
                return atlas;
            }
        }
        public static int ViewIndex(Vector3 cameraDirection,Vector3 heading)
        {
            cameraDirection.y=heading.y=0;
            float angle=Vector3.Angle(cameraDirection,heading)*-Mathf.Sign(Vector3.Cross(cameraDirection,heading).y);
            return (-Mathf.RoundToInt(angle/45f)+8)%8;
        }
        private static void Release(UnityEngine.Object value)
        {if(Application.isPlaying) UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        public void Dispose() {if(atlas) Release(atlas);atlas=null;}
    }
}
