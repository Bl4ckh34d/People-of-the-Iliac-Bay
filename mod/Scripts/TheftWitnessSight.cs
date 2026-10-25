using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class TheftWitnessSight
    {
        public static bool CanSee(Transform witness, Vector3 target, bool alive, bool awake, float range = 20)
        {
            if (!witness || !witness.gameObject.activeInHierarchy || !alive || !awake) return false;
            var body = witness.GetComponent<Collider>();
            Vector3 eye = body ? body.bounds.center : witness.position + Vector3.up;
            Vector3 delta = target - eye;
            if (delta.sqrMagnitude > range*range) return false;
            if (delta.sqrMagnitude < 0.001f) return true;
            foreach (var hit in Physics.RaycastAll(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == witness || hit.transform.IsChildOf(witness)) continue;
                if (hit.collider.GetComponentInParent<DaggerfallWorkshop.Game.PlayerMotor>() != null) continue;
                return false;
            }
            return true;
        }
    }
}
