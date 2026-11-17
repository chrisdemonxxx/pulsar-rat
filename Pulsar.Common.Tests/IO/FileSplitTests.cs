using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.IO;
using Pulsar.Common.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pulsar.Common.Tests.IO
{
    [TestClass]
    public class FileSplitTests
    {
        private readonly List<string> _tempFiles = new List<string>();

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch
                {
                    // best effort cleanup of temp files
                }
            }
        }

        [TestMethod]
        public void ReadChunks_MultiChunkFile_RoundTripsOriginalBytes()
        {
            var path = NewTempFile();
            // 200,000 bytes = 3 full chunks of 65,535 + one trailing chunk of 3,395.
            var original = CreateDeterministicBytes(200000);
            File.WriteAllBytes(path, original);

            var chunks = new List<FileChunk>();
            long fileSize;
            using (var split = new FileSplit(path, FileAccess.Read))
            {
                Assert.AreEqual(65535, split.MaxChunkSize);
                fileSize = split.FileSize;
                foreach (var chunk in split)
                    chunks.Add(chunk);
            }

            Assert.AreEqual(original.Length, fileSize);
            Assert.AreEqual(4, chunks.Count);

            // Offsets must be sequential multiples of MaxChunkSize.
            for (var i = 0; i < chunks.Count; i++)
                Assert.AreEqual((long)(i * 65535), chunks[i].Offset);

            Assert.AreEqual(65535, chunks[0].Data.Length);
            Assert.AreEqual(65535, chunks[1].Data.Length);
            Assert.AreEqual(65535, chunks[2].Data.Length);
            Assert.AreEqual(200000 - (3 * 65535), chunks[3].Data.Length);

            var reassembled = chunks.SelectMany(c => c.Data).ToArray();
            CollectionAssert.AreEqual(original, reassembled);
        }

        [TestMethod]
        public void ReadChunks_EmptyFile_YieldsSingleEmptyChunk()
        {
            // Documented quirk: the enumerator runs for currentChunk 0..(length/max) inclusive,
            // so an empty file still yields exactly one zero-length chunk.
            var path = NewTempFile();
            File.WriteAllBytes(path, new byte[0]);

            var chunks = new List<FileChunk>();
            using (var split = new FileSplit(path, FileAccess.Read))
            {
                Assert.AreEqual(0L, split.FileSize);
                foreach (var chunk in split)
                    chunks.Add(chunk);
            }

            Assert.AreEqual(1, chunks.Count);
            Assert.AreEqual(0L, chunks[0].Offset);
            Assert.AreEqual(0, chunks[0].Data.Length);
        }

        [TestMethod]
        public void ReadChunks_ExactMultipleOfChunkSize_YieldsTrailingEmptyChunk()
        {
            // Same inclusive-loop quirk: a file of exactly MaxChunkSize yields a second, empty chunk.
            var path = NewTempFile();
            var original = CreateDeterministicBytes(65535);
            File.WriteAllBytes(path, original);

            var chunks = new List<FileChunk>();
            using (var split = new FileSplit(path, FileAccess.Read))
            {
                foreach (var chunk in split)
                    chunks.Add(chunk);
            }

            Assert.AreEqual(2, chunks.Count);
            Assert.AreEqual(65535, chunks[0].Data.Length);
            CollectionAssert.AreEqual(original, chunks[0].Data);
            Assert.AreEqual(65535L, chunks[1].Offset);
            Assert.AreEqual(0, chunks[1].Data.Length);
        }

        [TestMethod]
        public void WriteChunks_OutOfOrder_ProducesOriginalFile()
        {
            var path = NewTempFile();
            var original = CreateDeterministicBytes(150000); // 2 full chunks + remainder
            File.WriteAllBytes(path, new byte[0]);

            using (var split = new FileSplit(path, FileAccess.Write))
            {
                // Write last chunk first to prove offset-based seeking works.
                for (var chunkIndex = 2; chunkIndex >= 0; chunkIndex--)
                {
                    var offset = chunkIndex * 65535;
                    var length = Math.Min(65535, original.Length - offset);
                    var data = new byte[length];
                    Buffer.BlockCopy(original, offset, data, 0, length);
                    split.WriteChunk(new FileChunk { Offset = offset, Data = data });
                }
            }

            var written = File.ReadAllBytes(path);
            CollectionAssert.AreEqual(original, written);
        }

        [TestMethod]
        public void Constructor_InvalidFileAccess_ThrowsArgumentException()
        {
            Assert.ThrowsException<ArgumentException>(
                () => { using (var split = new FileSplit(NewTempFile(), FileAccess.ReadWrite)) { } });
        }

        private string NewTempFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "pulsar_filesplit_" + Guid.NewGuid().ToString("N") + ".bin");
            _tempFiles.Add(path);
            return path;
        }

        private static byte[] CreateDeterministicBytes(int length)
        {
            var data = new byte[length];
            for (var i = 0; i < data.Length; i++)
                data[i] = (byte)((i * 31 + 7) % 251);
            return data;
        }
    }
}
