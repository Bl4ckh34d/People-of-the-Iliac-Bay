using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class AppointmentChatDecision
    {
        public string Kind;
        public AppointmentPlace Place;
        public int DayOffset, MinuteOfDay, DurationMinutes, BuildingKey, Importance, CancelIndex;
    }

}
