using System.Collections.Generic;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Collision permission belongs to the current shared movement frame only.
    internal sealed class NativePairCollision : MonoBehaviour
    {
        private struct Pair {internal Collider First,Second;internal bool Ignored;}
        private readonly List<Pair> pairs=new List<Pair>();
        private ResidentActor first,second;
        private int touched=-1;

        internal static void Refresh(ResidentActor first,ResidentActor second)
        {
            var link=first.GetComponent<NativePairCollision>() ?? first.gameObject.AddComponent<NativePairCollision>();
            if(link.second!=second)
            {
                link.Release();link.first=first;link.second=second;
                foreach(var a in first.GetComponentsInChildren<Collider>())
                    foreach(var b in second.GetComponentsInChildren<Collider>())
                    {
                        link.pairs.Add(new Pair {First=a,Second=b,Ignored=Physics.GetIgnoreCollision(a,b)});
                        Physics.IgnoreCollision(a,b,true);
                    }
            }
            first.GetComponent<CrowdMotion>().WalkingPartner=second.GetComponent<CrowdMotion>();
            second.GetComponent<CrowdMotion>().WalkingPartner=first.GetComponent<CrowdMotion>();
            link.touched=Time.frameCount;
        }

        private void Release()
        {
            foreach(var pair in pairs)
                if(pair.First && pair.Second)Physics.IgnoreCollision(pair.First,pair.Second,pair.Ignored);
            pairs.Clear();
            if(first && first.GetComponent<CrowdMotion>())first.GetComponent<CrowdMotion>().WalkingPartner=null;
            if(second && second.GetComponent<CrowdMotion>())second.GetComponent<CrowdMotion>().WalkingPartner=null;
            first=null;second=null;touched=-1;
        }
        private void LateUpdate(){if(touched<Time.frameCount)Release();}
        private void OnDisable(){Release();}
        private void OnDestroy(){Release();}
    }
}
