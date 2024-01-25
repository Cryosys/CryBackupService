using Newtonsoft.Json;

namespace CryBackup.CommonData
{
    public class StorageTransferSettings : ISettings
    {
        [JsonProperty("Version")]
        public int Version { get; set; } = 1;

        [JsonProperty("SourcePath")]
        public string SourcePath { get; set; }

        [JsonProperty("TargetPath")]
        public string TargetPath { get; set; }

        [JsonProperty("RevisionCollectionMetaDataPath ")]
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

        public StorageTransferSettings(string sourcePath, string targetPath, string revisionCollectionMetaDataPath, string revisionStoragePath, bool enableHashCompare, bool ignoreThumbsDB, bool keepDeletedFilesAndDirectories, int dataRetension = 2)
        {
            SourcePath = sourcePath;
            TargetPath = targetPath;
            RevisionCollectionMetaDataPath = revisionCollectionMetaDataPath;
            RevisionStoragePath = revisionStoragePath;
            EnableHashCompare = enableHashCompare;
            IgnoreThumbsDB = ignoreThumbsDB;
            KeepDeletedFilesAndDirectories = keepDeletedFilesAndDirectories;
            DataRetention = dataRetension;
        }

        public StorageTransferSettings(StorageTransferSettings settings)
        {
            SourcePath = settings.SourcePath;
            TargetPath = settings.TargetPath;
            RevisionCollectionMetaDataPath = settings.RevisionCollectionMetaDataPath;
            RevisionStoragePath = settings.RevisionStoragePath;
            EnableHashCompare = settings.EnableHashCompare;
            IgnoreThumbsDB = settings.IgnoreThumbsDB;
            KeepDeletedFilesAndDirectories = settings.KeepDeletedFilesAndDirectories;
            DataRetention = settings.DataRetention;
        }
    }
}
