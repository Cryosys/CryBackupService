using CryBackup.CommonData;
using System;

namespace CryBackupInterface.Data
{
    public class TaskDataViewModel : CryBaseViewModel
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

        public TaskInfo Info
        {
            get => _taskInfo;
            set => SetProperty(ref _taskInfo, value);
        }

        private TaskInfo _taskInfo;

        public TaskDataViewModel()
        {
            
        }

        public TaskDataViewModel(TaskData newData) => Set(newData);

        public void Set(TaskData newData)
        {
            ID = newData.ID;
            Name = newData.Name;
            Info = newData.Info;
        }
    }
}
