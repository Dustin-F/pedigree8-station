using System.Collections.Generic;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace Pedigree8.Core
{
    public static class QrMatrix
    {
        // Dark modules of the QR symbol, without the quiet zone. [x, y]
        public static bool[,] Create(string text)
        {
            // Level Q (25% recovery) copes better with glare and curved plastic than the default M.
            var code = Encoder.encode(text, ErrorCorrectionLevel.Q, new Dictionary<EncodeHintType, object>());
            var matrix = code.Matrix;
            var result = new bool[matrix.Width, matrix.Height];
            for (var y = 0; y < matrix.Height; y++)
            {
                for (var x = 0; x < matrix.Width; x++)
                {
                    result[x, y] = matrix[x, y] == 1;
                }
            }

            return result;
        }

        public static string? DecodeBgra(byte[] bgra, int width, int height)
        {
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = false,
                Options = new DecodingOptions
                {
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE },
                    TryHarder = false,
                },
            };
            return reader.Decode(new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32))?.Text;
        }
    }
}
