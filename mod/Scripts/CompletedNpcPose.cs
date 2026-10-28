using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Runs after native billboards and concealment. Owns appearance, never movement/damage.
    [DefaultExecutionOrder(200)]
    public sealed class CompletedNpcPose : MonoBehaviour
    {
        private static readonly FieldInfo Sizes=typeof(MobilePersonBillboard).GetField("recordSizes",BindingFlags.Instance|BindingFlags.NonPublic);
        private ResidentActor actor;
        private MeshRenderer original,rendered;
        private Transform native;
        private GameObject visual;
        private Mesh mesh;
        private Material material;
        private DaggerfallEntityBehaviour body;
        private readonly Vector2[] uv=new Vector2[4];
        private bool originalForceOff,ownsRenderer,running,moving,keeperWasActive;
        private DirectionalKeeperPose keeper;
        private float swingUntil,clock,phaseStarted;
        private string action;
        private Vector3 heading;
        public bool Active {get{return ownsRenderer;}}
        public int Orientation {get;private set;}
        public int Frame {get;private set;}
        public static CompletedNpcPose Bind(ResidentActor owner)
        {
            var pose=owner.GetComponent<CompletedNpcPose>();
            if(!pose)pose=owner.gameObject.AddComponent<CompletedNpcPose>();
            pose.actor=owner;pose.heading=owner.transform.forward;return pose;
        }
        public void Movement(bool isMoving,bool isRunning,Vector3 direction)
        {moving=isMoving;running=isMoving&&isRunning;if(direction.sqrMagnitude>0.00001f)heading=direction.normalized;}
        public void Running(bool value){running=value;if(!value&&action=="run")Release();}
        public void Swing(Vector3 direction)
        {
            swingUntil=clock+4f/7f;phaseStarted=clock;action="melee";
            direction.y=0;if(direction.sqrMagnitude>0.00001f)heading=direction.normalized;
        }
        // Release before other pose owners capture the original renderer state.
        public void Release()
        {
            if(ownsRenderer&&original)original.forceRenderingOff=originalForceOff;
            if(ownsRenderer&&keeper&&keeperWasActive)keeper.SetStanding(true);
            ownsRenderer=false;if(visual)visual.SetActive(false);
        }
        private bool Swimming()
        {
            var game=GameManager.Instance;
            if(!game||!game.PlayerEnterExit||!game.PlayerEnterExit.IsPlayerInsideDungeon)return false;
            var dungeon=game.PlayerEnterExit.Dungeon;if(!dungeon||!transform.IsChildOf(dungeon.transform))return false;
            int block=dungeon.GetPlayerBlockIndex(transform.position);DFLocation.DungeonBlock data;
            if(block<0||!dungeon.GetBlockData(block,out data)||data.WaterLevel==10000)return false;
            // Query the NPC's block, not the player's cached water level.
            return transform.position.y+0.30f<data.WaterLevel*-MeshReader.GlobalScale;
        }
        private void LateUpdate()
        {
            if(!GameManager.IsGamePaused)clock+=Time.deltaTime;
            if(!actor||actor.Resident==null||!actor.Resident.IsAlive||actor.IsSleeping||actor.IsSeated||CompletedNpcArt.Current==null)
            {Release();return;}
            var jump=GetComponent<NativeDungeonJumpTraversal>();var climb=GetComponent<NativeDungeonSurfaceTraversal>();
            string requested=NpcSpritePolicy.Choose(true,false,false,Swimming(),jump&&jump.Active,climb&&climb.Active&&climb.Outcome=="climbing",clock<swingUntil,running);
            if(actor.IsConversing)requested=actor.IsStationary?"idle":null;
            if(requested==null&&actor.IsStationary)requested=moving?"walk":"idle";
            var atlas=CompletedNpcArt.Current.ForActor(actor,requested);
            if(atlas==null){Release();action=null;return;}
            if(!EnsureVisual()){Release();return;}
            var camera=GameManager.Instance.MainCamera;if(!camera){Release();return;}
            actor.ReleaseStandingForCompleteArt();
            if(!ownsRenderer)
            {
                keeper=GetComponent<DirectionalKeeperPose>();keeperWasActive=keeper&&keeper.Active;
                if(keeperWasActive)keeper.SetStanding(false);
                originalForceOff=original.forceRenderingOff;ownsRenderer=true;visual.SetActive(true);
            }
            if(action!=requested){action=requested;phaseStarted=clock;}
            var definition=atlas.Definition;
            Vector3 forward=actor.IsStationary?heading:actor.transform.forward;
            Vector3 right=Vector3.Cross(Vector3.up,forward);
            var toward=camera.transform.position-native.position;toward.y=0;
            Orientation=SittingView.Select(Vector3.Dot(toward,forward),Vector3.Dot(toward,right));
            Frame=requested=="jump"&&jump?NpcSpritePolicy.JumpPhase(jump.Progress):NpcSpritePolicy.Phase(clock-phaseStarted,definition.phases,definition.fps,requested!="melee");
            float left=(float)Frame/definition.phases,rightUv=(float)(Frame+1)/definition.phases;
            float bottom=(7-Orientation)/8f,top=(8-Orientation)/8f;
            uv[0]=new Vector2(left,top);uv[1]=new Vector2(rightUv,top);uv[2]=new Vector2(left,bottom);uv[3]=new Vector2(rightUv,bottom);mesh.uv=uv;
            material.mainTexture=atlas.Texture;
            float height=NativeHeight(definition.referenceRecord);if(height<=0){Release();return;}
            float pixel=height/definition.referenceHeight;
            var facing=-new Vector3(camera.transform.forward.x,0,camera.transform.forward.z);
            visual.transform.rotation=Quaternion.LookRotation(facing.sqrMagnitude>0.0001f?facing:forward);
            visual.transform.localScale=new Vector3(definition.cellWidth*pixel,definition.cellHeight*pixel,1);
            Vector3 ground=NativeGround();
            if(requested=="swim")ground=native.position;
            visual.transform.position=ground+visual.transform.right*(definition.cellWidth*0.5f-definition.pivotX)*pixel+
                Vector3.up*(definition.pivotY-definition.cellHeight*0.5f)*pixel;
            rendered.enabled=original.enabled&&!originalForceOff&&(!body||body.Entity==null||!body.Entity.IsMagicallyConcealed);
            // Keep native enabled-state ownership with sleep, concealment and scene code.
            original.forceRenderingOff=true;
        }
        private float NativeHeight(int record)
        {
            if(actor.IsStationary){var flat=actor.GetComponent<DaggerfallBillboard>();return flat?flat.Summary.Size.y:0;}
            var sizes=Sizes==null?null:Sizes.GetValue(actor.Person.Asset) as Vector2[];
            return sizes!=null&&record<sizes.Length?sizes[record].y:0;
        }
        private Vector3 NativeGround()
        {
            if(actor.IsStationary)return native.position-Vector3.up*NativeHeight(0)*0.5f;
            return native.position-Vector3.up*actor.Person.Asset.GetSize().y*0.5f;
        }
        private bool EnsureVisual()
        {
            if(visual)return true;
            native=actor.IsStationary?actor.transform:actor.Person&&actor.Person.Asset?actor.Person.Asset.transform:null;
            if(!native)return false;
            original=native.GetComponent<MeshRenderer>();var filter=native.GetComponent<MeshFilter>();
            if(!original||!filter||!filter.sharedMesh||!original.sharedMaterial)return false;
            mesh=Instantiate(filter.sharedMesh);material=new Material(original.sharedMaterial);
            // Stationary flats bake dimensions into their vertices; mobile meshes are unit quads.
            mesh.vertices=new[]{new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,-.5f,0),new Vector3(-.5f,-.5f,0)};
            mesh.triangles=new[]{0,1,2,3,2,1};mesh.RecalculateBounds();
            material.mainTextureOffset=Vector2.zero;material.mainTextureScale=Vector2.one;
            visual=new GameObject("Completed NPC action");visual.layer=native.gameObject.layer;visual.transform.SetParent(transform,false);
            visual.AddComponent<MeshFilter>().sharedMesh=mesh;rendered=visual.AddComponent<MeshRenderer>();rendered.sharedMaterial=material;
            rendered.shadowCastingMode=original.shadowCastingMode;rendered.receiveShadows=original.receiveShadows;
            body=GetComponent<DaggerfallEntityBehaviour>();visual.SetActive(false);return true;
        }
        private void OnDisable(){Release();running=moving=false;swingUntil=0;action=null;}
        private void OnDestroy(){Release();if(visual)Destroy(visual);if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
