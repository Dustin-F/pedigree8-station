using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Pedigree8.Core;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Pedigree8.Station
{
    // Low-resolution camera frames → preview callback + QR decode. Decoding runs on a worker
    // thread and frames that arrive while it is busy are skipped, which keeps CPU use low.
    public sealed class QrScanner : IDisposable
    {
        private readonly bool _decode;
        private readonly int _maxWidth;
        private readonly object _frameLock = new object();
        private MediaCapture? _capture;
        private MediaFrameReader? _reader;
        private int _decoding;
        private bool _found;
        private (byte[] Pixels, int Width, int Height)? _latest;

        // decode=false + a larger maxWidth turns this into a plain photo camera.
        public QrScanner(bool decode = true, int maxWidth = 1280)
        {
            _decode = decode;
            _maxWidth = maxWidth;
        }

        public (byte[] Pixels, int Width, int Height)? LatestFrame
        {
            get
            {
                lock (_frameLock)
                {
                    return _latest;
                }
            }
        }

        public event Action<byte[], int, int>? Frame;

        public event Action<string>? Decoded;

        public string CameraName { get; private set; } = "";

        public static async Task<int> CountCamerasAsync() =>
            (await MediaFrameSourceGroup.FindAllAsync()).Count(g => g.SourceInfos.Any(s => s.SourceKind == MediaFrameSourceKind.Color));

        public async Task StartAsync(int cameraIndex)
        {
            var groups = (await MediaFrameSourceGroup.FindAllAsync())
                .Where(g => g.SourceInfos.Any(s => s.SourceKind == MediaFrameSourceKind.Color))
                .OrderBy(g => g.SourceInfos.Any(s => s.DeviceInformation?.EnclosureLocation?.Panel == Panel.Back) ? 0 : 1)
                .ToList();
            if (groups.Count == 0)
            {
                throw new InvalidOperationException("No camera found.");
            }

            var group = groups[cameraIndex % groups.Count];
            var info = group.SourceInfos.First(s => s.SourceKind == MediaFrameSourceKind.Color);
            CameraName = group.DisplayName;

            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });

            var source = _capture.FrameSources[info.Id];
            var format = source.SupportedFormats
                .Where(f => f.VideoFormat.Width >= 640 && f.VideoFormat.Width <= _maxWidth)
                .OrderBy(f => _decode ? f.VideoFormat.Width * f.VideoFormat.Height : -(f.VideoFormat.Width * f.VideoFormat.Height))
                .ThenByDescending(f => f.FrameRate.Denominator == 0 ? 0 : f.FrameRate.Numerator / f.FrameRate.Denominator)
                .FirstOrDefault();
            if (format != null)
            {
                await source.SetFormatAsync(format);
            }

            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += OnFrameArrived;
            await _reader.StartAsync();
        }

        private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            using var frame = sender.TryAcquireLatestFrame();
            var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null || _found)
            {
                return;
            }

            using var bgra = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? SoftwareBitmap.Copy(bitmap)
                : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
            var width = bgra.PixelWidth;
            var height = bgra.PixelHeight;
            var pixels = new byte[width * height * 4];
            bgra.CopyToBuffer(pixels.AsBuffer());

            lock (_frameLock)
            {
                _latest = (pixels, width, height);
            }

            Frame?.Invoke(pixels, width, height);

            if (!_decode || Interlocked.CompareExchange(ref _decoding, 1, 0) != 0)
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var text = QrMatrix.DecodeBgra(pixels, width, height);
                    if (text != null && !_found)
                    {
                        Decoded?.Invoke(text);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _decoding, 0);
                }
            });
        }

        public void Pause() => _found = true;

        public void Resume() => _found = false;

        public void Dispose()
        {
            _found = true;
            if (_reader != null)
            {
                _reader.FrameArrived -= OnFrameArrived;
                _ = _reader.StopAsync();
                _reader.Dispose();
                _reader = null;
            }

            _capture?.Dispose();
            _capture = null;
        }
    }
}
