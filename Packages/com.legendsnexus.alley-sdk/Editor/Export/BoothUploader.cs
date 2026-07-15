using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace LegendsNexus.Alley.Editor
{
    // pushes the zip up in hash checked chunks so big booths survive flaky
    // connections and proxy body size caps
    internal static class BoothUploader
    {
        public static async Task<AcceptedBooth> Upload(string zipPath, string eventId, Action<float, string> progress)
        {
            byte[] fileBytes = File.ReadAllBytes(zipPath);
            string fileSha = Sha256Hex(fileBytes);

            progress?.Invoke(0f, "Starting upload...");
            var init = await AlleyHttp.PostJson<UploadInitResponse>("/api/uploads/init", new InitRequest
            {
                eventId = eventId,
                totalSize = fileBytes.Length,
                sha256 = fileSha,
            }, AlleySession.Token);

            for (int index = 0; index < init.chunkCount; index++)
            {
                int offset = index * init.chunkSize;
                int length = Math.Min(init.chunkSize, fileBytes.Length - offset);
                var chunk = new byte[length];
                Buffer.BlockCopy(fileBytes, offset, chunk, 0, length);

                await SendChunkWithRetry(init.uploadId, index, chunk);
                progress?.Invoke(
                    (index + 1f) / init.chunkCount * 0.95f,
                    $"Uploading chunk {index + 1}/{init.chunkCount}...");
            }

            progress?.Invoke(0.97f, "Waiting for the server checks...");
            var complete = await AlleyHttp.PostJson<CompleteResponse>(
                $"/api/uploads/{init.uploadId}/complete", null, AlleySession.Token);

            progress?.Invoke(1f, "Done");
            return complete.booth;
        }

        private static async Task SendChunkWithRetry(string uploadId, int index, byte[] chunk)
        {
            string chunkSha = Sha256Hex(chunk);
            const int attempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await AlleyHttp.PutChunk($"/api/uploads/{uploadId}/chunks/{index}", chunk, chunkSha, AlleySession.Token);
                    return;
                }
                catch (AlleyApiException e) when (attempt < attempts && (e.Status == 0 || e.Status >= 500))
                {
                    await Task.Delay(1000 * attempt);
                }
            }
        }

        private static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            var builder = new System.Text.StringBuilder(hash.Length * 2);
            foreach (byte b in hash) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }

        [Serializable]
        private class InitRequest
        {
            public string eventId;
            public int totalSize;
            public string sha256;
        }
    }
}
