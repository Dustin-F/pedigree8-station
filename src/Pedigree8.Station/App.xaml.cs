using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Pedigree8.Core;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class App : Application
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "Pedigree8Station";
        private static readonly DateTime Started = DateTime.UtcNow;
        private Mutex? _singleInstance;
        private DispatcherTimer? _backupTimer;

        public static StationDb Db { get; private set; } = null!;

        public static Husbandry Husbandry { get; private set; } = null!;

        public static Breeding Breeding { get; private set; } = null!;

        public static Backups Backups { get; private set; } = null!;

        public static SettingsRepository Settings { get; private set; } = null!;

        public static Photos Photos { get; private set; } = null!;

        public static string DataDir { get; private set; } = "";

        public static string ExePath => Process.GetCurrentProcess().MainModule!.FileName;

        public static string? LastAutoBackupError { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            var dataArg = e.Args.SkipWhile(a => !a.Equals("--data", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
            DataDir = Path.GetFullPath(dataArg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pedigree8"));

            // One running copy per data folder.
            _singleInstance = new Mutex(true, @"Local\Pedigree8Station-" + DataDir.ToLowerInvariant().GetHashCode().ToString("x8"), out var isFirst);
            if (!isFirst && !(e.Args.Contains("--restarted") && WaitForPreviousInstance()))
            {
                BringExistingWindowToFront();
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, a) =>
            {
                Log(a.Exception);
                a.Handled = true;
                MessageBox.Show("Something went wrong, but your records are safe.\n\n" + a.Exception.Message,
                    "Pedigree8 Station", MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            {
                Log(a.ExceptionObject as Exception);
                if (DateTime.UtcNow - Started > TimeSpan.FromMinutes(1))
                {
                    Restart();
                }
            };
            TaskScheduler.UnobservedTaskException += (_, a) =>
            {
                Log(a.Exception);
                a.SetObserved();
            };

            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            try
            {
                Updater.CleanupOldFiles(Path.GetDirectoryName(ExePath)!);
                var download = Path.Combine(DataDir, "update-download");
                if (Directory.Exists(download))
                {
                    Directory.Delete(download, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }

            InstallSeedDataIfEmpty();

            Db = new StationDb(Path.Combine(DataDir, "station.db"));
            Db.Initialize();
            Husbandry = new Husbandry(Db);
            Breeding = new Breeding(Db, Husbandry);
            Backups = new Backups(Db);
            Settings = new SettingsRepository(Db);
            Photos = new Photos(Db);

            base.OnStartup(e);
            var window = new MainWindow();
            var fullScreen = Settings.Get("fullscreen");
            window.SetFullScreen(fullScreen is null ? e.Args.Contains("--kiosk") : fullScreen == "1");
            window.Show();

            _backupTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _backupTimer.Tick += (_, __) => RunAutoBackup();
            _backupTimer.Start();
            RunAutoBackup();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }

        public static void RunAutoBackup() => Task.Run(() =>
        {
            try
            {
                Backups.AutoBackupIfDue(TimeSpan.FromDays(1), keep: 14);
                LastAutoBackupError = null;
            }
            catch (Exception ex)
            {
                LastAutoBackupError = ex.Message;
                Log(ex);
            }
        });

        public static bool StartsWithWindows
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) != null;
            }

            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                {
                    key.SetValue(RunValue, $"\"{ExePath}\" --kiosk");
                }
                else
                {
                    key.DeleteValue(RunValue, throwOnMissingValue: false);
                }
            }
        }

        public static void Restart()
        {
            Process.Start(ExePath, "--restarted" + (DataDir.Length > 0 ? $" --data \"{DataDir}\"" : ""));
            Current.Dispatcher.BeginInvoke(new Action(() => Current.Shutdown()));
        }

        public static void Log(Exception? ex)
        {
            try
            {
                var dir = Path.Combine(DataDir.Length > 0 ? DataDir : Path.GetTempPath(), "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, $"errors-{DateTime.Now:yyyy-MM}.txt"), $"{DateTime.Now:s}  {ex}\r\n\r\n");
            }
            catch (IOException)
            {
            }
        }

        // On first launch after installing, copy the packaged collection (seed folder next to the exe).
        // Existing station data is never overwritten.
        private static void InstallSeedDataIfEmpty()
        {
            var seed = Path.Combine(Path.GetDirectoryName(ExePath)!, "seed");
            if (File.Exists(Path.Combine(DataDir, "station.db")) || !File.Exists(Path.Combine(seed, "station.db")))
            {
                return;
            }

            Directory.CreateDirectory(DataDir);
            foreach (var file in Directory.EnumerateFiles(seed, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(DataDir, file.Substring(seed.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }

        private bool WaitForPreviousInstance()
        {
            try
            {
                return _singleInstance!.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }

        private static void BringExistingWindowToFront()
        {
            var me = Process.GetCurrentProcess();
            var other = Process.GetProcessesByName(me.ProcessName).FirstOrDefault(p => p.Id != me.Id && p.MainWindowHandle != IntPtr.Zero);
            if (other != null)
            {
                ShowWindow(other.MainWindowHandle, 9);
                SetForegroundWindow(other.MainWindowHandle);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
