namespace Pedigree8.Core
{
    public static class EventTypes
    {
        public const string Feeding = "feeding";
        public const string Hydration = "hydration";
        public const string Molt = "molt";
        public const string Health = "health";
        public const string Sex = "sex";
        public const string Death = "death";
        public const string Note = "note";
        public const string Acquired = "acquired";
        public const string Status = "status";
        public const string Premolt = "premolt";
        public const string Observation = "observation";
    }

    public static class SpiderStatuses
    {
        public const string Alive = "alive";
        public const string Deceased = "deceased";
        public const string Sold = "sold";
        public const string Transferred = "transferred";
        public const string Missing = "missing";
    }

    public sealed class Spider
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string? Name { get; set; }
        public string SpeciesId { get; set; } = "";
        public string? MorphId { get; set; }
        public string? LineId { get; set; }
        public string Sex { get; set; } = "unknown";
        public string? LifeStageId { get; set; }
        public string Status { get; set; } = SpiderStatuses.Alive;
        public string? StatusChangedAt { get; set; }
        public string Origin { get; set; } = "unknown";
        public string BreedingStatus { get; set; } = "juvenile";
        public string? MotherId { get; set; }
        public string? FatherId { get; set; }
        public string? BroodId { get; set; }
        public string? HatchedOn { get; set; }
        public string? AcquiredOn { get; set; }
        public string? EnclosureId { get; set; }
        public string? PremoltSince { get; set; }
        public string? InMoltSince { get; set; }
        public string? Notes { get; set; }
        public int? FeedEveryDays { get; set; }
        public int? MistEveryDays { get; set; }
        public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
        public string? ArchivedAt { get; set; }

        public Spider Clone() => (Spider)MemberwiseClone();
    }

    // One home-screen tile.
    public sealed class SpiderOverview
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string? Name { get; set; }
        public string Sex { get; set; } = "unknown";
        public string Status { get; set; } = SpiderStatuses.Alive;
        public string SpeciesName { get; set; } = "";
        public string? MorphName { get; set; }
        public string? LifeStageName { get; set; }
        public string? EnclosureId { get; set; }
        public string? EnclosureCode { get; set; }
        public string? PremoltSince { get; set; }
        public string? InMoltSince { get; set; }
        public string? LastFedOn { get; set; }
        public string? LastHydratedOn { get; set; }
        public string? LastMoltOn { get; set; }
        public int? FeedEveryDays { get; set; }
        public int? MistEveryDays { get; set; }

        public int? DaysSinceFed => DbTime.DaysSince(LastFedOn);
        public int? DaysSinceHydrated => DbTime.DaysSince(LastHydratedOn);
        public int? DaysSinceMolt => DbTime.DaysSince(LastMoltOn);
        public bool InPremolt => PremoltSince != null || InMoltSince != null;
        public CareLevel FeedingLevel => StatusRules.Feeding(DaysSinceFed, FeedEveryDays, InPremolt);
        public CareLevel HydrationLevel => StatusRules.Hydration(DaysSinceHydrated, MistEveryDays);

        // Shown under "Needs care": alive and feeding or misting is due (feeding is paused in premolt).
        public bool NeedsCare => IsAlive && (FeedingLevel >= CareLevel.Due || HydrationLevel >= CareLevel.Due || FeedingLevel == CareLevel.Unknown);

        public int Urgency => (int)FeedingLevel * 1000 + (int)HydrationLevel * 100 + (DaysSinceFed ?? 99);
        public bool IsAlive => Status == SpiderStatuses.Alive;
        public string FedText => StatusRules.DaysText(DaysSinceFed);
        public string MistedText => StatusRules.DaysText(DaysSinceHydrated);
        public string MoltText => StatusRules.DaysText(DaysSinceMolt);
        public string FedShort => StatusRules.ShortDays(DaysSinceFed);
        public string MistedShort => StatusRules.ShortDays(DaysSinceHydrated);
    }

    public sealed class Enclosure
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string QrToken { get; set; } = "";
        public string? TypeName { get; set; }
        public string? Dimensions { get; set; }
        public string? Ventilation { get; set; }
        public string? Substrate { get; set; }
        public string? WaterMethod { get; set; }
        public string? Lighting { get; set; }
        public string? Heating { get; set; }
        public string? Contents { get; set; }
        public string? SetupNotes { get; set; }
        public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
        public string? ArchivedAt { get; set; }
    }

    public sealed class EnclosureOverview
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string QrToken { get; set; } = "";
        public string? TypeName { get; set; }
        public string? Occupants { get; set; }
        public string? OccupantId { get; set; }
        public int OccupantCount { get; set; }
        public bool IsEmpty => OccupantCount == 0;
        public string OccupantText => OccupantCount == 0 ? "Empty" : Occupants ?? "";
    }

    public sealed class StationEvent
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string? SpiderId { get; set; }
        public string? EnclosureId { get; set; }
        public string? BroodId { get; set; }
        public string? PairingId { get; set; }
        public string? Code { get; set; }
        public string OccurredOn { get; set; } = "";
        public string? OccurredAt { get; set; }
        public string? UntilOn { get; set; }
        public string? UntilAt { get; set; }
        public string DateQuality { get; set; } = "exact";
        public string? Action { get; set; }
        public string? Outcome { get; set; }
        public string? PreyTypeId { get; set; }
        public string? PreySize { get; set; }
        public double? Quantity { get; set; }
        public string? FromStageId { get; set; }
        public string? ToStageId { get; set; }
        public string? SexValue { get; set; }
        public string? Severity { get; set; }
        public string? Notes { get; set; }
        public string? Details { get; set; }
        public string Source { get; set; } = "station";
        public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
        public string? VoidedAt { get; set; }
        public string? VoidReason { get; set; }

        public StationEvent Clone() => (StationEvent)MemberwiseClone();
    }

    public sealed class Measurement
    {
        public string OccurredOn { get; set; } = "";
        public double BodyLengthMm { get; set; }
        public string? Instar { get; set; }
    }

    public sealed class Photo
    {
        public string Id { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string? TakenAt { get; set; }
        public string FullPath { get; set; } = "";
    }

    public sealed class LookupItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? ParentId { get; set; }
        public int SortOrder { get; set; }
    }
}
