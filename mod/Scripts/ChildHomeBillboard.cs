using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class ChildGreetingWindow : DaggerfallMessageBox
    {
        public ChildGreetingWindow(IUserInterfaceManager manager, string name) : base(manager)
        {
            SetText(new[] { name, "\"I'm staying close to home today.\"" });
            ClickAnywhereToClose = true;
        }
    }
    // Classic single-flat fallback. Emergency movement is allowed while directional
    // locomotion art remains deferred; movement must not fail because art is missing.
    public sealed class ChildHomeBillboard : MobilePersonAsset
    {
        private DaggerfallBillboard flat;
        public override bool IsIdle
        {
            get { return true; }
            set { }
        }
        public override void SetPerson(Races race, Genders gender, int variant, bool guard, int faceVariant, int faceRecord)
        {
            if (flat) { flat.gameObject.SetActive(false); Destroy(flat.gameObject); }
            // Original FLATS.CFG: 182/43 "little boy", 182/38 "little girl".
            // Keep these fixed until explicit appearance records support new variants.
            var go = GameObjectHelper.CreateDaggerfallBillboardGameObject(182, gender == Genders.Female ? 38 : 43, transform);
            go.transform.localPosition = Vector3.zero;
            go.layer = gameObject.layer;
            flat = go.GetComponent<DaggerfallBillboard>();
            foreach (var collider in go.GetComponents<Collider>()) collider.enabled = false;
            var trigger = GetComponentInParent<CapsuleCollider>();
            if (trigger)
            {
                trigger.height = flat.Summary.Size.y;
                trigger.radius = Mathf.Min(0.25f, flat.Summary.Size.x * 0.5f);
                trigger.center = Vector3.up * (flat.Summary.Size.y * 0.5f - 1f);
            }
        }
        public override Vector3 GetSize() { return flat ? (Vector3)flat.Summary.Size : Vector3.zero; }
    }
}
