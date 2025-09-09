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
        public required string OriginalFilename { get; set; }
        public required string Hash { get; set; }
    }

    static readonly HttpClient client = new HttpClient();
    static readonly string[] acceptedExtensions = { "onnx", "pt", "cfg" };
    static List<AttatchmentInfo> messages = new List<AttatchmentInfo>();
    static Dictionary<string, string> knownOriginals = new Dictionary<string, string>
    {
        { "8C33ECC90221267FCD6FB7DF7295841F4BFB061F3E3208344AB7E80C998AD40B", "Themida Arsenal (4k).onnx" },
        { "6E814BA61CE5A8CDBEBEB95F28B98DE562761A2F24A559A7A4EA417DDEB0A4E6", "Universal Hamsta v3.onnx"}
    };

    static async Task Main(string[] args)
    {
        string? token = null;
        string? guildId = null;
        string? channelId = null;
        bool checkDuplicates = false;
        bool skipDownload = false;
        bool cliOnly = false;
        int offset = 0;

        DotEnv.Load(".env");
        token ??= Environment.GetEnvironmentVariable("DISCORD_TOKEN");
        guildId ??= Environment.GetEnvironmentVariable("DISCORD_GUILD_ID");
        channelId ??= Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
        checkDuplicates = (Environment.GetEnvironmentVariable("CHECK_DUPLICATES") ?? "false").ToLowerInvariant() == "true";
        skipDownload = (Environment.GetEnvironmentVariable("SKIP_DOWNLOAD") ?? "false").ToLowerInvariant() == "true";
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(guildId) && !string.IsNullOrWhiteSpace(channelId))
            cliOnly = true;


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
                Console.WriteLine("Usage: --token <token> --guild <guild_id> --channel <channel_id> [--check-duplicates] [--skip-download]");
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
                        case "skip-download":
                            skipDownload = true;
                            break;
                    }
                    cliOnly = true;
                }
            }
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(channelId))
            {
                Console.WriteLine("Usage: --token <token> --guild <guild_id> --channel <channel_id> [--check-duplicates] [--skip-download]");
                return;
            }
        }

        foreach (var extension in acceptedExtensions)
        {
            if (!Directory.Exists(extension.TrimStart('.')))
                Directory.CreateDirectory(extension.TrimStart('.'));
        }

        if (!skipDownload)
        {
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
        }
        else
        {
            Console.WriteLine("[*] Skipping download phase...");
            LoadExistingFiles();
        }

        if (!cliOnly && !skipDownload)
        {
            Console.WriteLine("[*] Finished processing messages.");
            Console.Write("[?] Do you want to check for duplicates? (y/N): ");
            checkDuplicates = Console.ReadLine()?.Trim().ToLowerInvariant() == "y";
        }

        if (checkDuplicates || skipDownload)
        {
            CheckDuplicates();
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
                string filename = !string.IsNullOrWhiteSpace(attatchment["title"]?.Value<string>()) ? $"{attatchment["title"]?.Value<string>()}.{extension}" : attatchment["filename"]?.Value<string>() ?? $"{GenerateString(8)}.{extension}";
                string filePath = Path.Combine(extension, filename);
                string hash = SHA256(file);

                if (File.Exists(filePath))
                {
                    DateTime existingFileTime = File.GetLastWriteTimeUtc(filePath);
                    string existingFileHash = SHA256(await File.ReadAllBytesAsync(filePath));

                    if (existingFileTime <= timestamp && existingFileHash == hash)
                    {
                        string newerDir = Path.Combine(extension, "newer");
                        if (!Directory.Exists(newerDir))
                            Directory.CreateDirectory(newerDir);

                        string newFilePath = Path.Combine(newerDir, filename);

                        int counter = 1;
                        string originalNewFilePath = newFilePath;
                        while (File.Exists(newFilePath))
                        {
                            string nameWithoutExt = Path.GetFileNameWithoutExtension(originalNewFilePath);
                            string ext = Path.GetExtension(originalNewFilePath);
                            newFilePath = Path.Combine(newerDir, $"{nameWithoutExt}_{counter}{ext}");
                            counter++;
                        }

                        Console.WriteLine($"[+] Keeping older file: {filePath} (from {existingFileTime})");
                        Console.WriteLine($"[+] Saving newer file to: {newFilePath} (from {timestamp})");

                        await File.WriteAllBytesAsync(newFilePath, file);
                        File.SetCreationTimeUtc(newFilePath, timestamp);
                        File.SetLastWriteTimeUtc(newFilePath, timestamp);
                        File.SetLastAccessTimeUtc(newFilePath, timestamp);
                        return;
                    }
                    else
                    {
                        string newerDir = Path.Combine(extension, "newer");
                        if (!Directory.Exists(newerDir))
                            Directory.CreateDirectory(newerDir);

                        string newerFilePath = Path.Combine(newerDir, filename);

                        int counter = 1;
                        string originalNewerFilePath = newerFilePath;
                        while (File.Exists(newerFilePath))
                        {
                            string nameWithoutExt = Path.GetFileNameWithoutExtension(originalNewerFilePath);
                            string ext = Path.GetExtension(originalNewerFilePath);
                            newerFilePath = Path.Combine(newerDir, $"{nameWithoutExt}_{counter}{ext}");
                            counter++;
                        }

                        File.Move(filePath, newerFilePath);
                        Console.WriteLine($"[+] Moved newer file to: {newerFilePath} (from {existingFileTime})");
                        Console.WriteLine($"[+] Keeping older file: {filePath} (from {timestamp})");
                    }
                }

                var attatchmentInfo = new AttatchmentInfo
                {
                    AttatchmentURL = url,
                    AuthorID = authorId,
                    Username = username,
                    Timestamp = timestamp,
                    UNIXTimestamp = ((DateTimeOffset)timestamp).ToUnixTimeSeconds(),
                    OriginalFilename = !string.IsNullOrWhiteSpace(attatchment["title"]?.Value<string>())
                                        ? $"{attatchment["title"]?.Value<string>()}.{extension}"
                                        : attatchment["filename"]?.Value<string>() ?? $"{GenerateString(8)}.{extension}",
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
    static void CheckDuplicates()
    {
        Console.WriteLine("[*] Checking for duplicate files...");

        string duplicatesDir = "duplicates";
        if (!Directory.Exists(duplicatesDir))
            Directory.CreateDirectory(duplicatesDir);

        var duplicateGroups = messages
            .GroupBy(m => m.Hash)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicateGroups.Count == 0)
        {
            Console.WriteLine("[*] No duplicates found.");
            return;
        }

        Console.WriteLine($"[*] Found {duplicateGroups.Count} groups of duplicate files.");

        foreach (var group in duplicateGroups)
        {
            var sortedFiles = group.OrderBy(f => f.Timestamp).ToList();

            string? knownOriginalName = null;
            string hashUpper = group.Key.ToUpperInvariant();
            if (knownOriginals.ContainsKey(hashUpper))
            {
                knownOriginalName = knownOriginals[hashUpper];
                Console.WriteLine($"[*] Found known original for hash {group.Key.Substring(0, 8)}: {knownOriginalName}");
            }

            AttatchmentInfo? keepFile = null;
            var remainingFiles = new List<AttatchmentInfo>();

            if (!string.IsNullOrEmpty(knownOriginalName))
            {
                foreach (var file in sortedFiles)
                {
                    string actualPath = FindActualFilePath(file.Path);
                    if (!string.IsNullOrEmpty(actualPath))
                    {
                        string currentFileName = Path.GetFileName(actualPath);
                        if (currentFileName.Equals(knownOriginalName, StringComparison.OrdinalIgnoreCase))
                        {
                            keepFile = new AttatchmentInfo
                            {
                                AttatchmentURL = file.AttatchmentURL,
                                AuthorID = file.AuthorID,
                                Username = file.Username,
                                Timestamp = file.Timestamp,
                                UNIXTimestamp = file.UNIXTimestamp,
                                Path = actualPath,
                                OriginalFilename = file.OriginalFilename,
                                Hash = file.Hash
                            };
                            Console.WriteLine($"    Found file with known original name: {actualPath}");
                            break;
                        }
                    }
                }
            }

            if (keepFile == null)
            {
                foreach (var file in sortedFiles)
                {
                    string actualPath = FindActualFilePath(file.Path);
                    if (!string.IsNullOrEmpty(actualPath))
                    {
                        keepFile = new AttatchmentInfo
                        {
                            AttatchmentURL = file.AttatchmentURL,
                            AuthorID = file.AuthorID,
                            Username = file.Username,
                            Timestamp = file.Timestamp,
                            UNIXTimestamp = file.UNIXTimestamp,
                            Path = actualPath,
                            OriginalFilename = file.OriginalFilename,
                            Hash = file.Hash
                        };

                        if (!string.IsNullOrEmpty(knownOriginalName))
                        {
                            try
                            {
                                string extension = Path.GetExtension(knownOriginalName);
                                if (string.IsNullOrEmpty(extension))
                                {
                                    extension = Path.GetExtension(actualPath);
                                    knownOriginalName += extension;
                                }

                                string directory = Path.GetDirectoryName(actualPath) ?? "";
                                string newPath = Path.Combine(directory, knownOriginalName);

                                if (newPath != actualPath && !File.Exists(newPath))
                                {
                                    File.Move(actualPath, newPath);
                                    keepFile.Path = newPath;
                                    Console.WriteLine($"    Renamed to known original: {actualPath} -> {newPath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"    [!] Error renaming to known original: {ex.Message}");
                            }
                        }
                        break;
                    }
                }
            }

            foreach (var file in sortedFiles)
            {
                string actualPath = FindActualFilePath(file.Path);
                if (!string.IsNullOrEmpty(actualPath) && (keepFile == null || actualPath != keepFile.Path))
                {
                    file.Path = actualPath;
                    remainingFiles.Add(file);
                }
            }

            Console.WriteLine($"[*] Processing duplicates for hash: {group.Key.Substring(0, 8)}...");

            if (keepFile == null)
            {
                Console.WriteLine($"    [!] No files found for this hash group - all may have been moved already");
                continue;
            }

            Console.WriteLine($"    Keeping: {keepFile.Path} (from {keepFile.Timestamp})");

            var duplicatesByPath = remainingFiles
                .Where(f => FindActualFilePath(f.Path) != keepFile.Path)
                .GroupBy(d => FindActualFilePath(d.Path))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToList();

            foreach (var pathGroup in duplicatesByPath)
            {
                var duplicate = pathGroup.First();
                string actualPath = pathGroup.Key;

                try
                {
                    string filename = Path.GetFileName(actualPath);
                    string extension = Path.GetExtension(filename).TrimStart('.');

                    string duplicateExtensionDir = Path.Combine(duplicatesDir, extension);
                    if (!Directory.Exists(duplicateExtensionDir))
                        Directory.CreateDirectory(duplicateExtensionDir);

                    string duplicatePath = Path.Combine(duplicateExtensionDir, filename);

                    int counter = 1;
                    string originalDuplicatePath = duplicatePath;
                    while (File.Exists(duplicatePath))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(originalDuplicatePath);
                        string ext = Path.GetExtension(originalDuplicatePath);
                        duplicatePath = Path.Combine(duplicateExtensionDir, $"{nameWithoutExt}_{counter}{ext}");
                        counter++;
                    }

                    File.Move(actualPath, duplicatePath);
                    Console.WriteLine($"        Moved duplicate: {actualPath} -> {duplicatePath} (from {duplicate.Timestamp})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    [!] Error moving duplicate {actualPath}: {ex.Message}");
                }
            }
        }

        Console.WriteLine("[*] Duplicate checking completed.");
    }

    static void LoadExistingFiles()
    {
        Console.WriteLine("[*] Loading existing files for duplicate checking...");

        foreach (var extension in acceptedExtensions)
        {
            string extensionDir = extension.TrimStart('.');
            if (Directory.Exists(extensionDir))
            {
                LoadFilesFromDirectory(extensionDir, extension);

                string newerDir = Path.Combine(extensionDir, "newer");
                if (Directory.Exists(newerDir))
                {
                    LoadFilesFromDirectory(newerDir, extension);
                }
            }
        }

        Console.WriteLine($"[*] Loaded {messages.Count} files for processing.");
    }

    static void LoadFilesFromDirectory(string directory, string extension)
    {
        string[] files = Directory.GetFiles(directory, $"*.{extension}");

        foreach (string filePath in files)
        {
            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                string hash = SHA256(fileBytes);
                DateTime timestamp = File.GetLastWriteTimeUtc(filePath);
                string filename = Path.GetFileName(filePath);

                var attachmentInfo = new AttatchmentInfo
                {
                    AttatchmentURL = "",
                    AuthorID = null,
                    Username = "Unknown",
                    Timestamp = timestamp,
                    UNIXTimestamp = ((DateTimeOffset)timestamp).ToUnixTimeSeconds(),
                    Path = filePath,
                    OriginalFilename = filename,
                    Hash = hash
                };

                messages.Add(attachmentInfo);
                Console.WriteLine($"[+] Loaded: {filePath} (modified {timestamp})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Error loading file {filePath}: {ex.Message}");
            }
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

    static string FindActualFilePath(string originalPath)
    {
        if (File.Exists(originalPath))
        {
            return originalPath;
        }

        string directory = Path.GetDirectoryName(originalPath) ?? "";
        string filename = Path.GetFileName(originalPath);

        string[] subdirectories = { "newer" };

        foreach (string subdir in subdirectories)
        {
            string subdirPath = Path.Combine(directory, subdir);
            if (Directory.Exists(subdirPath))
            {
                string potentialPath = Path.Combine(subdirPath, filename);
                if (File.Exists(potentialPath))
                {
                    return potentialPath;
                }

                string nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
                string extension = Path.GetExtension(filename);

                for (int counter = 1; counter <= 10; counter++)
                {
                    string variantFilename = $"{nameWithoutExt}_{counter}{extension}";
                    string variantPath = Path.Combine(subdirPath, variantFilename);
                    if (File.Exists(variantPath))
                    {
                        return variantPath;
                    }
                }
            }
        }

        return "";
    }
}

/// <summary>
/// https://dusted.codes/dotenv-in-dotnet
/// </summary>
public static class DotEnv
{
    public static void Load(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        foreach (var line in File.ReadAllLines(filePath))
        {
            var parts = line.Split(
                '=',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                continue;

            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}