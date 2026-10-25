using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Seated visual overlay; own body collisions are suspended until departure.
    public sealed class SittingPose : MonoBehaviour
    {
        private ResidentActor actor;
        private MobilePersonBillboard native;
        private MeshRenderer original,renderer;
        private bool originalWasEnabled;
        private GameObject visual;
        private Mesh mesh;
        private Material material;
        private SittingArt.Atlas art;
        private SelectedSeatAccess access;
        private readonly Vector2[] uv=new Vector2[4];
        public bool Active {get;private set;}
        public string LastStopReason {get;private set;}
        public int Orientation {get;private set;}
        public Vector3 PelvisWorld {get;private set;}
        public float FeetWorldY {get;private set;}
        public bool Begin(ResidentActor owner,SelectedSeatAccess seat,SittingArt.Atlas atlas)
        {
            if(!owner||seat==null||!seat.Valid||atlas==null||!atlas.Texture||!(owner.Person.Asset is MobilePersonBillboard))return false;
            if(atlas.Frames==null||atlas.Frames.Length!=8||seat.Slot.Surface.y-seat.Slot.Ground.y<0.2f||seat.Slot.Surface.y-seat.Slot.Ground.y>1.2f)return false;
            if(native&&native!=owner.Person.Asset)return false;
            if(Active&&actor==owner&&access==seat&&art==atlas)return true;
            actor=owner;native=(MobilePersonBillboard)owner.Person.Asset;access=seat;art=atlas;
            if(!visual)
            {
                original=native.GetComponent<MeshRenderer>();mesh=Instantiate(native.GetComponent<MeshFilter>().sharedMesh);
                material=new Material(original.sharedMaterial);material.mainTextureOffset=Vector2.zero;material.mainTextureScale=Vector2.one;
                visual=new GameObject("Seated native appearance");visual.layer=native.gameObject.layer;visual.transform.SetParent(transform,false);
                visual.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=visual.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                renderer.shadowCastingMode=original.shadowCastingMode;renderer.receiveShadows=original.receiveShadows;
                visual.SetActive(false);
            }
            material.mainTexture=atlas.Texture;
            if(!Active){originalWasEnabled=original.enabled;Active=true;LastStopReason=null;visual.SetActive(true);}
            SeatBodyCollision.Set(owner,true);original.enabled=false;LateUpdate();return Active;
        }
        public void End(string reason="owner interrupted")
        {
            if(!Active)return;Active=false;LastStopReason=reason;
            if(actor)SeatBodyCollision.Set(actor,false);
            if(original)original.enabled=originalWasEnabled;if(visual)visual.SetActive(false);access=null;
        }
        private void LateUpdate()
        {
            if(!Active)return;
            string stopped=!actor?"actor missing":!actor.Resident.IsAlive?"dead":actor.IsSleeping?"sleeping":actor.IsConversing?"conversation":
                access==null?"seat missing":!access.Valid?"seat lease expired":art==null||!art.Texture?"art missing":
                Vector3.Distance(actor.transform.position-Vector3.up,access.Slot.Ground)>0.15f?"body left seat":null;
            if(stopped!=null){End(stopped);return;}
            var camera=GameManager.Instance.MainCamera;if(!camera)return;
            var slot=access.Slot;var toward=camera.transform.position-slot.Ground;toward.y=0;
            var heading=slot.Facing;heading.y=0;heading.Normalize();var right=Vector3.Cross(Vector3.up,heading);
            Orientation=SittingView.Select(Vector3.Dot(toward,heading),Vector3.Dot(toward,right));
            var frame=art.Frames[Orientation];var cell=art.Definition.cellSize;
            var fit=SittingView.Align(cell[0],cell[1],frame.seatPivotEstimate[0],frame.seatPivotEstimate[1],frame.groundPivot[1],slot.Surface.y-slot.Ground.y,art.PixelScale);
            var rect=art.Views[Orientation];
            uv[0]=new Vector2(rect.xMin,rect.yMax);uv[1]=new Vector2(rect.xMax,rect.yMax);uv[2]=new Vector2(rect.xMin,rect.yMin);uv[3]=new Vector2(rect.xMax,rect.yMin);mesh.uv=uv;
            var cameraFacing=-new Vector3(camera.transform.forward.x,0,camera.transform.forward.z);
            visual.transform.rotation=Quaternion.LookRotation(cameraFacing.sqrMagnitude>0.0001f?cameraFacing:heading);
            visual.transform.localScale=new Vector3((float)(cell[0]*fit.PixelScale),(float)(cell[1]*fit.PixelScale),1);
            var anchor=new Vector3(slot.Surface.x,slot.Ground.y,slot.Surface.z);
            visual.transform.position=anchor+visual.transform.right*(float)fit.CenterX+Vector3.up*(float)fit.CenterY;
            PelvisWorld=visual.transform.position+visual.transform.right*(float)((cell[0]*0.5-frame.seatPivotEstimate[0])*fit.PixelScale)+Vector3.up*(float)((cell[1]*0.5-frame.seatPivotEstimate[1])*fit.PixelScale);
            FeetWorldY=visual.transform.position.y+(float)((cell[1]*0.5-frame.groundPivot[1])*fit.PixelScale);
            original.enabled=false;
        }
        private void OnDisable(){End();}
        private void OnDestroy(){End();if(visual)Destroy(visual);if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
