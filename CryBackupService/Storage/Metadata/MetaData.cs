using Newtonsoft.Json;

using System.Security.Cryptography;

namespace CryBackupService.Storage
{
    internal class MetaData
    {
        [JsonProperty("Directories")]
        internal string[] Directories { get; set; } = new string[0];

        [JsonProperty("Files")]
        internal File[] Files { get; set; } = new File[0];

        /// <summary>
        /// Internal bool to check if the metadata changed and if it should be saved after alteration.
        /// </summary>
        [JsonIgnore]
        internal bool Changed = false;

        internal void BuildMetaData(string directoryPath)
        {
            string[] targetFolders = Directory.GetDirectories(directoryPath);
            List<string> dirs = new List<string>();
            foreach(string targetFolder in targetFolders)
                dirs.Add(new DirectoryInfo(targetFolder).Name);

            Directories = dirs.ToArray();

            string[] targetFiles = Directory.GetFiles(directoryPath);
            List<File> files = new List<File>();
            foreach (string file in targetFiles)
            {
                if (Path.GetFileName(file) == GlobalStatics.MetaDataName)
                    continue;

                var fileInfo = new FileInfo(file);

                files.Add(new File()
                {
                    Name = Path.GetFileName(file),
                    Size = fileInfo.Length,
                    LastChanged = System.IO.File.GetLastWriteTime(file),
                    Hash = _GetFileHash(file)
                });
            }

            Files = files.ToArray();
            Changed = true;
        }

        private static byte[] _GetFileHash(string path)
        {
            using (FileStream xFileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
                return SHA1.Create().ComputeHash(xFileStream);
        }
    }

    [JsonObject("File")]
    internal class File
    {
        [JsonProperty("Name")]
        internal string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the hash of the file. Should always be SHA1 to make it efficient and small.
        /// </summary>
        [JsonProperty("Hash")]
        internal byte[] Hash { get; set; } = new byte[0];

        [JsonProperty("LastChanged")]
        internal DateTime LastChanged { get; set; }

        [JsonProperty("Size")]
        /// <summary> The size of the file in bytes. </summary>
        internal long Size { get; set; } = 0;
    }
}
