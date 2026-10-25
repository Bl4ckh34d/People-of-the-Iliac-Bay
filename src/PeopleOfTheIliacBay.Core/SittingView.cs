using System;

namespace PeopleOfTheIliacBay.Core
{
    public struct SittingAlignment
    {
        public double PixelScale,CenterX,CenterY;
    }
    public static class SittingView
    {
        private static bool Finite(double value) {return !double.IsNaN(value)&&!double.IsInfinity(value);}
        public static int Select(double head,double side)
        {
            if(!Finite(head)||!Finite(side))throw new ArgumentException("Invalid sitting view direction.");
            return (8-(int)Math.Round(Math.Atan2(side,head)*4/Math.PI))%8;
        }
        public static double NativePixelScale(int height,int scaleY,double scaleDivisor,double worldScale)
        {
            if(height<1||!Finite(scaleDivisor)||scaleDivisor<=0||!Finite(worldScale)||worldScale<=0)
                throw new ArgumentException("Invalid native sprite scale.");
            double pixels=height+(int)(height*(scaleY/scaleDivisor));
            if(pixels<=0)throw new ArgumentException("Invalid native sprite height.");
            return pixels*worldScale/height;
        }
        public static SittingAlignment Align(int width,int height,double seatX,double seatY,double groundY,double seatHeight,double pixelScale)
        {
            if(width<1||height<1||!Finite(seatX)||!Finite(seatY)||!Finite(groundY)||!Finite(seatHeight)||
                seatX<0||seatX>width||seatY<0||groundY>height||groundY-seatY<5||seatHeight<0.2||seatHeight>1.2||!Finite(pixelScale)||pixelScale<=0)
                throw new ArgumentException("Invalid sitting pivots or seat height.");
            // DFU's quad maps its positive X vertex to the image's left edge.
            // Furniture places the pelvis; it must never resize the person.
            // Exported hip anchors are estimates. Lift the whole sprite when
            // they would bury its feet; never squash it to fit low furniture.
            double centerY=Math.Max(seatHeight-(height*0.5-seatY)*pixelScale,(groundY-height*0.5)*pixelScale);
            return new SittingAlignment {PixelScale=pixelScale,CenterX=(seatX-width*0.5)*pixelScale,CenterY=centerY};
        }
    }
}
