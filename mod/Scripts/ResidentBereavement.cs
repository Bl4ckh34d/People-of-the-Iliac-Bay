using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using LivingCities.Core;
namespace LivingCities.DFU
{
    public sealed partial class LivingCitiesMod
    {
        private readonly Dictionary<string,int> bereavementGreetings=new Dictionary<string,int>();
        internal void ProbeBereavementGreeting(string residentId)
        {
            var original=store;
            try
            {
                var isolated=new WorldStateStore(1,LivingCitiesMod.ConversationArchiveRoot());isolated.Restore(store.Snapshot());store=isolated;
                Resident person=null,partner=null;int settlement=0;
                foreach(var town in store.Snapshot().Settlements)
                    foreach(var home in town.Households)
                        if(home.Residents.Exists(r=>r.Id==residentId))
                        {person=home.Residents.Find(r=>r.Id==residentId);settlement=town.SettlementId;}
                if(person==null || person.PartnerId==null) throw new InvalidOperationException("Mourning fixture requires a partner.");
                var live=store.GetOrCreate(settlement,new int[0]);
                foreach(var home in live.Households) {partner=home.Residents.Find(r=>r.Id==person.PartnerId);if(partner!=null) break;}
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var incident=store.RecordViolence(settlement,partner.Id,now-10);
                partner.IsAlive=false;store.LocateViolence(incident.Id,partner.HomeKey,0,0,0);store.ConfirmCorpse(incident.Id,now-9);
                store.Restore((WorldState)DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Deserialize(typeof(WorldState),
                    DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
                int day=(int)(now/86400);
                while(StableSeed.Combine(person.AppearanceSeed,(uint)day,0x67726965,0)%10000>=5900) person.AppearanceSeed++;
                bereavementGreetings.Remove(person.Id);ShowBereavementGreeting(person);
                if(!(DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox) || !DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.TalkWindow))
                    throw new InvalidOperationException("Mourning greeting replaced normal dialogue.");
                DaggerfallUI.UIManager.PopWindow();
                if(DaggerfallUI.UIManager.TopWindow!=DaggerfallUI.Instance.TalkWindow)
                    throw new InvalidOperationException("Closing mourning greeting did not restore normal topics.");
                ShowBereavementGreeting(person);
                if(DaggerfallUI.UIManager.TopWindow!=DaggerfallUI.Instance.TalkWindow)
                    throw new InvalidOperationException("Mourning greeting repeated on the same day.");
                UnityEngine.Debug.Log("LIVING_CITIES_BEREAVEMENT_PASS: native death restore, mourning overlay, normal topics retained, no same-day repeat.");
            }
            finally {store=original;bereavementGreetings.Remove(residentId);}
        }
        private void ShowBereavementGreeting(Resident resident)
        {
            if(store==null || saveFault) return;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int day=(int)(now/86400);int previous;
            if(bereavementGreetings.TryGetValue(resident.Id,out previous) && previous==day) return;
            var grief=store.BereavementFor(resident,now);
            if(grief==null || grief.WitnessedPlayerKilling) return;
            bereavementGreetings[resident.Id]=day;
            // Stable daily choice prevents rerolling a conversation for a different response.
            if(StableSeed.Combine(resident.AppearanceSeed,(uint)day,0x67726965,0)%10000>=grief.MentionChance*10000) return;
            string line=now-grief.DiedAt<30*86400d
                ? "I lost "+grief.Name+" recently. Our home feels so empty."
                : "I still miss "+grief.Name+". Some days are harder than others.";
            var window=new DaggerfallMessageBox(DaggerfallUI.UIManager);
            window.SetText(new[]{resident.FullName,line});window.ClickAnywhereToClose=true;
            // Keep the normal talk window below this greeting, including its quest topics.
            DaggerfallUI.UIManager.PushWindow(window);
        }
    }
}
