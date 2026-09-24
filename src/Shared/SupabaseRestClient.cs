using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChromaticMenu.Shared
{
    public sealed class SupabaseResult
    {
        public bool Success { get; set; }

        // 0 when no HTTP response was received (offline, DNS, timeout).
        public int StatusCode { get; set; }

        public string Error { get; set; }

        public DateTimeOffset? ServerDate { get; set; }

        // A 4xx that retrying cannot fix (bad data). Auth, missing table and
        // rate-limit errors are excluded because fixing the setup makes them succeed.
        public bool IsPermanentDataError =>
            StatusCode >= 400 && StatusCode < 500 &&
            StatusCode != 401 && StatusCode != 403 && StatusCode != 404 &&
            StatusCode != 408 && StatusCode != 429;
    }

    // Minimal PostgREST client: plain HttpClient, no Supabase SDK.
    public sealed class SupabaseRestClient : IDisposable
    {
        private readonly HttpClient _http;

        static SupabaseRestClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public SupabaseRestClient()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public Task<SupabaseResult> InsertAsync(string baseUrl, string anonKey, string table, string jsonBody)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rest/v1/{table}")
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            AddAuthHeaders(request, anonKey);
            // The anon role cannot SELECT, so asking PostgREST to return the rows would fail.
            request.Headers.Add("Prefer", "return=minimal");
            return SendAsync(request);
        }

        public Task<SupabaseResult> CheckConnectionAsync(string baseUrl, string anonKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/auth/v1/settings");
            request.Headers.Add("apikey", anonKey);
            return SendAsync(request);
        }

        private static void AddAuthHeaders(HttpRequestMessage request, string anonKey)
        {
            request.Headers.Add("apikey", anonKey);
            request.Headers.Add("Authorization", "Bearer " + anonKey);
        }

        private async Task<SupabaseResult> SendAsync(HttpRequestMessage request)
        {
            var result = new SupabaseResult();
            try
            {
                using (request)
                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    result.StatusCode = (int)response.StatusCode;
                    result.ServerDate = response.Headers.Date;
                    result.Success = response.IsSuccessStatusCode;
                    if (!result.Success)
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        result.Error = ExtractMessage(body) ?? $"HTTP {result.StatusCode} {response.ReasonPhrase}";
                    }
                }
            }
            catch (TaskCanceledException)
            {
                result.Error = "The request timed out.";
            }
            catch (Exception ex)
            {
                result.Error = (ex.InnerException ?? ex).Message;
            }
            return result;
        }

        private static string ExtractMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                var json = JObject.Parse(body);
                return (json["message"] ?? json["msg"] ?? json["error_description"] ?? json["error"])?.ToString();
            }
            catch
            {
                return body.Length > 200 ? body.Substring(0, 200) : body;
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
