using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static object Call(string method,params object[] args)
    {
        var adapter=typeof(Program).Assembly.GetType("PeopleOfTheIliacBay.DFU.NativeInnCalendar");
        if(adapter==null)throw new InvalidOperationException("Missing native Inn calendar adapter.");
        try{return adapter.GetMethod(method,BindingFlags.Public|BindingFlags.Static).Invoke(null,args);}
        catch(TargetInvocationException e) when(e.InnerException!=null){throw e.InnerException;}
    }
    private static NpcInnServiceTariff Tariff(int region,double at){return (NpcInnServiceTariff)Call("ForServiceAt",region,at);}
    private static void Require(bool yes,string reason){if(!yes)throw new InvalidOperationException(reason);}
    private static void Rejected(int region,double at)
    {try{Tariff(region,at);}catch(ArgumentException){return;}throw new InvalidOperationException("Unsafe native calendar time/region was accepted.");}
    private static int Main()
    {
        var epochDate=new DaggerfallDateTime();epochDate.FromClassicDaggerfallTime(0);double epoch=epochDate.ToSeconds();
        double wrap=epoch+((double)uint.MaxValue+1)*60;
        var cases=new List<(string,Action)>{
            ("native epoch and sub-minute service date",()=>{
                var at=Tariff(1,epoch+.9);Require(at.AbsoluteDay==Math.Floor(epoch/86400d) && at.RegionIndex==1,"Wrong exact day/region.");
                Require(Tariff(1,epoch+59.9).AbsoluteDay==at.AbsoluteDay,"Fractional minute shifted the service date.");
            }),
            ("underflow and non-finite timestamps rejected",()=>{foreach(double at in new[]{epoch-1,-1d,double.NaN,double.PositiveInfinity,double.MaxValue})Rejected(1,at);}),
            ("native uint classic-minute wrap rejected",()=>{Require(Tariff(1,wrap-1).AbsoluteDay==Math.Floor((wrap-1)/86400),"Last representable native minute was lost.");Rejected(1,wrap);Rejected(1,wrap+1);}),
            ("native destination-region holiday provenance",()=>{Require(Tariff(24,epoch+86400).HolidayId==2,"Regional holiday was read from player region.");Require(Tariff(0,epoch+86400).HolidayId==0,"Regional holiday leaked to another region.");}),
            ("historical Harvest End and New Life use requested date",()=>{Require(Tariff(4,epoch+236*86400+3600).HolidayId==37,"Historical Harvest End was lost.");Require(Tariff(4,epoch+3600).HolidayId==1,"Historical New Life was lost.");}),
            ("calendar adapter preserves unrelated native date",()=>{var live=new DaggerfallDateTime();live.FromSeconds((ulong)(epoch+300*86400));ulong before=live.ToSeconds();Tariff(0,epoch+3600);Require(live.ToSeconds()==before,"Calendar quote mutated another clock.");}),
            ("invalid destination regions rejected",()=>{Rejected(-1,epoch);Rejected(256,epoch);}),
            ("native all-day sentinel preserves final minute",()=>{var args=new object[]{0,25,0,0};Require((bool)Call("TryServiceHours",args),"All-day Inn closed.");Require((int)args[2]==0 && (int)args[3]==1440,"Last native service minute was truncated.");}),
            ("ordinary native service hours preserved",()=>{var args=new object[]{6,21,0,0};Require((bool)Call("TryServiceHours",args),"Native service window rejected.");Require((int)args[2]==360 && (int)args[3]==1260,"Native hour conversion changed.");}),
            ("invalid and empty native hours rejected",()=>{foreach(var window in new[]{new[]{0,0},new[]{24,25},new[]{-1,25},new[]{0,26},new[]{22,8}}){var args=new object[]{window[0],window[1],0,0};Require(!(bool)Call("TryServiceHours",args),"Invalid native hours accepted.");}})
        };
        int failed=0;foreach(var test in cases)try{test.Item2();Console.WriteLine("PASS "+test.Item1);}catch(Exception e){failed++;Console.WriteLine("FAIL "+test.Item1+": "+e.Message);}
        Console.WriteLine($"Native Inn calendar: {cases.Count-failed}/{cases.Count} passed; {failed} failed.");return failed==0?0:1;
    }
}
