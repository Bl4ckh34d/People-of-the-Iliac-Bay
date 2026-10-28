using System;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.Core { public sealed class WorldStateStore {} }
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up {get{return new Vector3(0,1,0);}}
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
    }
}
namespace DaggerfallWorkshop.Game
{
    public sealed class GameManager
    {public static readonly GameManager Instance=new GameManager();public readonly PlayerMotor PlayerMotor=new PlayerMotor();}
    public sealed class Transform {public UnityEngine.Vector3 position;}
    public sealed class PlayerMotor
    {
        public readonly Transform transform=new Transform();public UnityEngine.Vector3 MeasuredFloor;
        public UnityEngine.Vector3 FindGroundPosition(float distance=10){return MeasuredFloor;}
    }
}
namespace DaggerfallWorkshop
{
    public sealed class DaggerfallUnity
    {
        public static readonly DaggerfallUnity Instance=new DaggerfallUnity();
        public readonly Clock WorldTime=new Clock();
    }
    public sealed class Clock {public readonly Date Now=new Date();}
    public sealed class Date {public ulong Whole;public float Second;public ulong ToSeconds(){return Whole;}}
}
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class ResidentActor {private readonly PeopleOfTheIliacBay.Core.WorldStateStore stateStore=new PeopleOfTheIliacBay.Core.WorldStateStore();}
}
internal static class Program
{
    private static int Main()
    {
        var player=DaggerfallWorkshop.Game.GameManager.Instance.PlayerMotor;
        player.MeasuredFloor=new UnityEngine.Vector3(28.3f,38.4f,12.8f);
        foreach(float height in new[]{1.6f,1f,0.5f})
        {
            player.transform.position=new UnityEngine.Vector3(28.3f,38.4f+height,12.8f);
            var target=NativeDungeonObserverGround.Read();
            if(target.y!=player.MeasuredFloor.y || target.x!=player.MeasuredFloor.x || target.z!=player.MeasuredFloor.z)
                throw new Exception("Dungeon destination used a fixed player-root offset instead of its native measured floor: "+target.y);
        }
        Console.WriteLine("PASS actual runtime dungeon observer target follows native measured floor across standing, restored and crouched root heights");
        if(!NativeDungeonSceneReadiness.AllowsAdvance(true,true,false,false,false))
            throw new Exception("Loaded dungeon follower is blocked by the deliberately suppressed exterior.");
        if(NativeDungeonSceneReadiness.AllowsAdvance(true,false,false,false,false) ||
            NativeDungeonSceneReadiness.AllowsAdvance(true,true,true,false,false) ||
            NativeDungeonSceneReadiness.AllowsAdvance(true,true,false,false,true) ||
            NativeDungeonSceneReadiness.AllowsAdvance(false,false,false,false,false) ||
            !NativeDungeonSceneReadiness.AllowsAdvance(false,false,false,true,false))
            throw new Exception("Actual dungeon/exterior readiness or active streaming protection was lost.");
        Console.WriteLine("PASS actual runtime scene readiness admits loaded dungeon with suppressed exterior and still rejects unavailable scene/active streaming");
        var clock=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now;
        foreach(ulong epoch in new[]{12597595560UL,12597595703UL,12597596161UL})
            foreach(float second in new[]{0.25f,5.5f,59.75f})
            {
                clock.Whole=epoch;clock.Second=second;
                double expected=epoch+(second-Math.Floor(second)),actual=ResidentActor.CompanionNow();
                if(actual!=expected)throw new Exception("Native companion clock lost epoch precision: expected="+expected.ToString("R")+"; actual="+actual.ToString("R"));
            }
        Console.WriteLine("PASS actual companion runtime clock preserves native ulong epoch and float fractional seconds in nine cases");return 0;
    }
}
