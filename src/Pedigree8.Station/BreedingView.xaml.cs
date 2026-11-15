using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class BreedingView : UserControl
    {
        private static string? _selectedPairingId;
        private string? _femaleId;
        private string? _maleId;

        public BreedingView()
        {
            InitializeComponent();
            Refresh();
        }

        private void Refresh()
        {
            var pairings = App.Breeding.GetPairings();
            Pairings.ItemsSource = pairings;
            NoPairings.Visibility = pairings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var selected = pairings.FirstOrDefault(p => p.Id == _selectedPairingId);
            PairingDetail.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
            if (selected != null)
            {
                PairingTitle.Text = selected.Title;
                PairingSubtitle.Text = $"{selected.Code} · paired {selected.PairedOn}";
                Chips.Fill(ResultChips, Chips.PairingResults, selected.Result,
                    v => MainWindow.Current.Run(() => App.Breeding.SetPairingResult(selected.Id, v), Refresh), minWidth: 120);
            }

            var broods = App.Breeding.GetBroods(selected?.Id);
            BroodsHeading.Text = selected is null ? "All egg sacs / broods" : "Egg sacs from this pairing";
            Broods.ItemsSource = broods;
            NoBroods.Visibility = broods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Pairing_Click(object sender, RoutedEventArgs e)
        {
            var id = (string)((Button)sender).Tag;
            _selectedPairingId = _selectedPairingId == id ? null : id;
            Refresh();
        }

        private void EggSac_Click(object sender, RoutedEventArgs e)
        {
            var pairingId = _selectedPairingId!;
            MainWindow.Current.Run(() => App.Breeding.AddBrood(pairingId), Refresh);
        }

        private void Brood_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowBrood((string)((Button)sender).Tag);

        private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowHome();

        private void NewPairing_Click(object sender, RoutedEventArgs e)
        {
            var alive = new SpiderRepository(App.Db).GetOverview();
            string Label(SpiderOverview s) => s.Sex == "unknown" ? s.Code : $"{s.Code} ({Chips.Sexes.First(x => x.Item1 == s.Sex).Item2.ToLowerInvariant()})";
            var females = alive.OrderBy(s => s.Sex.Contains("female") ? 0 : s.Sex.Contains("male") ? 2 : 1).ThenBy(s => s.Code);
            var males = alive.OrderBy(s => s.Sex == "male" || s.Sex == "suspected_male" ? 0 : s.Sex.Contains("female") ? 2 : 1).ThenBy(s => s.Code);
            _femaleId = _maleId = null;
            Chips.Fill(FemaleChips, females.Select(s => (s.Id, Label(s))), null, v => _femaleId = v);
            Chips.Fill(MaleChips, males.Select(s => (s.Id, Label(s))), null, v => _maleId = v);
            NewError.Text = "";
            NewPanel.Visibility = Visibility.Visible;
        }

        private void CancelNew_Click(object sender, RoutedEventArgs e) => NewPanel.Visibility = Visibility.Collapsed;

        private void SaveNew_Click(object sender, RoutedEventArgs e)
        {
            if (_femaleId is null || _maleId is null)
            {
                NewError.Text = "Tap one female and one male.";
                return;
            }

            if (_femaleId == _maleId)
            {
                NewError.Text = "Choose two different spiders.";
                return;
            }

            NewPanel.Visibility = Visibility.Collapsed;
            var (f, m) = (_femaleId, _maleId);
            MainWindow.Current.Run(() =>
            {
                var result = App.Breeding.CreatePairing(f, m);
                _selectedPairingId = App.Breeding.GetPairings().First().Id;
                return result;
            }, Refresh);
        }
    }
}
