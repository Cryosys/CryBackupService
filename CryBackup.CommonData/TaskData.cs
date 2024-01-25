using Newtonsoft.Json;

namespace CryBackup.CommonData
{
    [JsonObject("TaskData")]
    public class TaskData
    {
        [JsonProperty("ID")]
        public Guid ID { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; } = "";

        [JsonProperty("Info")]
        public CryBackup.CommonData.TaskInfo Info { get; set; }

        public void Set(TaskData newData)
        {
            ID = newData.ID;
            Name = newData.Name;
            Info = newData.Info;
        }
    }

    /// <summary>   Information about the task. </summary>
    public struct TaskInfo
    {
        /// <summary>   Gets the current status of the task. </summary>
        [JsonProperty("Status")]
        public CryBackup.CommonData.TaskStatus Status { get; set; }

        /// <summary>   May contain additional information about the current status. </summary>
        [JsonProperty("Info")]
        public string Info { get; set; }

        [JsonProperty("CreationTime")]
        public DateTime CreationTime { get; set; }

        [JsonProperty("LastUpdatedTime")]
        public DateTime LastUpdatedTime { get; set; }

        public TaskInfo()
        {
            Status = TaskStatus.AwaitingStart;
            Info = "";
        }

        public TaskInfo(TaskStatus status, string info, DateTime taskCreationTime, DateTime lastUpdatedTime)
        {
            Status = status;
            Info = info;
            CreationTime = taskCreationTime;
            LastUpdatedTime = lastUpdatedTime;
        }

        public TaskInfo(TaskInfo info)
        {
            Status = info.Status;
            Info = info.Info;
            CreationTime = info.CreationTime;
            LastUpdatedTime = info.LastUpdatedTime;
        }
    }

    public class TaskHistoryData
    {
        [JsonProperty("ID")]
        public Guid ID { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; }

        [JsonProperty("Info")]
        public TaskInfo Info { get; set; }

        public TaskHistoryData()
        {
            
        }

        public TaskHistoryData(Guid id, string name, TaskInfo info)
        {
            ID = id;
            Name = name;
            Info = info;
        }
    }

    /// <summary>   Values that represent task status. </summary>
    public enum TaskStatus
    {
        /// <summary>   Awaiting start can only happen once in the beginning and never reoccur. </summary>
        AwaitingStart = 0,
        /// <summary>   Running can happen multiple time, e.g. if the task was paused and the started again. </summary>
        Running = 1,
        /// <summary>   Failed is considered to be a final state. This state cannot be changed afterwards and will remain failed. </summary>
        Failed = 2,
        /// <summary>   Succeded is considered to be a final state. This state cannot be changed afterwards and will remain successful. </summary>
        Succeded = 3,
        /// <summary>   Paused can happen multiple time, e.g. if the task was paused and the started again. </summary>
        Paused
    }
}