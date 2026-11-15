using static Pedigree8.Migrate.Sql;

namespace Pedigree8.Migrate;

// One Pedigree8 table -> one station table. The main source table is always aliased "x";
// the report scans the SQL for "x.<column>" to work out which columns were carried over.
public sealed record Mapping(string SourceTable, string TargetTable, string Select, string Description, string[]? FoldedTables = null);

public static class Mappings
{
    private const string Org = "x.organization_id = @org";

    public static IReadOnlyList<Mapping> All(bool localDates)
    {
        var list = new List<Mapping>
        {
            new("taxa", "species", $@"
                SELECT x.id::text AS id, x.scientific_name, x.common_name, x.notes,
                       {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at, {Ts("x.archived_at")} AS archived_at
                FROM taxa x WHERE {Org}", "Species"),

            new("morphs", "morphs", $@"
                SELECT x.id::text AS id, x.taxon_id::text AS species_id, x.name, x.description,
                       {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at, {Ts("x.archived_at")} AS archived_at
                FROM morphs x WHERE {Org}", "Morphs"),

            new("life_stage_definitions", "life_stages", $@"
                SELECT x.id::text AS id, x.taxon_id::text AS species_id, x.code, x.label AS name, x.sort_order,
                       CASE WHEN x.is_terminal_adult THEN 1 ELSE 0 END AS is_adult,
                       {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at, {Ts("x.archived_at")} AS archived_at
                FROM life_stage_definitions x WHERE {Org}", "Life stages"),

            new("prey_types", "prey_types", $@"
                SELECT x.id::text AS id, x.name, 0 AS sort_order,
                       {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at, {Ts("x.archived_at")} AS archived_at
                FROM prey_types x WHERE {Org}", "Prey types"),

            new("parties", "suppliers", $@"
                SELECT x.id::text AS id, x.display_name AS name, x.party_kind AS kind, x.location_text AS location,
                       x.contact_notes, {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at,
                       {Ts("x.archived_at")} AS archived_at
                FROM parties x WHERE {Org}", "Suppliers / breeders"),

            new("source_lines", "lines", $@"
                SELECT x.id::text AS id, x.code, x.name, x.primary_party_id::text AS supplier_id, x.notes,
                       {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at, {Ts("x.archived_at")} AS archived_at
                FROM source_lines x WHERE {Org}", "Bloodlines"),

            new("enclosures", "enclosures", $@"
                SELECT x.id::text AS id, x.code, lower(coalesce(x.qr_token::text, gen_random_uuid()::text)) AS qr_token,
                       coalesce(x.enclosure_type, et.name) AS type_name,
                       coalesce(x.dimensions_text, et.dimensions_text,
                                CASE WHEN x.length_value IS NOT NULL THEN
                                  concat_ws(' × ', x.length_value::text, x.width_value::text, x.height_value::text) || coalesce(' ' || x.dimension_unit, '')
                                END) AS dimensions,
                       nullif(concat_ws('; ', x.ventilation_text, nullif(array_to_string(ARRAY[
                           CASE WHEN x.ventilation_top THEN 'top' END, CASE WHEN x.ventilation_bottom THEN 'bottom' END,
                           CASE WHEN x.ventilation_side THEN 'side' END, CASE WHEN x.ventilation_left_side THEN 'left side' END,
                           CASE WHEN x.ventilation_right_side THEN 'right side' END, CASE WHEN x.ventilation_front THEN 'front' END,
                           CASE WHEN x.ventilation_rear THEN 'rear' END, CASE WHEN x.ventilation_cross THEN 'cross-flow' END], ', '), '')), '') AS ventilation,
                       x.substrate_text AS substrate, x.water_method_text AS water_method,
                       x.lighting_text AS lighting, x.heating_text AS heating,
                       (SELECT nullif(string_agg(nullif(concat_ws(' ', nullif(c.kind, ''), nullif(c.label, ''),
                                                      '(' || nullif(c.quantity_text, '') || ')', '@ ' || nullif(c.location_text, ''),
                                                      '- ' || nullif(c.notes, '')), ''), '; ' ORDER BY c.sort_order, c.label), '')
                          FROM enclosure_contents c WHERE c.enclosure_id = x.id AND c.archived_at IS NULL) AS contents,
                       x.setup_notes, {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at,
                       {Ts("x.archived_at")} AS archived_at
                FROM enclosures x LEFT JOIN enclosure_types et ON et.id = x.enclosure_type_id
                WHERE {Org}", "Enclosures (QR tokens kept)", new[] { "enclosure_contents", "enclosure_types" }),

            new("specimens", "spiders", $@"
                SELECT x.id::text AS id, x.breeder_code AS code, x.name_or_label AS name, x.taxon_id::text AS species_id,
                       x.morph_id::text AS morph_id,
                       (SELECT a.source_line_id::text FROM acquisitions a
                         WHERE a.specimen_id = x.id AND a.source_line_id IS NOT NULL ORDER BY a.created_at DESC LIMIT 1) AS line_id,
                       coalesce((SELECT CASE sa.sex_value WHEN 'confirmed_male' THEN 'male' WHEN 'confirmed_female' THEN 'female'
                                        ELSE sa.sex_value::text END
                                   FROM sex_assessments sa WHERE sa.specimen_id = x.id AND sa.voided_at IS NULL
                                  ORDER BY coalesce(sa.assessed_on, (sa.assessed_at AT TIME ZONE @tz)::date) DESC NULLS LAST,
                                           sa.created_at DESC LIMIT 1), 'unknown') AS sex,
                       x.current_life_stage_definition_id::text AS life_stage_id,
                       CASE WHEN x.life_status = 'deceased' THEN 'deceased'
                            WHEN x.ownership_status = 'sold' THEN 'sold'
                            WHEN x.ownership_status = 'transferred' THEN 'transferred'
                            ELSE 'alive' END AS status,
                       (SELECT {Ts("coalesce(m.died_at, m.died_on::timestamp AT TIME ZONE @tz)")} FROM mortality_events m
                         WHERE m.specimen_id = x.id AND m.voided_at IS NULL ORDER BY m.created_at DESC LIMIT 1) AS status_changed_at,
                       x.origin_type::text AS origin, x.breeding_status::text AS breeding_status,
                       {Parent("dam")} AS mother_id, {Parent("sire")} AS father_id,
                       x.brood_id::text AS brood_id,
                       (SELECT coalesce({D("a.acquired_on")}, {Ld("a.acquired_at")}) FROM acquisitions a
                         WHERE a.specimen_id = x.id AND a.archived_at IS NULL ORDER BY a.created_at DESC LIMIT 1) AS acquired_on,
                       (SELECT ea.enclosure_id::text FROM enclosure_assignments ea
                         WHERE ea.specimen_id = x.id AND ea.removed_at IS NULL ORDER BY ea.assigned_at DESC LIMIT 1) AS enclosure_id,
                       {Advisory("suspected_premolt")} AS premolt_since, {Advisory("active_molt")} AS in_molt_since,
                       x.notes_summary AS notes, {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at,
                       {Ts("x.archived_at")} AS archived_at
                FROM specimens x WHERE {Org}", "Spiders", new[] { "parentage_links" }),

            new("broods", "broods", $@"
                SELECT x.id::text AS id, x.display_code AS code,
                       {ClutchPairing("x.clutch_id")} AS pairing_id,
                       coalesce({BroodParent("dam")}, c.female_specimen_id::text) AS mother_id,
                       coalesce({BroodParent("sire")}, {ClutchMale("x.clutch_id")}) AS father_id,
                       x.taxon_id::text AS species_id, x.morph_id::text AS morph_id,
                       coalesce({D("c.laid_on")}, {Ld("c.laid_at")}) AS laid_on,
                       coalesce({D("c.hatch_on")}, {Ld("c.hatch_at")}) AS hatched_on,
                       coalesce({D("x.emerged_on")}, {Ld("x.emerged_at")}, {D("c.emergence_on")}, {Ld("c.emergence_at")}) AS emerged_on,
                       c.estimated_egg_count AS egg_count, c.estimated_hatchling_count AS hatchling_count,
                       {Lines("x.notes", "c.notes")} AS notes,
                       {Json("clutch_id", "c.id", "clutch_code", "c.display_code", "fertility", "c.fertility_status",
                             "sac_number", "c.sac_number_for_female", "maternal_behavior_notes", "c.maternal_behavior_notes",
                             "condition_notes", "c.condition_notes", "date_quality", "x.date_quality")} AS details,
                       {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at,
                       {Ts("coalesce(x.archived_at, c.voided_at)")} AS archived_at
                FROM broods x LEFT JOIN clutches c ON c.id = x.clutch_id
                WHERE {Org}", "Broods (with their egg sac)", new[] { "brood_parent_links", "clutch_pairings" }),

            new("clutches", "broods", $@"
                SELECT x.id::text AS id, x.display_code AS code,
                       {ClutchPairing("x.id")} AS pairing_id,
                       x.female_specimen_id::text AS mother_id, {ClutchMale("x.id")} AS father_id,
                       (SELECT s.taxon_id::text FROM specimens s WHERE s.id = x.female_specimen_id) AS species_id,
                       coalesce({D("x.laid_on")}, {Ld("x.laid_at")}) AS laid_on,
                       coalesce({D("x.hatch_on")}, {Ld("x.hatch_at")}) AS hatched_on,
                       coalesce({D("x.emergence_on")}, {Ld("x.emergence_at")}) AS emerged_on,
                       x.estimated_egg_count AS egg_count, x.estimated_hatchling_count AS hatchling_count,
                       x.notes,
                       {Json("event_code", "x.event_code", "fertility", "x.fertility_status", "sac_number", "x.sac_number_for_female",
                             "maternal_behavior_notes", "x.maternal_behavior_notes", "condition_notes", "x.condition_notes",
                             "laid_at_end", "x.laid_at_end", "laid_on_end", "x.laid_on_end", "date_quality", "x.date_quality",
                             "void_reason", "x.void_reason")} AS details,
                       {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at, {Ts("x.voided_at")} AS archived_at
                FROM clutches x
                WHERE {Org} AND NOT EXISTS (SELECT 1 FROM broods b WHERE b.clutch_id = x.id)",
                "Egg sacs that never became a brood"),

            new("pairings", "pairings", $@"
                SELECT x.id::text AS id, x.display_code AS code, x.female_specimen_id::text AS female_id,
                       x.male_specimen_id::text AS male_id,
                       coalesce({D("x.occurred_on")}, {Ld("x.occurred_at")}, {Ld("x.introduced_at")}, {Ld("x.created_at")}) AS paired_on,
                       {Ts("coalesce(x.introduced_at, x.occurred_at)")} AS paired_at, {Ts("x.separated_at")} AS separated_at,
                       coalesce(x.result::text, 'unknown') AS result, x.notes,
                       {Json("event_code", "x.event_code", "attempt_number", "x.attempt_number", "occurred_at_end", "x.occurred_at_end",
                             "occurred_on_end", "x.occurred_on_end", "date_quality", "x.date_quality",
                             "courtship_observed", "x.courtship_observed", "mating_observed", "x.mating_observed",
                             "mating_duration_seconds", "x.mating_duration_seconds", "aggression_observed", "x.aggression_observed",
                             "injury_observed", "x.injury_observed", "sex_role_override_reason", "x.sex_role_override_reason")} AS details,
                       {Ts("x.created_at")} AS created_at, {Ts("x.updated_at")} AS updated_at,
                       {Ts("x.voided_at")} AS voided_at, x.void_reason
                FROM pairings x WHERE {Org}", "Pairings"),

            new("enclosure_assignments", "enclosure_history", $@"
                SELECT x.id::text AS id, x.enclosure_id::text AS enclosure_id, x.specimen_id::text AS spider_id,
                       x.brood_id::text AS brood_id, {Ts("x.assigned_at")} AS placed_at, {Ts("x.removed_at")} AS removed_at,
                       x.notes, {Ts("x.created_at")} AS created_at
                FROM enclosure_assignments x WHERE {Org}", "Enclosure moves"),
        };

        list.AddRange(Events(localDates));

        list.Add(new("media_assets", "media", $@"
                SELECT x.id::text AS id, l.specimen_id::text AS spider_id, l.enclosure_id::text AS enclosure_id,
                       coalesce(l.brood_id::text, (SELECT b.id::text FROM broods b WHERE b.clutch_id = l.clutch_id), l.clutch_id::text) AS brood_id,
                       coalesce(l.molt_event_id, l.health_event_id, l.feeding_event_id)::text AS event_id,
                       x.kind::text AS kind, x.filename AS file_name, x.storage_key, x.mime_type, x.byte_size, l.caption,
                       {Ts("coalesce(x.taken_at, x.taken_on::timestamp AT TIME ZONE @tz)")} AS taken_at,
                       {Ts("x.created_at")} AS created_at, {Ts("x.archived_at")} AS archived_at
                FROM media_assets x
                LEFT JOIN LATERAL (SELECT * FROM media_links ml WHERE ml.media_id = x.id
                                   ORDER BY ml.archived_at NULLS FIRST, ml.created_at LIMIT 1) l ON true
                WHERE {Org}", "Photos and attachments", new[] { "media_links" }));

        list.Add(new("generated_artifacts", "media", $@"
                SELECT x.id::text AS id,
                       CASE WHEN x.source_entity_type ILIKE 'specimen%' THEN x.source_entity_id::text END AS spider_id,
                       'report' AS kind, regexp_replace(x.storage_key, '^.*/', '') AS file_name, x.storage_key,
                       x.content_type AS mime_type, x.byte_size, x.title AS caption, {Ts("x.generated_at")} AS taken_at,
                       {Ts("x.created_at")} AS created_at, NULL AS archived_at
                FROM generated_artifacts x
                WHERE {Org} AND x.kind::text LIKE 'REPORT%' AND x.storage_key IS NOT NULL", "Saved reports"));

        list.Add(new("id_sequences", "id_sequences", @"
                SELECT x.sequence_key AS key, x.next_value FROM id_sequences x WHERE x.organization_id = @org",
                "Code counters"));

        return list;
    }

    private static IEnumerable<Mapping> Events(bool localDates)
    {
        string Dates(string at, string? atEnd, string? on, string? onEnd, string? quality)
        {
            var onExpr = on is null ? "NULL::date" : on;
            var occurredOn = localDates
                ? $"coalesce({Ld(at)}, {D(onExpr)}, {Ld("x.created_at")})"
                : $"coalesce({D(onExpr)}, {Ld(at)}, {Ld("x.created_at")})";
            var untilOn = onEnd is null
                ? (atEnd is null ? "NULL" : Ld(atEnd))
                : $"coalesce({D(onEnd)}, {(atEnd is null ? "NULL" : Ld(atEnd))})";
            var qualityExpr = quality is null
                ? $"CASE WHEN {at} IS NULL AND {onExpr} IS NULL THEN 'unknown' ELSE 'exact' END"
                : $"coalesce({quality}::text, CASE WHEN {at} IS NULL AND {onExpr} IS NULL THEN 'unknown' ELSE 'exact' END)";
            return $@"{occurredOn} AS occurred_on, {Ts(at)} AS occurred_at, {untilOn} AS until_on,
                      {(atEnd is null ? "NULL" : Ts(atEnd))} AS until_at, {qualityExpr} AS date_quality";
        }

        Mapping Ev(string table, string type, string description, string dates, string links, string extra,
                   string code = "x.event_code", string voided = "x.voided_at", string voidReason = "x.void_reason",
                   string where = "") => new(table, "events", $@"
                SELECT x.id::text AS id, '{type}' AS type, {links}, {code} AS code, {dates}, {extra},
                       'migrated' AS source, {Ts("x.created_at")} AS created_at, {Ts("coalesce(x.updated_at, x.created_at)")} AS updated_at,
                       {Ts(voided)} AS voided_at, {voidReason} AS void_reason
                FROM {table} x WHERE {Org} {where}", description);

        const string SpiderOnly = "x.specimen_id::text AS spider_id";
        var standardDates = Dates("x.occurred_at", "x.occurred_at_end", "x.occurred_on", "x.occurred_on_end", "x.date_quality");

        yield return Ev("feeding_events", "feeding", "Feedings", standardDates, SpiderOnly, $@"
            x.outcome::text AS outcome, x.prey_type_id::text AS prey_type_id, x.prey_size_text AS prey_size,
            x.quantity::float8 AS quantity, {Lines("x.notes", "x.response_notes")} AS notes,
            {Json("batch_id", "x.batch_id")} AS details");

        yield return Ev("brood_feeding_events", "feeding", "Brood feedings", standardDates,
            "x.brood_id::text AS brood_id, x.enclosure_id::text AS enclosure_id", $@"
            x.response::text AS outcome, x.prey_type_id::text AS prey_type_id, x.prey_size_text AS prey_size,
            x.quantity::float8 AS quantity, x.notes, {Json("quantity_is_approximate", "x.quantity_is_approximate")} AS details");

        yield return Ev("hydration_events", "hydration", "Hydration (misting / water)", standardDates,
            "x.specimen_id::text AS spider_id, x.enclosure_id::text AS enclosure_id, x.brood_id::text AS brood_id", $@"
            lower(x.action::text) AS action, x.notes, {Json("round_id", "x.round_id")} AS details");

        yield return Ev("molt_events", "molt", "Molts", standardDates, SpiderOnly, $@"
            x.from_stage_id::text AS from_stage_id, x.to_stage_id::text AS to_stage_id, x.outcome::text AS outcome,
            {Lines("x.notes", "'Pre-molt: ' || x.premolt_notes", "'Post-molt: ' || x.postmolt_notes")} AS notes");

        yield return Ev("health_events", "health", "Health records", standardDates, SpiderOnly, $@"
            (SELECT hc.label FROM health_event_categories hc WHERE hc.id = x.category_id) AS action,
            x.severity::text AS severity, x.outcome::text AS outcome,
            {Lines("x.notes", "'Observations: ' || x.observations", "'Treatment: ' || x.treatment")} AS notes,
            {Json("category_id", "x.category_id")} AS details");

        yield return Ev("sex_assessments", "sex", "Sex assessments",
            Dates("x.assessed_at", "x.assessed_at_end", "x.assessed_on", "x.assessed_on_end", "x.date_quality"), SpiderOnly, $@"
            CASE x.sex_value WHEN 'confirmed_male' THEN 'male' WHEN 'confirmed_female' THEN 'female' ELSE x.sex_value::text END AS sex_value,
            x.notes, {Json("confidence", "x.confidence", "method", "x.method", "evidence", "x.evidence",
                           "life_stage_id", "x.life_stage_definition_id")} AS details");

        yield return Ev("mortality_events", "death", "Deaths",
            Dates("x.died_at", "x.died_at_end", "x.died_on", "x.died_on_end", "x.date_quality"), SpiderOnly, $@"
            {Lines("x.notes", "'Suspected cause: ' || x.suspected_cause", "'Circumstances: ' || x.circumstances",
                   "'Necropsy: ' || x.necropsy_notes")} AS notes,
            {Json("life_stage_id", "x.life_stage_definition_id")} AS details");

        yield return Ev("notes", "note", "Notes", Dates("x.created_at", null, null, null, null), $@"
            x.specimen_id::text AS spider_id, x.pairing_id::text AS pairing_id,
            coalesce(x.brood_id::text, (SELECT b.id::text FROM broods b WHERE b.clutch_id = x.clutch_id), x.clutch_id::text) AS brood_id",
            $"x.body AS notes, {Json("visibility", "x.visibility", "pinned", "x.pinned")} AS details",
            code: "NULL", voided: "x.archived_at",
            voidReason: "CASE WHEN x.archived_at IS NOT NULL THEN 'Archived in Pedigree8' END");

        yield return Ev("acquisitions", "acquired", "Acquisitions",
            Dates("x.acquired_at", "x.acquired_at_end", "x.acquired_on", "x.acquired_on_end", "x.date_quality"), SpiderOnly, $@"
            x.notes, {Json("supplier_id", "x.party_id", "line_id", "x.source_line_id", "price", "x.purchase_price::text",
                           "currency", "x.currency_code", "listing_url", "x.listing_url", "location", "x.location_text",
                           "life_stage_id", "x.life_stage_definition_id")} AS details",
            code: "NULL", voided: "x.archived_at",
            voidReason: "CASE WHEN x.archived_at IS NOT NULL THEN 'Archived in Pedigree8' END");

        yield return Ev("specimen_molt_advisories", "premolt", "Pre-molt / in-molt flags",
            Dates("x.set_at", "x.cleared_at", null, null, null), SpiderOnly, $@"
            x.kind::text AS action, x.note AS notes, {Json("clear_reason", "x.clear_reason")} AS details",
            code: "NULL", voided: "NULL::timestamptz", voidReason: "NULL");

        yield return Ev("provenance_claims", "observation", "Provenance claims", Dates("x.created_at", null, null, null, null),
            SpiderOnly, $@"
            'provenance_claim' AS action, x.statement AS notes,
            {Json("claim_type", "x.claim_type", "confidence", "x.confidence", "acquisition_id", "x.acquisition_id",
                  "line_id", "x.source_line_id", "claimed_by_supplier_id", "x.claimed_by_party_id", "is_verified", "x.is_verified",
                  "verified_at", "x.verified_at", "notes", "x.notes")} AS details",
            code: "NULL", voided: "x.archived_at",
            voidReason: "CASE WHEN x.archived_at IS NOT NULL THEN 'Archived in Pedigree8' END");

        yield return Ev("body_condition_observations", "observation", "Body condition",
            Dates("NULL::timestamptz", null, "x.bio_date", null, null), SpiderOnly,
            "'body_condition' AS action, x.condition::text AS outcome, x.notes", code: "NULL");

        yield return Ev("specimen_measurements", "observation", "Measurements",
            Dates("NULL::timestamptz", null, "x.bio_date", null, null), SpiderOnly, $@"
            'measurement' AS action,
            {Lines("'Body length: ' || x.body_length_mm || ' mm'", "'Leg span: ' || x.diagonal_leg_span_mm || ' mm'",
                   "'Weight: ' || x.weight_mg || ' mg'", "x.notes")} AS notes,
            {Json("body_length_mm", "x.body_length_mm", "leg_span_mm", "x.diagonal_leg_span_mm", "weight_mg", "x.weight_mg",
                  "method", "x.measurement_method")} AS details", code: "NULL");

        yield return Ev("environmental_observations", "observation", "Temperature / humidity readings",
            Dates("x.observed_at", "x.observed_at_end", "x.observed_on", "x.observed_on_end", "x.date_quality"),
            "x.specimen_id::text AS spider_id, x.enclosure_id::text AS enclosure_id", $@"
            'environment' AS action,
            {Lines("coalesce(x.metric_label, x.metric_kind::text) || ': ' || x.value_numeric || coalesce(' ' || x.unit, '')", "x.notes")} AS notes,
            {Json("metric_kind", "x.metric_kind", "metric_label", "x.metric_label", "value", "x.value_numeric", "unit", "x.unit",
                  "observation_group_id", "x.observation_group_id")} AS details");

        yield return Ev("enclosure_maintenance_events", "observation", "Enclosure maintenance",
            Dates("x.occurred_at", null, "x.occurred_on", null, "x.date_quality"), "x.enclosure_id::text AS enclosure_id",
            "'maintenance' AS action, x.kind::text AS outcome, x.notes");

        yield return Ev("brood_population_observations", "observation", "Brood head counts", standardDates,
            "x.brood_id::text AS brood_id", $@"
            'brood_count' AS action, {Lines("x.notes", "'Mortality: ' || x.mortality_notes")} AS notes,
            {Json("life_stage_id", "x.life_stage_definition_id", "count_quality", "x.count_quality",
                  "alive_count", "x.collective_alive_count", "observed_count", "x.observed_count", "dead_count", "x.dead_count",
                  "cannibalism", "x.cannibalism_observed", "estimated_loss_count", "x.estimated_loss_count")} AS details");

        yield return Ev("trait_observations", "observation", "Trait observations",
            Dates("x.observed_at", "x.observed_at_end", "x.observed_on", "x.observed_on_end", "x.date_quality"), SpiderOnly, $@"
            'trait' AS action, x.observation_state::text AS outcome, x.notes,
            {Json("trait_definition_id", "x.trait_definition_id", "life_stage_id", "x.life_stage_definition_id",
                  "value_numeric", "x.value_numeric", "value_text", "x.value_text", "value_boolean", "x.value_boolean",
                  "categorical_option_id", "x.categorical_option_id", "confidence", "x.confidence")} AS details");

        yield return Ev("offspring_promotions", "observation", "Spiderlings promoted from a brood",
            Dates("x.promoted_at", null, "x.separated_on", "x.separated_on_end", "x.separation_date_quality"),
            "x.specimen_id::text AS spider_id, x.brood_id::text AS brood_id", $@"
            'promoted_from_brood' AS action, x.notes,
            {Json("suggested_code", "x.suggested_display_code", "final_code", "x.final_display_code",
                  "separated_at", "x.separated_at", "separated_at_end", "x.separated_at_end",
                  "physically_separated", "x.physically_separated", "life_stage_id", "x.life_stage_definition_id",
                  "source_enclosure_id", "x.source_enclosure_id", "source_assignment_id", "x.source_enclosure_assignment_id",
                  "purpose", "x.selection_purpose", "purpose_other", "x.selection_purpose_other")} AS details",
            code: "NULL");
    }

    private static string Parent(string role) => $@"(SELECT pl.parent_specimen_id::text FROM parentage_links pl
        WHERE pl.offspring_specimen_id = x.id AND pl.role = '{role}' AND pl.superseded_at IS NULL
        ORDER BY CASE pl.confidence WHEN 'confirmed' THEN 0 WHEN 'reported' THEN 1 WHEN 'probable' THEN 2 ELSE 3 END,
                 pl.effective_at DESC NULLS LAST, pl.created_at DESC LIMIT 1)";

    private static string Advisory(string kind) => $@"(SELECT {Ts("ad.set_at")} FROM specimen_molt_advisories ad
        WHERE ad.specimen_id = x.id AND ad.kind = '{kind}' AND ad.cleared_at IS NULL ORDER BY ad.set_at DESC LIMIT 1)";

    private static string BroodParent(string role) => $@"(SELECT bp.parent_specimen_id::text FROM brood_parent_links bp
        WHERE bp.brood_id = x.id AND bp.role = '{role}' AND bp.superseded_at IS NULL ORDER BY bp.created_at DESC LIMIT 1)";

    private static string ClutchPairing(string clutchId) => $@"(SELECT cp.pairing_id::text FROM clutch_pairings cp
        WHERE cp.clutch_id = {clutchId} AND cp.superseded_at IS NULL ORDER BY cp.created_at LIMIT 1)";

    private static string ClutchMale(string clutchId) => $@"(SELECT p.male_specimen_id::text FROM clutch_pairings cp
        JOIN pairings p ON p.id = cp.pairing_id
        WHERE cp.clutch_id = {clutchId} AND cp.superseded_at IS NULL ORDER BY cp.created_at LIMIT 1)";
}
