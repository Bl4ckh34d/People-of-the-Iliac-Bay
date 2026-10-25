using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class DirectionalKeeperPose : MonoBehaviour
    {
        private DaggerfallBillboard native;
        private MeshRenderer original,view;
        private GameObject visual;
        private Mesh mesh;
        private Material material;
        private Vector3 heading;
        private bool active,originalForceHidden;
        public int Orientation {get;private set;}
        public int Frame {get;private set;}
        public Vector3 Heading {get {return heading;}}
        internal void Face(Vector3 direction)
        {direction.y=0;if(direction.sqrMagnitude>0.01f)heading=direction.normalized;}
        public bool UsesNativeFront {get {return active && Orientation==0;}}
        public void Configure(DaggerfallBillboard source,KeeperStandingArt art,Vector3 forward)
        {
            if(native) return;
            native=source;original=source.GetComponent<MeshRenderer>();
            forward.y=0;heading=forward.sqrMagnitude>0.01f ? forward.normalized : Vector3.forward;
            material=new Material(original.sharedMaterial);material.mainTexture=art.Atlas;
            material.mainTextureOffset=Vector2.zero;material.mainTextureScale=Vector2.one;
            float size=native.Summary.Size.y*KeeperStandingArt.Cell/KeeperStandingArt.Height;
            mesh=DaggerfallUnity.Instance.MeshReader.GetSimpleBillboardMesh(new Vector2(size,size));
            visual=new GameObject("People directional shopkeeper idle");visual.layer=gameObject.layer;
            visual.transform.SetParent(transform,false);
            visual.AddComponent<MeshFilter>().sharedMesh=mesh;view=visual.AddComponent<MeshRenderer>();view.sharedMaterial=material;
            view.shadowCastingMode=original.shadowCastingMode;view.receiveShadows=original.receiveShadows;
            visual.SetActive(false);
        }
        public void SetStanding(bool standing)
        {
            if(!native || active==standing) return;
            active=standing;
            if(standing) originalForceHidden=original.forceRenderingOff;
            else {original.forceRenderingOff=originalForceHidden;visual.SetActive(false);}
        }
        private void LateUpdate()
        {
            if(!active || !native || !gameObject.activeInHierarchy) return;
            var camera=GameManager.Instance.MainCamera;if(!camera) return;
            var delta=camera.transform.position-transform.position;delta.y=0;
            Orientation=KeeperStandingArt.ViewIndex(delta,heading);
            // Native coroutine increments after displaying its frame. Reuse that
            // exact phase, including pauses, instead of starting another timer.
            Frame=native.Summary.CurrentFrame<=0 ? 0 : (native.Summary.CurrentFrame-1)%12;
            bool generated=Orientation!=0;
            // Preserve the native visibility owner's enabled flag. Suppress only
            // this renderer's draw while its directional counterpart is shown.
            original.forceRenderingOff=originalForceHidden || generated;
            visual.SetActive(original.enabled && !originalForceHidden && generated);
            if(!generated) return;
            float left=Frame/12f,right=(Frame+1)/12f,bottom=(7-Orientation)/7f,top=(8-Orientation)/7f;
            mesh.uv=new[]{new Vector2(left,top),new Vector2(right,top),new Vector2(left,bottom),new Vector2(right,bottom)};
            visual.transform.position=transform.position;
            var facing=-camera.transform.forward;facing.y=0;
            visual.transform.rotation=Quaternion.LookRotation(facing);
        }
        private void OnDisable() {SetStanding(false);}
        private void OnDestroy()
        {
            SetStanding(false);
            if(visual) Destroy(visual);if(mesh) Destroy(mesh);if(material) Destroy(material);
        }
    }
}
