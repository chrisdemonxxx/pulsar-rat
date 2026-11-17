using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.Cryptography;
using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Pulsar.Common.Tests.Cryptography
{
    [TestClass]
    public class Aes256PinnedVectorTests
    {
        // Pinned 2026-10-10 against Pulsar.Common net48 (AesGcmCng backend).
        // Key derivation contract: PBKDF2-SHA256, 100,000 iterations,
        // salt = SHA256("PULSAR_AES_256_KEY_DERIVE_SALT"), 32-byte key, AES-256-GCM,
        // payload layout [12 nonce][16 tag][ciphertext].
        //
        // If this test fails after a change to Aes256 key derivation (salt, iterations,
        // hash algorithm, key length), the wire keys of every deployed client/server change
        // silently - do NOT "fix" this test by regenerating the vector.
        private const string PinnedPassword = "PulsarPinnedKdfVector#2026!";
        private const string PinnedPlaintext = "PULSAR_PINNED_PLAINTEXT::aes256-gcm-pbkdf2-sha256-100k";
        private const string PinnedCiphertextBase64 =
            "DiHkwnV93+UoyZGtmXsmQ+Tmgk6Qd4+oGaYQZ9Sr3R054cFDmrrVQlNX0U/URx7y4+i85cqT6IRADCkTqTmCtzE7DfHnopQGaxCgOi1GU2TzSg==";

        [TestMethod, TestCategory("Cryptography")]
        public void Decrypt_PinnedCiphertext_ProducesPinnedPlaintext()
        {
            var aes = new Aes256(PinnedPassword);

            var decrypted = aes.Decrypt(Convert.FromBase64String(PinnedCiphertextBase64));

            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(PinnedPlaintext), decrypted);
        }

        [TestMethod, TestCategory("Cryptography")]
        public void KeyDerivation_SaltMatchesDocumentedSha256Constant()
        {
            // The Salt field documents itself as SHA256("PULSAR_AES_256_KEY_DERIVE_SALT") - pin that contract.
            var field = typeof(Aes256).GetField("Salt", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "Aes256.Salt field not found - the KDF layout changed.");

            var salt = (byte[])field.GetValue(null);
            byte[] expected;
            using (var sha = SHA256.Create())
            {
                expected = sha.ComputeHash(Encoding.UTF8.GetBytes("PULSAR_AES_256_KEY_DERIVE_SALT"));
            }

            CollectionAssert.AreEqual(expected, salt);
        }
    }
}
