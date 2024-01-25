using Newtonsoft.Json;

namespace CryBackup.CommonData
{
	public class StorageRestoreSettings : ISettings
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

		[JsonProperty("RevisionVersion")]
		public int RevisionVersion { get; set; }

		[JsonProperty("AllowOverwrite")]
		public bool AllowOverwrite { get; set; }

		public StorageRestoreSettings()
		{
			SourcePath                     = String.Empty;
			TargetPath                     = String.Empty;
			RevisionCollectionMetaDataPath = String.Empty;
			RevisionStoragePath            = String.Empty;
			RevisionVersion                = 1;
			AllowOverwrite                 = false;
		}

		public StorageRestoreSettings(string sourcePath, string targetPath, string revisionCollectionMetaDataPath, string revisionStoragePath, int revisionVersion = 1, bool allowOverwrite = false)
		{
			SourcePath                     = sourcePath;
			TargetPath                     = targetPath;
			RevisionCollectionMetaDataPath = revisionCollectionMetaDataPath;
			RevisionStoragePath            = revisionStoragePath;
			RevisionVersion                = revisionVersion;
			AllowOverwrite                 = allowOverwrite;
		}

		public StorageRestoreSettings(StorageRestoreSettings settings)
		{
			SourcePath                     = settings.SourcePath;
			TargetPath                     = settings.TargetPath;
			RevisionCollectionMetaDataPath = settings.RevisionCollectionMetaDataPath;
			RevisionStoragePath            = settings.RevisionStoragePath;
			RevisionVersion                = settings.RevisionVersion;
			AllowOverwrite                 = settings.AllowOverwrite;
		}
	}
}
