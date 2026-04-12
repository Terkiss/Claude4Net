using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using Claude4Net.SDK;

namespace Claude4Net.MyPlugins
{
    public class SimpleWebSearchTool : ITool
    {
        public string Name => "web_search";
        public string Description => "Search the web to find up-to-date information. Uses DuckDuckGo Lite.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new { query = new { type = "string", description = "The search query." } },
            required = new[] { "query" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string query = dict?["query"] ?? "";

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", query) });
                var response = await client.PostAsync("https://lite.duckduckgo.com/lite/", content);
                string html = await response.Content.ReadAsStringAsync();

                // Very basic stripping
                string text = Regex.Replace(html, "<.*?>", " ");
                text = Regex.Replace(text, @"\s+", " ").Trim();

                // Truncate to save tokens, just get the top chunk
                if (text.Length > 2500) text = text.Substring(0, 2500) + "...";

                return new { status = "Success", results = text };
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class ArchiveTool : ITool
    {
        public string Name => "create_zip_archive";
        public string Description => "Compress a file or directory into a .zip archive.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                sourcePath = new { type = "string", description = "Absolute path to the file or directory to compress." },
                destinationZipPath = new { type = "string", description = "Absolute path for the resulting .zip file." }
            },
            required = new[] { "sourcePath", "destinationZipPath" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string sourcePath = dict?["sourcePath"] ?? "";
            string destinationZipPath = dict?["destinationZipPath"] ?? "";

            try
            {
                if (Directory.Exists(sourcePath))
                {
                    if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);
                    ZipFile.CreateFromDirectory(sourcePath, destinationZipPath, CompressionLevel.Optimal, false);
                }
                else if (File.Exists(sourcePath))
                {
                    if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);
                    using var archive = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);
                    archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath));
                }
                else
                {
                    return new { status = "Error", message = "Source path does not exist." };
                }

                return new { status = "Success", message = $"Successfully created archive at {destinationZipPath}" };
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class FileInspectorTool : ITool
    {
        public string Name => "file_inspector";
        public string Description => "Inspect a file's size and metadata before taking actions like sending over Discord.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                filePath = new { type = "string", description = "Absolute path to the file." }
            },
            required = new[] { "filePath" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string filePath = dict?["filePath"] ?? "";

            try
            {
                if (!File.Exists(filePath)) return new { status = "Error", message = "File not found." };

                var info = new FileInfo(filePath);
                return new
                {
                    status = "Success",
                    fileName = info.Name,
                    sizeBytes = info.Length,
                    sizeMegabytes = Math.Round(info.Length / 1024.0 / 1024.0, 2),
                    creationTime = info.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    extension = info.Extension
                };
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }
}
