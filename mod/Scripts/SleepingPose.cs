using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class SleepingPose : MonoBehaviour
    {
        public bool Active { get; private set; }
        public int ViewIndex { get; private set; }
        private ResidentActor actor;
        private CapsuleCollider body;
        private float oldHeight, oldRadius, length, width;
        private int oldDirection;
        private Vector3 oldCenter;
        private bool assetWasActive;
        private GameObject quad;
        private Mesh mesh;
        private Material material;
        private SleepingArt.Atlas art;
        private Vector2[] originalUV, viewUV;
        private Renderer nativeRenderer;
        private BoxCollider nativeBody;
        private CharacterController guardBody;
        private float guardHeight, guardRadius;
        private Vector3 guardCenter;
        private bool nativeWasVisible;
        private Vector3 nativePosition, nativeSize, nativeCenter;
        private Quaternion nativeRotation;
        private BedAnchor currentBed;
        public Vector3 NativeStandingPosition { get { return nativePosition; } }
        private void SetArt(SleepingArt.Atlas atlas)
        {
            art = atlas;
            if (!quad)
            {
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Sleeping blanket"; quad.layer = gameObject.layer;
                quad.transform.SetParent(transform, false);
                var collider = quad.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
                mesh = Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
                originalUV = mesh.uv; viewUV = new Vector2[originalUV.Length];
                quad.GetComponent<MeshFilter>().sharedMesh = mesh;
                material = new Material(Shader.Find("Daggerfall/Billboard"));
                material.mainTexture = atlas.Texture;
                var renderer = quad.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        public void Begin(ResidentActor owner, Transform interior, BedAnchor bed, SleepingArt.Atlas atlas)
        {
            currentBed=bed;
            actor = owner; SetArt(atlas);
            if (!Active)
            {
                body = GetComponent<CapsuleCollider>();
                oldHeight = body.height; oldRadius = body.radius; oldDirection = body.direction; oldCenter = body.center;
                assetWasActive = actor.Person.Asset.gameObject.activeSelf;
                actor.Person.Asset.gameObject.SetActive(false);
                Active = true; quad.SetActive(true);
            }
            length = bed.Length * 0.9f; width = bed.Width * 0.85f;
            transform.position = interior.TransformPoint(bed.LocalCenter) + Vector3.up * 0.18f;
            transform.rotation = Quaternion.LookRotation(interior.TransformDirection(bed.LocalHeadDirection));
            body.direction = 2; body.radius = 0.18f; body.height = length; body.center = Vector3.zero;
        }
        public void BeginNative(Transform interior, BedAnchor bed, SleepingArt.Atlas atlas)
        {
            currentBed=bed;
            SetArt(atlas);
            if (!Active)
            {
                nativeRenderer = GetComponent<Renderer>(); nativeBody = GetComponent<BoxCollider>();
                if (!nativeRenderer || !nativeBody) throw new System.InvalidOperationException("Native sleeping flat lacks its renderer or collider.");
                nativePosition = transform.position; nativeRotation = transform.rotation;
                nativeSize = nativeBody.size; nativeCenter = nativeBody.center;
                nativeWasVisible = nativeRenderer.enabled; nativeRenderer.enabled = false;
                Active = true; quad.SetActive(true);
            }
            length = bed.Length * 0.9f; width = bed.Width * 0.85f;
            transform.position = interior.TransformPoint(bed.LocalCenter) + Vector3.up * 0.18f;
            transform.rotation = Quaternion.LookRotation(interior.TransformDirection(bed.LocalHeadDirection));
            nativeBody.size = new Vector3(width, 0.36f, length); nativeBody.center = Vector3.zero;
        }
        public void BeginGuard(Transform interior, BedAnchor bed, SleepingArt.Atlas atlas)
        {
            currentBed = bed;
            SetArt(atlas);
            if (!Active)
            {
                var mobile = GetComponentInChildren<DaggerfallWorkshop.MobileUnit>();
                nativeRenderer = mobile ? mobile.GetComponent<Renderer>() : null;
                guardBody = GetComponent<CharacterController>();
                if (!nativeRenderer || !guardBody) throw new System.InvalidOperationException("Sleeping guard lacks native sprite or controller.");
                nativePosition = transform.position; nativeRotation = transform.rotation;
                nativeWasVisible = nativeRenderer.enabled; nativeRenderer.enabled = false;
                guardHeight = guardBody.height; guardRadius = guardBody.radius; guardCenter = guardBody.center;
                guardBody.radius = 0.18f; guardBody.height = 0.36f; guardBody.center = Vector3.zero;
                Active = true; quad.SetActive(true);
            }
            length = bed.Length * 0.9f; width = bed.Width * 0.85f;
            transform.position = interior.TransformPoint(bed.LocalCenter) + Vector3.up * 0.18f;
            transform.rotation = Quaternion.LookRotation(interior.TransformDirection(bed.LocalHeadDirection));
        }
        private void LateUpdate()
        {
            if (!Active) return;
            // Native EntityConcealmentBehaviour writes this renderer every Update.
            // Keep the native body and its effects alive, but let the sleep pose
            // own its standing sprite until Wake restores the original visibility.
            if (nativeRenderer) nativeRenderer.enabled = false;
            // A sleeper belongs to the mattress, not the camera. Keep one complete
            // face/pillow/blanket image on a fixed horizontal bed-aligned surface.
            // Rotate the complete broadside drawing so its left-hand head points
            // toward the bed head. Preserve source pixels' aspect ratio.
            bool overhead=art.Views.Length==1;
            ViewIndex = overhead ? 0 : 2;
            Rect rect = art.Views[ViewIndex];
            for (int i = 0; i < viewUV.Length; i++) viewUV[i] = new Vector2(
                Mathf.Lerp(rect.xMin, rect.xMax, overhead ? originalUV[i].x : 1-originalUV[i].y),
                Mathf.Lerp(rect.yMin, rect.yMax, overhead ? originalUV[i].y : originalUV[i].x));
            mesh.uv = viewUV;
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            float aspect = rect.height*art.Texture.height/(rect.width*art.Texture.width);
            if (overhead) aspect=1/aspect;
            float fittedLength = Mathf.Min(length,width/aspect);
            quad.transform.localScale = new Vector3(fittedLength*aspect, fittedLength, 1);
        }
        public void Wake()
        {
            if (!Active) return;
            Active = false; quad.SetActive(false);
            if (actor && actor.Person.Asset) actor.Person.Asset.gameObject.SetActive(assetWasActive);
            if (body) { body.height = oldHeight; body.radius = oldRadius; body.direction = oldDirection; body.center = oldCenter; }
            if (nativeRenderer)
            {
                nativeRenderer.enabled = nativeWasVisible;
                transform.position = nativePosition; transform.rotation = nativeRotation;
                if (nativeBody) { nativeBody.size = nativeSize; nativeBody.center = nativeCenter; }
                if (guardBody) { guardBody.radius = guardRadius; guardBody.height = guardHeight; guardBody.center = guardCenter; }
            }
        }
        public bool WakeOnFloor(InteriorGeometry geometry, out Vector3 ground)
        {
            ground=Vector3.zero;
            if (!Active || currentBed==null || geometry==null || !geometry.StandBesideBed(currentBed,out ground)) return false;
            Wake();
            if (actor) actor.Place(ground,transform.forward,true);
            else
            {
                var billboard=GetComponent<DaggerfallWorkshop.DaggerfallBillboard>();
                transform.position=ground+Vector3.up*(billboard ? billboard.Summary.Size.y*0.5f : 1);
            }
            return true;
        }
        private void OnDisable() { Wake(); }
        private void OnDestroy() { if (material) Destroy(material); if (mesh) Destroy(mesh); }
    }
}
