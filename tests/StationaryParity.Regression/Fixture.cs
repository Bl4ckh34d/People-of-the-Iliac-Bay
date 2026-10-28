using System;
public class Component {public static implicit operator bool(Component value)=>value!=null;}
public class DaggerfallEntity {public int CurrentHealth=1;public Action<DaggerfallEntity> Death;public void SetHealth(int value){CurrentHealth=value;if(value<=0)Death?.Invoke(this);}}
public class NativePerson {public string Id="native";public bool IsAlive=true;}
public class Store {public NativePerson ResolveResident(string id)=>null;}
public class NativeNpcInventoryOwner:Component {public int Captures;public void Capture(){Captures++;}}
public class Person {public bool Destroyed;public object QuestorData;public int FactionIndex;public void DestroyNPC(){Destroyed=true;}}
public class Foe {public int Kills;public bool Injured;public void SetInjured(){Injured=true;}public void IncrementKills(){Kills++;}}
public class QuestResourceBehaviour:Component {public object TargetResource;public bool IsFoeDead;}
public class NpcData {public int factionID;}
public class StaticNPC:Component {public NpcData Data=new NpcData();}
public class QuestMachine:Component {public static QuestMachine Instance;public ulong[] GetAllActiveQuests()=>new ulong[0];public Quest GetQuest(ulong uid)=>null;public bool IsNPCDataEqual(object a,object b)=>false;public bool IsIndividualNPC(int id)=>false;}
public class Quest {public object[] GetAllResources(Type type)=>new object[0];}
public class GameObject {public bool Active=true;public void SetActive(bool active){Active=active;}}
public class Host {public int Observed,Deaths;public bool PlayerAttributed;public void ObserveStationaryPlayerHit(Fixture life){Observed++;}public void RecordStationaryDeath(Fixture life,bool player){Deaths++;PlayerAttributed=player;life.Person.IsAlive=false;}}
public class Fixture
{
    bool finished,playerHit,nativeDeath;Store store=new Store();Host host=new Host();NativeNpcInventoryOwner inventory=new NativeNpcInventoryOwner();
    DaggerfallEntity entity=new DaggerfallEntity();public NativePerson Person=new NativePerson();GameObject gameObject=new GameObject();QuestResourceBehaviour resource;int destroyed;
    public Fixture(){entity.Death=Died;}
    bool Current(object world)=>true;
    T GetComponent<T>() where T:class {if(typeof(T)==typeof(NativeNpcInventoryOwner))return inventory as T;if(typeof(T)==typeof(QuestResourceBehaviour))return resource as T;return null;}
    void Destroy(object value){destroyed++;}
    // ACTUAL_METHODS
    static int checks;static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);checks++;Console.WriteLine("PASS "+message);}
    public static int Run()
    {
        var civilian=new Fixture();var done=civilian.BeginPlayerHit(true);done(null);
        Need(civilian.entity.CurrentHealth==0 && civilian.host.Deaths==1 && !civilian.gameObject.Active,"stationary civilian melee uses actual native death event and disables one body");
        Need(civilian.inventory.Captures==1 && civilian.destroyed==1 && civilian.host.PlayerAttributed,"stationary death captures actual inventory once and retains real player attribution");
        civilian.entity.SetHealth(0);Need(civilian.host.Deaths==1 && civilian.destroyed==1,"repeated native death event creates no second body or capture");
        var questPerson=new Person();var quest=new Fixture{resource=new QuestResourceBehaviour{TargetResource=questPerson}};quest.BeginPlayerHit(true)(null);
        Need(questPerson.Destroyed && quest.host.Deaths==1,"quest person receives native DestroyNPC notification without invulnerability exemption");
        var foe=new Foe();var questFoe=new Fixture{resource=new QuestResourceBehaviour{TargetResource=foe}};questFoe.BeginPlayerHit(true)(null);
        Need(foe.Injured && foe.Kills==1,"static foe resource retains native injury and kill notifications");
        var spell=new Fixture();spell.BeginPlayerHit(false)(null);Need(spell.entity.CurrentHealth==1 && spell.host.Deaths==0,"nonlethal native spell never becomes forced melee death");
        spell.entity.SetHealth(0);Need(!spell.host.PlayerAttributed,"surviving player spell does not attribute later unrelated death");
        var lethalSpell=new Fixture();var after=lethalSpell.BeginPlayerHit(false);lethalSpell.entity.SetHealth(0);after(null);
        Need(lethalSpell.host.PlayerAttributed,"actual lethal player spell retains its native damage attribution");
        var native=new Fixture{nativeDeath=true};native.BeginPlayerHit(true)(null);Need(native.entity.CurrentHealth==1,"existing native enemy keeps actual combat damage rules");
        native.entity.SetHealth(0);Need(!native.Person.IsAlive && native.host.Deaths==0 && native.destroyed==0,"existing native enemy death keeps one native corpse and loot owner");
        var failed=new Fixture();failed.BeginPlayerHit(true)(new InvalidOperationException());Need(failed.entity.CurrentHealth==1,"failed native hit does not manufacture death");
        Console.WriteLine(checks+" runtime checks passed");return 0;
    }
}
