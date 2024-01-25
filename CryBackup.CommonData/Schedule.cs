using CryLib.Core;
using Newtonsoft.Json;

namespace CryBackup.CommonData
{
    public class Schedule
    {
        [JsonProperty("ID")]
        public Guid ID = Guid.NewGuid();

        [JsonProperty("Name")]
        public string Name = string.Empty;

        [JsonProperty("Days")]
        /// <summary>
        /// The days to run the schedule on.
        /// </summary>
        public List<DayOfWeek> Days = new List<DayOfWeek>();

        [JsonProperty("Time")]
        /// <summary>
        /// Specifies the Time to run the schedule at. Always expects UTC time.
        /// </summary>
        public DateTime Time;

        [JsonProperty("ScheduleType")]
        public ScheduleType ScheduleType;

        /// <summary>
        /// Optional settings for the scheduled operation.
        /// Settings may not exist for every operation.
        /// </summary>
        [JsonProperty("Settings")]
        public ISettings? Settings;

        [JsonIgnore]
        public RingBuffer<DayOfWeek> _internalQueue = new RingBuffer<DayOfWeek>(7);

        public Schedule()
        {

        }

        public Schedule(Guid id, string name, List<DayOfWeek> days, DateTime time, ScheduleType scheduleType, ISettings? settings = null)
        {
            this.ID = id;
            this.Name = name;
            this.Days = days;
            this.Time = time;
            this.ScheduleType = scheduleType;
            this.Settings = settings;
        }

        public Schedule(Schedule schedule)
        {
            Set(schedule);
        }

        public void Set(Schedule schedule)
        {
            this.ID = schedule.ID;
            this.Name = schedule.Name;
            this.Days = schedule.Days;
            this.Time = schedule.Time;
            this.ScheduleType = schedule.ScheduleType;
            this.Settings = schedule.Settings;
        }
    }

    public enum ScheduleType
    {
        StorageTransfer,
        Cleaning,
        DataScrubbing
    }
}
