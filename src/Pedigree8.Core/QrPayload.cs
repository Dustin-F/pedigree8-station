using System;

namespace Pedigree8.Core
{
    // Label payload is "P8:" + 32 uppercase hex digits: all QR "alphanumeric" characters,
    // which gives the smallest, easiest-to-scan code. Old Pedigree8 URLs (.../qr/<uuid>)
    // and bare UUIDs are also accepted.
    public static class QrPayload
    {
        private const string Prefix = "P8:";

        public static string Encode(string qrToken) =>
            Prefix + Guid.Parse(qrToken).ToString("N").ToUpperInvariant();

        public static bool TryDecode(string? text, out string qrToken)
        {
            qrToken = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var candidate = text!.Trim();
            if (candidate.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(Prefix.Length);
            }
            else
            {
                var marker = candidate.LastIndexOf("/qr/", StringComparison.OrdinalIgnoreCase);
                if (marker >= 0)
                {
                    candidate = candidate.Substring(marker + 4);
                    var end = candidate.IndexOfAny(new[] { '/', '?', '#' });
                    if (end >= 0)
                    {
                        candidate = candidate.Substring(0, end);
                    }
                }
            }

            if (!Guid.TryParse(candidate, out var guid))
            {
                return false;
            }

            qrToken = guid.ToString("D");
            return true;
        }
    }
}
