using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Pedigree8.Core;

namespace Pedigree8.Station
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private ActionResult? _lastAction;
        private Action? _afterUndo;

        public MainWindow()
        {
            InitializeComponent();
            _undoTimer.Tick += (_, __) => HideUndo();
            ShowHome();
        }

        public static MainWindow Current => (MainWindow)Application.Current.MainWindow;

        public void ShowHome() => Host.Content = new HomeView();

        public void ShowSpider(string spiderId) => Host.Content = new SpiderView(spiderId);

        public void ShowScan() => Host.Content = new ScanView();

        public void ShowEnclosures() => Host.Content = new EnclosuresView();

        public void ShowEnclosure(string enclosureId) => Host.Content = new EnclosureView(enclosureId);

        public void ShowNewSpider() => Host.Content = new NewSpiderView();

        public void ShowBreeding() => Host.Content = new BreedingView();

        public void ShowBrood(string broodId) => Host.Content = new BroodView(broodId);

        // Runs a station action, then offers Undo for 5 seconds. refresh redraws the current screen.
        public void Run(Func<ActionResult> action, Action refresh)
        {
            try
            {
                _lastAction = action();
                _afterUndo = refresh;
                refresh();
                UndoText.Text = "✓  " + _lastAction.Message;
                UndoBar.Visibility = Visibility.Visible;
                _undoTimer.Stop();
                _undoTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not save", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public bool IsFullScreen => WindowStyle == WindowStyle.None;

        public void SetFullScreen(bool on)
        {
            WindowState = WindowState.Normal;
            WindowStyle = on ? WindowStyle.None : WindowStyle.SingleBorderWindow;
            ResizeMode = on ? ResizeMode.NoResize : ResizeMode.CanResize;
            WindowState = on ? WindowState.Maximized : WindowState.Normal;
        }

        public void ShowSettings() => Host.Content = new SettingsView();

        public void ShowPhotoCamera(string spiderId) => Host.Content = new PhotoView(spiderId);

        public void ShowFeedAll() => Host.Content = new FeedAllView();

        public void ShowMistAll() => Host.Content = new FeedAllView(mist: true);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                SetFullScreen(!IsFullScreen);
            }

            base.OnKeyDown(e);
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            var action = _lastAction;
            var refresh = _afterUndo;
            HideUndo();
            if (action is null)
            {
                return;
            }

            action.Undo();
            refresh?.Invoke();
        }

        private void HideUndo()
        {
            _undoTimer.Stop();
            UndoBar.Visibility = Visibility.Collapsed;
            _lastAction = null;
            _afterUndo = null;
        }
    }
}
