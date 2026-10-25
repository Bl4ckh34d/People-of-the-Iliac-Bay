using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        public bool OwnsExterior { get { return OwnsTown() && !GameManager.Instance.PlayerEnterExit.IsPlayerInside; } }
        public void ObserveOutdoorCorpse(CivilianIncident alarm, Vector3 body, bool playerOnSite)
        {
            if (!OwnsExterior) return;
            var game = GameManager.Instance;
            foreach (var pair in actors)
            {
                var actor = pair.Value;
                if (!actor || !actor.gameObject.activeInHierarchy || !Roster.Guards.Exists(g => g.Id == pair.Key && g.IsAlive && g.Interior == null) ||
                    !CivilianResponses.Sees(actor.transform.position, body + Vector3.up * 0.3f, 20)) continue;
                bool sawPlayer = playerOnSite && !game.PlayerEntity.IsInvisible &&
                    CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20);
                worldStore.NoticeOutdoorCorpse(alarm.Id, pair.Key, true, Now(), sawPlayer);
                ReportOutdoorCorpse(alarm, actor.transform.position, pair.Key);
                var local=directory.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                if((actor.transform.position-body).sqrMagnitude<=10)
                    worldStore.ClearOutdoorCorpse(alarm.Id,pair.Key,local.x,local.z,Now());
                LearnCorpseSuspect(alarm,pair.Key,Now());
            }
        }
        public void ReportRememberedCorpse(CivilianIncident alarm, string guardId)
        {
            GuardActor actor;
            if (actors.TryGetValue(guardId, out actor) && actor && actor.gameObject.activeInHierarchy &&
                Roster.Guards.Exists(g => g.Id == guardId && g.IsAlive && g.Interior == null))
                ReportOutdoorCorpse(alarm, actor.transform.position, guardId);
        }
        private Dictionary<string, List<SavedRoutePoint>> NearbyResponseRoutes(Vector3 reporter, string observerGuard, SavedRoutePoint target)
        {
            var routes = new Dictionary<string, List<SavedRoutePoint>>();
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive || guard.Duty != GuardDuty.Posted || guard.Interior != null) continue;
                if (observerGuard != null && guard.Id != observerGuard) continue;
                var current = Roster.Position(guard.Id, Now());
                var standing = geometry.ScenePoint(new RoutePoint(current.X, current.Z)) + Vector3.up;
                if (!CivilianResponses.Sees(reporter, standing, 20)) continue;
                var route = geometry.RouteBetweenPoints(current, target);
                if (route.Count >= 2) routes.Add(guard.Id, route);
            }
            return routes;
        }
        public bool ReportOutdoorCorpse(CivilianIncident alarm, Vector3 reporter, string observerGuard)
        {
            if (!OwnsExterior || alarm == null || alarm.SettlementId != directory.MapID || alarm.Corpse == null ||
                alarm.Corpse.Discovery == null) return false;
            string id = "outdoor:" + alarm.Id;
            if (Roster.Reports.Exists(r => r.Id == id && r.GuardIds.Count > 0)) return true;
            double now = Now(); Roster.Advance(now);
            var body = alarm.Corpse;
            var target = new SavedRoutePoint { X = body.HasPosition ? body.X : alarm.X, Z = body.HasPosition ? body.Z : alarm.Z };
            var routes = NearbyResponseRoutes(reporter, observerGuard, target);
            if (routes.Count == 0) return false;
            var patrol = CorpseSearchPatrol(alarm,target);
            if (patrol == null) return false;
            var sent = Roster.DispatchOutdoorInvestigation(id, now, routes, patrol, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
            if (sent.Count == 0) return false;
            // Body discovery alone is not evidence of murder. Known witnessed crimes
            // retain their normal crime state through the separate witness pipeline.
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_DISPATCH: " + id + "; responders=" + sent.Count + "; suspect=" + body.Discovery.PlayerPresent);
            return true;
        }
        public void CorpseInteractionObservers(List<Vector3> observers)
        {
            if (!OwnsExterior || GameManager.Instance.PlayerEntity.IsInvisible) return;
            foreach (var pair in actors)
                if (pair.Value && pair.Value.gameObject.activeInHierarchy && Roster.Guards.Exists(g => g.Id == pair.Key && g.IsAlive) &&
                    CivilianResponses.Sees(pair.Value.transform.position, GameManager.Instance.PlayerMotor.transform.position, 20))
                    observers.Add(pair.Value.transform.position);
        }
        public void ReportCorpseTheft(string incidentId, Vector3 observer)
        {
            if (!OwnsExterior) return;
            double now = Now(); Roster.Advance(now);
            var routes = NearbyResponseRoutes(observer, null, Local(GameManager.Instance.PlayerMotor.transform.position));
            if (routes.Count > 0) Roster.Dispatch("corpse-theft:" + incidentId, now, routes, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
        }
    }
}
