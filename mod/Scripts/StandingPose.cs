using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class StandingPose : MonoBehaviour
    {
        private static readonly FieldInfo Sizes=typeof(MobilePersonBillboard).GetField("recordSizes",BindingFlags.Instance|BindingFlags.NonPublic);
        private ResidentActor actor;
        private MobilePersonBillboard native;
        private MeshRenderer original,rendered;
        private Mesh mesh;
        private Material material;
        private GameObject visual;
        private StandingArt.Atlas art;
        private Vector2 nativeSize;
        private DaggerfallEntityBehaviour entity;
        private readonly Vector2[] uv=new Vector2[4];
        private bool originalWasEnabled;
        public bool Active {get;private set;}
        public int Orientation {get;private set;}
        public bool Begin(ResidentActor owner,StandingArt.Atlas atlas)
        {
            if(!owner||atlas==null||!atlas.Texture||!(owner.Person.Asset is MobilePersonBillboard))return false;
            if(Active&&actor==owner&&art==atlas)return true;
            End();actor=owner;native=(MobilePersonBillboard)owner.Person.Asset;entity=owner.GetComponent<DaggerfallEntityBehaviour>();
            var sizes=Sizes==null?null:Sizes.GetValue(native) as Vector2[];
            if(sizes==null||sizes.Length<6)return false;
            nativeSize=sizes[5];art=atlas;
            if(!visual)
            {
                original=native.GetComponent<MeshRenderer>();
                mesh=Instantiate(native.GetComponent<MeshFilter>().sharedMesh);
                material=new Material(original.sharedMaterial);material.mainTextureOffset=Vector2.zero;material.mainTextureScale=Vector2.one;
                visual=new GameObject("Standing native appearance");visual.layer=native.gameObject.layer;visual.transform.SetParent(transform,false);
                visual.AddComponent<MeshFilter>().sharedMesh=mesh;rendered=visual.AddComponent<MeshRenderer>();rendered.sharedMaterial=material;
                rendered.shadowCastingMode=original.shadowCastingMode;rendered.receiveShadows=original.receiveShadows;
            }
            material.mainTexture=atlas.Texture;originalWasEnabled=original.enabled;
            Active=true;visual.SetActive(true);LateUpdate();return Active;
        }
        public void End()
        {
            if(!Active)return;Active=false;
            if(original)original.enabled=originalWasEnabled;if(visual)visual.SetActive(false);
        }
        private void LateUpdate()
        {
            if(!Active)return;
            if(!actor||!actor.Resident.IsAlive||actor.IsSleeping||actor.IsSeated||actor.IsConversing||!native||!native.gameObject.activeInHierarchy||art==null||!art.Texture)
            {End();return;}
            var camera=GameManager.Instance.MainCamera;if(!camera)return;
            var toward=camera.transform.position-native.transform.position;toward.y=0;
            Orientation=SittingView.Select(Vector3.Dot(toward,actor.transform.forward),Vector3.Dot(toward,actor.transform.right));
            var rect=art.Views[Orientation];
            uv[0]=new Vector2(rect.xMin,rect.yMax);uv[1]=new Vector2(rect.xMax,rect.yMax);
            uv[2]=new Vector2(rect.xMin,rect.yMin);uv[3]=new Vector2(rect.xMax,rect.yMin);mesh.uv=uv;
            var facing=-new Vector3(camera.transform.forward.x,0,camera.transform.forward.z);
            visual.transform.rotation=Quaternion.LookRotation(facing.sqrMagnitude>0.0001f?facing:actor.transform.forward);
            // Match the real native idle record, including XML size overrides.
            // Atlas padding changes canvas width, never body width or height.
            var size=art.Definition.nativeIdleSize;var cell=art.Definition.cellSize;
            visual.transform.localScale=new Vector3(nativeSize.x*cell[0]/size[0],nativeSize.y,1);
            visual.transform.position=native.transform.position;
            // Preserve native concealment rules while preventing double sprites.
            rendered.enabled=!entity||entity.Entity==null||!entity.Entity.IsMagicallyConcealed;original.enabled=false;
        }
        private void OnDisable(){End();}
        private void OnDestroy(){End();if(visual)Destroy(visual);if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
