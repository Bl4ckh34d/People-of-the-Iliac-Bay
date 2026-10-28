using System;
using System.Collections.Generic;

public class Body {
    public static implicit operator bool(Body value)=>value!=null;
    public Entity Entity=new Entity();public Transform transform=new Transform();public GameObject gameObject=new GameObject();
    public bool Wall=true,Visible=true;public GuardAttackEvidence Evidence;
    public T GetComponent<T>() where T:class=>Evidence as T;
}
public class DaggerfallEntityBehaviour:Body {}
public enum MobileTeams {Other,PlayerAlly}
public class Entity {public int CurrentHealth=100;public bool IsParalyzed,IsInvisible;public MobileTeams Team;}
public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 up=>new Vector3(0,1,0);public static Vector3 operator +(Vector3 left,Vector3 right)=>new Vector3(left.x+right.x,left.y+right.y,left.z+right.z);}
public class Transform {public Vector3 position;public Vector3 TransformPoint(Vector3 point)=>point;}
public class TopPoint {public double X=10,Y=8,Z=20;}
public class Directory {public Transform transform=new Transform();}
public class GameObject {public T AddComponent<T>() where T:new()=>new T();}
public class GuardAttackEvidence:Body {public bool ResidentAssault;public Body Victim;public double Until;}
public enum RefugePhase {WallPosted,Approaching}
public class Refuge {public object WallRoute=new object();public RefugePhase Phase=RefugePhase.WallPosted;public Refuge Evaluate(double now)=>this;}
public enum GuardDuty {Posted}
public class GuardRecord {public string Id="guard";public bool IsAlive=true;public object Interior;public Refuge Refuge=new Refuge();public object BowSupportGuardId;public GuardDuty Duty;public bool HasBow=true;public int ArrowsRemaining=20;}
public class SavedRoutePoint {public double X,Z;}
public class GuardRoster {public List<GuardRecord> Guards=new List<GuardRecord>();public SavedRoutePoint Position(string id,double now)=>new SavedRoutePoint();public List<string> DispatchDefense(string id,string key,double now,Dictionary<string,List<SavedRoutePoint>> routes,int count,double speed,double scale)=>new List<string>{id};}
public static class GuardArchery {public static void EnsureLoadout(GuardRecord guard){}}
public class Geometry {public Vector3 ScenePoint(RoutePoint p)=>new Vector3();public List<SavedRoutePoint> RouteBetweenPoints(SavedRoutePoint from,SavedRoutePoint to)=>new List<SavedRoutePoint>();}
public class RoutePoint {public RoutePoint(double x,double z){}}
public class DaggerfallUnity {public static DaggerfallUnity Instance=new DaggerfallUnity();public Clock WorldTime=new Clock();public TextProvider TextProvider=new TextProvider();}
public class TextProvider {public int GetRSCTokens(int id)=>id;}
public struct Color {public static Color clear;}
public class Panel {public Color BackgroundColor;}
public class DaggerfallUI {public static object UIManager=new object();}
public class DaggerfallMessageBox {
    public static DaggerfallMessageBox Latest;public static int Count;public int Text;public Panel ParentPanel=new Panel();
    public enum MessageBoxButtons {Yes,No}
    public event Action<DaggerfallMessageBox,MessageBoxButtons> OnButtonClick;
    public DaggerfallMessageBox(object manager){}
    public void SetTextTokens(int text){Text=text;}
    public void AddButton(MessageBoxButtons button){}
    public void Show(){Latest=this;Count++;}
    public void CloseWindow(){}
    public void Choose(MessageBoxButtons button){OnButtonClick(this,button);}
}
public class Clock {public double TimeScale=1;}
public enum MobileStates {Idle,Move,PrimaryAttack}
public class MobileUnit {public MobileStates EnemyState=MobileStates.PrimaryAttack;public void ChangeEnemyState(MobileStates state){EnemyState=state;}}
public class PlayerEntity {public enum Crimes {None,Trespassing,Assault,Murder} public Crimes CrimeCommitted;public bool HaveShownSurrenderToGuardsDialogue;public int RepCharges,NativeSurrenders;public void LowerRepForCrime(){RepCharges++;}public bool SurrenderToCityGuards(bool voluntary){NativeSurrenders++;return true;}}
public class GameManager {public static bool IsGamePaused;public static GameManager Instance=new GameManager();public DaggerfallEntityBehaviour PlayerEntityBehaviour=new DaggerfallEntityBehaviour();public PlayerEntity PlayerEntity=new PlayerEntity();}
public class EnemyMotor {public bool IsHostile,enabled;public void MakeEnemyHostileToAttacker(DaggerfallEntityBehaviour target){IsHostile=true;}}
public class EnemySenses {public bool enabled;public DaggerfallEntityBehaviour Target;public object LastKnownTargetPos,OldLastKnownTargetPos,PredictedTargetPos;}
public class EnemyAttack {public bool enabled;public static float minRangedDistance=6,maxRangedDistance=30;public float MeleeDistance=2.25f;}
public static class CivilianResponses {public static bool Sees(object from,object to,float range)=>true;}
public enum GuardShift {Day,Night}
public static class GuardShifts {
    public static GuardShift At(double now)=>now>=21600 && now<64800?GuardShift.Day:GuardShift.Night;
    static void Validate(double now){if(double.IsNaN(now)||now<0)throw new ArgumentException();}
    // POLICY_METHOD
}
public class GuardActor:Body {
    public string GuardId="guard";
    public EnemyMotor Motor=new EnemyMotor();public EnemySenses Senses=new EnemySenses();public EnemyAttack Attack=new EnemyAttack();
    public DaggerfallEntityBehaviour WallAttacker;public int Shots,Ends;public bool LastLaw;
    // OFFER_METHOD
    Entity entity=new Entity();object guardBow=new object(),guardArrows=new object(),wallAttackStart;bool wallNativeAttackReady=true;
    public bool WallLawActive;public MobileUnit mobile=new MobileUnit();
    public bool WallCombatActive,wallCombatDisplaced;public DaggerfallEntityBehaviour wallCombatTarget;public object wallReturnCorners;
    public void ApplyPose(){var refugeState=MobileStates.Idle;var snapshot=new Refuge(); // POSE_STATEMENT
    }
    public new T GetComponent<T>() where T:class=>Motor as T??Senses as T??Attack as T;
    void InitializeArchery(){}
    object StartCoroutine(object work)=>work;
    object InitializeWallAttack()=>new object();
    void ShootGuardArrow(DaggerfallEntityBehaviour target,bool holdPost){Shots++;}
    bool ClearGuardArrowShot(DaggerfallEntityBehaviour target)=>true;
    public bool ShotRange(float distance,bool holdPost){var record=new GuardRecord();var target=new DaggerfallEntityBehaviour();return SHOT_CONDITION;}
    public void EndCombat(){Ends++;WallLawActive=false;Motor.IsHostile=false;Senses.Target=null;Attack.enabled=false;}
    public void RememberWallAttacker(DaggerfallEntityBehaviour attacker){WallAttacker=attacker;}
    public void BeginCombat(double now){}
    public void BeginDefenseCombat(DaggerfallEntityBehaviour attacker,double now){}
    // ATTACK_METHOD
}
public class Watch {
    public GuardRoster Roster=new GuardRoster();public Dictionary<string,GuardActor> actors=new Dictionary<string,GuardActor>();
    bool interior;Geometry geometry=new Geometry();
    bool EnemyDefense=true;public bool Sheltering;public List<DaggerfallEntityBehaviour> wallIntruders=new List<DaggerfallEntityBehaviour>();
    Dictionary<string,DaggerfallEntityBehaviour> defenseEnemies=new Dictionary<string,DaggerfallEntityBehaviour>();
    bool HauntingShelterNow(double now)=>Sheltering;
    bool WallGuardSees(GuardActor actor,DaggerfallEntityBehaviour target)=>target.Visible;
    bool IsWallIntruder(DaggerfallEntityBehaviour target)=>target.Wall;
    bool IsDefenseTarget(DaggerfallEntityBehaviour target,double now)=>false;
    void SurveyWallIntruders(double now){}
    bool OwnsTown()=>true;double Now()=>43200;string ThreatKey(DaggerfallEntityBehaviour target)=>"source";
    SavedRoutePoint Local(Vector3 position)=>new SavedRoutePoint();bool TrySendForArcher(GuardRecord guard,string key,double now)=>false;
    // HIT_METHOD
    // WATCH_METHOD
    public void Tick(GuardRecord guard,GuardActor actor,double now){DefendWallWatch(guard,actor,now);}
}
public static class Fixture {
    static int pass,fail;
    static void Need(bool value,string name){Console.WriteLine((value?"PASS ":"FAIL ")+name);if(value)pass++;else fail++;}
    static float ReturnObserverHeight(){var game=GameManager.Instance;var directory=new Directory();var geometry=new Geometry();var top=new TopPoint();return (RETURN_OBSERVER).y;}
    public static int Run(){
        Need(GuardShifts.WallTrespassEnforced(12*3600,false),"actual upper wall remains restricted at noon");
        Need(GuardShifts.WallTrespassEnforced(0,false),"actual upper wall remains restricted at midnight");
        Need(GuardShifts.WallTrespassEnforced(12*3600,true),"trespass policy stays separate from haunting staffing");
        var player=GameManager.Instance.PlayerEntityBehaviour;var guard=new GuardRecord();var watch=new Watch();var actor=new GuardActor();
        watch.wallIntruders.Add(player);watch.Tick(guard,actor,12*3600);
        Need(GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.Trespassing,"observed upper-wall player enters native trespassing law");
        Need(actor.Motor.IsHostile && actor.Senses.enabled && actor.Attack.enabled && actor.Motor.enabled && actor.Senses.Target==player,
            "wall arrest retains native movement, attack and sight during combat");
        Need(DaggerfallMessageBox.Count==1 && DaggerfallMessageBox.Latest.Text==15 && actor.Shots==0,
            "visible wall contact offers native RSC15 arrest before any successful projectile hit");
        DaggerfallMessageBox.Latest.Choose(DaggerfallMessageBox.MessageBoxButtons.No);actor.DefendWallPost(player);
        Need(DaggerfallMessageBox.Count==1 && GameManager.Instance.PlayerEntity.RepCharges==1 &&
            GameManager.Instance.PlayerEntity.NativeSurrenders==0 && actor.Shots==1,"actual No callback leaves native attacks active without repeated offer or forced surrender");
        GameManager.Instance.PlayerEntity.HaveShownSurrenderToGuardsDialogue=false;actor.DefendWallPost(player);
        DaggerfallMessageBox.Latest.Choose(DaggerfallMessageBox.MessageBoxButtons.Yes);
        Need(GameManager.Instance.PlayerEntity.NativeSurrenders==1,"actual Yes callback delegates accepted arrest to native court API");
        GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;player.Wall=false;actor=new GuardActor();watch.Tick(guard,actor,12*3600);
        Need(GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None && actor.Shots==0,"street below wall creates no accusation or shot");
        player.Wall=true;player.Visible=false;actor=new GuardActor();watch.Tick(guard,actor,12*3600);
        Need(GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None && actor.Shots==0,"hidden upper-wall player creates no remote accusation or shot");
        player.Visible=true;GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Murder;actor=new GuardActor();watch.Tick(guard,actor,12*3600);
        Need(GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.Murder,"wall enforcement preserves existing more serious native crime");
        watch.Sheltering=true;actor=new GuardActor();watch.Tick(guard,actor,12*3600);
        Need(actor.Shots==0,"haunting shelter still prevents active wall duty response");
        GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
        var hitWatch=new Watch();actor=new GuardActor();hitWatch.Roster.Guards.Add(guard);hitWatch.actors.Add(guard.Id,actor);
        player.Entity.Team=MobileTeams.PlayerAlly;hitWatch.ObserveGuardAttack(actor,player);
        Need(actor.WallAttacker==player && GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.Assault,
            "exact native player attack reaches wall guard despite player team and refuge duty");
        actor.WallLawActive=true;actor.ApplyPose();
        Need(actor.mobile.EnemyState==MobileStates.PrimaryAttack,"wall placement preserves native melee one-shot during lawful attack");
        GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;player.Wall=false;actor.Shots=0;hitWatch.Tick(guard,actor,12*3600);
        Need(actor.Shots==0,"native crime resolution ends remembered player assault response");
        actor.WallLawActive=true;
        Need(actor.ShotRange(3,true) && !actor.ShotRange(1,true),"fixed wall guard uses arrow in three-metre gap and melee within reach");
        actor.Motor.IsHostile=true;actor.DefendWallPost(new DaggerfallEntityBehaviour());
        Need(!actor.Motor.IsHostile && !actor.WallLawActive,"nonplayer wall target does not inherit native hostility to player");
        Need(ReturnObserverHeight()==1,"wall return geometry observer remains below actual eight-metre guard post");
        Console.WriteLine(pass+" passed; "+fail+" failed");return fail==0?0:1;
    }
}
