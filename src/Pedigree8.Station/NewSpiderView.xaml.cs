using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dapper;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class NewSpiderView : UserControl
    {
        private const string None = "";
        private readonly LookupRepository _lookups = new LookupRepository(App.Db);
        private string? _lineId;
        private string? _speciesId;
        private string? _morphId;
        private string? _stageId;
        private string _sex = "unknown";
        private string? _enclosureId;
        private string? _suggestedCode;

        public NewSpiderView()
        {
            InitializeComponent();

            using (var c = App.Db.Open())
            {
                var lines = c.Query<(string Id, string Code)>("SELECT id, code FROM lines WHERE archived_at IS NULL ORDER BY code").ToList();
                Chips.Fill(LineChips, lines.Select(l => (l.Id, l.Code)).Append((None, "Other (type a code)")), None, SelectLine);
            }

            var species = _lookups.GetSpecies();
            _speciesId = species.FirstOrDefault()?.Id;
            SpeciesHeading.Visibility = SpeciesChips.Visibility = species.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            Chips.Fill(SpeciesChips, species.Select(s => (s.Id, s.Name)), _speciesId, v =>
            {
                _speciesId = v;
                FillSpeciesDependent();
            });
            Chips.Fill(SexChips, Chips.Sexes, _sex, v => _sex = v);

            var empty = new EnclosureRepository(App.Db).GetOverview().Where(e => e.IsEmpty).Select(e => (e.Id, e.Code));
            Chips.Fill(EnclosureChips, empty.Append((None, "None yet")), None, v => _enclosureId = v == None ? null : v);
            FillSpeciesDependent();
        }

        private void FillSpeciesDependent()
        {
            var morphs = _lookups.GetMorphs().Where(m => m.ParentId is null || m.ParentId == _speciesId).ToList();
            _morphId = morphs.FirstOrDefault()?.Id;
            Chips.Fill(MorphChips, morphs.Select(m => (m.Id, m.Name)).Append((None, "Not sure")), _morphId ?? None,
                v => _morphId = v == None ? null : v);

            _stageId = null;
            Chips.Fill(StageChips, new Instars(App.Db).PickerOptions(_speciesId).Append((None, "Not sure")), None,
                v => _stageId = v == None ? null : v, minWidth: 80);
        }

        private void SelectLine(string lineId)
        {
            _lineId = lineId == None ? null : lineId;
            if (_lineId is null)
            {
                if (CodeBox.Text == _suggestedCode)
                {
                    CodeBox.Text = "";
                }

                CodeBox.Focus();
                return;
            }

            // Preview only; the number is taken from the counter when saving.
            using var c = App.Db.Open();
            var line = c.QuerySingle<(string Code, long? Last)>(@"
                SELECT l.code, q.next_value FROM lines l LEFT JOIN id_sequences q ON q.key = 'source_line:' || l.id
                WHERE l.id = @lineId", new { lineId });
            var n = (line.Last ?? 0) + 1;
            while (c.ExecuteScalar<long>("SELECT count(*) FROM spiders WHERE code = @code COLLATE NOCASE", new { code = $"{line.Code}-{n:D2}" }) > 0)
            {
                n++;
            }

            _suggestedCode = $"{line.Code}-{n:D2}";
            CodeBox.Text = _suggestedCode;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var code = CodeBox.Text.Trim();
            if (_speciesId is null)
            {
                ErrorText.Text = "No species set up yet.";
                return;
            }

            if (_lineId != null && code == _suggestedCode)
            {
                using var c = App.Db.Open();
                var lineCode = c.ExecuteScalar<string>("SELECT code FROM lines WHERE id = @_lineId", new { _lineId })!;
                code = new SequenceRepository(App.Db).NextSpiderCode(_lineId, lineCode);
            }

            if (code.Length == 0)
            {
                ErrorText.Text = "Choose a bloodline or type a code.";
                return;
            }

            if (new SpiderRepository(App.Db).GetByCode(code) != null)
            {
                ErrorText.Text = $"{code} is already used.";
                return;
            }

            var spider = new Spider
            {
                Code = code,
                Name = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim(),
                SpeciesId = _speciesId,
                MorphId = _morphId,
                LineId = _lineId,
                LifeStageId = _stageId is null ? null : new Instars(App.Db).Resolve(_stageId, _speciesId),
                Sex = _sex,
                Origin = "transferred",
                EnclosureId = _enclosureId,
            };

            try
            {
                MainWindow.Current.Run(() => App.Husbandry.NewSpider(spider), () =>
                {
                    if (new SpiderRepository(App.Db).Get(spider.Id) != null)
                    {
                        MainWindow.Current.ShowSpider(spider.Id);
                    }
                    else
                    {
                        MainWindow.Current.ShowHome();
                    }
                });
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowHome();
    }
}
