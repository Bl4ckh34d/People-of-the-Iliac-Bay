using System;
using System.Collections;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class BackgroundWork
    {
        private static readonly WorkStartGate gate=new WorkStartGate(0.1);
        public static bool TryStart() {return gate.TryStart(Time.unscaledTime);}
        public static IEnumerator Wait(Func<bool> stillCurrent)
        {
            if(stillCurrent==null) throw new ArgumentNullException(nameof(stillCurrent));
            float readyAt=Time.unscaledTime+0.1f;
            while(stillCurrent())
            {
                if(Time.unscaledTime>=readyAt && TryStart()) yield break;
                yield return null;
            }
        }
    }
}
