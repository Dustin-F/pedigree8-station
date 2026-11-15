using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Pedigree8.Core;

namespace Pedigree8.Station
{
    public sealed class HistoryRow
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string When { get; set; } = "";
        public string Detail { get; set; } = "";
        public bool HasDetail => Detail.Length > 0;
        public Brush Brush { get; set; } = Brushes.Gray;

        public static HistoryRow From(StationEvent ev, IReadOnlyDictionary<string, string> names)
        {
            string Name(string? id) => id != null && names.TryGetValue(id, out var n) ? n : "";
            var parts = new List<string>();
            string title;
            string colour;

            switch (ev.Type)
            {
                case EventTypes.Feeding:
                    title = ev.Outcome switch
                    {
                        "refused" => "Refused food",
                        "partial" => "Ate some",
                        "unknown" => "Offered food",
                        _ => "Fed",
                    };
                    colour = ev.Outcome == "refused" ? "Orange" : "Green";
                    parts.Add(Name(ev.PreyTypeId));
                    parts.Add(ev.PreySize ?? "");
                    break;
                case EventTypes.Hydration:
                    title = ev.Action switch
                    {
                        "water_added" => "Water added",
                        "source_refilled" => "Water refilled",
                        "checked_adequate" => "Checked water",
                        _ => "Misted",
                    };
                    colour = "Blue";
                    break;
                case EventTypes.Molt:
                    title = "Molted";
                    colour = "Purple";
                    var from = Name(ev.FromStageId);
                    var to = Name(ev.ToStageId);
                    if (to.Length > 0 && to != from)
                    {
                        parts.Add(from.Length > 0 ? $"{from} → {to}" : $"to {to}");
                    }

                    if (ev.Outcome is { } o && o != "successful")
                    {
                        parts.Add(o);
                    }

                    break;
                case EventTypes.Premolt:
                    title = ev.Action == "active_molt" ? "In molt" : "Premolt watch";
                    colour = "Purple";
                    if (ev.UntilOn != null)
                    {
                        parts.Add("ended " + Pretty(ev.UntilOn));
                    }

                    break;
                case EventTypes.Health:
                    title = "Health" + (ev.Severity is { } s && s != "note" ? $" · {s}" : "");
                    colour = "Orange";
                    parts.Add(ev.Action ?? "");
                    break;
                case EventTypes.Death:
                    title = "Died";
                    colour = "Red";
                    break;
                case EventTypes.Sex:
                    title = "Sexed: " + (ev.SexValue ?? "unknown").Replace('_', ' ');
                    colour = "Grey";
                    break;
                case EventTypes.Status:
                    title = "Status: " + (Chips.Statuses.FirstOrDefault(s => s.Item1 == ev.Action).Item2 ?? ev.Action);
                    colour = "Orange";
                    break;
                case EventTypes.Acquired:
                    title = "Acquired";
                    colour = "Grey";
                    break;
                case EventTypes.Note:
                    title = "Note";
                    colour = "Grey";
                    break;
                default:
                    title = (ev.Action ?? ev.Type).Replace('_', ' ');
                    title = char.ToUpperInvariant(title[0]) + title.Substring(1);
                    colour = "Grey";
                    break;
            }

            if (!string.IsNullOrWhiteSpace(ev.Notes))
            {
                parts.Add(ev.Notes!);
            }

            return new HistoryRow
            {
                Id = ev.Id,
                Title = title,
                When = Pretty(ev.OccurredOn) + (ev.DateQuality == "estimated" ? " (approx.)" : ""),
                Detail = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p))),
                Brush = (Brush)Application.Current.Resources[colour],
            };
        }

        private static string Pretty(string date)
        {
            var days = DbTime.DaysSince(date);
            if (days == 0)
            {
                return "today";
            }

            if (days == 1)
            {
                return "yesterday";
            }

            var d = DbTime.ParseDate(date);
            return d.Year == DateTime.Now.Year
                ? d.ToString("d MMM", CultureInfo.CurrentCulture)
                : d.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        }
    }
}
