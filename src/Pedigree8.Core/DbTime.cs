using System;
using System.Globalization;

namespace Pedigree8.Core
{
    public static class DbTime
    {
        private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        private const string DateFormat = "yyyy-MM-dd";

        public static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        public static string Now() => ToDb(UtcNow());

        public static string Today() => LocalDate(UtcNow());

        public static string ToDb(DateTime value) =>
            value.ToUniversalTime().ToString(InstantFormat, CultureInfo.InvariantCulture);

        public static DateTime FromDb(string value) =>
            DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        public static string LocalDate(DateTime utc) =>
            utc.ToUniversalTime().ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture);

        public static DateTime ParseDate(string value) =>
            DateTime.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);

        // Same local time of day, moved to another calendar date.
        public static string MoveToDate(string occurredAtUtc, string newLocalDate)
        {
            var local = FromDb(occurredAtUtc).ToLocalTime();
            var moved = DateTime.SpecifyKind(ParseDate(newLocalDate).Add(local.TimeOfDay), DateTimeKind.Local);
            return ToDb(moved);
        }

        public static string ShiftDate(string localDate, int days) =>
            ParseDate(localDate).AddDays(days).ToString(DateFormat, CultureInfo.InvariantCulture);

        public static int? DaysSince(string? localDate)
        {
            if (localDate is null)
            {
                return null;
            }

            var today = ParseDate(Today());
            return (int)(today - ParseDate(localDate)).TotalDays;
        }
    }
}
