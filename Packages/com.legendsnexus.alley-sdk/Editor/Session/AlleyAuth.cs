using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // discord sign in: we listen on a loopback port, send the browser to the backend,
    // and it bounces back here with a one time grant we trade for the real token
    internal static class AlleyAuth
    {
        private const int TimeoutSeconds = 180;

        public static async Task<ExchangeResponse> SignIn(CancellationToken cancel)
        {
            string verifier = RandomUrlSafe(48);
            string challenge = Sha256UrlSafe(verifier);

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            try
            {
                Application.OpenURL($"{AlleyConfig.ApiBase}/api/auth/sdk/start?port={port}&challenge={challenge}");

                string grant = await WaitForGrant(listener, cancel);
                return await AlleyHttp.PostJson<ExchangeResponse>(
                    "/api/auth/sdk/exchange",
                    new ExchangeRequest { grant = grant, verifier = verifier });
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<string> WaitForGrant(TcpListener listener, CancellationToken cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancel);
            linked.Token.Register(() => { try { listener.Stop(); } catch { } });

            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    if (cancel.IsCancellationRequested) throw new OperationCanceledException();
                    throw new AlleyApiException(0, "Sign in timed out. Try again.");
                }

                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    string requestLine = await ReadRequestLine(stream);
                    string target = ParseTarget(requestLine);
                    if (target == null || !target.StartsWith("/callback"))
                    {
                        await Respond(stream, "Legends Alley", "Nothing to see here.");
                        continue;
                    }

                    string grant = QueryValue(target, "grant");
                    string error = QueryValue(target, "error");

                    if (!string.IsNullOrEmpty(error))
                    {
                        await Respond(stream, "Sign in failed", error);
                        throw new AlleyApiException(401, error);
                    }
                    if (string.IsNullOrEmpty(grant))
                    {
                        await Respond(stream, "Sign in failed", "The sign in response was missing its grant. Try again.");
                        throw new AlleyApiException(400, "Sign in response was malformed. Try again.");
                    }

                    await Respond(stream, "You are signed in", "You can close this tab and head back to Unity.");
                    return grant;
                }
            }
        }

        private static async Task<string> ReadRequestLine(NetworkStream stream)
        {
            var buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0) return "";
            string text = Encoding.ASCII.GetString(buffer, 0, read);
            int lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
            return lineEnd > 0 ? text.Substring(0, lineEnd) : text;
        }

        private static string ParseTarget(string requestLine)
        {
            string[] parts = requestLine.Split(' ');
            if (parts.Length < 2 || parts[0] != "GET") return null;
            return parts[1];
        }

        private static string QueryValue(string target, string key)
        {
            int queryStart = target.IndexOf('?');
            if (queryStart < 0) return null;
            foreach (string pair in target.Substring(queryStart + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (pair.Substring(0, eq) == key)
                {
                    return Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                }
            }
            return null;
        }

        private static async Task Respond(Stream stream, string title, string message)
        {
            string html =
                "<!doctype html><html><head><title>" + WebUtility.HtmlEncode(title) + "</title></head>" +
                "<body style=\"background:#0A0A0A;color:#fff;font-family:sans-serif;display:grid;place-items:center;height:100vh;margin:0\">" +
                "<div style=\"text-align:center;padding:2rem;border:1px solid rgba(138,43,226,.35);border-radius:1rem;background:#181B1F\">" +
                "<h2 style=\"color:#805AD5\">" + WebUtility.HtmlEncode(title) + "</h2>" +
                "<p>" + WebUtility.HtmlEncode(message) + "</p></div></body></html>";
            byte[] body = Encoding.UTF8.GetBytes(html);
            string header =
                "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(body, 0, body.Length);
            await stream.FlushAsync();
        }

        private static string RandomUrlSafe(int bytes)
        {
            var data = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(data);
            return Base64Url(data);
        }

        private static string Sha256UrlSafe(string value)
        {
            using var sha = SHA256.Create();
            return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(value)));
        }

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        [Serializable]
        private class ExchangeRequest
        {
            public string grant;
            public string verifier;
        }
    }
}
