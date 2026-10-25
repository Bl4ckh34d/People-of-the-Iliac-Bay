using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // One native crime-update event invalidates the verbal-only exception, even for another Assault.
    public sealed class NativeVerbalJustice : MonoBehaviour
    {
        public static NativeVerbalJustice Current {get;private set;}
        private Func<WorldStateStore> getStore;
        private Func<bool> enabledForGame;
        private PlayerEntity player;

        public void Install(Func<WorldStateStore> store,Func<bool> available)
        {
            getStore=store;enabledForGame=available;Current=this;
            Update();
            UIWindowFactory.RegisterCustomUIWindow(UIWindowType.Court,typeof(NpcVerbalCourtWindow));
        }
        private void Update()
        {
            var game=GameManager.Instance;var current=game?game.PlayerEntity:null;
            if(ReferenceEquals(player,current))return;
            if(player!=null)player.OnCrimeUpdate-=CrimeUpdated;
            player=current;
            if(player!=null)player.OnCrimeUpdate+=CrimeUpdated;
        }
        private void CrimeUpdated(PlayerEntity.Crimes crime)
        {if(getStore!=null)getStore().ClearNpcVerbalCharge();}

        public WorldStateStore CourtStore()
        {
            return enabledForGame!=null && enabledForGame() && player!=null &&
                player.CrimeCommitted==PlayerEntity.Crimes.Assault?getStore():null;
        }
        private void OnDestroy()
        {
            if(player!=null)player.OnCrimeUpdate-=CrimeUpdated;
            if(Current==this)Current=null;
        }
    }
}
