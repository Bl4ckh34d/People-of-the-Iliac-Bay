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
        private int lastView = -1;
        private bool lastMirror;
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
            var camera = Camera.main;
            if (!Active || !camera) return;
            Vector3 delta = camera.transform.position - transform.position;
            Vector3 horizontal = new Vector3(delta.x, 0, delta.z).normalized;
            var view = SleepView.Select(Vector3.Dot(horizontal, transform.forward), Vector3.Dot(horizontal, transform.right));
            ViewIndex = view.Index;
            Rect rect = art.Views[view.Index];
            float left = view.Mirror ? rect.xMax : rect.xMin, right = view.Mirror ? rect.xMin : rect.xMax;
            if (lastView != view.Index || lastMirror != view.Mirror)
            {
                for (int i = 0; i < viewUV.Length; i++) viewUV[i] = new Vector2(
                    Mathf.Lerp(left, right, originalUV[i].x), Mathf.Lerp(rect.yMin, rect.yMax, originalUV[i].y));
                mesh.uv = viewUV; lastView = view.Index; lastMirror = view.Mirror;
            }
            Vector3 sight = transform.position - camera.transform.position;
            if (sight.sqrMagnitude < 0.000001f) return;
            Vector3 referenceUp = Mathf.Abs(Vector3.Dot(sight.normalized, Vector3.up)) > 0.99f ? transform.forward : Vector3.up;
            quad.transform.position = transform.position;
            quad.transform.rotation = Quaternion.LookRotation(sight, referenceUp);
            Vector3 cameraRight = quad.transform.right, cameraUp = quad.transform.up;
            float span = length * Mathf.Abs(Vector3.Dot(transform.forward, cameraRight)) +
                width * Mathf.Abs(Vector3.Dot(transform.right, cameraRight)) + 0.36f * Mathf.Abs(Vector3.Dot(Vector3.up, cameraRight));
            float height = span * (rect.height * art.Texture.height) / (rect.width * art.Texture.width);
            // A tall generated end-view must not become an upright person or be
            // lifted off the bed. Preserve aspect where it fits, otherwise bound
            // the drawing by the projected mattress footprint and body thickness.
            float projectedHeight = length * Mathf.Abs(Vector3.Dot(transform.forward, cameraUp)) +
                width * Mathf.Abs(Vector3.Dot(transform.right, cameraUp)) + 0.36f * Mathf.Abs(Vector3.Dot(Vector3.up, cameraUp));
            height = Mathf.Min(height, projectedHeight);
            quad.transform.localScale = new Vector3(span, height, 1);
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
