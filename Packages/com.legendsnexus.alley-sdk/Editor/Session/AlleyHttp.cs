using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    public class AlleyApiException : Exception
    {
        public int Status { get; }
        public string[] Details { get; }

        public AlleyApiException(int status, string message, string[] details = null) : base(message)
        {
            Status = status;
            Details = details ?? Array.Empty<string>();
        }
    }

    internal static class AlleyHttp
    {
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.Add("User-Agent", $"LegendsAlleySDK/{AlleyConfig.SdkVersion}");
            return client;
        }

        public static async Task<T> GetJson<T>(string path, string token = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, AlleyConfig.ApiBase + path);
            Authorize(request, token);
            return await Send<T>(request);
        }

        public static async Task<T> PostJson<T>(string path, object body, string token = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, AlleyConfig.ApiBase + path);
            Authorize(request, token);
            if (body != null)
            {
                request.Content = new StringContent(JsonUtility.ToJson(body), Encoding.UTF8, "application/json");
            }
            return await Send<T>(request);
        }

        public static async Task<ChunkResponse> PutChunk(string path, byte[] chunk, string sha256, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, AlleyConfig.ApiBase + path);
            Authorize(request, token);
            request.Content = new ByteArrayContent(chunk);
            request.Content.Headers.Add("Content-Type", "application/octet-stream");
            request.Headers.Add("x-chunk-sha256", sha256);
            return await Send<ChunkResponse>(request);
        }

        public static async Task<T> PutBytes<T>(string path, byte[] bytes, string contentType, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, AlleyConfig.ApiBase + path);
            Authorize(request, token);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.Add("Content-Type", contentType);
            return await Send<T>(request);
        }

        public static async Task<T> PatchJson<T>(string path, object body, string token)
        {
            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), AlleyConfig.ApiBase + path);
            Authorize(request, token);
            request.Content = new StringContent(JsonUtility.ToJson(body), Encoding.UTF8, "application/json");
            return await Send<T>(request);
        }

        public static async Task<byte[]> GetBytes(string path, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, AlleyConfig.ApiBase + path);
            Authorize(request, token);
            HttpResponseMessage response;
            try
            {
                response = await Client.SendAsync(request);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                throw new AlleyApiException(0, "Could not reach the Legends Alley server. Check your connection and try again.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new AlleyApiException((int)response.StatusCode, $"Download failed ({(int)response.StatusCode}).");
            }
            return await response.Content.ReadAsByteArrayAsync();
        }

        private static void Authorize(HttpRequestMessage request, string token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Add("Authorization", "Bearer " + token);
            }
        }

        private static async Task<T> Send<T>(HttpRequestMessage request)
        {
            HttpResponseMessage response;
            try
            {
                response = await Client.SendAsync(request);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                throw new AlleyApiException(0, "Could not reach the Legends Alley server. Check your connection and try again.");
            }

            string bodyText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                string message = $"Request failed ({(int)response.StatusCode}).";
                string[] details = null;
                try
                {
                    ApiError parsed = JsonUtility.FromJson<ApiError>(bodyText);
                    if (parsed != null && !string.IsNullOrEmpty(parsed.error)) message = parsed.error;
                    details = parsed?.details;
                }
                catch
                {
                    // non json error body, keep the generic message
                }
                throw new AlleyApiException((int)response.StatusCode, message, details);
            }

            return JsonUtility.FromJson<T>(bodyText);
        }
    }
}
