using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.Cryptography;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Pulsar.Common.Tests.Cryptography
{
    [TestClass]
    public class Aes256TamperTests
    {
        // Layout of the encrypted payload: [12 nonce][16 tag][ciphertext].
        private const int NonceLength = 12;
        private const int TagLength = 16;

        private const string Password = "tamper-test-password";
        private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("authenticated encryption must reject every modification");

        /// <summary>
        /// Asserts the action throws a CryptographicException. On .NET 9, AesGcm may throw the
        /// derived AuthenticationTagMismatchException; both prove tamper-rejection, so any
        /// exception assignable to CryptographicException is accepted (net48 client + net9 server
        /// share these assertions).
        /// </summary>
        private static void AssertThrowsCryptographicException(Action action)
        {
            try
            {
                action();
            }
            catch (CryptographicException)
            {
                return; // includes the derived AuthenticationTagMismatchException on net9
            }
            catch (Exception ex)
            {
                Assert.Fail($"Expected CryptographicException, got {ex.GetType().Name}: {ex.Message}");
            }

            Assert.Fail("Expected CryptographicException, but no exception was thrown");
        }

        [TestMethod, TestCategory("Cryptography")]
        public void Decrypt_TamperedCiphertextByte_Throws()
        {
            var aes = new Aes256(Password);
            var encrypted = aes.Encrypt(Plaintext);

            var tampered = (byte[])encrypted.Clone();
            tampered[tampered.Length - 1] ^= 0xFF; // flip one bit in the last ciphertext byte

            AssertThrowsCryptographicException(() => { aes.Decrypt(tampered); });
        }

        [TestMethod, TestCategory("Cryptography")]
        public void Decrypt_TamperedTagByte_Throws()
        {
            var aes = new Aes256(Password);
            var encrypted = aes.Encrypt(Plaintext);

            var tampered = (byte[])encrypted.Clone();
            tampered[NonceLength] ^= 0x01; // flip one bit in the first tag byte

            AssertThrowsCryptographicException(() => { aes.Decrypt(tampered); });
        }

        [TestMethod, TestCategory("Cryptography")]
        public void Decrypt_TamperedNonce_Throws()
        {
            var aes = new Aes256(Password);
            var encrypted = aes.Encrypt(Plaintext);

            var tampered = (byte[])encrypted.Clone();
            tampered[0] ^= 0xFF; // flip one bit in the nonce - GCM tag verification must fail

            AssertThrowsCryptographicException(() => { aes.Decrypt(tampered); });
        }

        [TestMethod, TestCategory("Cryptography")]
        public void Decrypt_TruncatedCiphertext_Throws()
        {
            var aes = new Aes256(Password);
            var encrypted = aes.Encrypt(Plaintext);

            var truncated = new byte[encrypted.Length - 1];
            System.Buffer.BlockCopy(encrypted, 0, truncated, 0, truncated.Length);

            AssertThrowsCryptographicException(() => { aes.Decrypt(truncated); });
        }

        [TestMethod, TestCategory("Cryptography")]
        public void Decrypt_InputShorterThanNoncePlusTag_Throws()
        {
            var aes = new Aes256(Password);
            var tooShort = new byte[NonceLength + TagLength - 1];

            AssertThrowsCryptographicException(() => { aes.Decrypt(tooShort); });
        }

        [TestMethod, TestCategory("Cryptography")]
        public void Decrypt_WrongPassword_Throws()
        {
            var aes = new Aes256(Password);
            var encrypted = aes.Encrypt(Plaintext);

            var otherKey = new Aes256(Password + "-wrong");

            // A wrong key is a tag mismatch - must throw, never return garbage plaintext.
            AssertThrowsCryptographicException(() => { otherKey.Decrypt(encrypted); });
        }
    }
}
