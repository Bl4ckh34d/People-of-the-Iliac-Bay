using System;
using PeopleOfTheIliacBay.Core;

static class Program
{
    static int checks;
    static void Check(bool result,string name){if(!result)throw new Exception(name);checks++;}
    static void Main()
    {
        Check(NpcSpritePolicy.Choose(false,false,false,true,true,true,true,true)==null,"dead actors have no override");
        Check(NpcSpritePolicy.Choose(true,true,false,true,true,true,true,true)==null,"sleep owns renderer");
        Check(NpcSpritePolicy.Choose(true,false,true,true,true,true,true,true)==null,"seat owns renderer");
        Check(NpcSpritePolicy.Choose(true,false,false,true,true,true,true,true)=="swim","water before grounded actions");
        Check(NpcSpritePolicy.Choose(true,false,false,false,true,true,true,true)=="jump","jump before melee/run");
        Check(NpcSpritePolicy.Choose(true,false,false,false,false,true,true,true)=="climb","climb before melee/run");
        Check(NpcSpritePolicy.Choose(true,false,false,false,false,false,true,true)=="melee","attack before run");
        Check(NpcSpritePolicy.Choose(true,false,false,false,false,false,false,true)=="run","run request");
        Check(NpcSpritePolicy.Choose(true,false,false,false,false,false,false,false)==null,"no inferred action");
        Check(NpcSpritePolicy.MatchesProvider("","") && !NpcSpritePolicy.MatchesProvider("","replacement"),"no vanilla art for replaced identity");
        Check(NpcSpritePolicy.MatchesProvider("ABC","abc") && !NpcSpritePolicy.MatchesProvider("ABC","DEF"),"provider exact ignoring GUID case");
        Check(NpcSpritePolicy.Phase(0.99,4,8,true)==3,"loop phase");
        Check(NpcSpritePolicy.Phase(1,4,8,true)==0,"loop boundary");
        Check(NpcSpritePolicy.Phase(3,4,8,false)==3,"one shot never loops");
        Check(NpcSpritePolicy.JumpPhase(0.1)==0 && NpcSpritePolicy.JumpPhase(0.3)==1 && NpcSpritePolicy.JumpPhase(0.6)==2 && NpcSpritePolicy.JumpPhase(0.95)==3,"jump uses trajectory progress");
        Console.WriteLine("Completed NPC policy: "+checks+" passed");
        RendererHandoff.Verify();
    }
}
