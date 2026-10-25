using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Immutable snapshot of the provider's road and track layers.
    public sealed class OverlandRoadNetwork
    {
        private readonly byte[] links;
        private readonly int width, height;
        public OverlandRoadNetwork(byte[] roads, byte[] tracks, int width, int height)
        {
            if(width<=0 || height<=0 || roads==null || tracks==null ||
                (long)width*height!=roads.Length || roads.Length!=tracks.Length)
                throw new ArgumentException("Invalid road network layers.");
            this.width=width; this.height=height;
            links=new byte[roads.Length];
            for(int i=0;i<links.Length;i++) links[i]=(byte)(roads[i]|tracks[i]);
        }
        public List<RoutePoint> Find(int fromX,int fromZ,int toX,int toZ,int maximumExpanded)
        { return OverlandRoads.Find(links,width,height,fromX,fromZ,toX,toZ,maximumExpanded); }
        public OverlandRoads.Search BeginSearch(int fromX,int fromZ,int toX,int toZ,int maximumExpanded)
        { return OverlandRoads.Begin(links,width,height,fromX,fromZ,toX,toZ,maximumExpanded); }
    }
}
