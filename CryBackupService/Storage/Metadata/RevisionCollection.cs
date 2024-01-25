using Newtonsoft.Json;

namespace CryBackupService.Storage.Metadata
{
    [JsonObject("RevisionCollection")]
    internal class RevisionCollection
    {
        [JsonProperty("Version")]
        internal string Version { get; set; } = "1";

        [JsonProperty("Revisions")]
        internal List<Revision> Revisions { get; set; } = new List<Revision>();
                
        public RevisionCollection() { }
    }
}
