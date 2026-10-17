using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    // Reads original block navigation and service metadata without constructing
    // Unity objects, changing player location or spawning native actors.
    public sealed class UnloadedSettlementSurvey : IWalkGrid
    {
        public sealed class Result {public UnloadedSettlementSurvey Grid;}
        public sealed class RouteResult {public IReadOnlyList<RoutePoint> Points;}
        public sealed class Gate
        {
            public Vector3 Center,Inside,Outside;
        }
        private readonly List<Gate> gates=new List<Gate>();
        public IReadOnlyList<Gate> Gates {get {return gates.AsReadOnly();}}
        public sealed class Entrance
        {
            public int BuildingKey;
            public int BlockX,BlockY;
            public StaticDoor NativeDoor;
            public DFBlock SourceBlock;
            // Relative to the RMB block, exactly like native static doors.
            public Vector3 Position,Normal;
        }
        private readonly List<Entrance> entrances=new List<Entrance>();
        public IReadOnlyList<Entrance> Entrances {get {return entrances.AsReadOnly();}}
        private readonly byte[,] costs;
        private readonly MeshWalkClearance clearance=new MeshWalkClearance();
        private readonly MeshWalkClearance openGateClearance=new MeshWalkClearance(),closedGateClearance=new MeshWalkClearance();
        private Vector3 layoutOrigin;
        private readonly List<BuildingSummary> buildings=new List<BuildingSummary>();
        public IReadOnlyList<BuildingSummary> Buildings {get {return buildings.AsReadOnly();}}
        public int Width {get {return costs.GetLength(0);}}
        public int Height {get {return costs.GetLength(1);}}
        public int SettlementId {get;private set;}
        // Native tile-space bounds, including its clearance margin. Full RMB
        // dimensions include blend tiles and cannot identify a wilderness join.
        public Rect LocationRect {get;private set;}
        public int MapPixelX {get;private set;}
        public int MapPixelY {get;private set;}
        public Vector3 LocalRoadPoint(RoutePoint point)
        {
            // Validate against the same world bounds used by road actors.
            var terrainPoint=new RoadTerrainPoint(point);
            float side=MapsFile.WorldMapTerrainDim*MeshReader.GlobalScale;
            return new Vector3((float)(terrainPoint.PixelX-MapPixelX+terrainPoint.U)*side,0,
                (float)(MapPixelY-terrainPoint.PixelY+terrainPoint.V)*side);
        }
        public sealed class RoadAccess
        {
            public GridCell StreetCell;
            public RoutePoint RoadPoint;
            public List<Vector3> Points;
            public float Metres;
        }
        public sealed class RoadAccessResult {public IReadOnlyList<RoadAccess> Accesses;}
        public sealed class BuildingRoadRoute
        {
            public Entrance Door;
            public IReadOnlyList<RoutePoint> Points;
            public int BuildingKey;
            public RoutePoint RoadPoint;
            public double Metres,EntryGateMetres;
            public bool Walled;
            public ResidentDestination Visit(ResidentVisitPurpose purpose,int openMinute,int closeMinute,double dwellSeconds,double secondsPerMetre)
            {
                if(Points==null || double.IsNaN(secondsPerMetre) || double.IsInfinity(secondsPerMetre) || secondsPerMetre<=0)
                    throw new ArgumentException("A completed route and positive walking time are required.");
                var destination=new ResidentDestination {Walled=Walled,ExitWalkSeconds=Metres*secondsPerMetre,
                    EntryGateSeconds=EntryGateMetres*secondsPerMetre,
                    ExitGateSeconds=Walled ? (Metres-EntryGateMetres)*secondsPerMetre : 0};
                var stop=new ResidentVisitStop {BuildingKey=BuildingKey,Purpose=purpose,OpenMinute=openMinute,CloseMinute=closeMinute,
                    WalkSeconds=Metres*secondsPerMetre,DwellSeconds=dwellSeconds};
                foreach(var point in Points) stop.Route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                for(int i=Points.Count-1;i>=0;i--) destination.ExitRoute.Add(new SavedRoutePoint {X=Points[i].X,Z=Points[i].Z});
                destination.Stops.Add(stop);
                return ResidentVisitJourney.Copy(destination);
            }
        }
        // Road-to-door direction; reverse the returned path for a departure.
        // As with road accesses, this is model geometry, not a terrain survey.
        public IEnumerator ReadRoadBuildingRouteSteps(RoadAccess access,Entrance entrance,BuildingRoadRoute result,Func<bool> stillValid)
        {
            if(access==null || entrance==null || result==null || stillValid==null) throw new ArgumentNullException();
            result.Points=null;
            GridCell doorCell;
            if(!stillValid() || !TryApproach(entrance,out doorCell)) yield break;
            var streets=new RouteResult();
            yield return ReadRouteSteps(access.StreetCell,doorCell,streets,stillValid,128,true);
            if(!stillValid() || streets.Points==null || streets.Points.Count==0) yield break;
            var points=new List<RoutePoint>();
            for(int i=access.Points.Count-1;i>=0;i--) Append(points,access.Points[i]);
            foreach(var point in streets.Points) Append(points,new Vector3((float)point.X,0,(float)point.Z));
            var normal=entrance.Normal; normal.y=0;
            Append(points,LocalEntrance(entrance)+normal.normalized*0.6f);
            double metres=0;
            for(int i=1;i<points.Count;i++)
            {
                var a=points[i-1]; var b=points[i];
                if(!ClearWalk(new Vector3((float)a.X,0,(float)a.Z),new Vector3((float)b.X,0,(float)b.Z),true)) yield break;
                double dx=b.X-a.X,dz=b.Z-a.Z; metres+=Math.Sqrt(dx*dx+dz*dz);
                if(i%128==0) {yield return null; if(!stillValid()) yield break;}
            }
            if(metres<=0 || !stillValid()) yield break;
            result.Door=entrance; result.BuildingKey=entrance.BuildingKey; result.RoadPoint=access.RoadPoint;
            result.Metres=metres;
            result.Walled=gates.Count>0;
            result.EntryGateMetres=gates.Count>0 ? Vector3.Distance(access.Points[access.Points.Count-1],access.Points[access.Points.Count-2]) : 0;
            result.Points=points.AsReadOnly();
        }
        public IEnumerator ReadBuildingRouteSteps(Entrance from,Entrance to,RouteResult result,Func<bool> stillValid)
        {
            if(from==null || to==null || result==null || stillValid==null) throw new ArgumentNullException();
            result.Points=null;
            GridCell start,end;
            if(!stillValid() || from.BuildingKey==to.BuildingKey || !TryApproach(from,out start) || !TryApproach(to,out end)) yield break;
            var streets=new RouteResult();
            yield return ReadRouteSteps(start,end,streets,stillValid);
            if(!stillValid() || streets.Points==null || streets.Points.Count==0) yield break;
            var points=new List<RoutePoint>();
            var normal=from.Normal; normal.y=0;
            Append(points,LocalEntrance(from)+normal.normalized*0.6f);
            foreach(var point in streets.Points) Append(points,new Vector3((float)point.X,0,(float)point.Z));
            normal=to.Normal; normal.y=0;
            Append(points,LocalEntrance(to)+normal.normalized*0.6f);
            for(int i=1;i<points.Count;i++)
            {
                var a=points[i-1]; var b=points[i];
                if(!ClearWalk(new Vector3((float)a.X,0,(float)a.Z),new Vector3((float)b.X,0,(float)b.Z))) yield break;
                if(i%128==0) {yield return null; if(!stillValid()) yield break;}
            }
            if(stillValid() && points.Count>=2) result.Points=points.AsReadOnly();
        }
        private static void Append(List<RoutePoint> points,Vector3 point)
        {
            if(points.Count>0)
            {
                var previous=points[points.Count-1];
                if(Math.Abs(previous.X-point.x)<0.0001 && Math.Abs(previous.Z-point.z)<0.0001) return;
            }
            points.Add(new RoutePoint(point.x,point.z));
        }
        // Candidate geometry only: callers must validate terrain and non-model
        // obstacles before installing this as an actor's physical route.
        public IEnumerator ReadRoadAccessSteps(IReadOnlyList<RoutePoint> road,RoadAccessResult result,Func<bool> stillValid)
        {
            if(result==null || stillValid==null) throw new ArgumentNullException();
            result.Accesses=null;
            if(road==null || road.Count<2 || road[0].X!=MapPixelX || road[0].Z!=MapPixelY)
                throw new ArgumentException("Road must start at this settlement's map pixel.");
            yield return BackgroundWork.Wait(stillValid);
            if(!stillValid()) yield break;
            var starts=new List<RoadAccess>();
            foreach(var gate in gates)
            {
                if(!stillValid()) yield break;
                GridCell cell;
                if(TryGateAccess(gate,out cell)) starts.Add(new RoadAccess {StreetCell=cell,
                    Points=new List<Vector3> {LocalCell(cell),gate.Inside,gate.Outside}});
                yield return null;
            }
            if(gates.Count==0)
                for(int side=0;side<4;side++)
                {
                    int length=side<2 ? Width : Height,begin=-1;
                    for(int n=0;n<=length;n++)
                    {
                        var cell=BoundaryCell(side,n);
                        bool isRoad=n<length && Cost(cell.X,cell.Z)==1;
                        if(isRoad && begin<0) begin=n;
                        if(!isRoad && begin>=0)
                        {
                            cell=BoundaryCell(side,(begin+n-1)/2); begin=-1;
                            starts.Add(new RoadAccess {StreetCell=cell,Points=new List<Vector3> {LocalCell(cell)}});
                        }
                    }
                    if(!stillValid()) yield break;
                    yield return null;
                }
            var found=new List<RoadAccess>();
            for(int step=1;step<=9;step++)
            {
                double fraction=step*0.05;
                var join=new RoutePoint(road[0].X+(road[1].X-road[0].X)*fraction,road[0].Z+(road[1].Z-road[0].Z)*fraction);
                var uv=new RoadTerrainPoint(join);
                if(uv.PixelX!=MapPixelX || uv.PixelY!=MapPixelY || LocationRect.Contains(new Vector2((float)uv.U*MapsFile.WorldMapTileDim,(float)uv.V*MapsFile.WorldMapTileDim))) continue;
                var target=LocalRoadPoint(join);
                foreach(var start in starts)
                {
                    if(!stillValid()) yield break;
                    var last=start.Points[start.Points.Count-1];
                    if(Vector3.Distance(last,target)<=512 && ClearWalk(last,target,true))
                    {
                        var points=new List<Vector3>(start.Points); points.Add(target);
                        float metres=0;
                        for(int i=1;i<points.Count;i++) metres+=Vector3.Distance(points[i-1],points[i]);
                        found.Add(new RoadAccess {StreetCell=start.StreetCell,RoadPoint=join,Points=points,Metres=metres});
                    }
                    yield return null;
                }
            }
            found.Sort((a,b)=>a.Metres.CompareTo(b.Metres));
            if(stillValid()) result.Accesses=found.AsReadOnly();
        }
        private GridCell BoundaryCell(int side,int n)
        {
            if(side==0) return new GridCell(n,0);
            if(side==1) return new GridCell(n,Height-1);
            if(side==2) return new GridCell(0,n);
            return new GridCell(Width-1,n);
        }
        private UnloadedSettlementSurvey(int town,int width,int height)
        {SettlementId=town; costs=new byte[width*64,height*64];}
        public int Cost(int x,int z)
        {return x<0 || z<0 || x>=Width || z>=Height ? 0 : costs[x,z];}
        public Vector3 LocalCell(GridCell cell)
        {
            if(cell.X<0 || cell.Z<0 || cell.X>=Width || cell.Z>=Height)
                throw new ArgumentOutOfRangeException(nameof(cell));
            float tile=64*MeshReader.GlobalScale;
            return layoutOrigin+new Vector3((cell.X+0.5f)*tile,0,(Height-cell.Z-0.5f)*tile);
        }
        public Vector3 LocalEntrance(Entrance entrance)
        {
            if(entrance==null) throw new ArgumentNullException(nameof(entrance));
            // StreamingWorld raises exterior blocks by two classic units.
            return layoutOrigin+new Vector3(entrance.BlockX*RMBLayout.RMBSide,2*MeshReader.GlobalScale,entrance.BlockY*RMBLayout.RMBSide)+entrance.Position;
        }
        public bool ModelClear(Vector3 from,Vector3 to,float radius,bool gatesOpen=false)
        {return clearance.Clear(from,to,radius) && (gatesOpen ? openGateClearance : closedGateClearance).Clear(from,to,radius);}
        // Matches the two collision bands used by loaded TownGeometry routes.
        // Terrain height is supplied separately when actors are instantiated.
        public bool ClearWalk(Vector3 from,Vector3 to,bool gatesOpen=false)
        {
            return ModelClear(new Vector3(from.x,0.4f,from.z),new Vector3(to.x,0.4f,to.z),0.2f,gatesOpen) &&
                ModelClear(new Vector3(from.x,1,from.z),new Vector3(to.x,1,to.z),0.2f,gatesOpen);
        }
        private sealed class ModelWalkGrid : IWalkEdgeGrid
        {
            private readonly UnloadedSettlementSurvey source;
            private readonly bool gatesOpen;
            private readonly Dictionary<GridCell,int> cells=new Dictionary<GridCell,int>();
            private readonly Dictionary<long,bool> edges=new Dictionary<long,bool>();
            public ModelWalkGrid(UnloadedSettlementSurvey source,bool gatesOpen) {this.source=source; this.gatesOpen=gatesOpen;}
            public int Width {get {return source.Width;}}
            public int Height {get {return source.Height;}}
            public int Cost(int x,int z)
            {
                var cell=new GridCell(x,z); int cost;
                if(cells.TryGetValue(cell,out cost)) return cost;
                cost=source.Cost(x,z);
                if(cost>0)
                {
                    var point=source.LocalCell(cell);
                    if(!source.ClearWalk(point,point,gatesOpen)) cost=0;
                }
                cells.Add(cell,cost); return cost;
            }
            public bool CanStep(GridCell from,GridCell to)
            {
                int a=from.Z*Width+from.X,b=to.Z*Width+to.X;
                long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b); bool clear;
                if(!edges.TryGetValue(key,out clear))
                {
                    clear=source.ClearWalk(source.LocalCell(from),source.LocalCell(to),gatesOpen);
                    edges.Add(key,clear);
                }
                return clear;
            }
        }
        public bool TryApproach(Entrance entrance,out GridCell result)
        {
            result=default(GridCell);
            var normal=entrance.Normal; normal.y=0;
            if(normal.sqrMagnitude<0.01f) return false;
            var door=LocalEntrance(entrance)+normal.normalized*0.6f;
            if(Math.Abs(door.y-1)>1.5f) return false;
            return TryApproachPoint(door,false,out result);
        }
        public bool TryGateAccess(Gate gate,out GridCell insideCell)
        {
            insideCell=default(GridCell);
            return gate!=null && ClearWalk(gate.Outside,gate.Inside,true) && TryApproachPoint(gate.Inside,true,out insideCell);
        }
        private bool TryApproachPoint(Vector3 door,bool gatesOpen,out GridCell result)
        {
            result=default(GridCell);
            float tile=64*MeshReader.GlobalScale;
            int nearX=Mathf.FloorToInt((door.x-layoutOrigin.x)/tile);
            int nearZ=Mathf.FloorToInt(Height-(door.z-layoutOrigin.z)/tile);
            float best=float.PositiveInfinity;
            var from=new Vector3(door.x,1,door.z);
            for(int z=Math.Max(0,nearZ-8);z<=Math.Min(Height-1,nearZ+8);z++)
                for(int x=Math.Max(0,nearX-8);x<=Math.Min(Width-1,nearX+8);x++)
                {
                    if(Cost(x,z)==0) continue;
                    var cell=new GridCell(x,z); var target=LocalCell(cell)+Vector3.up;
                    float distance=(target-from).sqrMagnitude;
                    if(distance>64 || distance>=best || !ClearWalk(from,target,gatesOpen)) continue;
                    result=cell; best=distance;
                }
            return !float.IsPositiveInfinity(best);
        }
        // Native navigation costs plus classic-model collision. Terrain,
        // replacement prefabs, billboards and gate state remain separate adapters.
        public IEnumerator ReadRouteSteps(GridCell from,GridCell to,RouteResult result,Func<bool> stillValid,int workPerFrame=128,bool gatesOpen=false)
        {
            if(result==null || stillValid==null) throw new ArgumentNullException();
            if(workPerFrame<1) throw new ArgumentOutOfRangeException(nameof(workPerFrame));
            result.Points=null;
            yield return BackgroundWork.Wait(stillValid);
            if(!stillValid()) yield break;
            var search=GridPathfinder.Begin(new ModelWalkGrid(this,gatesOpen),from,to,30000);
            while(!search.IsComplete)
            {
                if(!stillValid()) yield break;
                // Model collision checks make equal node counts unequal CPU work.
                // Preserve deterministic search order, but yield after a soft 4 ms
                // budget. A single small batch can exceed that budget.
                long sliceStart=System.Diagnostics.Stopwatch.GetTimestamp();
                int remaining=workPerFrame;
                do
                {
                    int batch=Math.Min(8,remaining);
                    search.Step(batch);remaining-=batch;
                }
                while(!search.IsComplete && remaining>0 &&
                    (System.Diagnostics.Stopwatch.GetTimestamp()-sliceStart)*1000d/System.Diagnostics.Stopwatch.Frequency<4);
                yield return null;
            }
            var points=new List<RoutePoint>();
            int converted=0;
            foreach(var cell in search.Path)
            {
                if(!stillValid()) yield break;
                var point=LocalCell(cell); points.Add(new RoutePoint(point.x,point.z));
                if(++converted%workPerFrame==0) yield return null;
            }
            if(stillValid()) result.Points=points.AsReadOnly();
        }
        public static IEnumerator ReadSteps(DFLocation location,Result result)
        {
            if(result==null) throw new ArgumentNullException(nameof(result));
            result.Grid=null;
            int width=location.Exterior.ExteriorData.Width,height=location.Exterior.ExteriorData.Height;
            if(width<1 || width>8 || height<1 || height>8) throw new ArgumentException("Invalid settlement dimensions.");
            var blocks=RMBLayout.GetLocationBuildingData(location);
            if(blocks==null || blocks.Length!=width*height) throw new ArgumentException("Missing settlement blocks.");
            var grid=new UnloadedSettlementSurvey(location.MapTableData.MapId,width,height);
            var mapPixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
            grid.MapPixelX=mapPixel.X; grid.MapPixelY=mapPixel.Y;
            var tileOrigin=TerrainHelper.GetLocationTerrainTileOrigin(location);
            grid.layoutOrigin=new Vector3(tileOrigin.X*RMBLayout.RMBTileSide,0,tileOrigin.Y*RMBLayout.RMBTileSide);
            int xmin=int.MaxValue,ymin=int.MaxValue,xmax=0,ymax=0;
            for(int by=0;by<height;by++) for(int bx=0;bx<width;bx++)
            {
                var block=blocks[by*width+bx];
                for(int ty=0;ty<RMBLayout.RMBTilesPerBlock;ty++) for(int tx=0;tx<RMBLayout.RMBTilesPerBlock;tx++)
                {
                    if(block.RmbBlock.FldHeader.GroundData.GroundTiles[tx,RMBLayout.RMBTilesPerBlock-1-ty].TextureRecord>=56) continue;
                    int x=tileOrigin.X+bx*RMBLayout.RMBTilesPerBlock+tx;
                    int y=tileOrigin.Y+by*RMBLayout.RMBTilesPerBlock+ty;
                    xmin=Math.Min(xmin,x); xmax=Math.Max(xmax,x);
                    ymin=Math.Min(ymin,y); ymax=Math.Max(ymax,y);
                }
                var blockMatrix=Matrix4x4.Translate(grid.layoutOrigin+new Vector3(bx*RMBLayout.RMBSide,2*MeshReader.GlobalScale,by*RMBLayout.RMBSide));
                for(int y=0;y<64;y++) for(int x=0;x<64;x++)
                {
                    if(block.RmbBlock.FldHeader.AutoMapData[y*64+x]!=0) continue;
                    byte tile=(byte)block.RmbBlock.FldHeader.GroundData.GroundTiles[x/4,y/4].TextureRecord;
                    int weight=TileWeight(tile);
                    grid.costs[bx*64+x,(height-1-by)*64+y]=(byte)(weight==0 ? 0 : 16-weight);
                }
                var summaries=RMBLayout.GetBuildingData(block,bx,by);
                if(summaries!=null) grid.buildings.AddRange(summaries);
                int record=0;
                foreach(var sub in block.RmbBlock.SubRecords)
                {
                    var position=new Vector3(sub.XPos,0,BlocksFile.RMBDimension-sub.ZPos)*MeshReader.GlobalScale;
                    var rotation=Quaternion.Euler(0,-sub.YRotation/BlocksFile.RotationDivisor,0);
                    var parent=Matrix4x4.TRS(position,rotation,Vector3.one);
                    foreach(var obj in sub.Exterior.Block3dObjectRecords)
                    {
                        ModelData model;
                        if(!DaggerfallUnity.Instance.MeshReader.GetModelData(obj.ModelIdNum,out model)) continue;
                        var local=new Vector3(obj.XPos,-obj.YPos,obj.ZPos)*MeshReader.GlobalScale;
                        var angles=new Vector3(-obj.XRotation/BlocksFile.RotationDivisor,-obj.YRotation/BlocksFile.RotationDivisor,-obj.ZRotation/BlocksFile.RotationDivisor);
                        var matrix=parent*Matrix4x4.TRS(local,Quaternion.Euler(angles),RMBLayout.GetModelScaleVector(obj));
                        grid.AddModel(obj.ModelIdNum,model,blockMatrix*matrix);
                        if(model.Doors==null) continue;
                        foreach(var door in GameObjectHelper.GetStaticDoors(ref model,block.Index,record,matrix))
                            if(door.doorType==DoorTypes.Building)
                            {
                                var nativeDoor=door;
                                nativeDoor.buildingKey=BuildingDirectory.MakeBuildingKey((byte)bx,(byte)by,(byte)record);
                                grid.entrances.Add(new Entrance {
                                    BuildingKey=nativeDoor.buildingKey,NativeDoor=nativeDoor,SourceBlock=block,
                                    BlockX=bx,BlockY=by,
                                    Position=matrix.MultiplyPoint3x4(door.centre),
                                    Normal=matrix.MultiplyVector(door.normal).normalized });
                            }
                    }
                    record++;
                    // Model reads can be expensive on a cold cache. Never batch a city
                    // worth of model decoding into one frame.
                    yield return null;
                }
                foreach(var obj in block.RmbBlock.Misc3dObjectRecords)
                {
                    ModelData model;
                    if(!DaggerfallUnity.Instance.MeshReader.GetModelData(obj.ModelIdNum,out model)) continue;
                    var local=new Vector3(obj.XPos,-obj.YPos-4,obj.ZPos+BlocksFile.RMBDimension)*MeshReader.GlobalScale;
                    var angles=new Vector3(-obj.XRotation/BlocksFile.RotationDivisor,-obj.YRotation/BlocksFile.RotationDivisor,-obj.ZRotation/BlocksFile.RotationDivisor);
                    grid.AddModel(obj.ModelIdNum,model,blockMatrix*Matrix4x4.TRS(local,Quaternion.Euler(angles),RMBLayout.GetModelScaleVector(obj)));
                    yield return null;
                }
                yield return null;
            }
            grid.buildings.Sort((a,b)=>a.buildingKey.CompareTo(b.buildingKey));
            int margin=location.MapTableData.LocationType==DFRegion.LocationTypes.TownCity ? 3 : 2;
            grid.LocationRect=Rect.MinMaxRect(xmin-margin,ymin-margin,xmax+margin,ymax+margin);
            result.Grid=grid;
        }
        private void AddModel(uint modelId,ModelData model,Matrix4x4 matrix)
        {
            if(modelId!=RMBLayout.CityGateOpenModelID && modelId!=RMBLayout.CityGateClosedModelID)
            {clearance.Add(model,matrix); return;}
            ModelData open,closed;
            var reader=DaggerfallUnity.Instance.MeshReader;
            if(!reader.GetModelData(RMBLayout.CityGateOpenModelID,out open) ||
                !reader.GetModelData(RMBLayout.CityGateClosedModelID,out closed) || open.Vertices==null || open.Vertices.Length==0)
                throw new InvalidOperationException("Native gate collision models unavailable.");
            openGateClearance.Add(open,matrix); closedGateClearance.Add(closed,matrix);
            var bounds=new Bounds(open.Vertices[0],Vector3.zero);
            foreach(var point in open.Vertices) bounds.Encapsulate(point);
            bool alongX=bounds.size.x<bounds.size.z;
            var axis=alongX ? Vector3.right : Vector3.forward;
            var transformed=matrix.MultiplyVector(axis);
            var normal=transformed.normalized;
            var center=matrix.MultiplyPoint3x4(bounds.center);
            float tile=64*MeshReader.GlobalScale;
            var cityCenter=layoutOrigin+new Vector3(Width*tile/2,0,Height*tile/2);
            if(Vector3.Dot(normal,center-cityCenter)<0) normal=-normal;
            float extent=(alongX ? bounds.extents.x : bounds.extents.z)*transformed.magnitude+1;
            var inside=center-normal*extent; inside.y=0;
            var outside=center+normal*extent; outside.y=0;
            gates.Add(new Gate {Center=center,Inside=inside,Outside=outside});
        }
        // Same terrain preferences as DFU CityNavigation.SetRMBData/GetTileWeight.
        // Native comparison covers every cell to detect adapter drift.
        private static int TileWeight(byte tile)
        {
            switch(tile)
            {
                case 0: case 5: case 6: case 20: case 21: case 30: case 31: return 0;
                case 3: return 4;
                case 1: return 6;
                case 2: return 12;
                case 46: case 47: case 55: return 15;
                default: return 7;
            }
        }
    }
}
