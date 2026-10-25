using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Development input for the Editor baker. NavMeshData itself ships as a
    // native Unity asset; this file never attempts to serialize native pointers.
    public static class NativeNavigationGeometry
    {
        public static NavMeshBuildSettings Settings()
        {
            var settings=NavMesh.GetSettingsByIndex(0);
            settings.agentRadius=0.30f;settings.agentHeight=1.9f;settings.agentClimb=0.5f;settings.agentSlope=45;
            settings.overrideVoxelSize=true;settings.voxelSize=0.12f;
            settings.overrideTileSize=true;settings.tileSize=128;
            return settings;
        }
        public static List<NavMeshBuildSource> Collect(Bounds bounds)
        {
            var sources=new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds,~0,NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
            RemoveMovingBodies(sources);
            return sources;
        }
        public static List<NavMeshBuildSource> Collect(Transform root)
        {
            var sources=new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(root,~0,NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
            RemoveMovingBodies(sources);
            return sources;
        }
        private static void RemoveMovingBodies(List<NavMeshBuildSource> sources)
        {
            sources.RemoveAll(s=>s.component && IsDynamicNavigationBody(s.component));
        }
        internal static bool IsDynamicNavigationBody(Component component)
        {
            if(!component)return false;
            if(component.GetComponentInParent<DaggerfallEntityBehaviour>() ||
                component.GetComponentInParent<MobilePersonNPC>() ||
                component.GetComponentInParent<CrowdMotion>() ||
                component.GetComponentInParent<StaticNPC>() ||
                component.GetComponentInParent<DaggerfallActionDoor>() ||
                component.GetComponentInParent<DaggerfallActionDoorSpecial>())return true;
            var action=component.GetComponentInParent<DaggerfallAction>();
            if(!action)return false;
            if(action.IsMoving)return true;
            switch(action.ActionFlag)
            {
                case DFBlock.RdbActionFlags.Translation:
                case DFBlock.RdbActionFlags.Rotation:
                case DFBlock.RdbActionFlags.PositiveX:
                case DFBlock.RdbActionFlags.NegativeX:
                case DFBlock.RdbActionFlags.PositiveY:
                case DFBlock.RdbActionFlags.NegativeY:
                case DFBlock.RdbActionFlags.PositiveZ:
                case DFBlock.RdbActionFlags.NegativeZ:
                    return true;
                default:return false;
            }
        }
        public static void Write(string file,List<NavMeshBuildSource> sources,Vector3 origin,Bounds localBounds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using(var stream=File.Create(file))Write(stream,sources,origin,localBounds);
        }
        // Shares the exact exported bytes with runtime compatibility hashing.
        // Hashing sources does not build a NavMesh or write a temporary file.
        internal static void Write(Stream stream,List<NavMeshBuildSource> sources,Vector3 origin,Bounds localBounds)
        {
            using(var output=new BinaryWriter(stream,Encoding.UTF8,true))
            {
                output.Write(2);Vector(output,localBounds.center);Vector(output,localBounds.size);
                output.Write(sources.Count);
                foreach(var source in sources)
                {
                    var matrix=Matrix4x4.Translate(-origin)*source.transform;
                    var terrain=source.sourceObject as TerrainData;
                    Mesh mesh=source.sourceObject as Mesh;
                    // Terrain is sampled only inside this module. Native terrain
                    // colliders otherwise duplicate an entire terrain per block.
                    if(terrain)
                    {
                        mesh=TerrainMesh(terrain,source.transform,origin,localBounds);
                        matrix=Matrix4x4.identity;
                    }
                    output.Write((int)(mesh?NavMeshBuildSourceShape.Mesh:source.shape));
                    for(int i=0;i<16;i++)output.Write(matrix[i]);
                    Vector(output,source.size);output.Write(source.area);
                    output.Write(mesh?(mesh.isReadable?1:2):0);
                    if(mesh && !mesh.isReadable)
                    {output.Write(mesh.name);output.Write(mesh.vertexCount);Vector(output,mesh.bounds.center);Vector(output,mesh.bounds.size);}
                    else if(mesh)
                    {
                        var vertices=mesh.vertices;var triangles=mesh.triangles;
                        output.Write(vertices.Length);foreach(var vertex in vertices)Vector(output,vertex);
                        output.Write(triangles.Length);foreach(int index in triangles)output.Write(index);
                    }
                    if(terrain && mesh)UnityEngine.Object.Destroy(mesh);
                }
            }
        }
        public static List<NavMeshBuildSource> Read(string file,out Bounds bounds,List<Mesh> owned,Func<string,int,Bounds,Mesh> resolve=null)
        {
            using(var input=new BinaryReader(File.OpenRead(file)))
            {
                if(input.ReadInt32()!=2)throw new InvalidDataException("Navigation geometry must be re-exported.");
                bounds=new Bounds(Vector(input),Vector(input));
                int count=input.ReadInt32();var result=new List<NavMeshBuildSource>(count);
                for(int n=0;n<count;n++)
                {
                    var source=new NavMeshBuildSource {shape=(NavMeshBuildSourceShape)input.ReadInt32()};
                    var matrix=new Matrix4x4();for(int i=0;i<16;i++)matrix[i]=input.ReadSingle();source.transform=matrix;
                    source.size=Vector(input);source.area=input.ReadInt32();
                    int meshKind=input.ReadInt32();
                    if(meshKind==2)
                    {
                        string name=input.ReadString();int vertices=input.ReadInt32();var meshBounds=new Bounds(Vector(input),Vector(input));
                        var mesh=resolve==null?null:resolve(name,vertices,meshBounds);
                        if(!mesh)throw new InvalidDataException("Cannot resolve exact mod collision mesh: "+name+"; vertices="+vertices);
                        source.sourceObject=mesh;
                    }
                    else if(meshKind==1)
                    {
                        int length=input.ReadInt32();var vertices=new Vector3[length];for(int i=0;i<length;i++)vertices[i]=Vector(input);
                        length=input.ReadInt32();var triangles=new int[length];for(int i=0;i<length;i++)triangles[i]=input.ReadInt32();
                        var mesh=new Mesh {indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=vertices,triangles=triangles};
                        mesh.RecalculateBounds();owned.Add(mesh);source.sourceObject=mesh;
                    }
                    result.Add(source);
                }
                return result;
            }
        }
        private static Mesh TerrainMesh(TerrainData terrain,Matrix4x4 world,Vector3 origin,Bounds bounds)
        {
            const float spacing=2;
            int nx=Mathf.CeilToInt(bounds.size.x/spacing)+1,nz=Mathf.CeilToInt(bounds.size.z/spacing)+1;
            var vertices=new Vector3[nx*nz];var triangles=new int[(nx-1)*(nz-1)*6];var inverse=world.inverse;
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
            {
                float px=Mathf.Lerp(bounds.min.x,bounds.max.x,(float)x/(nx-1)),pz=Mathf.Lerp(bounds.min.z,bounds.max.z,(float)z/(nz-1));
                var local=inverse.MultiplyPoint3x4(new Vector3(px,0,pz)+origin);
                float y=terrain.GetInterpolatedHeight(local.x/terrain.size.x,local.z/terrain.size.z);
                vertices[z*nx+x]=new Vector3(px,world.MultiplyPoint3x4(new Vector3(local.x,y,local.z)).y-origin.y,pz);
            }
            int at=0;for(int z=0;z<nz-1;z++)for(int x=0;x<nx-1;x++)
            {int a=z*nx+x,b=a+1,c=a+nx,d=c+1;triangles[at++]=a;triangles[at++]=c;triangles[at++]=b;triangles[at++]=b;triangles[at++]=c;triangles[at++]=d;}
            var mesh=new Mesh {indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,vertices=vertices,triangles=triangles};mesh.RecalculateBounds();return mesh;
        }
        private static void Vector(BinaryWriter writer,Vector3 value){writer.Write(value.x);writer.Write(value.y);writer.Write(value.z);}
        private static Vector3 Vector(BinaryReader reader){return new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());}
    }
    [Serializable] public sealed class NativeNavigationManifest
    {
        public int version=1;
        public string profile;
        public List<NativeNavigationModule> modules=new List<NativeNavigationModule>();
        public static NativeNavigationManifest Read(string json)
        {return (NativeNavigationManifest)DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Deserialize(typeof(NativeNavigationManifest),json);}
        public string Write()
        {
            string json=DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Serialize(typeof(NativeNavigationManifest),this);
            var restored=Read(json);
            if(restored==null || restored.modules==null || restored.modules.Count!=modules.Count)throw new InvalidDataException("Navigation manifest lost modules during serialization.");
            for(int i=0;i<modules.Count;i++)
                if(restored.modules[i].asset!=modules[i].asset || restored.modules[i].scene!=modules[i].scene ||
                    restored.modules[i].variant!=modules[i].variant ||
                    restored.modules[i].collisionProviders!=modules[i].collisionProviders ||
                    restored.modules[i].position!=modules[i].position || restored.modules[i].size!=modules[i].size)
                    throw new InvalidDataException("Navigation manifest changed module placement.");
            return json;
        }
    }
    [Serializable] public sealed class NativeNavigationModule
    {
        public string scene,asset,geometry,variant;
        // Optional digest of native provider/prefab identities for unreadable
        // collision meshes. Readable source vertices remain in the geometry SHA.
        public string collisionProviders;
        public Vector3 position,size;
    }
}
