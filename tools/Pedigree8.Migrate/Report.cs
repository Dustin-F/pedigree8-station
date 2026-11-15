using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pedigree8.Migrate;

public sealed class OrganizationInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
    public string? TimeZone { get; set; }
    public string? OwnerEmail { get; set; }
    public string? Role { get; set; }
}

public sealed class TableStat
{
    public string SourceTable { get; set; } = "";
    public string? Target { get; set; }
    public string? Description { get; set; }
    public string[]? FoldedTables { get; set; }
    public long SourceRows { get; set; }
    public long Archived { get; set; }
    public long Migrated { get; set; }
    public long Skipped { get; set; }
    public long LinkedInArchive { get; set; }
    public List<string> ArchiveOnlyColumns { get; } = new();

    [JsonIgnore]
    public HashSet<string> ConsumedColumns { get; set; } = new();
}

public sealed record MediaResult(string Id, string Path, long Bytes, string Sha256, string Status);

public sealed record Metric(long SourceCount, long StationCount, string? SourceLast, string? StationLast)
{
    public bool Matches => SourceCount == StationCount && SourceLast == StationLast;
}

public sealed class SpiderCheck
{
    public string Code { get; set; } = "";
    public Metric Feedings { get; set; } = null!;
    public Metric Hydrations { get; set; } = null!;
    public Metric Molts { get; set; } = null!;
    public bool Matches => Feedings.Matches && Hydrations.Matches && Molts.Matches;
}

public sealed class DateReviewItem
{
    public string Kind { get; set; } = "";
    public string Spider { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string StoredDate { get; set; } = "";
    public string LocalDate { get; set; } = "";
    public string LocalTime { get; set; } = "";
}

public sealed class MigrationReport
{
    public bool Succeeded { get; set; }
    public string StartedAt { get; set; } = "";
    public string FinishedAt { get; set; } = "";
    public string TimeZone { get; set; } = "";
    public string DateRule { get; set; } = "";
    public OrganizationInfo? Organization { get; set; }
    public string? PreviousOutputKeptAt { get; set; }
    public List<TableStat> Tables { get; } = new();
    public List<SpiderCheck> SpiderChecks { get; } = new();
    public List<DateReviewItem> DateReview { get; } = new();
    public List<MediaResult> Media { get; } = new();
    public List<string> Fatal { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public TableStat Table(string sourceTable)
    {
        var stat = Tables.FirstOrDefault(t => t.SourceTable == sourceTable);
        if (stat is null)
        {
            stat = new TableStat { SourceTable = sourceTable };
            Tables.Add(stat);
        }

        return stat;
    }
}

public static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ToJson(MigrationReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static void Write(MigrationReport report, string directory)
    {
        File.WriteAllText(Path.Combine(directory, "migration-report.json"), ToJson(report));
        File.WriteAllText(Path.Combine(directory, "migration-report.html"), ToHtml(report));
    }

    private static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "");

