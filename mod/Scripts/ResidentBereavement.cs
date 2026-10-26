using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        internal void ProbeBereavementGreeting(string residentId)
        {
            var original=store;
            try
            {
                var isolated=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot());isolated.Restore(store.Snapshot());store=isolated;
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
                if(store.BereavementFor(person,now)!=null)throw new InvalidOperationException("Remote death leaked into grief.");
                store.NoticeInteriorCorpse(incident.Id,person.Id,partner.HomeKey,now-8);
                store.Restore((WorldState)DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Deserialize(typeof(WorldState),
                    DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
                person=store.ResolveResident(residentId);
                int day=(int)(now/86400);
                while(StableSeed.Combine(person.AppearanceSeed,(uint)day,0x67726965,0)%10000>=5900) person.AppearanceSeed++;
                ShowBereavementGreeting(person);
                if(!(DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox) || !DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.TalkWindow))
                    throw new InvalidOperationException("Mourning greeting replaced normal dialogue.");
                DaggerfallUI.UIManager.PopWindow();
                if(DaggerfallUI.UIManager.TopWindow!=DaggerfallUI.Instance.TalkWindow)
                    throw new InvalidOperationException("Closing mourning greeting did not restore normal topics.");
                store.Restore((WorldState)DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Deserialize(typeof(WorldState),
                    DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
                ShowBereavementGreeting(store.ResolveResident(residentId));
                if(DaggerfallUI.UIManager.TopWindow!=DaggerfallUI.Instance.TalkWindow)
                    throw new InvalidOperationException("Mourning greeting repeated on the same day.");
                UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BEREAVEMENT_PASS: unknown remote death excluded, discovered death retained, mourning overlay, normal topics retained, no repeat after native restore.");
            }
            finally {store=original;}
        }
        private void ShowBereavementGreeting(Resident resident)
        {
            if(store==null || saveFault || resident==null) return;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            Bereavement grief;
            if(!store.TryBereavementGreeting(resident,now,out grief))return;
            string line=now-grief.DiedAt<30*86400d
                ? "I lost my "+grief.Relationship+", "+grief.Name+", recently. I miss them."
                : "I still miss "+grief.Name+". Some days are harder than others.";
            var window=new DaggerfallMessageBox(DaggerfallUI.UIManager);
            window.SetText(new[]{resident.FullName,line});window.ClickAnywhereToClose=true;
            // Keep the normal talk window below this greeting, including its quest topics.
            DaggerfallUI.UIManager.PushWindow(window);
        }
    }
}
