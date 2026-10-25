using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Runs only on a disposable seat-fixture actor. Does not edit saved residents.
    internal static class StandingPoseProbe
    {
        internal static IEnumerator Run(ResidentActor actor)
        {
            var game=GameManager.Instance;var camera=game.MainCamera;
            var playerPosition=game.PlayerMotor.transform.position;var playerRotation=game.PlayerMotor.transform.rotation;var cameraRotation=camera.transform.localRotation;
            var ground=actor.transform.position-Vector3.up;var forward=actor.transform.forward;
            var original=actor.Person.Asset.GetComponent<MeshRenderer>();
            var sizes=(Vector2[])typeof(MobilePersonBillboard).GetField("recordSizes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(actor.Person.Asset);
            try
            {
                for(int view=0;view<8;view++)
                {
                    var direction=Quaternion.AngleAxis(-view*45,Vector3.up)*forward;
                    game.PlayerMotor.transform.position+=ground+Vector3.up*1.3f+direction*2.5f-camera.transform.position;
                    SleepProbe.AimCamera(ground+Vector3.up*1.1f);
                    actor.Place(ground,forward,true);yield return null;
                    actor.Place(ground,forward,true);yield return new WaitForEndOfFrame();
                    var pose=actor.GetComponent<StandingPose>();var visual=actor.transform.Find("Standing native appearance");
                    if(!pose||!pose.Active||pose.Orientation!=view||!visual||!visual.GetComponent<MeshRenderer>().enabled||original.enabled)
                        throw new InvalidOperationException("Standing renderer/bearing failed at "+view+"; actual="+(pose?pose.Orientation:-1));
                    if(Vector3.Dot(actor.transform.forward,forward)<0.999f)throw new InvalidOperationException("Focused standing changed world heading while camera orbited.");
                    if(Math.Abs(visual.localScale.y-sizes[5].y)>0.000001||Vector3.Distance(visual.position,actor.Person.Asset.transform.position)>0.00001f)
                        throw new InvalidOperationException("Standing atlas changed native idle height or anchor.");
                    var atlas=StandingArt.Current.ForActor(actor);var uv=visual.GetComponent<MeshFilter>().sharedMesh.uv;var rect=atlas.Views[view];
                    if((uv[0]-new Vector2(rect.xMin,rect.yMax)).sqrMagnitude>0.0000001f||(uv[3]-new Vector2(rect.xMax,rect.yMin)).sqrMagnitude>0.0000001f)
                        throw new InvalidOperationException("Standing texture is mirrored or selects another cell.");
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"People-standing-view-"+view+".png"));
                }
                // Native conversation must show only its original frontal frame.
                actor.SetConversationFacing(true);yield return new WaitForEndOfFrame();
                if(actor.GetComponent<StandingPose>().Active||!original.enabled)throw new InvalidOperationException("Standing renderer did not release for conversation.");
                actor.SetConversationFacing(false);actor.Place(ground,forward,true);yield return new WaitForEndOfFrame();
                if(!actor.GetComponent<StandingPose>().Active)throw new InvalidOperationException("Standing did not resume.");
                actor.gameObject.SetActive(false);
                if(actor.GetComponent<StandingPose>().Active||!original.enabled)throw new InvalidOperationException("Standing did not release on disable.");
                actor.gameObject.SetActive(true);actor.Place(ground,forward,true);yield return new WaitForEndOfFrame();
                // Use the open public seat approach for a nearby observer. The
                // renderer test above locks activity facing; this checks free idle.
                var toward=(playerPosition-actor.transform.position);toward.y=0;
                if(toward.sqrMagnitude<0.01f)toward=forward;
                toward.Normalize();
                game.PlayerMotor.transform.position+=actor.transform.position+toward*1.5f+Vector3.up*0.3f-camera.transform.position;
                actor.transform.rotation=Quaternion.LookRotation(-toward);
                float deadline=Time.realtimeSinceStartup+2;
                bool visible=InteriorActivitySight.Visible(actor.transform.position+Vector3.up*0.5f,camera.transform.position,game.PlayerMotor.transform);
                if(!visible)throw new InvalidOperationException("Standing attention fixture player is occluded.");
                while(Time.realtimeSinceStartup<deadline)
                {actor.Place(ground,forward,true,freeStanding:true);yield return null;}
                if(Vector3.Dot(actor.transform.forward,toward)<0.98f)throw new InvalidOperationException("Free standing NPC did not turn toward nearby visible player.");
                actor.Place(ground,forward,true);yield return new WaitForEndOfFrame();
                if(Vector3.Dot(actor.transform.forward,forward)<0.999f)throw new InvalidOperationException("Activity focus did not override nearby player attention.");
                // Release through the exact locomotion boundary used by Place.
                var hold=(DirectionalStandingPose)typeof(ResidentActor).GetField("standingPose",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(actor);
                hold.Set(actor.Person.Asset,false);yield return new WaitForEndOfFrame();
                float speed=(float)typeof(MobilePersonBillboard).GetField("animSpeed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(actor.Person.Asset);
                if(actor.GetComponent<StandingPose>().Active||!original.enabled||actor.Person.Asset.IsIdle||speed<=0)
                    throw new InvalidOperationException("Locomotion did not restore native walking animation.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STANDING_POSE_PASS: eight unmirrored bearings; native idle size/anchor; focused heading; free-idle nearby attention; conversation/disable/walking restore.");
            }
            finally
            {
                game.PlayerMotor.transform.position=playerPosition;game.PlayerMotor.transform.rotation=playerRotation;camera.transform.localRotation=cameraRotation;
                actor.Place(ground,forward,true);
            }
        }
    }
}
