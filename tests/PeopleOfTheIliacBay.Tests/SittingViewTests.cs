using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SittingViewTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Chair height must not change a seated NPC's body scale",()=>{
                double pixel=SittingView.NativePixelScale(166,-134,256,0.025);
                Check.Near(2.0/166,pixel);
                var low=SittingView.Align(71,166,36,117,165,0.35,pixel);
                var high=SittingView.Align(71,166,36,117,165,0.75,pixel);
                Check.Near(low.PixelScale,high.PixelScale);
                Check.True(low.CenterY+(83-165)*pixel>=-0.000001,"Low chairs must not bury the feet.");
                Check.Near(0.75,high.CenterY+(83-117)*pixel);
            }));
            cases.Add(("Sitting views use all eight native bearings without mirrored frame reuse",()=>{
                double[] head={1,1,0,-1,-1,-1,0,1};double[] side={0,-1,-1,-1,0,1,1,1};
                for(int i=0;i<8;i++)Check.Equal(i,SittingView.Select(head[i],side[i]));
                Check.Equal(0,SittingView.Select(0,0));Check.Throws<ArgumentException>(()=>SittingView.Select(double.NaN,0));
            }));
            cases.Add(("Sitting alignment anchors pelvis without changing supplied native pixel scale",()=>{
                var fit=SittingView.Align(72,163,55,115,162,0.47,0.01);
                Check.Near(0.01,fit.PixelScale);Check.Near(0.19,fit.CenterX);Check.Near(0.805,fit.CenterY);
                Check.Near(0,fit.CenterX+(36-55)*fit.PixelScale);
                Check.Near(0.47,fit.CenterY+(81.5-115)*fit.PixelScale);
                Check.Near(0,fit.CenterY+(81.5-162)*fit.PixelScale);
                Check.Throws<ArgumentException>(()=>SittingView.Align(72,163,55,162,162,0.47,0.01));
                Check.Throws<ArgumentException>(()=>SittingView.Align(72,163,55,115,162,double.NaN,0.01));
                Check.Throws<ArgumentException>(()=>SittingView.Align(72,163,55,115,162,0.47,double.NaN));
            }));
        }
    }
}
