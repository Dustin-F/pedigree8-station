using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class FeedAllView : UserControl
    {
        private static readonly (string, string)[] Outcomes =
            { ("accepted", "All ate"), ("partial", "Ate some"), ("refused", "Refused"), ("unknown", "Not sure") };

        private readonly List<SpiderOverview> _spiders;
        private readonly HashSet<string> _selected;
        private string _date = DbTime.Today();
        private string _outcome = "accepted";
        private string? _preyId;
        private int _quantity = 1;

        public FeedAllView()
        {
            InitializeComponent();
            _spiders = new SpiderRepository(App.Db).GetOverview().ToList();
            _selected = new HashSet<string>(_spiders.Select(s => s.Id));
            _preyId = App.Husbandry.MostRecentPreyType();
            Chips.Fill(OutcomeChips, Outcomes, _outcome, v => _outcome = v, minWidth: 130);
            FillPrey();
            Refresh();
        }

        private void Refresh()
        {
            SpiderGrid.Children.Clear();
            foreach (var spider in _spiders)
            {
                var on = _selected.Contains(spider.Id);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = (on ? "✓ " : "") + spider.Code, FontSize = 22, FontWeight = FontWeights.Bold });
                text.Children.Add(new TextBlock
                {
                    Text = spider.InPremolt ? "premolt" : "fed " + spider.FedShort,
                    FontSize = 15,
                    FontWeight = FontWeights.Normal,
                    Foreground = (Brush)FindResource(on ? "Bg" : spider.InPremolt ? "Purple" : "Muted"),
                });
                var button = new Button
                {
                    Content = text,
                    Style = (Style)FindResource("FlatButton"),
                    Height = 70,
                    Background = (Brush)FindResource(on ? "Green" : "Surface2"),
                    Foreground = (Brush)FindResource(on ? "Bg" : "Ink"),
                };
                var id = spider.Id;
                button.Click += (_, __) =>
                {
                    if (!_selected.Remove(id))
                    {
                        _selected.Add(id);
                    }

                    Refresh();
                };
                SpiderGrid.Children.Add(button);
            }

            DateText.Text = _date == DbTime.Today() ? "Today" : DbTime.ParseDate(_date).ToString("ddd d MMM");            DatePlus.IsEnabled = string.CompareOrdinal(_date, DbTime.Today()) < 0;
            QuantityText.Text = _quantity.ToString();
            SaveButton.Content = _selected.Count == 0 ? "Select spiders" : $"Feed {_selected.Count} spider{(_selected.Count == 1 ? "" : "s")}";
            SaveButton.IsEnabled = _selected.Count > 0;
        }

        private void FillPrey()
        {
            var prey = new LookupRepository(App.Db).GetPreyTypes().Select(p => (p.Id, p.Name)).ToList();
            _preyId ??= prey.FirstOrDefault().Id;
            prey.Add(("+new", "+ Other food…"));
            Chips.Fill(PreyChips, prey, _preyId, v =>
            {
                if (v == "+new")
                {
                    NewPreyRow.Visibility = Visibility.Visible;
                    NewPreyBox.Text = "";
                    NewPreyBox.Focus();
                    Chips.Highlight(PreyChips, _preyId);
                }
                else
                {
                    _preyId = v;
                }
            }, minWidth: 120);
            NewPreyRow.Visibility = Visibility.Collapsed;
        }

        private void AddPrey_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NewPreyBox.Text))
            {
                _preyId = App.Husbandry.AddPreyType(NewPreyBox.Text);
                FillPrey();
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            _selected.UnionWith(_spiders.Select(s => s.Id));
            Refresh();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            _selected.Clear();
            Refresh();
        }

        private void DateMinus_Click(object sender, RoutedEventArgs e)
        {
            _date = DbTime.ShiftDate(_date, -1);
            Refresh();
        }

        private void DatePlus_Click(object sender, RoutedEventArgs e)
        {
            _date = DbTime.ShiftDate(_date, 1);
            Refresh();
        }

        private void DateToday_Click(object sender, RoutedEventArgs e)
        {
            _date = DbTime.Today();
            Refresh();
        }

        private void QuantityMinus_Click(object sender, RoutedEventArgs e)
        {
            _quantity = Math.Max(1, _quantity - 1);
            Refresh();
        }

        private void QuantityPlus_Click(object sender, RoutedEventArgs e)
        {
            _quantity = Math.Min(50, _quantity + 1);
            Refresh();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var ids = _spiders.Where(s => _selected.Contains(s.Id)).Select(s => s.Id).ToList();
            var (date, outcome, prey, quantity) = (_date, _outcome, _preyId, (double)_quantity);
            MainWindow.Current.Run(() => App.Husbandry.FeedMany(ids, date, outcome, prey, quantity), () => MainWindow.Current.ShowHome());
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowHome();
    }
}
