using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GamePatchKit.Core
{
    // The single digest primitive behind fileHash, artifactHash, partHash, dataVersion and manifestHash:
    // SHA-256 rendered as lowercase hex64 (see Hex64). Stream input is consumed in fixed-size blocks and
    // never materialized as a whole, so hashing a package-sized payload costs one buffer regardless of
    // input length (PRD peak-RSS criterion 17).
    public static class Sha256Hash
    {
        private const int StreamBufferSize = 64 * 1024;

        public static string ComputeHex(Stream source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[StreamBufferSize];
                int read;

                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                }

                return ToHex(hash.GetHashAndReset());
            }
        }

        public static string ComputeHex(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(data));
            }
        }

        private static string ToHex(byte[] digest)
        {
            var builder = new StringBuilder(digest.Length * 2);

            foreach (byte value in digest)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
