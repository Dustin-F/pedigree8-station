using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class HomeView : UserControl
    {
        private static bool _showAll;
        private static bool _needsCareOnly;

        public HomeView()
        {
            InitializeComponent();
            Refresh();
        }

        private void Refresh()
        {
            var repo = new SpiderRepository(App.Db);
            var everyone = repo.GetOverview(includeInactive: true);
            var alive = everyone.Where(s => s.IsAlive).ToList();
            var needing = alive.Where(s => s.NeedsCare).OrderByDescending(s => s.Urgency).ThenBy(s => s.Code).ToList();

            Tiles.ItemsSource = _needsCareOnly ? needing : _showAll ? everyone.ToList() : alive;
            AllDoneText.Visibility = _needsCareOnly && needing.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Chips.Fill(FilterChips, new[] { ("all", "All spiders"), ("care", $"Needs care ({needing.Count})") },
                _needsCareOnly ? "care" : "all", v =>
                {
                    _needsCareOnly = v == "care";
                    Refresh();
                }, minWidth: 180);

            SubtitleText.Text = $"{DateTime.Now.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}  ·  {alive.Count} alive" +
                                (needing.Count > 0 ? $"  ·  {needing.Count} need care" : "  ·  all cared for");
            var past = everyone.Count - alive.Count;
            ShowAllButton.Visibility = _needsCareOnly ? Visibility.Collapsed : Visibility.Visible;
            ShowAllButton.Content = _showAll ? "Hide past spiders" : $"Show past spiders ({past})";
        }

        private void Tile_Click(object sender, RoutedEventArgs e) =>
            MainWindow.Current.ShowSpider((string)((Button)sender).Tag);

        private void MistAll_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowMistAll();

        private void Scan_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowScan();

        private void FeedAll_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowFeedAll();

        private void Enclosures_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowEnclosures();

        private void Breeding_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowBreeding();

        private void Settings_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowSettings();

        private void NewSpider_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowNewSpider();

        private void ShowAll_Click(object sender, RoutedEventArgs e)
        {
            _showAll = !_showAll;
            Refresh();
        }
    }
}
