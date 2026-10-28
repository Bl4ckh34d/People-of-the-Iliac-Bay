using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int checks;
    private static void Require(bool condition,string reason)
    { if(!condition)throw new InvalidOperationException(reason);checks++; }
    private static int Main()
    {
        try
        {
        for(uint person=0;person<200;person++)
        for(int day=0;day<10;day++)
        {
            int morning=MarketWorkday.ArrivalMinute(person,day);
            int empty=MarketWorkday.ClosingMinute(person,day,false);
            int customer=MarketWorkday.ClosingMinute(person,day,true);
            Require(morning>=360 && morning<=390,"Morning must fit 06:00–06:30.");
            Require(empty>=1065 && empty<=1080,"Empty post must close during early half of window.");
            Require(customer>=1080 && customer<=1095 && customer>=empty,"Actual customers extend work into late half.");
            Require(morning==MarketWorkday.ArrivalMinute(person,day),"Same day must remain deterministic.");
        }
        Require(MarketWorkday.ArrivalMinute(9,1)!=MarketWorkday.ArrivalMinute(9,2),"Morning needs day variation.");
        Require(!MarketWorkday.MayClose(1064*60,1070,false,false),"Do not close before selected time.");
        Require(MarketWorkday.MayClose(1070*60,1070,false,false),"Close at selected time.");
        Require(!MarketWorkday.MayClose(1200*60,1070,true,false),"Never interrupt active dialogue.");
        Require(!MarketWorkday.MayClose(1200*60,1070,false,true),"Never interrupt active transaction.");
        Require(MarketWorkday.MayClose(86400+1070*60,1070,false,false),"Clock day offset must not change close boundary.");
        Console.WriteLine("PASS market commute policy: "+checks+" checks");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
    }
}
