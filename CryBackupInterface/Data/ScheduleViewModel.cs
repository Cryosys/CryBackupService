using CryBackup.CommonData;
using System;
using System.Collections.Generic;

namespace CryBackupInterface.Data
{
    public class ScheduleViewModel : CryBaseViewModel
    {
        public Guid ID
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private Guid _id;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string _name = "";

        public List<DayOfWeek> Days
        {
            get => _days; 
            set => SetProperty(ref _days, value);
        } 

        private List<DayOfWeek> _days = new List<DayOfWeek>();

        public DateTime Time
        {
            get => _time;
            set => SetProperty(ref _time, value);
        }

        private DateTime _time;

        public ScheduleType ScheduleType
        {
            get => _scheduleType;
            set => SetProperty(ref _scheduleType, value);
        }

        private ScheduleType _scheduleType;

        public ISettings? Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        private ISettings? _settings;

        public ScheduleViewModel(Schedule schedule)
        {
            this.ID = schedule.ID;
            this.Name = schedule.Name;
            this.Days = schedule.Days;
            this.ScheduleType = schedule.ScheduleType;
            this.Settings = schedule.Settings;
        }
    }
}
