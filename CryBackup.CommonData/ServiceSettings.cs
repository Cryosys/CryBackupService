using Newtonsoft.Json;

namespace CryBackup.CommonData
{
    [JsonObject("ServiceSettings")]
    public class ServiceSettings
    {
        [JsonProperty("Version")]
        public int Version { get; set; } = 1;

        [JsonProperty("SourcePath")]
        public string SourcePath { get; set; }

        [JsonProperty("TargetPath")]
        public string TargetPath { get; set; }

        [JsonProperty("RevisionCollectionMetaDataPath")]
        public string RevisionCollectionMetaDataPath { get; set; }

        [JsonProperty("RevisionStoragePath")]
        public string RevisionStoragePath { get; set; }

        [JsonProperty("EnableHashCompare")]
        public bool EnableHashCompare { get; set; }

        [JsonProperty("IgnoreThumbsDB")]
        public bool IgnoreThumbsDB { get; set; }

        [JsonProperty("KeepDeletedFilesAndDirectories")]
        public bool KeepDeletedFilesAndDirectories { get; set; }

        [JsonProperty("DataRetention")]
        public int DataRetention { get; set; }

        public ServiceSettings()
        {
            SourcePath = "";
            TargetPath = "";
            RevisionCollectionMetaDataPath = Path.Combine(CryLib.Core.Paths.ExecuterPath, "~meta.data");
            RevisionStoragePath = Path.Combine(CryLib.Core.Paths.ExecuterPath, "Revisions");
            EnableHashCompare = false;
            IgnoreThumbsDB = true;
            KeepDeletedFilesAndDirectories = false;
            DataRetention = 2;
        }
    }
}