    private static string ToHtml(MigrationReport r)
    {
        var mapped = r.Tables.Where(t => t.Target is not null).OrderBy(t => t.Target).ThenBy(t => t.SourceTable).ToList();
        var archiveOnly = r.Tables.Where(t => t.Target is null && t.SourceRows > 0).OrderBy(t => t.SourceTable).ToList();
        var folded = mapped.SelectMany(t => t.FoldedTables ?? Array.Empty<string>()).ToHashSet();
        var sb = new StringBuilder();

        sb.Append($@"<!doctype html><html lang=""en""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Migration Report</title>
<style>
:root {{ --bg:#f7f7f5; --card:#fff; --ink:#1f2328; --muted:#5f6670; --line:#e3e3df; --ok:#1a7f37; --bad:#c62828; --warn:#9a6700; --accent:#2f5d8a; }}
@media (prefers-color-scheme: dark) {{ :root {{ --bg:#121417; --card:#1b1e22; --ink:#e8eaed; --muted:#9aa0a6; --line:#2c3036; --ok:#4cc26b; --bad:#ff6b6b; --warn:#e3b341; --accent:#7fb0e0; }} }}
* {{ box-sizing:border-box; }}
body {{ margin:0; background:var(--bg); color:var(--ink); font:15px/1.5 ""Segoe UI"",system-ui,sans-serif; }}
main {{ max-width:1100px; margin:0 auto; padding:24px 16px 64px; }}
h1 {{ font-size:26px; margin:0 0 4px; }} h2 {{ font-size:18px; margin:32px 0 10px; }}
.muted {{ color:var(--muted); }}
.badge {{ display:inline-block; padding:4px 12px; border-radius:999px; font-weight:600; color:#fff; background:var(--ok); }}
.badge.bad {{ background:var(--bad); }}
.tiles {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(150px,1fr)); gap:12px; margin-top:16px; }}
.tile {{ background:var(--card); border:1px solid var(--line); border-radius:10px; padding:12px 14px; }}
.tile b {{ display:block; font-size:24px; }}
.card {{ background:var(--card); border:1px solid var(--line); border-radius:10px; overflow-x:auto; }}
table {{ border-collapse:collapse; width:100%; font-size:14px; }}
th, td {{ text-align:left; padding:7px 10px; border-bottom:1px solid var(--line); vertical-align:top; }}
th {{ color:var(--muted); font-weight:600; white-space:nowrap; }}
tr:last-child td {{ border-bottom:none; }}
td.num {{ text-align:right; font-variant-numeric:tabular-nums; }}
.ok {{ color:var(--ok); font-weight:600; }} .bad {{ color:var(--bad); font-weight:600; }} .warn {{ color:var(--warn); }}
ul.issues {{ margin:0; padding:12px 12px 12px 32px; }}
code {{ font-size:13px; }}
</style></head><body><main>");

        sb.Append($@"<h1>Pedigree8 → Station migration report</h1>
<p class=""muted"">Lab <b>{E(r.Organization?.Name)}</b> · owner {E(r.Organization?.OwnerEmail)} · run {E(r.StartedAt)} → {E(r.FinishedAt)} (UTC)</p>
<p><span class=""badge{(r.Succeeded ? "" : " bad")}"">{(r.Succeeded ? "Validated — all checks passed" : "Failed — output not replaced")}</span></p>
<p class=""muted"">Time zone: <b>{E(r.TimeZone)}</b> (Pedigree8 lab setting was {E(r.Organization?.TimeZone)}). {E(r.DateRule)}</p>");

        sb.Append(@"<div class=""tiles"">");
        Tile(sb, "Source rows (this lab)", r.Tables.Sum(t => t.SourceRows));
        Tile(sb, "Archived verbatim", r.Tables.Sum(t => t.Archived));
        Tile(sb, "In station tables", r.Tables.Sum(t => t.Migrated));
        Tile(sb, "Files copied", r.Media.Count(m => m.Status == "copied"));
        Tile(sb, "Errors", r.Fatal.Count + r.Errors.Count);
        Tile(sb, "Warnings", r.Warnings.Count);
        sb.Append("</div>");

        Issues(sb, "Blocking problems", r.Fatal, "bad");
        Issues(sb, "Errors", r.Errors, "bad");
        Issues(sb, "Warnings", r.Warnings, "warn");

        sb.Append(@"<h2>Spider spot checks</h2><p class=""muted"">Count and latest date per spider, Pedigree8 vs station.</p><div class=""card""><table>
<tr><th>Spider</th><th>Feedings</th><th>Last fed</th><th>Hydrations</th><th>Last hydrated</th><th>Molts</th><th>Last molt</th><th>Result</th></tr>");
        foreach (var c in r.SpiderChecks)
        {
            sb.Append($"<tr><td><b>{E(c.Code)}</b></td>{MetricCells(c.Feedings)}{MetricCells(c.Hydrations)}{MetricCells(c.Molts)}" +
                      $"<td class=\"{(c.Matches ? "ok" : "bad")}\">{(c.Matches ? "Match" : "Mismatch")}</td></tr>");
        }

        sb.Append("</table></div>");

        sb.Append(@"<h2>Table by table</h2><p class=""muted"">Every row is archived verbatim in <code>legacy_records</code>. ""Kept in archive only"" lists source columns that hold data but are not shown by the station.</p>
<div class=""card""><table><tr><th>Pedigree8 table</th><th>Station table</th><th class=""num"">Rows</th><th class=""num"">Archived</th><th class=""num"">Migrated</th><th class=""num"">Skipped</th><th>Kept in archive only</th></tr>");
        foreach (var t in mapped)
        {
            var skippedClass = t.Skipped > 0 ? " bad" : "";
            sb.Append($"<tr><td>{E(t.SourceTable)}<div class=\"muted\">{E(t.Description)}</div></td><td>{E(t.Target)}</td>" +
                      $"<td class=\"num\">{t.SourceRows}</td><td class=\"num\">{t.Archived}</td><td class=\"num\">{t.Migrated}</td>" +
                      $"<td class=\"num{skippedClass}\">{t.Skipped}</td><td>{E(string.Join(", ", t.ArchiveOnlyColumns))}</td></tr>");
        }

        sb.Append("</table></div>");

        sb.Append(@"<h2>Tables kept in the archive only</h2><div class=""card""><table><tr><th>Pedigree8 table</th><th class=""num"">Rows</th><th>Why</th></tr>");
        foreach (var t in archiveOnly)
        {
            var why = folded.Contains(t.SourceTable)
                ? "Folded into another station table (see above); full rows archived."
                : "Feature not used by the station; full rows archived.";
            sb.Append($"<tr><td>{E(t.SourceTable)}</td><td class=\"num\">{t.SourceRows}</td><td class=\"muted\">{why}</td></tr>");
        }

        sb.Append("</table></div>");

        sb.Append($@"<h2>Dates to review ({r.DateReview.Count})</h2>
<p class=""muted"">Records whose stored calendar date differs from the local date of their recorded time — usually caused by the old Asia/Shanghai lab setting. Nothing has been changed; tell me whether to use the local date instead.</p>");
        if (r.DateReview.Count == 0)
        {
            sb.Append("<p class=\"ok\">None.</p>");
        }
        else
        {
            sb.Append("<div class=\"card\"><table><tr><th>Type</th><th>Spider</th><th>Stored date</th><th>Local date</th><th>Local time recorded</th></tr>");
            foreach (var d in r.DateReview)
            {
                sb.Append($"<tr><td>{E(d.Kind)}</td><td>{E(d.Spider)}</td><td>{E(d.StoredDate)}</td><td><b>{E(d.LocalDate)}</b></td><td>{E(d.LocalTime)}</td></tr>");
            }

            sb.Append("</table></div>");
        }

        sb.Append("<h2>Files</h2>");
        if (r.Media.Count == 0)
        {
            sb.Append("<p class=\"muted\">No photos or attachments in this lab.</p>");
        }
        else
        {
            sb.Append("<div class=\"card\"><table><tr><th>File</th><th class=\"num\">Bytes</th><th>SHA-256</th><th>Status</th></tr>");
            foreach (var m in r.Media)
            {
                var cls = m.Status == "copied" ? "ok" : "bad";
                sb.Append($"<tr><td><code>{E(m.Path)}</code></td><td class=\"num\">{m.Bytes}</td><td><code>{E(m.Sha256.Length > 16 ? m.Sha256[..16] + "…" : m.Sha256)}</code></td><td class=\"{cls}\">{E(m.Status)}</td></tr>");
            }

            sb.Append("</table></div>");
        }

        if (r.PreviousOutputKeptAt is not null)
        {
            sb.Append($"<p class=\"muted\">The previous output was kept at <code>{E(r.PreviousOutputKeptAt)}</code>.</p>");
        }

        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static void Tile(StringBuilder sb, string label, long value) =>
        sb.Append($"<div class=\"tile\"><b>{value}</b><span class=\"muted\">{E(label)}</span></div>");

    private static void Issues(StringBuilder sb, string title, List<string> items, string cls)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.Append($"<h2 class=\"{cls}\">{E(title)} ({items.Count})</h2><div class=\"card\"><ul class=\"issues\">");
        foreach (var item in items)
        {
            sb.Append($"<li>{E(item)}</li>");
        }

        sb.Append("</ul></div>");
    }

    private static string MetricCells(Metric m)
    {
        var countCls = m.SourceCount == m.StationCount ? "" : " class=\"bad\"";
        var dateCls = m.SourceLast == m.StationLast ? "" : " class=\"bad\"";
        var count = m.SourceCount == m.StationCount ? $"{m.StationCount}" : $"{m.SourceCount} → {m.StationCount}";
        var date = m.SourceLast == m.StationLast ? E(m.StationLast ?? "—") : $"{E(m.SourceLast)} → {E(m.StationLast)}";
        return $"<td{countCls}>{count}</td><td{dateCls}>{date}</td>";
    }
}
