using Newtonsoft.Json;

namespace CryBackupService.Storage.Metadata
{
    internal class Revision
    {
        [JsonProperty("Version")]
        internal string Version { get; set; } = "1";

        /// <summary>
        /// The Age of the Revision is its current hierarchical position in the restore tree.
        /// Meaning that 1 is the first revision. 2 is the second revision and so on.
        /// </summary>
        [JsonProperty("Age")]
        internal uint Age { get; set; }

        /// <summary>
        /// Local folder name of the revision relative to root
        /// </summary>
        [JsonProperty("FolderName")]
        internal string FolderName { get; set; }

        /// <summary>   Gets or sets the Date/Time of the timestamp. The timestamp will always be in UTC. </summary>
        [JsonProperty("Timestamp")]
        internal DateTime Timestamp { get; set; }

        [JsonProperty("RevisionData")]
        internal List<RevisionData> RevisionData { get; set; }

        public Revision()
        {
            FolderName = "";
            RevisionData = new List<RevisionData>();
        }
    }

    [JsonObject("RevisionData")]
    internal class RevisionData
    {
        [JsonProperty("Version")]
        internal string Version { get; set; } = "1";

        /// <summary>   Gets the full pathname of the original file. Should only ever be the relative path to the revision file. </summary>
        [JsonProperty("OriginalPath")]
        internal string OriginalPath { get; set; }

        /// <summary>   Gets the name of the file or directory. </summary>
        [JsonProperty("DataName")]
        internal string DataName { get; set; }

        [JsonProperty("TemporalName")]
        internal string TemporalName { get; set; }

        [JsonProperty("IsDirectory")]
        internal bool IsDirectory { get; set; }

        /// <summary>   Gets or sets the size. </summary>
        [JsonProperty("Size")]
        internal long Size { get; set; }

        public RevisionData()
        {
            OriginalPath = "";
            DataName = "";
            TemporalName = "";
            IsDirectory = false;
        }

        public RevisionData(string originalPath, string dataName, bool isDirectory, long size)
        {
            OriginalPath = originalPath;
            DataName = dataName;
            TemporalName = dataName;
            IsDirectory = isDirectory;
            Size = size;
        }
    }
}
