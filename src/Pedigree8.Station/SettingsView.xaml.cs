using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class SettingsView : UserControl
    {
        private readonly DispatcherTimer _usbTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private BackupInfo? _chosen;

        public SettingsView()
        {
            InitializeComponent();
            _usbTimer.Tick += (_, __) => RefreshUsb();
            Loaded += (_, __) => _usbTimer.Start();
            Unloaded += (_, __) => _usbTimer.Stop();
            Refresh();
        }

        private void Refresh()
        {
            var latest = App.Backups.LatestLocal();
            BackupStatus.Text = (latest is null ? "No backup on this tablet yet." : $"Last backup on this tablet: {latest.CreatedLocal:dddd d MMM, HH:mm}.") +
                              " A backup is made automatically every day and the last 14 are kept." +
                              (App.LastAutoBackupError is null ? "" : $"\nLast automatic backup failed: {App.LastAutoBackupError}");
            RefreshUsb();

            var window = MainWindow.Current;
            FullScreenButton.Content = window.IsFullScreen ? "Full screen: ON" : "Full screen: OFF";
            AutoStartButton.Content = App.StartsWithWindows ? "Start with Windows: ON" : "Start with Windows: OFF";

            var drive = new DriveInfo(Path.GetPathRoot(App.DataDir)!);
            long spiders, records;
            using (var c = App.Db.Open())
            {
                spiders = Dapper.SqlMapper.ExecuteScalar<long>(c, "SELECT count(*) FROM spiders");
                records = Dapper.SqlMapper.ExecuteScalar<long>(c, "SELECT count(*) FROM events WHERE voided_at IS NULL");
            }

            var freeGb = drive.AvailableFreeSpace / 1024d / 1024 / 1024;
            AboutInfo.Text = $"{spiders} spiders · {records} records · database {new FileInfo(App.Db.Path).Length / 1024} KB\n" +
                             $"Free space on the tablet: {freeGb:0.0} GB" + (freeGb < 1 ? "  — LOW, please free up space" : "") + "\n" +
                             $"Data folder: {App.DataDir}\n" +
                             $"Version {CurrentVersion}";
            UpdateStatus.Text = UpdateStatus.Text.Length > 0 ? UpdateStatus.Text : $"You have version {CurrentVersion}. Updates download from GitHub when you tap the button.";
        }

        private void RefreshUsb()
        {
            var usb = Backups.UsbDrives().FirstOrDefault();
            UsbButton.IsEnabled = usb != null;
            UsbButton.Content = usb is null ? "Back up to USB (plug in a stick)" : $"Back up to USB ({usb.Name.TrimEnd('\\')})";
        }

        private async void RunBackup(Func<BackupInfo> make, string where)
        {
            BackupNowButton.IsEnabled = UsbButton.IsEnabled = false;
            BackupResult.Text = "Backing up…";
            try
            {
                var info = await Task.Run(make);
                BackupResult.Text = $"✓ Backup saved and checked {where}: {info.FileName} ({info.Spiders} spiders, {info.Events} records).";
            }
            catch (Exception ex)
            {
                App.Log(ex);
                BackupResult.Text = "Backup failed: " + ex.Message;
            }
            finally
            {
                BackupNowButton.IsEnabled = true;
                Refresh();
            }
        }

        private void BackupNow_Click(object sender, RoutedEventArgs e) =>
            RunBackup(() => App.Backups.Create(App.Backups.LocalDir, "manual"), "on this tablet");

        private void BackupUsb_Click(object sender, RoutedEventArgs e)
        {
            var usb = Backups.UsbDrives().FirstOrDefault();
            if (usb != null)
            {
                RunBackup(() => App.Backups.Create(Backups.UsbFolder(usb), "usb"), $"to {usb.Name.TrimEnd('\\')}");
            }
        }

        private async void ShowRestore_Click(object sender, RoutedEventArgs e)
        {
            RestorePanel.Visibility = Visibility.Visible;
            ConfirmRow.Visibility = Visibility.Collapsed;
            RestoreTitle.Text = "Checking backups…";
            BackupList.Children.Clear();
            var found = await Task.Run(() => Backups.UsbDrives().Select(Backups.UsbFolder).Prepend(App.Backups.LocalDir)
                .SelectMany(dir => App.Backups.List(dir)).OrderByDescending(b => b.CreatedLocal).ToList());

            RestoreTitle.Text = found.Count == 0 ? "No backups found" : "Choose a backup to restore";
            foreach (var backup in found)
            {
                var where = backup.Path.StartsWith(App.Backups.LocalDir, StringComparison.OrdinalIgnoreCase) ? "tablet" : "USB";
                var button = new Button
                {
                    Content = $"{backup.Summary}   ({where}, {backup.Kind})",
                    Style = (Style)FindResource("FlatButton"),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Height = 64,
                };
                var chosen = backup;
                button.Click += (_, __) =>
                {
                    _chosen = chosen;
                    ConfirmText.Text = $"Restore the backup from {chosen.CreatedLocal:dddd d MMM yyyy, HH:mm}? " +
                                       "Everything entered after that time will be replaced. Your current data is saved as a backup first, so this can be undone.";
                    ConfirmRow.Visibility = Visibility.Visible;
                };
                BackupList.Children.Add(button);
            }
        }

        private async void ConfirmRestore_Click(object sender, RoutedEventArgs e)
        {
            if (_chosen is null)
            {
                return;
            }

            var path = _chosen.Path;
            ConfirmText.Text = "Restoring…";
            try
            {
                await Task.Run(() => App.Backups.Restore(path));
                App.Restart();
            }
            catch (Exception ex)
            {
                App.Log(ex);
                ConfirmText.Text = "Restore failed, nothing was changed: " + ex.Message;
            }
        }

        private void CloseRestore_Click(object sender, RoutedEventArgs e) => RestorePanel.Visibility = Visibility.Collapsed;

        // ---- Updates (only when tapped; nothing runs in the background) ----
        private ReleaseInfo? _available;

        private static Version CurrentVersion => Updater.Normalize(typeof(App).Assembly.GetName().Version);

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            InstallUpdateButton.Visibility = Visibility.Collapsed;
            UpdateStatus.Text = "Checking GitHub…";
            try
            {
                var latest = await new Updater().GetLatestAsync();
                if (latest is null)
                {
                    UpdateStatus.Text = "No updates have been published yet.";
                }
                else if (!Updater.IsNewer(latest, CurrentVersion))
                {
                    UpdateStatus.Text = $"✓ You have the latest version ({CurrentVersion}).";
                }
                else
                {
                    _available = latest;
                    UpdateStatus.Text = $"Version {latest.Version} is available (you have {CurrentVersion})." +
                                        (latest.Notes.Length > 0 ? "\nWhat's new: " + latest.Notes : "");
                    InstallUpdateButton.Content = $"Update to {latest.Version}";
                    InstallUpdateButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                App.Log(ex);
                UpdateStatus.Text = "Couldn't reach GitHub. Is the tablet connected to the internet?";
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
            }
        }

        private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            var release = _available;
            if (release is null)
            {
                return;
            }

            CheckUpdateButton.IsEnabled = InstallUpdateButton.IsEnabled = false;
            try
            {
                UpdateStatus.Text = "Backing up your data first…";
                await Task.Run(() => App.Backups.Create(App.Backups.LocalDir, "before-update"));
                UpdateStatus.Text = $"Downloading version {release.Version}…";
                var newApp = await new Updater().DownloadAsync(release, Path.Combine(App.DataDir, "update-download"));
                UpdateStatus.Text = "Installing…";
                var appDir = Path.GetDirectoryName(App.ExePath)!;
                await Task.Run(() => Updater.Apply(newApp, appDir));
                UpdateStatus.Text = "Restarting…";
                App.Restart();
            }
            catch (Exception ex)
            {
                App.Log(ex);
                UpdateStatus.Text = "Update failed, nothing was changed: " + ex.Message;
                CheckUpdateButton.IsEnabled = InstallUpdateButton.IsEnabled = true;
            }
        }

        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            var on = !MainWindow.Current.IsFullScreen;
            MainWindow.Current.SetFullScreen(on);
            App.Settings.Set("fullscreen", on ? "1" : "0");
            Refresh();
        }

        private void AutoStart_Click(object sender, RoutedEventArgs e)
        {
            App.StartsWithWindows = !App.StartsWithWindows;
            Refresh();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowHome();
    }
}
