namespace Pedigree8.Migrate;

// Postgres expressions that produce values already in the station's text formats.
// @tz is the local IANA time zone used to turn instants into calendar dates.
public static class Sql
{
    public static string Ts(string expr) => $"to_char(({expr}) AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.MS\"Z\"')";

    public static string D(string expr) => $"to_char({expr}, 'YYYY-MM-DD')";

    public static string Ld(string expr) => $"to_char(({expr}) AT TIME ZONE @tz, 'YYYY-MM-DD')";

    public static string Json(params string[] keyValuePairs)
    {
        var parts = new List<string>();
        for (var i = 0; i < keyValuePairs.Length; i += 2)
        {
            parts.Add($"'{keyValuePairs[i]}', {keyValuePairs[i + 1]}");
        }

        return $"nullif(jsonb_strip_nulls(jsonb_build_object({string.Join(", ", parts)}))::text, '{{}}')";
    }

    public static string Lines(params string[] exprs) => $"nullif(concat_ws(E'\\n', {string.Join(", ", exprs)}), '')";
}
