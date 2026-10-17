using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class StreetChatTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Social interactions use a shared 1.0 to 1.3 metre center-distance band",()=>{
                Check.True(SocialInteractionSpacing.Contains(1.0));
                Check.True(SocialInteractionSpacing.Contains(1.3));
                Check.True(!SocialInteractionSpacing.Contains(0.99));
                Check.True(!SocialInteractionSpacing.Contains(1.31));
                Check.True(!SocialInteractionSpacing.Contains(double.NaN));
                Check.True(!SocialInteractionSpacing.Contains(double.PositiveInfinity));
            }));
            cases.Add(("Street chat timing is symmetric bounded and varies between pairs",()=>{
                var durations=new HashSet<int>();int selected=0;
                for(uint i=1;i<100;i++)
                {
                    int duration=StreetChatTiming.Duration(12,i,10);
                    Check.Equal(duration,StreetChatTiming.Duration(i,12,10));
                    Check.True(duration>=120 && duration<=360);durations.Add(duration);
                    if(StreetChatTiming.ShouldMeet(12,i,10)) selected++;
                }
                Check.True(durations.Count>10 && selected>0 && selected<50);
            }));
        }
    }
}
