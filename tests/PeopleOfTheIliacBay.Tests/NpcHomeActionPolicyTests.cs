using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcHomeActionPolicyTests
    {
        private static bool Allows(NpcActionKind kind,bool atHome,bool awake,Activity current,
            IList<Activity> window,out string reason)
        {
            reason="Policy unavailable.";
            var type=typeof(NpcActionContract).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcHomeActionPolicy");
            var method=type==null?null:type.GetMethod("Allows");
            if(method==null)return false;
            var args=new object[]{kind,atHome,awake,current,window,null};
            bool result=(bool)method.Invoke(null,args);reason=(string)args[5];return result;
        }
        private static bool CanRelease(Activity current,bool homeOnly,bool hasFutureCommitment,out string reason)
        {
            reason="Policy unavailable.";
            var type=typeof(NpcActionContract).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcHomeActionPolicy");
            var method=type==null?null:type.GetMethod("CanReleaseHomeOnlyRecovery");
            if(method==null)return false;
            var args=new object[]{current,homeOnly,hasFutureCommitment,null};
            bool result=(bool)method.Invoke(null,args);reason=(string)args[3];return result;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("A voluntary repair errand uses free home time and keeps every binding duty",()=>{
                var type=typeof(NpcActionContract).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcHomeActionPolicy");
                var method=type==null?null:type.GetMethod("AllowsRepairErrand");
                Check.True(method!=null,"Repair errand policy unavailable.");
                bool Accept(NpcActionKind kind,bool context,bool awake,Activity current,Activity[] window,bool family,bool medical,bool safe)
                {
                    var arguments=new object[]{kind,context,awake,current,window,family,medical,safe,null};
                    return (bool)method.Invoke(null,arguments);
                }
                var free=new[]{Activity.Home,Activity.Home};
                foreach(var kind in new[]{NpcActionKind.Exit,NpcActionKind.Enter,NpcActionKind.Wait,NpcActionKind.Purchase})
                {
                    Check.True(Accept(kind,true,true,Activity.Home,free,true,true,true),kind+" must use genuine free time.");
                    Check.True(!Accept(kind,false,true,Activity.Home,free,true,true,true),"Unverified repair context.");
                    Check.True(!Accept(kind,true,false,Activity.Home,free,true,true,true),"Sleeping actor.");
                    Check.True(!Accept(kind,true,true,Activity.Work,free,true,true,true),"Current fixed work.");
                    Check.True(!Accept(kind,true,true,Activity.Home,new[]{Activity.Home,Activity.Work},true,true,true),"Upcoming work.");
                    Check.True(!Accept(kind,true,true,Activity.Home,new[]{Activity.Home,Activity.Sleep},true,true,true),"Upcoming sleep.");
                    Check.True(!Accept(kind,true,true,Activity.Home,free,false,true,true),"Childcare obligation.");
                    Check.True(!Accept(kind,true,true,Activity.Home,free,true,false,true),"Medical obligation.");
                    Check.True(!Accept(kind,true,true,Activity.Home,free,true,true,false),"Alarm or custody.");
                }
                Check.True(!Accept(NpcActionKind.Attack,true,true,Activity.Home,free,true,true,true),"No unrelated action expansion.");
            }));
            cases.Add(("Awake adult home actions may use local adapters but never cross work or sleep",()=>{
                string reason;
                var window=new[]{Activity.Home,Activity.Travel,Activity.Visit};
                foreach(var kind in new[]{NpcActionKind.Wait,NpcActionKind.Follow,NpcActionKind.MoveTo,
                    NpcActionKind.Interact,NpcActionKind.Transfer,NpcActionKind.TakeSeat})
                    Check.True(Allows(kind,true,true,Activity.Home,window,out reason),kind+": "+reason);
                Check.True(!Allows(NpcActionKind.Wait,true,true,Activity.Home,
                    new[]{Activity.Home,Activity.Work},out reason) && reason.Contains("work"),reason);
                Check.True(!Allows(NpcActionKind.Transfer,true,true,Activity.Home,
                    new[]{Activity.Home,Activity.Sleep},out reason) && reason.Contains("sleep"),reason);
                Check.True(!Allows(NpcActionKind.Wait,false,true,Activity.Home,window,out reason));
                Check.True(!Allows(NpcActionKind.Wait,true,false,Activity.Home,window,out reason));
                Check.True(!Allows(NpcActionKind.Wait,true,true,Activity.Visit,window,out reason));
                foreach(var kind in new[]{NpcActionKind.Purchase,NpcActionKind.ChangeSchedule,NpcActionKind.Attack})
                    Check.True(!Allows(kind,true,true,Activity.Home,window,out reason),kind+" must remain unsupported at home.");
            }));
            cases.Add(("Home-only recovery releases only at home with no future commitments",()=>{
                string reason;
                Check.True(CanRelease(Activity.Home,true,false,out reason),reason);
                Check.True(CanRelease(Activity.Sleep,true,false,out reason),reason);
                Check.True(!CanRelease(Activity.Travel,true,false,out reason) && reason.Contains("home"),reason);
                Check.True(!CanRelease(Activity.Home,false,false,out reason) && reason.Contains("verified"),reason);
                Check.True(!CanRelease(Activity.Home,true,true,out reason) && reason.Contains("future"),reason);
            }));
        }
    }
}
