using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    // Values come from the native clock, never from dialogue or model output.
    public sealed class AppointmentCalendar
    {
        public readonly int Year, Month, Day;
        public AppointmentCalendar(int year, int month, int day)
        {
            if (year < 0 || year > 9999 || month < 1 || month > 12 || day < 1 || day > 30)
                throw new ArgumentOutOfRangeException(nameof(day));
            Year = year; Month = month; Day = day;
        }
    }
}
