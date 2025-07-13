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
        public required string Classes { get; set; }
        public required string Labels { get; set; }
        public required int[] ImageSize { get; set; }
        public required int BatchSize { get; set; }
    }
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(@"Usage: onnx-md-parser <root_directory> [metadata_path]");
            return;
        }

        var rootDirectory = args[0].Trim('"');

        var metadataPath = args.Length > 1 ? args[1] : Path.Combine(rootDirectory, "metadata.json");

        var onnxFiles = Directory.GetFiles(rootDirectory, "*.onnx", SearchOption.AllDirectories);
        if (onnxFiles.Length == 0)
        {
            Console.WriteLine("No ONNX files found in the specified directory.");
            return;
        }

        Console.WriteLine($"[INFO] Found {onnxFiles.Length} ONNX files in {rootDirectory}.");

        var existingMetadata = LoadExisting(metadataPath);
        var hashes = new HashSet<string>(existingMetadata.Select(m => m.Hash));

        foreach (var file in onnxFiles)
        {
            var hash = GitSHA1Hash(file);

            if (hashes.Contains(hash))
            {
                Console.WriteLine($"[-] Skipping {file} - Hash: {hash}");
                continue;
            }

            var info = ExtractMetadata(file, hash);
            existingMetadata.Add(info);
            hashes.Add(hash);
            Console.WriteLine($"[+] Processed {file} - Hash: {hash}");
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
                Created = DateTime.UtcNow.ToString("o"),
                Version = metadata.ContainsKey("version") ? metadata["version"] : "1.0.0",
                ModelType = metadata.ContainsKey("model_type") ? metadata["model_type"] : "N/A",
                Classes = metadata.ContainsKey("classes") ? metadata["classes"] : "N/A",
                Labels = metadata.ContainsKey("labels") ? metadata["labels"] : "N/A",
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