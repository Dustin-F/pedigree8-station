using Pedigree8.Migrate;

static string? Arg(string[] args, string name) =>
    args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

static bool Flag(string[] args, string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

if (args.Length == 0 || Flag(args, "--help"))
{
    Console.WriteLine("""
        Pedigree8.Migrate — copy one Pedigree8 lab into a new station database (Pedigree8 is only read).

          --env-file <path>       Pedigree8 .env with DATABASE_URL and MINIO_* (required)
          --owner-email <email>   Lab owner to migrate (or --org-id <uuid>)
          --out <dir>             Output folder (default: migration-output\station-data)
          --timezone <iana>       Time zone for calendar dates (default: this PC's)
          --local-dates           Re-derive calendar dates from recorded times (see "Dates to review")
          --allow-overwrite-station-data  Replace an output that already has records entered on the station
        """);
    return 2;
}

string timeZone;
if (Arg(args, "--timezone") is { } tzArg)
{
    timeZone = tzArg;
}
else if (!TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana))
{
    timeZone = TimeZoneInfo.Local.Id;
}
else
{
    timeZone = iana!;
}

var options = new MigrateOptions(
    SourceSettings.FromEnvFile(Arg(args, "--env-file") ?? throw new ArgumentException("--env-file is required")),
    Arg(args, "--owner-email"),
    Arg(args, "--org-id"),
    Path.GetFullPath(Arg(args, "--out") ?? Path.Combine("migration-output", "station-data")),
    timeZone,
    Flag(args, "--local-dates"),
    Flag(args, "--allow-overwrite-station-data"));

if (options.OwnerEmail is null && options.OrgId is null)
{
    Console.Error.WriteLine("Give --owner-email or --org-id.");
    return 2;
}

Console.WriteLine($"Reading Pedigree8 (read-only) → {options.OutputDir}  [time zone {options.TimeZone}]");
var report = new Migrator(options).Run();

foreach (var t in report.Tables.Where(t => t.SourceRows > 0).OrderBy(t => t.SourceTable))
{
    Console.WriteLine($"  {t.SourceTable,-34} {t.SourceRows,6} rows  → {t.Target ?? "(archive only)",-18} {(t.Target is null ? "" : $"{t.Migrated} migrated")}");
}

Console.WriteLine();
Console.WriteLine($"Spot checks: {report.SpiderChecks.Count(c => c.Matches)}/{report.SpiderChecks.Count} spiders match");
Console.WriteLine($"Dates to review: {report.DateReview.Count}   Files: {report.Media.Count}   Warnings: {report.Warnings.Count}   Errors: {report.Errors.Count + report.Fatal.Count}");
foreach (var problem in report.Fatal.Concat(report.Errors).Take(20))
{
    Console.WriteLine("  ! " + problem);
}

var reportDir = report.Succeeded ? options.OutputDir : options.OutputDir.TrimEnd('\\', '/') + ".building";
Console.WriteLine(report.Succeeded ? "RESULT: validated and saved." : "RESULT: FAILED — existing output left untouched.");
Console.WriteLine($"Report: {Path.Combine(reportDir, "migration-report.html")}");
return report.Succeeded ? 0 : 1;
