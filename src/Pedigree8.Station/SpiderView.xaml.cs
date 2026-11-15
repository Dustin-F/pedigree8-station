using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class SpiderView : UserControl
    {
        private enum EntryMode
        {
            Note,
            Health,
            Sex,
            Stage,
            Status,
            NameNotes,
        }

        private readonly string _spiderId;
        private readonly Dictionary<string, string> _names;
        private EntryMode _mode;
        private string? _choice;
        private string? _removeId;
        private bool _inPremolt;
        private Spider _spider = null!;

        public SpiderView(string spiderId)
        {
            InitializeComponent();
            _spiderId = spiderId;
            var lookups = new LookupRepository(App.Db);
            _names = lookups.GetPreyTypes().Concat(lookups.GetLifeStages())
                .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Name);
            Refresh();
        }

        private void Refresh()
        {
            _spider = new SpiderRepository(App.Db).Get(_spiderId)!;
            var spider = _spider;
            var tile = new SpiderRepository(App.Db).GetOverview(includeInactive: true).First(s => s.Id == _spiderId);

            CodeText.Text = spider.Code;
            NameText.Text = string.IsNullOrWhiteSpace(spider.Name) ? "" : "  " + spider.Name;
            SubtitleText.Text = string.Join("  ·  ", new[]
            {
                tile.LifeStageName, tile.MorphName, "Sex: " + Chips.Sexes.First(s => s.Item1 == spider.Sex).Item2.ToLowerInvariant(), tile.EnclosureCode,
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var alive = spider.Status == SpiderStatuses.Alive;
            StatusBadge.Visibility = alive ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Text = Chips.Statuses.First(s => s.Item1 == spider.Status).Item2.ToUpperInvariant();
            FedButton.IsEnabled = MistedButton.IsEnabled = MoltedButton.IsEnabled = alive;

            FedLast.Text = "last: " + tile.FedText + (spider.FeedEveryDays is { } fe ? $" · every {fe}d" : "") +
                           (tile.InPremolt ? " · premolt" : "");
            MistedLast.Text = "last: " + tile.MistedText + (spider.MistEveryDays is { } me ? $" · every {me}d" : "");
            var next = App.Husbandry.NextInstarName(spider);
            MoltedLast.Text = "last: " + tile.MoltText + (next is null ? "" : $"  →  {next}");

            _inPremolt = tile.InPremolt;
            PremoltText.Visibility = _inPremolt ? Visibility.Visible : Visibility.Collapsed;
            PremoltText.Text = "Premolt watch since " + (tile.PremoltSince ?? tile.InMoltSince ?? "").Split('T')[0];
            PremoltButton.Content = _inPremolt ? "Premolt watch off" : "Premolt watch on";

            var details = new List<string>();
            if (spider.AcquiredOn != null)
            {
                details.Add("Acquired " + spider.AcquiredOn);
            }

            if (spider.HatchedOn != null)
            {
                details.Add("Hatched " + spider.HatchedOn);
            }

            if (!string.IsNullOrWhiteSpace(spider.Notes))
            {
                details.Add(spider.Notes!);
            }

            DetailText.Text = details.Count > 0 ? string.Join("  ·  ", details) : "No extra details.";

            History.ItemsSource = new EventRepository(App.Db).GetForSpider(_spiderId, limit: 60)
                .Select(ev => HistoryRow.From(ev, _names)).ToList();
        }

        private void Do(System.Func<ActionResult> action)
        {
            ClosePanels();
            MainWindow.Current.Run(action, Refresh);
        }

        private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowHome();

        private void Fed_Click(object sender, RoutedEventArgs e) => Do(() => App.Husbandry.Fed(_spiderId));

        private void Misted_Click(object sender, RoutedEventArgs e) => Do(() => App.Husbandry.Misted(_spiderId));

        private void Molted_Click(object sender, RoutedEventArgs e) => Do(() => App.Husbandry.Molted(_spiderId));

        private void Refused_Click(object sender, RoutedEventArgs e) => Do(() => App.Husbandry.Fed(_spiderId, "refused"));

        private void Premolt_Click(object sender, RoutedEventArgs e) => Do(() => App.Husbandry.SetPremolt(_spiderId, !_inPremolt));

        private void More_Click(object sender, RoutedEventArgs e) => MorePanel.Visibility = Visibility.Visible;

        private void Note_Click(object sender, RoutedEventArgs e) =>
            OpenEntry(EntryMode.Note, "Write a note", null, null, text: true, "Save note");

        private void Health_Click(object sender, RoutedEventArgs e) =>
            OpenEntry(EntryMode.Health, "Health issue — how bad, and what did you notice?",
                new[] { ("mild", "Mild"), ("moderate", "Moderate"), ("severe", "Severe") }, "mild", text: true, "Save");

        private void Sex_Click(object sender, RoutedEventArgs e) =>
            OpenEntry(EntryMode.Sex, "What sex is " + _spider.Code + "?", Chips.Sexes, _spider.Sex, text: false, "Save");

        private void Stage_Click(object sender, RoutedEventArgs e)
        {
            var stages = new Instars(App.Db).PickerOptions(_spider.SpeciesId).ToArray();
            OpenEntry(EntryMode.Stage, "Correct the instar (logging a molt moves it up automatically)", stages, _spider.LifeStageId, text: false, "Save");
        }

        private void Status_Click(object sender, RoutedEventArgs e) =>
            OpenEntry(EntryMode.Status, "What happened to " + _spider.Code + "? Add a note if you like.", Chips.Statuses, _spider.Status, text: true, "Save");

        private void NameNotes_Click(object sender, RoutedEventArgs e)
        {
            OpenEntry(EntryMode.NameNotes, "Name (optional) and notes", null, null, text: true, "Save");
            NameBox.Visibility = Visibility.Visible;
            NameBox.Text = _spider.Name ?? "";
            EntryText.Text = _spider.Notes ?? "";
            NameBox.Focus();
        }

        private void OpenEntry(EntryMode mode, string title, (string, string)[]? choices, string? selected, bool text, string saveLabel)
        {
            _mode = mode;
            _choice = selected;
            MorePanel.Visibility = Visibility.Collapsed;
            TextPanelTitle.Text = title;
            SaveButton.Content = saveLabel;
            NameBox.Visibility = Visibility.Collapsed;
            ChoiceRow.Visibility = choices is null ? Visibility.Collapsed : Visibility.Visible;
            if (choices != null)
            {
                Chips.Fill(ChoiceRow, choices, selected, v => _choice = v);
            }

            EntryText.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
            EntryText.Text = "";
            TextPanel.Visibility = Visibility.Visible;
            if (mode == EntryMode.Note)
            {
                EntryText.Focus();
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var text = EntryText.Text;
            switch (_mode)
            {
                case EntryMode.Note when string.IsNullOrWhiteSpace(text):
                    EntryText.Focus();
                    return;
                case EntryMode.Note:
                    Do(() => App.Husbandry.AddNote(_spiderId, text));
                    break;
                case EntryMode.Health:
                    Do(() => App.Husbandry.AddHealth(_spiderId, _choice ?? "mild", text));
                    break;
                case EntryMode.Sex when _choice != null && _choice != _spider.Sex:
                    Do(() => App.Husbandry.SetSex(_spiderId, _choice));
                    break;
                case EntryMode.Stage when _choice != null && _choice != _spider.LifeStageId:
                    var stageId = new Instars(App.Db).Resolve(_choice, _spider.SpeciesId);
                    Do(() => App.Husbandry.SetLifeStage(_spiderId, stageId));
                    break;
                case EntryMode.Status when _choice != null && _choice != _spider.Status:
                    Do(() => App.Husbandry.SetStatus(_spiderId, _choice, text));
                    break;
                case EntryMode.NameNotes:
                    var name = NameBox.Text;
                    Do(() => App.Husbandry.UpdateNameAndNotes(_spiderId, name, text));
                    break;
                default:
                    ClosePanels();
                    break;
            }
        }

        private void Family_Click(object sender, RoutedEventArgs e)
        {
            var tree = App.Breeding.Family(_spiderId);
            FamilyContent.Children.Clear();
            AddFamilyRow("Grandparents (mother's side)", tree.MothersMother, tree.MothersFather);
            AddFamilyRow("Grandparents (father's side)", tree.FathersMother, tree.FathersFather);
            AddFamilyRow("Parents", tree.Mother, tree.Father);
            AddFamilyRow("Brothers and sisters", tree.Siblings.ToArray());
            AddFamilyRow("Offspring", tree.Offspring.ToArray());
            if (tree.Mother is null && tree.Father is null)
            {
                FamilyContent.Children.Insert(0, new TextBlock
                {
                    Text = $"{tree.Self.Code} has no recorded parents (founder or bought in).",
                    FontSize = 18,
                    Foreground = (Brush)FindResource("Muted"),
                    Margin = new Thickness(8, 0, 8, 8),
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            MorePanel.Visibility = Visibility.Collapsed;
            FamilyPanel.Visibility = Visibility.Visible;
        }

        private void AddFamilyRow(string title, params FamilyMember?[] members)
        {
            var present = members.Where(m => m != null).ToList();
            if (present.Count == 0)
            {
                return;
            }

            FamilyContent.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                Foreground = (Brush)FindResource("Muted"),
                Margin = new Thickness(8, 12, 0, 2),
            });
            var row = new WrapPanel();
            foreach (var m in present)
            {
                var button = new Button
                {
                    Content = m!.Status == SpiderStatuses.Alive ? m.Code : $"{m.Code} ({m.Status})",
                    Style = (Style)FindResource("FlatButton"),
                    MinWidth = 150,
                };
                var id = m.Id;
                button.Click += (_, __) => MainWindow.Current.ShowSpider(id);
                row.Children.Add(button);
            }

            FamilyContent.Children.Add(row);
        }

        // ---- Edit a history entry ----
        private StationEvent? _editing;

        private static readonly (string, string)[] FeedingOutcomes =
            { ("accepted", "Ate it"), ("partial", "Ate some"), ("refused", "Refused"), ("unknown", "Not sure") };

        private static readonly (string, string)[] HydrationActions =
            { ("misted", "Misted"), ("water_added", "Water added"), ("source_refilled", "Water refilled"), ("checked_adequate", "Checked, fine") };

        private static readonly (string, string)[] MoltOutcomes =
            { ("successful", "Good molt"), ("partial", "Stuck / partial"), ("abnormal", "Abnormal"), ("failed", "Failed") };

        private static readonly (string, string)[] Severities =
            { ("mild", "Mild"), ("moderate", "Moderate"), ("severe", "Severe") };

        private void HistoryRow_Click(object sender, RoutedEventArgs e)
        {
            _removeId = (string)((Button)sender).Tag;
            var ev = new EventRepository(App.Db).Get(_removeId)!;
            _editing = ev.Clone();
            var row = ((IEnumerable<HistoryRow>)History.ItemsSource).First(r => r.Id == _removeId);
            EditTitle.Text = "Edit: " + row.Title;

            FeedingFields.Visibility = ev.Type == EventTypes.Feeding ? Visibility.Visible : Visibility.Collapsed;
            if (ev.Type == EventTypes.Feeding)
            {
                Chips.Fill(OutcomeChips, FeedingOutcomes, ev.Outcome ?? "accepted", v => _editing.Outcome = v, minWidth: 120);
                FillPreyChips();
                ShowQuantity();
            }

            var actionChoices = ev.Type switch
            {
                EventTypes.Hydration => ("What did you do?", HydrationActions, ev.Action, (System.Action<string>)(v => _editing.Action = v)),
                EventTypes.Molt => ("How did the molt go?", MoltOutcomes, ev.Outcome, v => _editing.Outcome = v),
                EventTypes.Health => ("How bad?", Severities, ev.Severity, v => _editing.Severity = v),
                _ => ((string, (string, string)[], string?, System.Action<string>)?)null,
            };
            ActionFields.Visibility = actionChoices is null ? Visibility.Collapsed : Visibility.Visible;
            if (actionChoices is { } a)
            {
                ActionLabel.Text = a.Item1;
                Chips.Fill(ActionChips, a.Item2, a.Item3, a.Item4, minWidth: 140);
            }

            EditNotes.Text = ev.Notes ?? "";
            ShowEditDate();
            RemovePanel.Visibility = Visibility.Visible;
        }

        private void FillPreyChips()
        {
            var prey = new LookupRepository(App.Db).GetPreyTypes().Select(p => (p.Id, p.Name)).ToList();
            prey.Add(("+new", "+ Other food…"));
            Chips.Fill(PreyChips, prey, _editing!.PreyTypeId, v =>
            {
                if (v == "+new")
                {
                    NewPreyRow.Visibility = Visibility.Visible;
                    NewPreyBox.Text = "";
                    NewPreyBox.Focus();
                    Chips.Highlight(PreyChips, _editing.PreyTypeId);
                }
                else
                {
                    _editing.PreyTypeId = v;
                }
            }, minWidth: 120);
            NewPreyRow.Visibility = Visibility.Collapsed;
        }

        private void AddPrey_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NewPreyBox.Text))
            {
                return;
            }

            _editing!.PreyTypeId = App.Husbandry.AddPreyType(NewPreyBox.Text);
            FillPreyChips();
        }

        private void ShowQuantity() => QuantityText.Text = ((int)(_editing!.Quantity ?? 1)).ToString();

        private void QuantityMinus_Click(object sender, RoutedEventArgs e)
        {
            _editing!.Quantity = System.Math.Max(1, (_editing.Quantity ?? 1) - 1);
            ShowQuantity();
        }

        private void QuantityPlus_Click(object sender, RoutedEventArgs e)
        {
            _editing!.Quantity = System.Math.Min(50, (_editing.Quantity ?? 1) + 1);
            ShowQuantity();
        }

        private void ShowEditDate()
        {
            var date = _editing!.OccurredOn;
            EditDateText.Text = DbTime.ParseDate(date).ToString("ddd d MMM yyyy");
            EditDatePlus.IsEnabled = string.CompareOrdinal(date, DbTime.Today()) < 0;
        }

        private void EditDateMinus_Click(object sender, RoutedEventArgs e)
        {
            _editing!.OccurredOn = DbTime.ShiftDate(_editing.OccurredOn, -1);
            ShowEditDate();
        }

        private void EditDatePlus_Click(object sender, RoutedEventArgs e)
        {
            _editing!.OccurredOn = DbTime.ShiftDate(_editing.OccurredOn, 1);
            ShowEditDate();
        }

        private void EditDateToday_Click(object sender, RoutedEventArgs e)
        {
            _editing!.OccurredOn = DbTime.Today();
            ShowEditDate();
        }

        private void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            var edited = _editing!;
            edited.Notes = EditNotes.Text;
            Do(() => App.Husbandry.EditEntry(edited));
        }

        private void ConfirmRemove_Click(object sender, RoutedEventArgs e)
        {
            var id = _removeId!;
            Do(() => App.Husbandry.RemoveEntry(id));
        }

        private void MoveEnclosure_Click(object sender, RoutedEventArgs e)
        {
            EnclosureChoices.ItemsSource = new EnclosureRepository(App.Db).GetOverview()
                .Where(x => x.Id != _spider.EnclosureId).OrderBy(x => x.IsEmpty ? 0 : 1).ThenBy(x => x.Code).ToList();
            MorePanel.Visibility = Visibility.Collapsed;
            MovePanel.Visibility = Visibility.Visible;
        }

        private void EnclosureChoice_Click(object sender, RoutedEventArgs e)
        {
            var enclosureId = (string)((Button)sender).Tag;
            Do(() => App.Husbandry.MoveToEnclosure(_spiderId, enclosureId));
        }

        private void EnclosureDetails_Click(object sender, RoutedEventArgs e)
        {
            if (_spider.EnclosureId != null)
            {
                MainWindow.Current.ShowEnclosure(_spider.EnclosureId);
            }
        }

        // ---- Photos ----
        private Photo? _largePhoto;

        private void Photos_Click(object sender, RoutedEventArgs e)
        {
            MorePanel.Visibility = Visibility.Collapsed;
            FillPhotos();
            PhotosPanel.Visibility = Visibility.Visible;
        }

        private void FillPhotos()
        {
            var photos = App.Photos.ForSpider(_spiderId);
            PhotosTitle.Text = photos.Count == 0 ? $"No photos of {_spider.Code} yet" : $"Photos of {_spider.Code} ({photos.Count})";
            PhotoLarge.Visibility = Visibility.Collapsed;
            PhotoThumbs.Children.Clear();
            foreach (var photo in photos)
            {
                var button = new Button { Style = (Style)FindResource("FlatButton"), Padding = new Thickness(4), Width = 210, Height = 170 };
                button.Content = new Image { Source = LoadImage(photo.FullPath, 200), Stretch = Stretch.UniformToFill };
                var p = photo;
                button.Click += (_, __) => ShowLargePhoto(p);
                PhotoThumbs.Children.Add(button);
            }
        }

        private void ShowLargePhoto(Photo photo)
        {
            _largePhoto = photo;
            PhotoLargeImage.Source = LoadImage(photo.FullPath, 1200);
            PhotoDate.Text = photo.TakenAt is null ? "" : DbTime.FromDb(photo.TakenAt).ToLocalTime().ToString("d MMM yyyy, HH:mm");
            PhotoLarge.Visibility = Visibility.Visible;
        }

        // Decodes at the size shown, so a gallery of photos stays light on the tablet's memory.
        private static System.Windows.Media.Imaging.BitmapImage LoadImage(string path, int decodeWidth)
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodeWidth;
            image.UriSource = new System.Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void PhotoBack_Click(object sender, RoutedEventArgs e) => PhotoLarge.Visibility = Visibility.Collapsed;

        private void PhotoRemove_Click(object sender, RoutedEventArgs e)
        {
            var id = _largePhoto!.Id;
            MainWindow.Current.Run(() => App.Photos.Remove(id), FillPhotos);
        }

        private void TakePhoto_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowPhotoCamera(_spiderId);

        // ---- Measure ----
        private double _length;

        private void Measure_Click(object sender, RoutedEventArgs e)
        {
            MorePanel.Visibility = Visibility.Collapsed;
            var history = App.Husbandry.GetMeasurements(_spiderId);
            _length = history.Count > 0 ? history[0].BodyLengthMm : 5.0;
            ShowLength();
            GrowthList.Children.Clear();
            if (history.Count == 0)
            {
                GrowthList.Children.Add(new TextBlock { Text = "No measurements yet.", FontSize = 17, Foreground = (Brush)FindResource("Muted") });
            }

            foreach (var m in history)
            {
                GrowthList.Children.Add(new TextBlock
                {
                    FontSize = 18,
                    Margin = new Thickness(0, 2, 0, 2),
                    Text = $"{DbTime.ParseDate(m.OccurredOn):d MMM yyyy}    {m.BodyLengthMm:0.0} mm    {m.Instar}",
                });
            }

            MeasurePanel.Visibility = Visibility.Visible;
        }

        private void ShowLength() => LengthText.Text = $"{_length:0.0} mm";

        private void LengthStep_Click(object sender, RoutedEventArgs e)
        {
            _length = System.Math.Max(0.5, System.Math.Min(40, _length + double.Parse((string)((Button)sender).Tag, System.Globalization.CultureInfo.InvariantCulture)));
            ShowLength();
        }

        private void SaveMeasure_Click(object sender, RoutedEventArgs e)
        {
            var length = _length;
            Do(() => App.Husbandry.AddMeasurement(_spiderId, length));
        }

        // ---- Care schedule ----
        private string _feedEvery = "", _mistEvery = "";

        private void Schedule_Click(object sender, RoutedEventArgs e)
        {
            MorePanel.Visibility = Visibility.Collapsed;
            ScheduleTitle.Text = "Care schedule for " + _spider.Code;
            _feedEvery = _spider.FeedEveryDays?.ToString() ?? "";
            _mistEvery = _spider.MistEveryDays?.ToString() ?? "";
            (string, string)[] Days(params int[] values) =>
                new[] { ("", "Standard") }.Concat(values.Select(v => (v.ToString(), v == 1 ? "Daily" : $"{v} days"))).ToArray();
            Chips.Fill(FeedEveryChips, Days(2, 3, 4, 5, 7, 10, 14), _feedEvery, v => _feedEvery = v, minWidth: 100);
            Chips.Fill(MistEveryChips, Days(1, 2, 3, 4, 5, 7), _mistEvery, v => _mistEvery = v, minWidth: 100);
            SchedulePanel.Visibility = Visibility.Visible;
        }

        private void SaveSchedule_Click(object sender, RoutedEventArgs e)
        {
            int? feed = _feedEvery.Length == 0 ? (int?)null : int.Parse(_feedEvery);
            int? mist = _mistEvery.Length == 0 ? (int?)null : int.Parse(_mistEvery);
            Do(() => App.Husbandry.SetSchedule(_spiderId, feed, mist));
        }

        private void ClosePanels_Click(object sender, RoutedEventArgs e) => ClosePanels();

        private void ClosePanels()
        {
            MorePanel.Visibility = TextPanel.Visibility = RemovePanel.Visibility = MovePanel.Visibility = FamilyPanel.Visibility =
                PhotosPanel.Visibility = MeasurePanel.Visibility = SchedulePanel.Visibility = Visibility.Collapsed;
        }
    }
}
