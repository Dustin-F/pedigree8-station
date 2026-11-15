namespace Pedigree8.Core
{
    public enum CareLevel
    {
        Unknown,
        Paused,
        Good,
        Due,
        Attention,
        Overdue,
    }

    public static class StatusRules
    {
        // Without a schedule, feeding buckets match Pedigree8 (packages/shared/src/feeding-recency.ts):
        // 0–1 days recent, 2–3 due, 4–6 attention, 7+ overdue — counted from the last accepted/partial meal.
        // With "feed every N days": before N good, N..N+1 due, up to 2N attention, beyond that overdue.
        public static CareLevel Feeding(int? daysSinceAte, int? everyDays = null, bool inPremolt = false)
        {
            if (inPremolt)
            {
                return CareLevel.Paused;
            }

            if (daysSinceAte is null || daysSinceAte < 0)
            {
                return CareLevel.Unknown;
            }

            var d = daysSinceAte.Value;
            if (everyDays is { } n)
            {
                if (d < n)
                {
                    return CareLevel.Good;
                }

                if (d <= n + 1)
                {
                    return CareLevel.Due;
                }

                return d <= 2 * n ? CareLevel.Attention : CareLevel.Overdue;
            }

            if (d <= 1)
            {
                return CareLevel.Good;
            }

            if (d <= 3)
            {
                return CareLevel.Due;
            }

            return d <= 6 ? CareLevel.Attention : CareLevel.Overdue;
        }

        // Without a schedule: 0–2 days good, 3–4 due, 5+ overdue.
        public static CareLevel Hydration(int? daysSinceMisted, int? everyDays = null)
        {
            if (daysSinceMisted is null || daysSinceMisted < 0)
            {
                return CareLevel.Unknown;
            }

            var d = daysSinceMisted.Value;
            var n = everyDays ?? 3;
            if (d < n)
            {
                return CareLevel.Good;
            }

            return d <= n + 1 ? CareLevel.Due : CareLevel.Overdue;
        }

        public static string DaysText(int? days)
        {
            if (days is null)
            {
                return "never";
            }

            if (days == 0)
            {
                return "today";
            }

            return days == 1 ? "yesterday" : $"{days} days ago";
        }

        public static string ShortDays(int? days) =>
            days is null ? "never" : days == 0 ? "today" : days == 1 ? "yesterday" : $"{days}d ago";
    }
}
