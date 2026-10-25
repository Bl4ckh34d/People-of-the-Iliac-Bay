using System;
using System.Collections;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class SittingPoseProbe
    {
        internal static IEnumerator Run(ResidentActor actor,SelectedSeatAccess access,Action refreshClaims)
        {
            // Native concealment updates Renderer.enabled during Update. The
            // seated overlay hides it in LateUpdate, before the frame is drawn.
            refreshClaims();yield return new WaitForEndOfFrame();refreshClaims();
            var game=GameManager.Instance;var camera=game.MainCamera;var beforePosition=game.PlayerMotor.transform.position;
            var beforeRotation=game.PlayerMotor.transform.rotation;var beforeCamera=camera.transform.localRotation;
            var original=actor.Person.Asset.GetComponent<MeshRenderer>();
            var slot=access.Slot;var toward=slot.Facing;var pose=actor.GetComponent<SittingPose>();
            var atlas=SittingArt.Current.ForActor(actor);var visual=actor.transform.Find("Seated native appearance");
            if(!pose||!pose.Active||original.enabled)throw new InvalidOperationException("Supported seat art did not replace the native renderer: active="+(pose&&pose.Active)+"; original="+original.enabled+"; stop="+(pose?pose.LastStopReason:"missing")+"; lease="+access.Valid+"; distance="+Vector3.Distance(actor.transform.position-Vector3.up,slot.Ground));
            if(atlas==null||!visual)throw new InvalidOperationException("Sitting atlas or rendered quad is unavailable.");
            var sizes=(Vector2[])typeof(DaggerfallWorkshop.MobilePersonBillboard).GetField("recordSizes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(actor.Person.Asset);
            double nativePixel=sizes[5].y/atlas.Definition.nativeIdleSize[1];
            double seatedPixel=nativePixel*SittingArt.BodyScale;
            if(Math.Abs(atlas.PixelScale-seatedPixel)>0.000001)throw new InvalidOperationException("Sitting scale differs from native idle scale and the seated art calibration.");
            try
            {
                for(int view=0;view<8;view++)
                {
                    refreshClaims();var direction=Quaternion.AngleAxis(-view*45,Vector3.up)*toward;
                    var desired=slot.Ground+Vector3.up*1.1f+direction*2.8f;
                    game.PlayerMotor.transform.position+=desired-camera.transform.position;
                    SleepProbe.AimCamera(slot.Ground+Vector3.up*0.95f);
                    access.Advance(actor);yield return null;refreshClaims();access.Advance(actor);yield return new WaitForEndOfFrame();
                    if(!pose.Active||pose.Orientation!=view)throw new InvalidOperationException("Sitting view does not match native bearing: expected="+view+"; actual="+pose.Orientation);
                    if(Vector3.Dot(actor.transform.forward,slot.Facing)<0.999f)throw new InvalidOperationException("Camera changed the seated actor's chair heading.");
                    var frame=atlas.Frames[view];var mesh=visual.GetComponent<MeshFilter>().sharedMesh;Vector3 pelvis,feet;
                    if(!TryMapPixelToWorld(visual,mesh,atlas.Texture,frame,frame.seatPivotEstimate[0],frame.seatPivotEstimate[1],out pelvis)||
                        !TryMapPixelToWorld(visual,mesh,atlas.Texture,frame,frame.groundPivot[0],frame.groundPivot[1],out feet))
                        throw new InvalidOperationException("Sitting pivots fall outside rendered quad UV triangles.");
                    float unliftedFeet=slot.Surface.y-(float)((frame.groundPivot[1]-frame.seatPivotEstimate[1])*seatedPixel);
                    float lift=Mathf.Max(0,slot.Ground.y-unliftedFeet);
                    if(Vector3.Distance(pelvis,slot.Surface+Vector3.up*lift)>0.002f||feet.y<slot.Ground.y-0.002f||
                        Mathf.Abs(feet.y-(unliftedFeet+lift))>0.002f||
                        Math.Abs(visual.lossyScale.y/atlas.Definition.cellSize[1]-seatedPixel)>0.000001)
                        throw new InvalidOperationException("Rendered mesh changed calibrated body scale or lost its seat anchor.");
                    if(original.enabled)throw new InvalidOperationException("Native standing sprite remained visible at render time.");
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"People-sitting-view-"+view+".png"));
                    if(view==6)
                    {
                        pose.End();refreshClaims();yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"People-sitting-standing-comparison.png"));
                        refreshClaims();access.Advance(actor);yield return new WaitForEndOfFrame();
                    }
                }
                actor.SetConversationFacing(true);
                if(actor.IsSeated||!original.enabled)throw new InvalidOperationException("Sitting interruption did not restore the native renderer.");
                actor.SetConversationFacing(false);refreshClaims();access.Advance(actor);
                if(!actor.IsSeated)throw new InvalidOperationException("Seat pose could not resume after interruption.");
                actor.gameObject.SetActive(false);
                if(actor.IsSeated||!original.enabled)throw new InvalidOperationException("Disabling a seated actor did not restore the native renderer.");
                actor.gameObject.SetActive(true);refreshClaims();access.Advance(actor);
                if(!actor.IsSeated)throw new InvalidOperationException("Seat pose could not resume after actor reactivation.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SITTING_POSE_PASS: eight native bearings; actual chair heading; estimated seat anchor with floor lift; native idle pixel scale="+nativePixel+"; seated art factor="+SittingArt.BodyScale+"; seatHeight="+(slot.Surface.y-slot.Ground.y)+"; feetAboveFloor="+(pose.FeetWorldY-slot.Ground.y)+"; native renderer restored on interruption.");
            }
            finally
            {
                game.PlayerMotor.transform.position=beforePosition;game.PlayerMotor.transform.rotation=beforeRotation;camera.transform.localRotation=beforeCamera;
            }
        }
        private static bool TryMapPixelToWorld(Transform visual,Mesh mesh,Texture texture,SittingArt.Frame frame,float pixelX,float pixelY,out Vector3 world)
        {
            world=Vector3.zero;
            if(!visual||!mesh||!texture||frame==null||frame.rect==null||frame.rect.Length!=4)return false;
            var target=new Vector2((frame.rect[0]+pixelX)/texture.width,1f-(frame.rect[1]+pixelY)/texture.height);
            var vertices=mesh.vertices;var uvs=mesh.uv;var triangles=mesh.triangles;
            for(int i=0;i+2<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if(a>=vertices.Length||b>=vertices.Length||c>=vertices.Length||a>=uvs.Length||b>=uvs.Length||c>=uvs.Length)continue;
                var ab=uvs[b]-uvs[a];var ac=uvs[c]-uvs[a];var ap=target-uvs[a];
                float denominator=ab.x*ac.y-ab.y*ac.x;if(Mathf.Abs(denominator)<0.0000001f)continue;
                float wb=(ap.x*ac.y-ap.y*ac.x)/denominator;float wc=(ab.x*ap.y-ab.y*ap.x)/denominator;float wa=1-wb-wc;
                if(wa<-0.0001f||wb<-0.0001f||wc<-0.0001f)continue;
                world=visual.TransformPoint(vertices[a]*wa+vertices[b]*wb+vertices[c]*wc);return true;
            }
            return false;
        }
    }
}
