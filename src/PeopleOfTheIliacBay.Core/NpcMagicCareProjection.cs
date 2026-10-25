using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Read-only event projected from a retained native magic-care receipt.</summary>
    public sealed class NpcMagicCareEvent
    {
        public string PersonId {get;private set;}
        public string TaskId {get;private set;}
        public string ReceiptId {get;private set;}
        public string SpellId {get;private set;}
        public string EffectId {get;private set;}
        public NpcMagicCareNeed Need {get;private set;}
        public double ObservedGameTime {get;private set;}
        public int MagickaCost {get;private set;}
        public int ConditionBefore {get;private set;}
        public int ConditionAfter {get;private set;}

        internal NpcMagicCareEvent(NpcMagicCareTask task)
        {
            PersonId=task.PersonId;TaskId=task.Id;ReceiptId=task.Receipt.TransactionId;
            SpellId=task.Receipt.SpellId;EffectId=task.Receipt.EffectId;Need=task.Need;
            ObservedGameTime=task.Receipt.ObservedGameTime;
            MagickaCost=task.Receipt.MagickaCost;
            ConditionBefore=task.Receipt.ConditionBefore;
            ConditionAfter=task.Receipt.ConditionAfter;
        }
    }

    public sealed partial class WorldStateStore
    {
        public NpcMagicCareEvent[] ReadNpcMagicCareEvents(string personId,double now)
        {
            if(string.IsNullOrWhiteSpace(personId) || double.IsNaN(now) || double.IsInfinity(now) || now<0)
                return new NpcMagicCareEvent[0];
            var result=new List<NpcMagicCareEvent>();
            foreach(var task in state.NpcMagicCareTasks)
            {
                if(task.PersonId!=personId || task.Phase!=NpcMagicCareTaskPhase.Completed ||
                    task.Receipt==null || task.Receipt.ObservedGameTime>now)continue;
                NpcMagicCareTask.Validate(task);
                result.Add(new NpcMagicCareEvent(task));
            }
            result.Sort((a,b)=>
            {
                int time=a.ObservedGameTime.CompareTo(b.ObservedGameTime);
                return time!=0?time:StringComparer.Ordinal.Compare(a.TaskId,b.TaskId);
            });
            return result.ToArray();
        }

        /// <summary>Receipt-backed recall only; this text grants no future cast authority.</summary>
        public string ReadNpcMagicCareRecall(string personId,double now)
        {
            var events=ReadNpcMagicCareEvents(personId,now);
            if(events.Length==0)return null;
            var latest=events[events.Length-1];
            string before=latest.ConditionBefore.ToString(CultureInfo.InvariantCulture);
            string after=latest.ConditionAfter.ToString(CultureInfo.InvariantCulture);
            string time=latest.ObservedGameTime.ToString("G17",CultureInfo.InvariantCulture);
            string cost=latest.MagickaCost.ToString(CultureInfo.InvariantCulture);
            string subject=latest.Need==NpcMagicCareNeed.Heal?"my health improved":
                latest.Need==NpcMagicCareNeed.CurePoison?"my poison effects fell":"my disease effects fell";
            return "I cast "+latest.EffectId+" on myself at game time "+time+"; "+subject+
                " from "+before+" to "+after+". The spell spent "+cost+" magicka.";
        }
    }
}
