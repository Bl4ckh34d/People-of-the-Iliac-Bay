using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeNpcGuildMembership
    {
        internal static int RestFaction(BuildingSummary building)
        {
            if(building.BuildingType!=DFLocation.BuildingTypes.GuildHall)return 0;
            FactionFile.FactionData faction;
            return building.FactionId==FightersGuild.FactionId ||
                GameManager.Instance.PlayerEntity.FactionData.GetFactionData(building.FactionId,out faction) &&
                faction.ggroup==(int)FactionFile.GuildGroups.FightersGuild ? FightersGuild.FactionId : 0;
        }
        internal static bool CanRest(Resident person,int faction)
        {
            if(!NpcGuildMembership.Matches(person,faction) || faction!=FightersGuild.FactionId)return false;
            // This detached native rule object never joins or changes the player.
            return new FightersGuild {Rank=person.GuildRank}.CanRest();
        }
        internal static bool AccessAnytime(Resident person)
        {return person!=null && new FightersGuild {Rank=person.GuildRank}.HallAccessAnytime();}
        private static bool InitialMember(WorldStateStore store,Resident person)
        {
            if(person==null || !person.IsAlive || person.IsQuestProtected || person.NativeIdentity!=null ||
                person.AgeGroup!=ResidentAgeGroup.Adult || StableSeed.Combine(person.AppearanceSeed,0x6775696c,0,0)%4!=0)return false;
            var guild=new FightersGuild();var profile=store.ReadNpcActor(person.Id,store.ResolveNpcArchetype(person.Id));int high=0,low=0;
            foreach(var skill in guild.GuildSkills)
            {int value=profile.Skill((int)skill);if(value>=Guild.rankReqSkillHigh[0])high++;else if(value>=Guild.rankReqSkillLow[0])low++;}
            // New generated identities start with neutral NPC reputation. Use
            // native entry skill thresholds; class alone supplies no membership.
            return Guild.rankReqReputation[0]<=0 && high>0 && high+low>1;
        }
        internal static void InitializeKnown(WorldStateStore store,Resident person,IEnumerable<BuildingSummary> buildings)
        {
            if(person==null || person.GuildIdentityKnown || buildings==null)return;
            int hall=-1;
            if(InitialMember(store,person))foreach(var building in buildings)
                if(RestFaction(building)>0 && (hall<0 || building.buildingKey<hall))hall=building.buildingKey;
            store.RecordInitialNpcGuildIdentity(person.Id,hall<0?0:FightersGuild.FactionId,hall<0?-1:0,hall);
        }
        internal static IEnumerator InitializeSteps(WorldStateStore store,Resident person,DFLocation origin,Func<bool> valid)
        {
            if(person==null || person.GuildIdentityKnown || !valid())yield break;
            bool possible=false;
            if(InitialMember(store,person))foreach(var building in origin.Exterior.Buildings)
                if(building.BuildingType==DFLocation.BuildingTypes.GuildHall && building.FactionId==FightersGuild.FactionId){possible=true;break;}
            if(!possible){store.RecordInitialNpcGuildIdentity(person.Id,0,-1,-1);yield break;}
            // Metadata only: no source town collision/household/bed survey and
            // no native actors are created to establish one real origin hall.
            var source=new IncrementalLocationBlocks.Result();yield return IncrementalLocationBlocks.ReadSteps(origin,source);
            if(!valid() || source.Blocks==null)yield break;
            int width=origin.Exterior.ExteriorData.Width;var buildings=new List<BuildingSummary>();
            for(int index=0;index<source.Blocks.Length;index++)
                buildings.AddRange(RMBLayout.GetBuildingData(source.Blocks[index],index%width,index/width));
            InitializeKnown(store,person,buildings);
        }
    }
}
