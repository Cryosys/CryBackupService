namespace CryBackupInterface.Data
{
    public class TaskDataCollectionViewModel : CryBaseViewModel
    {
        public TaskDataViewModel[] Tasks
        {
            get => _tasks;
            set => SetProperty(ref _tasks, value);
        }

        private TaskDataViewModel[] _tasks = new TaskDataViewModel[0];
    }
}
