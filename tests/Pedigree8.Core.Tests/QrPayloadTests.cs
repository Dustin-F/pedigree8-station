using Pedigree8.Core;
using Xunit;

namespace Pedigree8.Core.Tests
{
    public sealed class QrPayloadTests
    {
        private const string Token = "3f2a9c1e-7b4d-4e8a-9f10-2c3d4e5f6a7b";

        [Fact]
        public void Encode_is_compact_alphanumeric()
        {
            Assert.Equal("P8:3F2A9C1E7B4D4E8A9F102C3D4E5F6A7B", QrPayload.Encode(Token));
        }

        [Theory]
        [InlineData("P8:3F2A9C1E7B4D4E8A9F102C3D4E5F6A7B")]
        [InlineData("  p8:3f2a9c1e7b4d4e8a9f102c3d4e5f6a7b \r\n")]
        [InlineData("http://localhost:5173/qr/3f2a9c1e-7b4d-4e8a-9f10-2c3d4e5f6a7b")]
        [InlineData("https://lab.example/qr/3F2A9C1E-7B4D-4E8A-9F10-2C3D4E5F6A7B?src=label")]
        [InlineData("3f2a9c1e-7b4d-4e8a-9f10-2c3d4e5f6a7b")]
        public void Decode_accepts_station_legacy_and_bare_formats(string scanned)
        {
            Assert.True(QrPayload.TryDecode(scanned, out var token));
            Assert.Equal(Token, token);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("YI-04-ENC")]
        [InlineData("P8:not-a-token")]
        public void Decode_rejects_other_text(string? scanned)
        {
            Assert.False(QrPayload.TryDecode(scanned, out _));
        }
    }
}
