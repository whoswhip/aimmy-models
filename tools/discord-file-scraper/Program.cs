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
    static List<FileInfo> fileInfos = new List<FileInfo>();
    static long lastMetadataTimestamp = 0;
    static Dictionary<string, string> knownOriginals = new Dictionary<string, string>
    {
        { "8C33ECC90221267FCD6FB7DF7295841F4BFB061F3E3208344AB7E80C998AD40B", "Themida Arsenal (4k).onnx" },
        { "6E814BA61CE5A8CDBEBEB95F28B98DE562761A2F24A559A7A4EA417DDEB0A4E6", "Universal Hamsta v3.onnx"},
        { "46A7FC44BFE047E3078ED924DA5D7BEDC23561D5754690CB30B8E54416EA7172", "AIOv7.onnx"},
        { "6E602D7B48CE6C701BD83417397AEA55C329E25C0951107EBD22885DFF3B07C2", "AIOv11.onnx"}
    };
    // static Dictionary<string, string> skipFiles = new Dictionary<string, string>
    // {
    //     //{ "SHA256HASH", "FILENAME" },
    // }; // incase someone requests their model to be skipped

    static async Task Main(string[] args)
    {
        string? token = null;
        string? guildId = null;
        string? channelId = null;
        bool checkDuplicates = false;
        bool skipDownload = false;
        bool cliOnly = false;
        bool deleteOld = false;
        bool skipOld = false;
        int offset = 0;
        DateTime start = DateTime.UtcNow;

        DotEnv.Load(".env");
        token ??= Environment.GetEnvironmentVariable("DISCORD_TOKEN");
        guildId ??= Environment.GetEnvironmentVariable("DISCORD_GUILD_ID");
        channelId ??= Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
        checkDuplicates = (Environment.GetEnvironmentVariable("CHECK_DUPLICATES") ?? "false").ToLowerInvariant() == "true";
        skipDownload = (Environment.GetEnvironmentVariable("SKIP_DOWNLOAD") ?? "false").ToLowerInvariant() == "true";
        deleteOld = (Environment.GetEnvironmentVariable("DELETE_OLD") ?? "false").ToLowerInvariant() == "true";
        skipOld = (Environment.GetEnvironmentVariable("SKIP_OLD") ?? "false").ToLowerInvariant() == "true";
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(guildId) && !string.IsNullOrWhiteSpace(channelId))
            cliOnly = true;
        if (deleteOld && skipOld)
        {
            Console.WriteLine("[!] Cannot use both DELETE_OLD and SKIP_OLD options together.");
            return;
        }


        if (args.Length == 0 && !cliOnly)
        {
            Console.Write("[?] Discord token: ");
            token = Console.ReadLine()?.Trim();
            Console.Write("[?] Guild ID: ");
            guildId = Console.ReadLine()?.Trim();
            Console.Write("[?] Channel ID: ");
            channelId = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(channelId))
            {
                Console.WriteLine("Usage: --token <token> --guild <guild_id> --channel <channel_id> [--check-duplicates] [--skip-download] [--delete-old] [--skip-old]");
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
                        case "delete-old":
                            deleteOld = true;
                            break;
                        case "skip-old":
                            skipOld = true;
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
        if (cliOnly)
        {
            Console.WriteLine($"[*] Using token: {(token.Length > 8 ? token.Substring(0, 4) + new string('*', token.Length - 8) + token.Substring(token.Length - 4) : token)}");
            Console.WriteLine($"[*] Using guild ID: {guildId}");
            Console.WriteLine($"[*] Using channel ID: {channelId}");
            Console.WriteLine($"[*] Check duplicates: {checkDuplicates}");
            Console.WriteLine($"[*] Skip download: {skipDownload}");
            Console.WriteLine($"[*] Delete old files: {deleteOld}");
            Console.WriteLine($"[*] Skip old files: {skipOld}");
        }

        if (deleteOld)
        {
            Console.WriteLine("[*] Deleting old files...");
            foreach (var ext in acceptedExtensions)
            {
                string dir = ext.TrimStart('.');
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        foreach (var extension in acceptedExtensions)
        {
            if (!Directory.Exists(extension.TrimStart('.')))
                Directory.CreateDirectory(extension.TrimStart('.'));
        }

        if (!skipDownload)
        {
            LoadExistingFiles(true);
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

                    if (skipOld && lastMetadataTimestamp > 0)
                    {
                        var oldestMessageArray = JArray.Parse(messages.Last.ToString());
                        var oldestMessage = oldestMessageArray[0];
                        DateTime oldestTimestamp = oldestMessage["timestamp"]?.Value<DateTime>() ?? DateTime.UtcNow;
                        long oldestUnix = ((DateTimeOffset)oldestTimestamp).ToUnixTimeSeconds();
                        if (oldestUnix <= lastMetadataTimestamp)
                        {
                            Console.WriteLine("[*] Reached messages older than the latest metadata timestamp. Stopping download.");
                            break;
                        }
                    }

                    await ProcessMessagesAsync(messages);

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

    static async Task ProcessMessagesAsync(JArray messages)
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

    static async Task DownloadAsync(string url, string extension, JToken attachment, DateTime timestamp, string? authorId, string? username)
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
                string filename = !string.IsNullOrWhiteSpace(attachment["title"]?.Value<string>()) ? $"{attachment["title"]?.Value<string>()}.{extension}" : attachment["filename"]?.Value<string>() ?? $"{GenerateString(8)}.{extension}";
                string filePath = Path.Combine(extension, filename);
                byte[] hashBytes = SHA256Bytes(file);
                string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                if (filename.StartsWith("best", StringComparison.OrdinalIgnoreCase))
                    return;
                if (File.Exists(filePath))
                {
                    DateTime existingFileTime = File.GetLastWriteTimeUtc(filePath);
                    string existingFileHash = SHA256(await File.ReadAllBytesAsync(filePath));

                    if (existingFileHash == hash)
                    {
                        if (existingFileTime >= timestamp)
                        {
                            Console.WriteLine($"[*] Skipping download, file already exists and is up to date: {filePath}");
                            return;
                        }
                        else
                        {
                            string newerDir = Path.Combine(extension, "newer");
                            if (!Directory.Exists(newerDir))
                                Directory.CreateDirectory(newerDir);
                            string newerFilePath = Path.Combine(newerDir, filename);
                            int counter = 1;
                            while (File.Exists(newerFilePath))
                            {
                                string nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
                                string ext = Path.GetExtension(filename);
                                newerFilePath = Path.Combine(newerDir, $"{nameWithoutExt}_{counter}{ext}");
                                counter++;
                            }
                            filePath = newerFilePath;
                            Console.WriteLine($"[~] Existing file is older, saving to 'newer' directory: {filePath}");
                        }
                    }
                    else
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
                        string ext = Path.GetExtension(filename);
                        string duplicateFilePath = Path.Combine(extension, $"{nameWithoutExt}_~~{hash.Substring(0, 4)}{ext}");
                        filePath = duplicateFilePath;
                        Console.WriteLine($"[~] File with same name but different content exists, saving as: {filePath}");
                    }
                }

                var attatchmentInfo = new AttatchmentInfo
                {
                    AttatchmentURL = url,
                    AuthorID = authorId,
                    Username = username,
                    Timestamp = timestamp,
                    UNIXTimestamp = ((DateTimeOffset)timestamp).ToUnixTimeSeconds(),
                    OriginalFilename = !string.IsNullOrWhiteSpace(attachment["title"]?.Value<string>())
                                        ? $"{attachment["title"]?.Value<string>()}.{extension}"
                                        : attachment["filename"]?.Value<string>() ?? $"{GenerateString(8)}.{extension}",
                    Path = filePath,
                    Hash = hash
                };
                var fileInfo = new FileInfo(attatchmentInfo.OriginalFilename, hashBytes, attatchmentInfo.UNIXTimestamp);

                messages.Add(attatchmentInfo);
                Console.WriteLine($"[+] Downloaded: {filePath} by {username} created at {timestamp}");

                await File.WriteAllBytesAsync(filePath, file);
                File.SetCreationTimeUtc(filePath, timestamp);
                File.SetLastWriteTimeUtc(filePath, timestamp);
                File.SetLastAccessTimeUtc(filePath, timestamp);

                if (!FileInfo.FileInfoInList(fileInfos, fileInfo))
                {
                    await FileInfo.AppendFileInfoAsync("metadata.dat", fileInfo);
                    fileInfos.Add(fileInfo);
                }
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
        Console.WriteLine("[*] Checking for duplicate files (messages + metadata)...");

        string duplicatesDir = "duplicates";
        if (!Directory.Exists(duplicatesDir))
            Directory.CreateDirectory(duplicatesDir);

        var existingKeys = new HashSet<string>(messages.Select(m => m.Hash + "::" + Path.GetFileName(m.OriginalFilename)), StringComparer.OrdinalIgnoreCase);

        foreach (var fi in fileInfos)
        {
            string hashLower = BitConverter.ToString(fi.SHA256Hash).Replace("-", "").ToLowerInvariant();
            string key = hashLower + "::" + Path.GetFileName(fi.FileName);
            if (existingKeys.Contains(key))
                continue;

            string resolvedPath = FindFileInKnownDirectories(fi.FileName);
            if (string.IsNullOrEmpty(resolvedPath))
                resolvedPath = fi.FileName;

            var attach = new AttatchmentInfo
            {
                AttatchmentURL = string.Empty,
                AuthorID = null,
                Username = "Unknown",
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(fi.Timestamp).UtcDateTime,
                UNIXTimestamp = fi.Timestamp,
                Path = resolvedPath,
                OriginalFilename = Path.GetFileName(fi.FileName),
                Hash = hashLower
            };
            messages.Add(attach);
            existingKeys.Add(key);
        }

        var duplicateGroups = messages
            .GroupBy(m => m.Hash, StringComparer.OrdinalIgnoreCase)
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

    static string FindFileInKnownDirectories(string fileName)
    {
        if (File.Exists(fileName)) return fileName;
        foreach (var ext in acceptedExtensions)
        {
            string root = ext.TrimStart('.');
            string candidate = Path.Combine(root, fileName);
            if (File.Exists(candidate)) return candidate;
            string newer = Path.Combine(root, "newer", fileName);
            if (File.Exists(newer)) return newer;
            string dups = Path.Combine("duplicates", ext, fileName);
            if (File.Exists(dups)) return dups;
        }
        return string.Empty;
    }

    static void LoadExistingFiles(bool metadataOnly = false)
    {
        Console.WriteLine("[*] Loading existing files for duplicate checking...");

        if (metadataOnly)
        {
            if (!File.Exists("metadata.dat"))
                return;

            fileInfos = FileInfo.LoadFileInfosAsync("metadata.dat").Result;
            lastMetadataTimestamp = FileInfo.GetLatestTimestamp(fileInfos);
            Console.WriteLine($"[*] Loaded {fileInfos.Count} file metadata entries.");
            Console.WriteLine($"[*] Latest metadata timestamp: {lastMetadataTimestamp}");
            return;
        }
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

        fileInfos = FileInfo.LoadFileInfosAsync("metadata.dat").Result;
        lastMetadataTimestamp = FileInfo.GetLatestTimestamp(fileInfos);
        Console.WriteLine($"[*] Loaded {fileInfos.Count} file metadata entries.");
        Console.WriteLine($"[*] Loaded {messages.Count} files for processing.");
        Console.WriteLine($"[*] Latest metadata timestamp: {lastMetadataTimestamp}");
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
    static byte[] SHA256Bytes(byte[] file)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            return sha256.ComputeHash(file);
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


public sealed class FileInfo
{
    private const int SHA256Length = 32;
    private const int TimestampLength = 8;

    public string FileName { get; }
    public byte[] SHA256Hash { get; }
    public long Timestamp { get; }

    public FileInfo(string filename, byte[] hash, long timestamp)
    {
        if (filename is null) throw new ArgumentNullException(nameof(filename));
        if (hash is null) throw new ArgumentNullException(nameof(hash));
        if (hash.Length != SHA256Length) throw new ArgumentException($"Hash must be {SHA256Length} bytes long.", nameof(hash));

        byte[] name = System.Text.Encoding.UTF8.GetBytes(filename);
        if (name.Length > byte.MaxValue) throw new ArgumentException($"Filename must be less than {byte.MaxValue} bytes long.", nameof(filename));

        FileName = filename;
        SHA256Hash = (byte[])hash.Clone();
        Timestamp = timestamp;
    }

    public byte[] ToBytes()
    {
        byte[] name = System.Text.Encoding.UTF8.GetBytes(FileName);
        if (name.Length > byte.MaxValue) throw new InvalidOperationException($"Filename must be less than {byte.MaxValue} bytes long.");

        using var ms = new MemoryStream(1 + name.Length + SHA256Length + TimestampLength);
        ms.WriteByte((byte)name.Length);
        ms.Write(name, 0, name.Length);
        ms.Write(SHA256Hash, 0, SHA256Hash.Length);
        ms.Write(BitConverter.GetBytes(Timestamp), 0, TimestampLength);
        return ms.ToArray();
    }

    public static FileInfo FromBytes(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < 1 + SHA256Length + TimestampLength) throw new ArgumentException("Data is too short to be a valid FileInfo.", nameof(data));
        using var ms = new MemoryStream(data);
        int nameLength = ms.ReadByte();
        if (nameLength < 0) throw new ArgumentException("Data is too short to be a valid FileInfo.", nameof(data));
        if (ms.Length < 1 + nameLength + SHA256Length + TimestampLength) throw new ArgumentException("Data is too short to be a valid FileInfo.", nameof(data));
        byte[] name = new byte[nameLength];
        ms.Read(name, 0, nameLength);
        byte[] hash = new byte[SHA256Length];
        ms.Read(hash, 0, SHA256Length);
        byte[] timestampBytes = new byte[TimestampLength];
        ms.Read(timestampBytes, 0, TimestampLength);
        long timestamp = BitConverter.ToInt64(timestampBytes, 0);
        return new FileInfo(System.Text.Encoding.UTF8.GetString(name), hash, timestamp);
    }

    public static async Task<List<FileInfo>> LoadFileInfosAsync(string filePath)
    {
        if (filePath is null) throw new ArgumentNullException(nameof(filePath));
        var fileInfos = new List<FileInfo>();
        if (!File.Exists(filePath)) return fileInfos;

        byte[] data = await File.ReadAllBytesAsync(filePath);
        int index = 0;
        while (index < data.Length)
        {
            int nameLength = data[index];
            if (nameLength <= 0 || index + 1 + nameLength + SHA256Length + TimestampLength > data.Length)
                break;

            byte[] entryData = new byte[1 + nameLength + SHA256Length + TimestampLength];
            Array.Copy(data, index, entryData, 0, entryData.Length);
            var fileInfo = FromBytes(entryData);
            fileInfos.Add(fileInfo);
            index += entryData.Length;
            Console.WriteLine($"    Loaded metadata: {fileInfo.FileName}, Timestamp: {fileInfo.Timestamp}");
        }
        // print
        return fileInfos;
    }

    public static async Task AppendFileInfoAsync(string filePath, FileInfo fileInfo)
    {
        if (filePath is null) throw new ArgumentNullException(nameof(filePath));
        if (fileInfo is null) throw new ArgumentNullException(nameof(fileInfo));
        if (!File.Exists(filePath))
            await File.WriteAllBytesAsync(filePath, Array.Empty<byte>());
        byte[] data = fileInfo.ToBytes();
        using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None);
        await fs.WriteAsync(data, 0, data.Length);
    }

    public static bool FileInfoInList(List<FileInfo> list, FileInfo fileInfo)
    {
        return list.Any(f => f.FileName == fileInfo.FileName &&
                            f.Timestamp == fileInfo.Timestamp &&
                            f.SHA256Hash.SequenceEqual(fileInfo.SHA256Hash));
    }

    public static long GetLatestTimestamp(List<FileInfo> list)
    {
        if (list == null || list.Count == 0)
            return 0;
        return list.Max(f => f.Timestamp);
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