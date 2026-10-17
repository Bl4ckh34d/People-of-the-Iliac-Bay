using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class HauntingActivation : MonoBehaviour
    {
        private HauntingLifetime owner;
        public void Watch(HauntingLifetime value) { owner=value; if(gameObject.activeInHierarchy) owner.ExpireActivated(gameObject); }
        private void OnEnable() { if(owner) owner.ExpireActivated(gameObject); }
    }
    // Watch only actual haunting quest actors. Do not change general enemy persistence.
    [DefaultExecutionOrder(-10000)]
    public sealed class HauntingLifetime : MonoBehaviour
    {
        private readonly Dictionary<GameObject,double> expires=new Dictionary<GameObject,double>();
        private readonly List<GameObject> removed=new List<GameObject>();
        private Func<bool> enabledBySettings;
        public void Install(Func<bool> enabled)
        {
            enabledBySettings=enabled;
            GameManager.OnEnemySpawn+=Track;
            SaveLoadManager.OnLoad+=Loaded;
            PlayerEnterExit.OnTransitionExterior+=Exterior;
        }
        private void OnDestroy()
        {
            GameManager.OnEnemySpawn-=Track;
            SaveLoadManager.OnLoad-=Loaded;
            PlayerEnterExit.OnTransitionExterior-=Exterior;
        }
        private static double Now() { return DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(); }
        private void Track(GameObject actor)
        {
            if(!actor || expires.ContainsKey(actor)) return;
            var owner=actor.GetComponent<QuestResourceBehaviour>();
            var quest=owner ? QuestMachine.Instance.GetQuest(owner.QuestUID) : null;
            if(quest==null || !string.Equals(quest.QuestName,"S0000977",StringComparison.OrdinalIgnoreCase)) return;
            var enemy=actor.GetComponent<DaggerfallEnemy>();
            if(!enemy || (enemy.MobileUnit.Enemy.ID!=(int)MobileTypes.Ghost && enemy.MobileUnit.Enemy.ID!=(int)MobileTypes.Wraith)) return;
            double now=Now(),day=now-now%86400;
            double deadline=now%86400<5*3600 ? day+5*3600 :
                now%86400>=19*3600 ? day+86400+5*3600 : now;
            expires.Add(actor,deadline);
            var activation=actor.GetComponent<HauntingActivation>();
            if(!activation) activation=actor.AddComponent<HauntingActivation>();
            activation.Watch(this);
        }
        public void ExpireActivated(GameObject actor)
        {
            double deadline;
            if(enabledBySettings==null || !enabledBySettings() || !expires.TryGetValue(actor,out deadline)) return;
            if(Now()<deadline && !GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0)) return;
            actor.SetActive(false); Destroy(actor); expires.Remove(actor);
        }
        private void Loaded(SaveData_v1 data)
        {
            expires.Clear();
            // Includes the inactive exterior while loading a save made inside an inn.
            foreach(var owner in Resources.FindObjectsOfTypeAll<QuestResourceBehaviour>())
                if(owner && owner.gameObject.scene.IsValid()) Track(owner.gameObject);
            Sweep();
        }
        private void Exterior(PlayerEnterExit.TransitionEventArgs args) { Sweep(); }
        private void Update() { Sweep(); }
        private void Sweep()
        {
            if(enabledBySettings==null || !enabledBySettings() || expires.Count==0) return;
            double now=Now(); bool lifted=GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0);
            removed.Clear();
            foreach(var pair in expires)
            {
                if(!pair.Key) { removed.Add(pair.Key); continue; }
                // A false activeSelf denotes a not-yet-placed native spawn. Keep its
                // reference valid for CreateFoe; OnEnable expires it synchronously.
                // Existing exterior enemies have activeSelf=true under an inactive parent.
                if(!pair.Key.activeSelf) continue;
                if(!lifted && now<pair.Value) continue;
                // Despawn, never kill: no loot, death counters, or civilian crime events.
                pair.Key.SetActive(false);
                Destroy(pair.Key);
                removed.Add(pair.Key);
            }
            foreach(var actor in removed) expires.Remove(actor);
        }
    }
}
