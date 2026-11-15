using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class EnclosuresView : UserControl
    {
        private readonly EnclosureRepository _enclosures = new EnclosureRepository(App.Db);

        public EnclosuresView()
        {
            InitializeComponent();
            Refresh();
        }

        private void Refresh()
        {
            var all = _enclosures.GetOverview();
            Tiles.ItemsSource = all;
            SubtitleText.Text = $"{all.Count} enclosures · {all.Count(e => e.IsEmpty)} empty";
        }

        private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowHome();

        private void Tile_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowEnclosure((string)((Button)sender).Tag);

        private void PrintAll_Click(object sender, RoutedEventArgs e) => Labels.Print(_enclosures.GetOverview());

        private void New_Click(object sender, RoutedEventArgs e)
        {
            NewCode.Text = "";
            NewError.Text = "";
            NewPanel.Visibility = Visibility.Visible;
            NewCode.Focus();
        }

        private void CancelNew_Click(object sender, RoutedEventArgs e) => NewPanel.Visibility = Visibility.Collapsed;

        private void CreateNew_Click(object sender, RoutedEventArgs e)
        {
            var code = NewCode.Text.Trim();
            if (code.Length == 0)
            {
                NewError.Text = "Type a code first.";
                return;
            }

            if (_enclosures.GetAll(includeArchived: true).Any(x => x.Code.Equals(code, System.StringComparison.OrdinalIgnoreCase)))
            {
                NewError.Text = $"{code} is already used.";
                return;
            }

            var enclosure = new Enclosure { Code = code };
            _enclosures.Insert(enclosure);
            MainWindow.Current.ShowEnclosure(enclosure.Id);
        }
    }
}
