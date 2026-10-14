using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
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
        private bool nativeWasVisible;
        private Vector3 nativePosition, nativeSize, nativeCenter;
        private Quaternion nativeRotation;
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
        private void LateUpdate()
        {
            if (!Active) return;
            // A sleeper belongs to the mattress, not the camera. Keep one complete
            // face/pillow/blanket image on a fixed horizontal bed-aligned surface.
            // The existing face-on frame puts the head at the top of the image.
            ViewIndex = 0;
            Rect rect = art.Views[0];
            for (int i = 0; i < viewUV.Length; i++) viewUV[i] = new Vector2(
                Mathf.Lerp(rect.xMin, rect.xMax, originalUV[i].x), Mathf.Lerp(rect.yMin, rect.yMax, originalUV[i].y));
            mesh.uv = viewUV;
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            quad.transform.localScale = new Vector3(width, length, 1);
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
            }
        }
        private void OnDisable() { Wake(); }
        private void OnDestroy() { if (material) Destroy(material); if (mesh) Destroy(mesh); }
    }
}
