using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class EnclosureView : UserControl
    {
        private readonly string _enclosureId;
        private readonly EnclosureRepository _enclosures = new EnclosureRepository(App.Db);
        private EnclosureOverview _overview = null!;

        public EnclosureView(string enclosureId)
        {
            InitializeComponent();
            _enclosureId = enclosureId;
            LoadFields();
            Refresh();
        }

        private void LoadFields()
        {
            var e = _enclosures.Get(_enclosureId)!;
            CodeBox.Text = e.Code;
            TypeBox.Text = e.TypeName;
            DimensionsBox.Text = e.Dimensions;
            VentilationBox.Text = e.Ventilation;
            SubstrateBox.Text = e.Substrate;
            WaterBox.Text = e.WaterMethod;
            ContentsBox.Text = e.Contents;
            NotesBox.Text = e.SetupNotes;
            QrPreview.Source = Labels.QrImage(e.QrToken);
        }

        private void Refresh()
        {
            _overview = _enclosures.GetOverview().First(x => x.Id == _enclosureId);
            TitleText.Text = _overview.Code;
            SubtitleText.Text = _overview.TypeName ?? "";
            OccupantText.Text = _overview.OccupantText;
            OpenSpiderButton.Visibility = _overview.OccupantId is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var code = CodeBox.Text.Trim();
            if (code.Length == 0)
            {
                ErrorText.Text = "The code cannot be empty.";
                return;
            }

            if (_enclosures.GetAll(includeArchived: true).Any(x => x.Id != _enclosureId && x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            {
                ErrorText.Text = $"{code} is already used by another enclosure.";
                return;
            }

            var enclosure = _enclosures.Get(_enclosureId)!;
            enclosure.Code = code;
            enclosure.TypeName = Clean(TypeBox.Text);
            enclosure.Dimensions = Clean(DimensionsBox.Text);
            enclosure.Ventilation = Clean(VentilationBox.Text);
            enclosure.Substrate = Clean(SubstrateBox.Text);
            enclosure.WaterMethod = Clean(WaterBox.Text);
            enclosure.Contents = Clean(ContentsBox.Text);
            enclosure.SetupNotes = Clean(NotesBox.Text);
            _enclosures.Update(enclosure);
            ErrorText.Text = "";
            SaveButton.Content = "Saved ✓";
            Refresh();
        }

        private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

        private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowEnclosures();

        private void PrintOne_Click(object sender, RoutedEventArgs e) => Labels.Print(new[] { _overview });

        private void OpenSpider_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowSpider(_overview.OccupantId!);

        private void PickSpider_Click(object sender, RoutedEventArgs e)
        {
            SpiderChoices.ItemsSource = new SpiderRepository(App.Db).GetOverview().Where(s => s.EnclosureId != _enclosureId).ToList();
            PickPanel.Visibility = Visibility.Visible;
        }

        private void ClosePick_Click(object sender, RoutedEventArgs e) => PickPanel.Visibility = Visibility.Collapsed;

        private void SpiderChoice_Click(object sender, RoutedEventArgs e)
        {
            var spiderId = (string)((Button)sender).Tag;
            PickPanel.Visibility = Visibility.Collapsed;
            MainWindow.Current.Run(() => App.Husbandry.MoveToEnclosure(spiderId, _enclosureId), Refresh);
        }
    }
}
