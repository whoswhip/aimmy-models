using Newtonsoft.Json.Linq;

class Program
{
    class AttatchmentInfo
    {
        public required string AttatchmentURL { get; set; }
        public string? AuthorID { get; set; }
        public string? Username { get; set; }
        public required DateTime Timestamp { get; set; }
        public required long UNIXTimestamp { get; set; }
        public required string Path { get; set; }
        public required string Hash { get; set; }
    }

    static readonly HttpClient client = new HttpClient();
    static readonly string[] acceptedExtensions = { "onnx", "pt", "cfg" };
    static List<AttatchmentInfo> messages = new List<AttatchmentInfo>();

    static async Task Main(string[] args)
    {
        string? token = null;
        string? guildId = null;
        string? channelId = null;
        bool checkDuplicates = false;
        bool cliOnly = false;
        int offset = 0;


        if (args.Length == 0)
        {
            Console.Write("[?] Discord token: ");
            token = Console.ReadLine()?.Trim();
            Console.Write("[?] Guild ID: ");
            guildId = Console.ReadLine()?.Trim();
            Console.Write("[?] Channel ID: ");
            channelId = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(channelId))
            {
                Console.WriteLine("Usage: --token <token> --guild <guild_id> --channel <channel_id> [--check-duplicates]");
                return;
            }
        }
        else
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string option = args[i].Substring(2);
                    string? value = null;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        value = args[++i];
                    }
                    switch (option)
                    {
                        case "token":
                            token = value;
                            break;
                        case "guild":
                            guildId = value;
                            break;
                        case "channel":
                            channelId = value;
                            break;
                        case "check-duplicates":
                            checkDuplicates = true;
                            break;
                    }
                    cliOnly = true;
                }
            }
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(channelId))
            {
                Console.WriteLine("Usage: --token <token> --guild <guild_id> --channel <channel_id> [--check-duplicates]");
                return;
            }
        }

        foreach (var extension in acceptedExtensions)
        {
            if (!Directory.Exists(extension.TrimStart('.')))
                Directory.CreateDirectory(extension.TrimStart('.'));
        }

        while (true)
        {
            try
            {
                var response = await GetMessagesAsync(token, guildId, channelId, offset);
                if (response == null)
                    continue;

                long totalResults = response["total_results"]?.Value<long>() ?? 0;
                if (totalResults == 0 || offset >= totalResults)
                {
                    Console.WriteLine("[!] No messages found.");
                    break;
                }

                JArray? messages = response["messages"] != null
                    ? JArray.Parse(response["messages"]!.ToString())
                    : null;

                if (messages == null || messages.Count == 0)
                {
                    Console.WriteLine("[!] No messages found in the response.");
                    break;
                }

                await ProccessMessagesAsync(messages);

                offset += messages.Count;
                await Task.Delay(750);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Error: {ex.Message}");
                break;
            }
        }

        if (!cliOnly)
        {
            Console.WriteLine("[*] Finished processing messages.");
            Console.Write("[?] Do you want to check for duplicates? (y/N): ");
            checkDuplicates = Console.ReadLine()?.Trim().ToLowerInvariant() == "y";
        }

        if (checkDuplicates)
        {
            var duplicates = messages.GroupBy(m => m.Hash)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.OrderBy(m => m.Timestamp).Skip(1));
            if (!Directory.Exists("duplicates"))
                Directory.CreateDirectory("duplicates");
            foreach (var duplicate in duplicates)
            {
                Console.WriteLine($"[!] Duplicate found: {duplicate.Path} (Hash: {duplicate.Hash})");
                Console.WriteLine($"    └Original: {messages.First(m => m.Hash == duplicate.Hash).Path} (Hash: {duplicate.Hash})");

                var fullPath = Path.GetFullPath(duplicate.Path);

                File.Move(fullPath, Path.Combine("duplicates", Path.GetFileName(fullPath)), true);
            }
            Console.WriteLine($"[*] Duplicate check completed, moved {duplicates.ToList().Count} duplicate(s).");
        }

    }

    static async Task<JObject?> GetMessagesAsync(string token, string guildId, string channelId, int offset)
    {
        try
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri($"https://discord.com/api/v9/guilds/{guildId}/messages/search?channel_id={channelId}&has=file&offset={offset}"),
                Headers =
                {
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) discord/1.0.9175 Chrome/128.0.6613.186 Electron/32.2.7 Safari/537.36" },
                    { "Authorization", token }
                }
            };

            using (var response = await client.SendAsync(request))
            {
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("[!] Failed to fetch messages. Status code: " + response.StatusCode);
                    return null;
                }
                var content = await response.Content.ReadAsStringAsync();
                return JObject.Parse(content);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Error fetching messages: {ex.Message}");
            return null;
        }
    }

    static async Task ProccessMessagesAsync(JArray messages)
    {
        foreach (var _message in messages)
        {
            var message = JArray.Parse(_message.ToString())[0];
            var attachments = message["attachments"]?.ToObject<JArray>();

            if (attachments == null || attachments.Count == 0)
            {
                Console.WriteLine("[!] No attachments found in the message.");
                continue;
            }

            foreach (var attachment in attachments)
            {
                string? url = attachment["url"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(url))
                {
                    Console.WriteLine("[!] Attachment URL is empty.");
                    continue;
                }
                string? extension = attachment["filename"]?.Value<string>()?.Split('.').LastOrDefault()?.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(extension) || !acceptedExtensions.Contains(extension))
                    continue;

                string? authorId = message["author"]?["id"]?.Value<string>();
                string? username = message["author"]?["username"]?.Value<string>();

                await DownloadAsync(url, extension, attachment, message["timestamp"]?.Value<DateTime>() ?? DateTime.UtcNow, authorId, username);
                await Task.Delay(10);
            }
        }
    }

    static async Task DownloadAsync(string url, string extension, JToken attatchment, DateTime timestamp, string? authorId, string? username)
    {
        try
        {
            using (var response = await client.GetAsync(url))
            {
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[!] Failed to download file from {url}. Status code: {response.StatusCode}");
                    return;
                }

                byte[] file = await response.Content.ReadAsByteArrayAsync();
                string filename = attatchment["filename"]?.Value<string>() ?? $"{GenerateString(8)}.{extension}";
                string filePath = Path.Combine(extension, filename);

                if (File.Exists(filePath))
                {
                    filePath = Path.Combine(extension, $"{filename.Split(".")[0]}_{GenerateString(4)}.{extension}");
                }



                string hash = SHA256(file);
                var attatchmentInfo = new AttatchmentInfo
                {
                    AttatchmentURL = url,
                    AuthorID = authorId,
                    Username = username,
                    Timestamp = timestamp,
                    UNIXTimestamp = ((DateTimeOffset)timestamp).ToUnixTimeSeconds(),
                    Path = filePath,
                    Hash = hash
                };

                messages.Add(attatchmentInfo);
                Console.WriteLine($"[+] Downloaded: {filePath} by {username} created at {timestamp}");

                await File.WriteAllBytesAsync(filePath, file);
                File.SetCreationTimeUtc(filePath, timestamp);
                File.SetLastWriteTimeUtc(filePath, timestamp);
                File.SetLastAccessTimeUtc(filePath, timestamp);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Error downloading file: {ex.Message}");
            return;
        }
    }

    static string GenerateString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
    static string SHA256(byte[] file)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            return BitConverter.ToString(sha256.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
    }
}