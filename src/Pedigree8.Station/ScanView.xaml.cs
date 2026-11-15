using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class ScanView : UserControl
    {
        private static int _cameraIndex;
        private QrScanner? _scanner;
        private WriteableBitmap? _preview;
        private DateTime _lastPreview;
        private bool _closing;

        public ScanView()
        {
            InitializeComponent();
            Loaded += async (_, __) =>
            {
                try
                {
                    SwitchButton.Visibility = await QrScanner.CountCamerasAsync() > 1 ? Visibility.Visible : Visibility.Collapsed;
                    await StartCamera();
                }
                catch (UnauthorizedAccessException)
                {
                    StatusText.Text = "Windows is blocking the camera. Turn on Settings → Privacy → Camera → \"Allow desktop apps to access your camera\".";
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Camera problem: " + ex.Message;
                }
            };
            Unloaded += (_, __) => StopCamera();
        }

        private async System.Threading.Tasks.Task StartCamera()
        {
            StopCamera();
            _scanner = new QrScanner();
            _scanner.Frame += OnFrame;
            _scanner.Decoded += text => Dispatcher.BeginInvoke(new Action(() => OnDecoded(text)));
            await _scanner.StartAsync(_cameraIndex);
            CameraText.Text = _scanner.CameraName;
            StatusText.Text = "Hold a Pedigree8 label inside the square";
        }

        private void StopCamera()
        {
            _scanner?.Dispose();
            _scanner = null;
        }

        private void OnFrame(byte[] pixels, int width, int height)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastPreview).TotalMilliseconds < 80)
            {
                return;
            }

            _lastPreview = now;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (_closing)
                {
                    return;
                }

                if (_preview == null || _preview.PixelWidth != width || _preview.PixelHeight != height)
                {
                    _preview = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                    Preview.Source = _preview;
                }

                _preview.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            }));
        }

        private void OnDecoded(string text)
        {
            if (_closing || _scanner is null)
            {
                return;
            }

            var enclosure = new EnclosureRepository(App.Db).FindByScan(text);
            if (enclosure is null)
            {
                StatusText.Text = "That is not a Pedigree8 enclosure label";
                return;
            }

            _closing = true;
            _scanner.Pause();
            var occupant = new SpiderRepository(App.Db).GetOverview().FirstOrDefault(s => s.EnclosureId == enclosure.Id);
            if (occupant != null)
            {
                MainWindow.Current.ShowSpider(occupant.Id);
            }
            else
            {
                MainWindow.Current.ShowEnclosure(enclosure.Id);
            }
        }

        private async void Switch_Click(object sender, RoutedEventArgs e)
        {
            _cameraIndex++;
            StatusText.Text = "Switching camera…";
            try
            {
                await StartCamera();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Camera problem: " + ex.Message;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            _closing = true;
            MainWindow.Current.ShowHome();
        }
    }
}
