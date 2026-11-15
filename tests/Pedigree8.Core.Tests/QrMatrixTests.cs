using System;
using Pedigree8.Core;
using Xunit;

namespace Pedigree8.Core.Tests
{
    public sealed class QrMatrixTests
    {
        [Fact]
        public void Station_label_is_a_small_qr_code()
        {
            var matrix = QrMatrix.Create(QrPayload.Encode(Guid.NewGuid().ToString()));

            Assert.Equal(29, matrix.GetLength(0));
        }

        [Fact]
        public void Generated_label_decodes_back_to_the_same_token()
        {
            var token = Guid.NewGuid().ToString();
            var payload = QrPayload.Encode(token);
            var (bgra, size) = Render(QrMatrix.Create(payload), moduleSize: 6);

            var decoded = QrMatrix.DecodeBgra(bgra, size, size);

            Assert.Equal(payload, decoded);
            Assert.True(QrPayload.TryDecode(decoded, out var back));
            Assert.Equal(token, back);
        }

        private static (byte[] Bgra, int Size) Render(bool[,] matrix, int moduleSize)
        {
            const int quiet = 4;
            var modules = matrix.GetLength(0);
            var size = (modules + 2 * quiet) * moduleSize;
            var bgra = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var mx = x / moduleSize - quiet;
                    var my = y / moduleSize - quiet;
                    var dark = mx >= 0 && my >= 0 && mx < modules && my < modules && matrix[mx, my];
                    var i = (y * size + x) * 4;
                    var v = dark ? (byte)0 : (byte)255;
                    bgra[i] = bgra[i + 1] = bgra[i + 2] = v;
                    bgra[i + 3] = 255;
                }
            }

            return (bgra, size);
        }
    }
}
