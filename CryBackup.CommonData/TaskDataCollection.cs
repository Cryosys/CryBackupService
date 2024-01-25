using Newtonsoft.Json;

namespace CryBackup.CommonData
{
    public class TaskDataCollection
    {
        [JsonProperty("Tasks")]
        public TaskData[] Tasks { get; set; } = new TaskData[0];
    }
}
