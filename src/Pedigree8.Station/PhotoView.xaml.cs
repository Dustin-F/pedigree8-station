using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pedigree8.Core.Data;

namespace Pedigree8.Station
{
    public partial class PhotoView : UserControl
    {
        private const int MaxPhotoEdge = 1600;
        private static int _cameraIndex;
        private readonly string _spiderId;
        private QrScanner? _camera;
        private WriteableBitmap? _preview;
        private DateTime _lastPreview;
        private int _taken;

        public PhotoView(string spiderId)
        {
            InitializeComponent();
            _spiderId = spiderId;
            TitleText.Text = "Photo of " + new SpiderRepository(App.Db).Get(spiderId)!.Code;
            Loaded += async (_, __) =>
            {
                try
                {
                    SwitchButton.Visibility = await QrScanner.CountCamerasAsync() > 1 ? Visibility.Visible : Visibility.Collapsed;
                    await StartCamera();
                }
                catch (UnauthorizedAccessException)
                {
                    StatusText.Text = "Windows is blocking the camera (Settings → Privacy → Camera).";
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Camera problem: " + ex.Message;
                }
            };
            Unloaded += (_, __) => StopCamera();
        }

        private async Task StartCamera()
        {
            StopCamera();
            _camera = new QrScanner(decode: false, maxWidth: 1920);
            _camera.Frame += OnFrame;
            await _camera.StartAsync(_cameraIndex);
            CameraText.Text = _camera.CameraName;
            StatusText.Text = "Frame the spider, then tap TAKE PHOTO";
            ShutterButton.IsEnabled = true;
        }

        private void StopCamera()
        {
            _camera?.Dispose();
            _camera = null;
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
                if (_preview == null || _preview.PixelWidth != width || _preview.PixelHeight != height)
                {
                    _preview = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                    Preview.Source = _preview;
                }

                _preview.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            }));
        }

        private void Shutter_Click(object sender, RoutedEventArgs e)
        {
            var frame = _camera?.LatestFrame;
            if (frame is null)
            {
                return;
            }

            var (pixels, width, height) = frame.Value;
            BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
            var scale = Math.Min(1.0, (double)MaxPhotoEdge / Math.Max(width, height));
            if (scale < 1.0)
            {
                image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            }

            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(image));
            byte[] jpeg;
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                jpeg = stream.ToArray();
            }

            _taken++;
            MainWindow.Current.Run(() => App.Photos.Add(_spiderId, jpeg), () =>
                StatusText.Text = $"✓ {_taken} photo{(_taken == 1 ? "" : "s")} saved — take another or tap Done");
        }

        private async void Switch_Click(object sender, RoutedEventArgs e)
        {
            _cameraIndex++;
            ShutterButton.IsEnabled = false;
            try
            {
                await StartCamera();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Camera problem: " + ex.Message;
            }
        }

        private void Done_Click(object sender, RoutedEventArgs e) => MainWindow.Current.ShowSpider(_spiderId);
    }
}
