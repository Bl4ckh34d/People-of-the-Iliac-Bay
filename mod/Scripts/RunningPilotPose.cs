using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using UnityEngine;

namespace LivingCities.DFU
{
    // Explicit opt-in art pilot for archive 385 only. The caller owns fleeing state.
    // Native animation keeps updating invisibly, so stopping restores walk/idle.
    public sealed class RunningPilotPose : MonoBehaviour
    {
        public const string AssetName = "run-pilot-385.png.bytes";
        private static readonly int[] Records = { 0,1,2,3,4,3,2,1 };
        private MobilePersonBillboard native;
        private MeshRenderer original, renderer;
        private Mesh mesh;
        private Material material;
        private GameObject visual;
        private float started;
        private bool originalWasEnabled;
        public bool Active { get; private set; }
        public int Orientation { get; private set; }
        public int Frame { get; private set; }

        public static Texture2D Load(Mod mod)
        {
            var bytes = mod.GetAsset<TextAsset>(AssetName);
            if (!bytes) throw new InvalidOperationException("Running pilot atlas missing: " + AssetName);
            var texture = new Texture2D(2,2,TextureFormat.RGBA32,false);
            if (!texture.LoadImage(bytes.bytes) || texture.width != 384 || texture.height != 640)
            {
                Destroy(texture);
                throw new InvalidOperationException("Running pilot requires a 384x640 atlas.");
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        public bool Configure(MobilePersonAsset asset, int archive, Texture2D atlas)
        {
            if (archive != 385 || !(asset is MobilePersonBillboard) || !atlas) return false;
            if (native) return native == asset;
            native = (MobilePersonBillboard)asset;
            original = native.GetComponent<MeshRenderer>();
            mesh = Instantiate(native.GetComponent<MeshFilter>().sharedMesh);
            material = new Material(original.sharedMaterial);
            material.mainTexture = atlas;
            material.mainTextureOffset = Vector2.zero;
            material.mainTextureScale = Vector2.one;
            visual = new GameObject("Archive 385 running pilot");
            visual.layer = native.gameObject.layer;
            visual.transform.SetParent(native.transform.parent, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = original.shadowCastingMode;
            renderer.receiveShadows = original.receiveShadows;
            visual.SetActive(false);
            return true;
        }

        public void SetRunning(bool running)
        {
            if (!native) return;
            if (Active == running) return;
            Active = running;
            if (running)
            {
                started = Time.time;
                originalWasEnabled = original.enabled;
                original.enabled = false;
                visual.SetActive(true);
                LateUpdate();
            }
            else
            {
                original.enabled = originalWasEnabled;
                visual.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (!Active || !native || !native.transform.parent) return;
            var camera = GameManager.Instance.MainCamera;
            if (!camera) return;
            Vector3 dir = camera.transform.position-native.transform.position;
            dir.y = 0;
            Vector3 forward = native.transform.parent.forward;
            forward.y = 0;
            float angle = Vector3.Angle(dir,forward) * -Mathf.Sign(Vector3.Cross(dir,forward).y);
            Orientation = (-Mathf.RoundToInt(angle/45f)+8)%8;
            Frame = Mathf.FloorToInt((Time.time-started)*10f)%4;
            int record = Records[Orientation];
            bool flip = Orientation > 4;
            float left=Frame/4f, right=(Frame+1)/4f;
            float bottom=(4-record)/5f, top=(5-record)/5f;
            mesh.uv = new[] { new Vector2(flip?right:left,top), new Vector2(flip?left:right,top),
                new Vector2(flip?right:left,bottom), new Vector2(flip?left:right,bottom) };
            float pixel = native.GetSize().y/110f;
            // Original native ground is center-55px. Our ground pivot is 56px
            // below center (128px cell, ground at y120), hence center+1px.
            visual.transform.localPosition = native.transform.localPosition+Vector3.up*pixel;
            visual.transform.rotation = native.transform.rotation;
            visual.transform.localScale = new Vector3(96*pixel,128*pixel,1);
            original.enabled = false;
        }

        private void OnDisable() { SetRunning(false); }
        private void OnDestroy()
        {
            if (original && Active) original.enabled = originalWasEnabled;
            if (visual) Destroy(visual);
            if (mesh) Destroy(mesh);
            if (material) Destroy(material);
        }
    }
}
