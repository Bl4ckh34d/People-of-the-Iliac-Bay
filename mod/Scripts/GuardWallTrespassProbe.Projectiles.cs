using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GuardWallTrespassProbe
    {
        private static void ObserveNativeWallProjectiles(GuardActor actor)
        {
            foreach(var missile in UnityEngine.Object.FindObjectsOfType<DaggerfallMissile>())
                if(missile.IsArrow && missile.Caster==actor.GetComponent<DaggerfallEntityBehaviour>() &&
                    !missile.GetComponent<WallProjectileObservation>())
                {
                    var observer=missile.gameObject.AddComponent<WallProjectileObservation>();
                    observer.Guard=actor.GuardId;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_NATIVE_PROJECTILE: guard="+actor.GuardId+
                        "; position="+missile.transform.position+"; customAim="+missile.CustomAimPosition+
                        "; playerCollider="+GameManager.Instance.PlayerMotor.GetComponent<CharacterController>().bounds);
                }
        }
        private static void VerifyWallCombatSurface(GuardActor actor,float wallHeight)
        {
            var controller=actor.GetComponent<CharacterController>();
            var foot=actor.transform.TransformPoint(controller.center)-Vector3.up*(controller.height*.5f);
            RaycastHit floor;NavMeshHit nav;
            if(!Physics.Raycast(foot+Vector3.up*.5f,Vector3.down,out floor,1.3f,~0,QueryTriggerInteraction.Ignore) ||
                floor.normal.y<.65f || floor.point.y<wallHeight-1.5f || !NavMesh.SamplePosition(floor.point,out nav,.5f,NavMesh.AllAreas) ||
                Mathf.Abs(nav.position.y-floor.point.y)>.4f)
                throw new InvalidOperationException("Native wall combat left its actual supported elevated navigation surface.");
        }
    }
    // Probe-only collision observer. Native DaggerfallMissile keeps every hit,
    // collision, damage roll and lifetime; this component only logs evidence.
    internal sealed class WallProjectileObservation : MonoBehaviour
    {
        internal string Guard;
        private bool observed;
        private void OnTriggerEnter(Collider other){Observe(other);}
        private void OnCollisionEnter(Collision collision){Observe(collision.collider);}
        private void Observe(Collider other)
        {
            if(observed)return;var missile=GetComponent<DaggerfallMissile>();
            if(!missile || !other || other.GetComponentInParent<DaggerfallEntityBehaviour>()==missile.Caster)return;
            observed=true;var entity=other.GetComponentInParent<DaggerfallEntityBehaviour>();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_NATIVE_PROJECTILE_IMPACT: guard="+Guard+
                "; collider="+other.name+"; position="+transform.position+
                "; player="+(entity==GameManager.Instance.PlayerEntityBehaviour)+
                "; nativeDirectEntity="+(other.GetComponent<DaggerfallEntityBehaviour>()!=null));
        }
    }
}
