using DaggerfallWorkshop.Game;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Unity avoidance also needs the player and stationary native bodies.
    internal sealed class NativeNavigationBody : MonoBehaviour
    {
        private Collider body;
        private NavMeshObstacle obstacle;
        private void Awake()
        {
            body=GetComponent<Collider>();
            if(!body){enabled=false;return;}
            obstacle=gameObject.AddComponent<NavMeshObstacle>();obstacle.enabled=false;
            obstacle.shape=NavMeshObstacleShape.Capsule;obstacle.carving=false;
            var capsule=body as CapsuleCollider;var controller=body as CharacterController;
            obstacle.radius=capsule?capsule.radius:controller?controller.radius:0.3f;
            obstacle.height=capsule?capsule.height:controller?controller.height:1.9f;
            obstacle.center=capsule?capsule.center:controller?controller.center:Vector3.zero;
        }
        private void LateUpdate()
        {
            if(!obstacle)return;
            var navigation=GetComponent<NativeResidentNavigation>();
            obstacle.enabled=body&&body.enabled&&NativeNavigationWorld.Current&&NativeNavigationWorld.Current.Ready&&(!navigation||!navigation.Active);
        }
        private void OnDisable(){if(obstacle)obstacle.enabled=false;}
        private void OnDestroy(){if(obstacle)Destroy(obstacle);}
    }
}
