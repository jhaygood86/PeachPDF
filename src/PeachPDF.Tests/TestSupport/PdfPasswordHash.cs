using System;
using System.Security.Cryptography;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// The hash of ISO 32000-2, 7.6.4.3.4 ("Algorithm 2.B"), written from the specification so a test can unlock a document without calling
    /// back into the writer that produced it.
    /// </summary>
    internal static class PdfPasswordHash
    {
        public static byte[] Hash(byte[] password, byte[] salt, byte[] userKey)
        {
            var k = SHA256.HashData([.. password, .. salt, .. userKey]);

            for (var round = 0; ; round++)
            {
                var unit = new byte[password.Length + k.Length + userKey.Length];
                password.CopyTo(unit, 0);
                k.CopyTo(unit, password.Length);
                userKey.CopyTo(unit, password.Length + k.Length);

                var k1 = new byte[unit.Length * 64];
                for (var i = 0; i < 64; i++)
                    unit.CopyTo(k1, i * unit.Length);

                using var aes = Aes.Create();
                aes.Key = k[..16];
                var encrypted = aes.EncryptCbc(k1, k[16..32], PaddingMode.None);

                var remainder = 0;
                for (var i = 0; i < 16; i++)
                    remainder += encrypted[i];

                k = (remainder % 3) switch
                {
                    0 => SHA256.HashData(encrypted),
                    1 => SHA384.HashData(encrypted),
                    _ => SHA512.HashData(encrypted),
                };

                if (round >= 63 && encrypted[^1] <= round - 32)
                    break;
            }

            return k[..32];
        }
    }
}
