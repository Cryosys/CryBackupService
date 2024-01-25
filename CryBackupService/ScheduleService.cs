using CryBackup.CommonData;
using System.Text;

namespace CryBackupService
{
    internal class ScheduleService
    {
        internal event ScheduleEventHandler? ScheduleTriggered;

        private readonly string _schedulePath = System.IO.Path.Combine(CryLib.Core.Paths.ExecuterPath, "schedule.conf");
        private List<Schedule> _schedules = new List<Schedule>();
        private object _schedulesLock = new object();

        private Thread? _scheduleThread;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        internal ScheduleService()
        {
        }

        internal void LoadSchedule()
        {
            if (!File.Exists(_schedulePath))
                _Save();

            string conf = File.ReadAllText(_schedulePath, Encoding.UTF8);

            List<Schedule>? temp = conf.FromCryJson<List<Schedule>>();
            if (temp is null)
                return;

            lock (_schedulesLock)
            {
                foreach(Schedule schedule in temp)
                    AddToSchedule(schedule);
            }
        }

        internal void AddToSchedule(Schedule schedule)
        {
            lock (_schedulesLock)
            {
                DayOfWeek day = DateTime.UtcNow.DayOfWeek;

                // Starts to fill the list starting at todays weekday
                for (var i = (int)day; i - (int)day < 7; i++)
                {
                    if (schedule.Days.Contains((DayOfWeek)i))
                        schedule._internalQueue.Add((DayOfWeek)i);
                }

                // Fills the list with the rest of the days prior to todays weekday if it is not Sunday.
                // This is just so that we do not add Sunday twice
                if (day != 0)
                {
                    for (var i = 0; i < (int)day; i++)
                    {
                        if (schedule.Days.Contains((DayOfWeek)i))
                            schedule._internalQueue.Add((DayOfWeek)i);
                    }
                }

                _schedules.Add(schedule);
                _Save();
            }
        }

        internal bool AlterSchedule(Schedule alteredSchedule)
        {
            lock (_schedulesLock)
            {
                Schedule? schedule;
                if ((schedule = _schedules.Find(tmpSchedule => tmpSchedule.ID == alteredSchedule.ID)) == null)
                    return false;

                schedule.Set(alteredSchedule);
            }

            return true;
        }

        internal Schedule[] GetSchedules()
        {
            Schedule[] schedules;
            lock (_schedulesLock)
            {
                schedules = new Schedule[_schedules.Count];

                for (int i = 0; i < schedules.Length; i++)
                    schedules[i] = new Schedule(_schedules[i]);
            }

            return schedules;
        }

        internal void StartScheduler()
        {
            if (_scheduleThread is not null)
                return;

            if (!_cancellationTokenSource.TryReset())
                _cancellationTokenSource = new CancellationTokenSource();

            _scheduleThread = new Thread(_ScheduleThread);
            _scheduleThread.Start();
        }

        internal void StopScheduler()
        {
            if (_scheduleThread is null)
                return;

            _cancellationTokenSource.Cancel();
            _scheduleThread.Join();
            _scheduleThread = null;
        }

        private void _ScheduleThread()
        {
            try
            {
                while (true)
                {
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    foreach (var schedule in _schedules)
                    {
                        if (schedule._internalQueue.Count <= 0)
                            continue;

                        DayOfWeek day = schedule._internalQueue.Peek();
                        DateTime now = DateTime.UtcNow;

                        // We do not continue if the day is not the current day
                        if (day != now.DayOfWeek)
                            continue;

                        // After getting the day we can pop the value from the schedule
                        day = schedule._internalQueue.Next();

                        // After popping the value we add it back at the end so that the scheduler can continue
                        schedule._internalQueue.Add(day);

                        // Now we need to check if the time fits or we already past it
                        if (schedule.Time.Hour < now.Hour)
                            continue;

                        // Check if we are at the same hour but the minutes past it
                        if (schedule.Time.Hour == now.Hour && schedule.Time.Minute < now.Minute)
                            continue;

                        // Finally we can schedule
                        Task.Run(() =>
                        {
                            try
                            {
                                int current = Math.Max((now.Hour - schedule.Time.Hour) + (now.Minute - schedule.Time.Minute), 1);
                                Task.Delay(current).Wait(_cancellationTokenSource.Token);

                                ScheduleTriggered?.Invoke(this, schedule.ScheduleType, schedule.Settings);
                            }
                            // We expect a task canceled exception here as we wait with delay
                            catch (TaskCanceledException)
                            {
                                return;
                            }
                        }, _cancellationTokenSource.Token);
                    }

                    // Only check every hour after the first check
                    Thread.Sleep(TimeSpan.FromHours(1));
                }
            }
            // We expect a task canceled exception if the token was canceled.
            catch (TaskCanceledException)
            {
                return;
            }
        }

        private void _Save()
        {
            string json;
            lock (_schedulesLock)
                json = _schedules.ToCryJson();

            File.WriteAllText(_schedulePath, json, Encoding.UTF8);
        }

        internal delegate void ScheduleEventHandler(ScheduleService sender, ScheduleType scheduleType, ISettings? settings);
    }
}
