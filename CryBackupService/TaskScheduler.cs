using CryBackup.CommonData;

namespace CryBackupService.TaskScheduler
{
    internal static class TaskScheduler
    {
        public static event Action<Task>? HistoryChanged;

        private static Queue<Task> _queue = new Queue<Task>();
        private static object _queueLock = new object();

        private static Thread? _worker;
        private static object _workerLock = new object();
        private static CancellationTokenSource? _cancellationTokenSource;
        private static Task? _currentTask;
        private static object _currentTaskLock = new object();

        private static readonly string _taskHistoryPath = Path.Combine(CryLib.Core.Paths.ExecuterPath, "~task history.metadata");
        private static List<TaskHistoryData> _taskHistory = new List<TaskHistoryData>();
        private static object _taskHistoryLock = new object();

        static TaskScheduler()
        {
            List<TaskHistoryData>? tmp = null;
            if (File.Exists(_taskHistoryPath))
                tmp = File.ReadAllText(_taskHistoryPath).FromCryJson<List<TaskHistoryData>>();

            _taskHistory = tmp ?? new List<TaskHistoryData>();
            SaveHistory();
        }

        internal static void SaveHistory()
        {
            string json;
            lock (_taskHistoryLock)
                json = _taskHistory.ToCryJson();

            File.WriteAllText(_taskHistoryPath, json);
        }

        internal static void StartScheduler()
        {
            lock (_workerLock)
            {
                if (_worker is not null)
                    return;

                _cancellationTokenSource = new CancellationTokenSource();

                _worker = new Thread(WorkerThread);
                _worker.Start();
            }
        }

        internal static void StopScheduler()
        {
            lock (_workerLock)
            {
                if (_worker is null)
                    return;

                if (_cancellationTokenSource is null)
                    throw new Exception("CancellationToken is somehow null");

                if (_cancellationTokenSource.IsCancellationRequested)
                    return;

                _cancellationTokenSource.Cancel();

                // Wait 60 seconds for the worker thread to stop. 
                // It should never take longer than 60 seconds for the worker thread to join as it only starts the task.
                _worker.Join(TimeSpan.FromSeconds(60));
            }
        }

        internal static void AddTaskToQueue(Task task)
        {
            lock (_queueLock)
                _queue.Enqueue(task);
        }

        internal static TaskDataCollection GetTasks()
        {
            TaskDataCollection tasksCol = new TaskDataCollection();
            TaskData[] tasksData;
            Task[] tasks;

            lock (_queueLock)
            {
                tasks = new Task[_queue.Count];
                _queue.CopyTo(tasks, 0);
            }

            TaskHistoryData[] history;
            lock(_taskHistoryLock)
            {
                history = new TaskHistoryData[_taskHistory.Count];
                _taskHistory.CopyTo(history);
            }

            tasksData = new TaskData[tasks.Length + history.Length];

            int i = 0;
            for (; i < tasks.Length; i++)
            {
                tasksData[i] = new TaskData()
                {
                    Name = tasks[i].GetType().Name,
                    ID = tasks[i].ID,
                    Info = tasks[i].GetInfo()
                };
            }

            foreach(TaskHistoryData historyData in history)
            {
                tasksData[i] = new TaskData()
                {
                    Name = historyData.Name,
                    ID = historyData.ID,
                    Info = historyData.Info
                };

                i++;
            }

            tasksCol.Tasks = tasksData;
            return tasksCol;
        }

        private static void WorkerThread()
        {
            try
            {
                while (!_cancellationTokenSource?.IsCancellationRequested ?? false)
                {
                    Thread.Sleep(5000);

                    lock (_currentTaskLock)
                    {
                        if (_currentTask is not null)
                            continue;

                        lock (_queueLock)
                        {
                            lock (_currentTaskLock)
                            {
                                if (_queue.Count > 0)
                                {
                                    _currentTask = _queue.Dequeue();

                                    System.Threading.Tasks.Task.Run(() =>
                                    {
                                        lock (_taskHistoryLock)
                                            _taskHistory.Insert(0, new TaskHistoryData(_currentTask.ID, _currentTask.GetType().Name, _currentTask.GetInfo()));

                                        HistoryChanged?.Invoke(_currentTask);

                                        Thread.CurrentThread.Name = $"{_currentTask.GetType().Name} {_currentTask.ID}";
                                        _currentTask.TaskStateChanged += _currentTask_TaskStateChanged;
                                        _currentTask.Start();
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        private static void _currentTask_TaskStateChanged(Task? sender, TaskInfo e)
        {
            // We need to lock the whole thing as we could run into the situation that we set _currentTask to null right after the worker thread assigned it
            // This would lead to a task running but the scheduler does not know about it
            lock (_queueLock)
            {
                if (sender is null)
                    throw new Exception("The task schedulers task state event received somehow an event were the task was null");

                lock (_taskHistoryLock)
                {
                    TaskHistoryData? history = _taskHistory.Find(task => task.ID == sender.ID);

                    if (history is null)
                    {
                        // Assume that the info was somehow not added and insert it into the first position
                        history = new TaskHistoryData(sender.ID, sender.GetType().Name, sender.GetInfo());
                        _taskHistory.Insert(0, history);
                    }
                    else
                        history.Info = sender.GetInfo();
                }

                if (!sender.ID.Equals(_currentTask?.ID))
                    throw new Exception("Something went horribly wrong. The task scheduler did get an event that is not of the current task. " +
                                        "It is only allowed to ever run 1 task at the same time to ensure data integrity.");

                // Once we are here we know that the current task is the sender task
                if (e.Status == CryBackup.CommonData.TaskStatus.Failed || e.Status == CryBackup.CommonData.TaskStatus.Succeded)
                    lock (_currentTaskLock)
                    {
                        _currentTask.TaskStateChanged -= _currentTask_TaskStateChanged;
                        _currentTask = null;
                    }
            }

            // Save the history and signal that the history changed, but do not block this thread.
            System.Threading.Tasks.Task.Run(() =>
            {
                SaveHistory();
                HistoryChanged?.Invoke(sender);
            });
        }
    }

    internal delegate void TaskEventHandler(Task sender, TaskInfo info);

    internal abstract class Task
    {
        internal Guid ID { get; } = Guid.NewGuid();

        internal abstract event TaskEventHandler? TaskStateChanged;

        protected Task() { }

        internal abstract void Start();

        /// <summary>
        /// Gets information about the task.
        /// <see cref="Storage.StorageTransferTask.GetInfo"/>
        /// </summary>
        internal abstract TaskInfo GetInfo();
    }
}
