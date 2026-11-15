using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pedigree8.Station
{
    // A row of large tap-to-select buttons, used instead of dropdowns and typing.
    public static class Chips
    {
        public static void Fill(Panel panel, IEnumerable<(string Value, string Label)> options, string? selected,
                                Action<string> onSelect, double minWidth = 140)
        {
            panel.Children.Clear();
            foreach (var (value, label) in options)
            {
                var button = new Button
                {
                    Content = label,
                    Tag = value,
                    Style = (Style)Application.Current.Resources["FlatButton"],
                    MinWidth = minWidth,
                };
                button.Click += (_, __) =>
                {
                    Highlight(panel, value);
                    onSelect(value);
                };
                panel.Children.Add(button);
            }

            Highlight(panel, selected);
        }

        public static void Highlight(Panel panel, string? selected)
        {
            foreach (var child in panel.Children)
            {
                if (child is Button b)
                {
                    var on = Equals(b.Tag, selected);
                    b.Background = (Brush)Application.Current.Resources[on ? "Blue" : "Surface2"];
                    b.Foreground = (Brush)Application.Current.Resources[on ? "Bg" : "Ink"];
                }
            }
        }

        public static readonly (string, string)[] Sexes =
        {
            ("unknown", "Unknown"), ("suspected_female", "Probably female"), ("suspected_male", "Probably male"),
            ("female", "Female"), ("male", "Male"), ("indeterminate", "Can't tell"),
        };

        public static readonly (string, string)[] Statuses =
        {
            ("alive", "Alive"), ("sold", "Sold"), ("transferred", "Given away"), ("missing", "Missing / escaped"), ("deceased", "Died"),
        };

        public static readonly (string, string)[] PairingResults =
        {
            ("mating_observed", "Mated"), ("possible_mating", "Maybe mated"), ("no_mating", "No mating"),
            ("aggression", "Aggression"), ("injury", "Injury"), ("unknown", "Not sure"),
        };
    }
}
