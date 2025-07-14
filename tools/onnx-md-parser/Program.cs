using Newtonsoft.Json;
using Onnx;
using System.Text;

class Program
{
    public class Metadata
    {
        public required string Hash { get; set; }
        public required string FileName { get; set; }
        public required string Author { get; set; }
        public required string License { get; set; }
        public required string Description { get; set; }
        public required string Created { get; set; }
        public required string Version { get; set; }
        public required string ModelType { get; set; }
        public required string Labels { get; set; }
        public required int[] ImageSize { get; set; }
        public required int BatchSize { get; set; }
    }
    static void Main(string[] args)
    {
        string? rootDirectory = null;
        string? metadataPath = null;
        bool ignore = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dir" when i + 1 < args.Length:
                    rootDirectory = args[i + 1].Trim('"');
                    i++;
                    break;
                case "--md-path" when i + 1 < args.Length:
                    metadataPath = args[i + 1];
                    i++;
                    break;
                case "--ignore" when i + 1 < args.Length:
                    ignore = args[i + 1].ToLower() == "true";
                    i++;
                    break;
            }
        }

        if (string.IsNullOrEmpty(rootDirectory))
        {
            Console.WriteLine(@"Usage: onnx-md-parser --dir <root_directory> [--md-path <metadata_path>] [--ignore true/false]");
            return;
        }

        if (string.IsNullOrEmpty(metadataPath))
        {
            metadataPath = Path.Combine(rootDirectory, "metadata.json");
        }

        var onnxFiles = Directory.GetFiles(rootDirectory, "*.onnx", SearchOption.AllDirectories);
        if (onnxFiles.Length == 0)
        {
            Console.WriteLine("No ONNX files found in the specified directory.");
            return;
        }

        Console.WriteLine($"[INFO] Found {onnxFiles.Length} ONNX files in {rootDirectory}.");

        var existingMetadata = ignore ? new List<Metadata>() : LoadExisting(metadataPath);
        var hashes = new HashSet<string>(existingMetadata.Select(m => m.Hash));

        foreach (var file in onnxFiles)
        {
            var hash = GitSHA1Hash(file);

            if (hashes.Contains(hash))
            {
                Console.WriteLine($"[-] Skipping {file} - Hash: {hash}");
            }
            else
            {
                var info = ExtractMetadata(file, hash);
                existingMetadata.Add(info);
                hashes.Add(hash);
                Console.WriteLine($"[+] Processed {file} - Hash: {hash}");
            }
        }

        Console.WriteLine($"[INFO] Proccessed {existingMetadata.Count}/{onnxFiles.Length} models");

        File.WriteAllText(metadataPath, JsonConvert.SerializeObject(existingMetadata, Formatting.Indented));
    }

    static Metadata ExtractMetadata(string path, string githash)
    {
        using (var file = File.OpenRead(path))
        {
            var model = ModelProto.Parser.ParseFrom(file);
            var metadata = model.MetadataProps.ToDictionary(m => m.Key, m => m.Value);

            int[] imageSize = [640, 640]; // default image size when exporting, if not specified
            int batchSize = 1;
            
            if (model.Graph.Input.Count > 0)
            {
                var dims = model.Graph.Input[0].Type.TensorType.Shape.Dim;
                if (dims.Count >= 4)
                {
                    imageSize = [(int)dims[2].DimValue, (int)dims[3].DimValue];
                    batchSize = (int)dims[0].DimValue;
                }
            }

            return new Metadata
            {
                Hash = githash,
                FileName = Path.GetFileName(path),
                Author = metadata.ContainsKey("author") ? metadata["author"] : "N/A",
                License = metadata.ContainsKey("license") ? metadata["license"] : "N/A",
                Description = metadata.ContainsKey("description") ? metadata["description"] : "No description provided.",
                Created = metadata.ContainsKey("date") ? metadata["date"] : DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Version = metadata.ContainsKey("version") ? metadata["version"] : model.ProducerVersion ?? "N/A", /* metadata["version"] should be the yolo/onnx version, 
                                                                                                                   * its also what's shown as the version in netron.app */
                ModelType = metadata.ContainsKey("task") ? metadata["task"] : "N/A",
                Labels = metadata.ContainsKey("names") ? metadata["names"] : "N/A",
                ImageSize = imageSize,
                BatchSize = batchSize
            };
        } 
    }

    static string GitSHA1Hash(string file)
    {
        string header = "blob " + new FileInfo(file).Length + "\0";
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        byte[] fileBytes = File.ReadAllBytes(file);
        using (var sha1 = System.Security.Cryptography.SHA1.Create())
        {
            byte[] hashBytes = sha1.ComputeHash(headerBytes.Concat(fileBytes).ToArray());
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }

    static List<Metadata> LoadExisting(string metadataPath)
    {
        if (!File.Exists(metadataPath))
        {
            return new List<Metadata>();
        }
        var json = File.ReadAllText(metadataPath);
        return JsonConvert.DeserializeObject<List<Metadata>>(json) ?? new List<Metadata>();
    }
}