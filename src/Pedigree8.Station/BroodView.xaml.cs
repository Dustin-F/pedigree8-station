using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class BroodView : UserControl
    {
        private readonly string _broodId;
        private BroodOverview _brood = null!;
        private int _count = 1;
        private string? _stageId;

        public BroodView(string broodId)
        {
            InitializeComponent();
            _broodId = broodId;
            Refresh();
            var options = new Instars(App.Db).PickerOptions(_brood.SpeciesId);
            _stageId = options.FirstOrDefault(o => o.Label == "L2").Value ?? options.FirstOrDefault().Value;
            Chips.Fill(StageChips, options, _stageId, v => _stageId = v, minWidth: 80);
        }

        private void Refresh()
        {
            _brood = App.Breeding.GetBrood(_broodId);
            TitleText.Text = "Egg sac " + _brood.Code;
            SubtitleText.Text = "Parents: " + _brood.Parents;

            DateRows.Children.Clear();
            AddDateRow("Laid", "laid_on", _brood.LaidOn);
            AddDateRow("Hatched", "hatched_on", _brood.HatchedOn);
            AddDateRow("Left the sac", "emerged_on", _brood.EmergedOn);

            EggText.Text = _brood.EggCount?.ToString(CultureInfo.InvariantCulture) ?? "–";
            CountText.Text = _count.ToString(CultureInfo.InvariantCulture);
            AddButton.Content = $"Add {_count} spiderling{(_count == 1 ? "" : "s")}";

            var offspring = App.Breeding.GetBroodOffspring(_broodId);
            OffspringHeading.Text = $"Spiderlings ({offspring.Count})";
            OffspringRow.Children.Clear();
            foreach (var o in offspring)
            {
                var b = new Button
                {
                    Content = o.Status == SpiderStatuses.Alive ? o.Code : $"{o.Code} ({o.Status})",
                    Style = (Style)FindResource("FlatButton"),
                    MinWidth = 140,
                };
                var id = o.Id;
                b.Click += (_, __) => MainWindow.Current.ShowSpider(id);
                OffspringRow.Children.Add(b);
            }
        }

        private void AddDateRow(string label, string field, string? value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = label, FontSize = 20, Width = 190, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            row.Children.Add(new TextBlock
            {
                Text = value is null ? "not yet" : DbTime.ParseDate(value).ToString("d MMM yyyy", CultureInfo.CurrentCulture),
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Width = 170,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource(value is null ? "Muted" : "Ink"),
            });
            row.Children.Add(DateButton("−1 day", field, value is null ? null : Shift(value, -1)));
            row.Children.Add(DateButton("+1 day", field, value is null ? null : Shift(value, 1)));
            row.Children.Add(DateButton("Today", field, DbTime.Today()));
            if (value != null)
            {
                row.Children.Add(DateButton("Clear", field, null, clear: true));
            }

            DateRows.Children.Add(row);
        }

        private Button DateButton(string text, string field, string? newValue, bool clear = false)
        {
            var b = new Button { Content = text, Style = (Style)FindResource("FlatButton"), IsEnabled = newValue != null || clear };
            b.Click += (_, __) => MainWindow.Current.Run(() => App.Breeding.SetBroodDate(_broodId, field, newValue), Refresh);
            return b;
        }

        private static string Shift(string date, int days) =>
            DbTime.ParseDate(date).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private void SetEggs(int? value) =>
            MainWindow.Current.Run(() => App.Breeding.SetEggCount(_broodId, value is null ? null : Math.Max(0, value.Value)), Refresh);

        private void EggsMinus_Click(object sender, RoutedEventArgs e) => SetEggs(_brood.EggCount is null || _brood.EggCount <= 1 ? (int?)null : _brood.EggCount - 1);

        private void EggsPlus_Click(object sender, RoutedEventArgs e) => SetEggs((_brood.EggCount ?? 0) + 1);

        private void EggsPlusTen_Click(object sender, RoutedEventArgs e) => SetEggs((_brood.EggCount ?? 0) + 10);

        private void SetCount(int value)
        {
            _count = Math.Max(1, Math.Min(200, value));
            Refresh();
        }

        private void CountMinus_Click(object sender, RoutedEventArgs e) => SetCount(_count - 1);

        private void CountPlus_Click(object sender, RoutedEventArgs e) => SetCount(_count + 1);

        private void CountPlusTen_Click(object sender, RoutedEventArgs e) => SetCount(_count + 10);

        private void AddOffspring_Click(object sender, RoutedEventArgs e)
        {
            var count = _count;
            var stage = _stageId is null ? null : new Instars(App.Db).Resolve(_stageId, _brood.SpeciesId);
            MainWindow.Current.Run(() => App.Breeding.AddOffspring(_broodId, count, stage), Refresh);
        }

        private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowBreeding();
    }
}
