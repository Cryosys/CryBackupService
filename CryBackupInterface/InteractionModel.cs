using CryBackup.CommonData;
using CryBackupInterface.Data;
using CryLib;
using CryLib.Core;
using CryLib.Network.Protocols;
using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace CryBackupInterface
{
	public class InteractionModel : CryBaseModel
	{
		public TaskDataCollectionViewModel Tasks
		{
			set => SetProperty(ref _tasks, value);
			get => _tasks;
		}

		private TaskDataCollectionViewModel _tasks = new TaskDataCollectionViewModel();
		private object _tasksLock                  = new object();

		public ServiceSettings? Settings
		{
			set => SetProperty(ref _settings, value);
			get => _settings;
		}

		private ServiceSettings? _settings = null;
		private object _settingsLock       = new object();

		public StorageRestoreSettings? RestoreSettings
		{
			set => SetProperty(ref _restoreSettings, value);
			get => _restoreSettings;
		}

		private StorageRestoreSettings? _restoreSettings = new StorageRestoreSettings();
		private object _restoreSettingsLock              = new object();

		public ScheduleViewModel[] Schedules
		{
			set => SetProperty(ref _schedules, value);
			get => _schedules;
		}

		private ScheduleViewModel[] _schedules = new ScheduleViewModel[0];
		private object _schedulesLock = new object();

		private CommonProtocolBase _protocol;

		public InteractionModel()
		{
			ProtocolSettings.AllowEmptySends  = true;
			ProtocolSettings.CallDataTransfer = false;
			ProtocolSettings.TcpNoDelay       = true;

			_protocol               = new CommonProtocolBase(System.IO.Path.Combine(Paths.ExecuterPath, "Logs", $"Log - {DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss")}"));
			_protocol.ProtocolLog  += _protocol_ProtocolLog;
			_protocol.Disconnected += _protocol_Disconnected;
			_protocol.ReceivedData += _protocol_ReceivedData;
		}

		public void Connect()
		{
			_protocol.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 61321));

			if (!_protocol.Connected)
			{
				CryMessagebox.Create("Could not connect to service");
				return;
			}

			RequestServiceSettings();
			RequestTasks();
			RequestSchedules();
		}

		public void Disconnect()
		{
			_protocol.Disconnect();
		}

		public void RequestServiceSettings()
		{
			_protocol.Send(RequestType.GetSettings, "");
		}

		public void SendServiceSettings()
		{
			if (_settings is null)
				return;

			string settingsJson;
			lock (_settingsLock)
				settingsJson = _settings.ToCryJson();

			_protocol.Send(RequestType.SetSettings, settingsJson);
		}

		public void SendRestore()
		{
			if (_restoreSettings is null)
				return;

			string restoreSettingsJson;
			lock (_restoreSettingsLock)
				restoreSettingsJson = _restoreSettings.ToCryJson();

			_protocol.Send(RequestType.RestoreBackup, restoreSettingsJson);
		}

		public void RequestTasks()
		{
			_protocol.Send(RequestType.ListOfTasks, "");
		}

		public void RequestSchedules()
		{
			_protocol.Send(RequestType.ListOfSchedules, "");
		}

		private void _protocol_ReceivedData(CommonProtocolBase prot, System.Net.IPEndPoint? remoteEndPoint, CommonData data)
		{
			if (data.Data is null)
				return;

			if (data.Descriptor == RequestType.TaskUpdated)
			{
				TaskData? newData = ((string) data.Data).FromCryJson<TaskData>();
				if (newData is null)
					return;

				this.Dispatcher.Invoke(new Action(() =>
				{
					lock (_tasksLock)
					{
						TaskDataViewModel? Olddata = Tasks.Tasks.FirstOrDefault(data => data.ID == newData.ID);
						if (Olddata is null)
							return;

						Olddata.Set(newData);
						Changed(nameof(Tasks));
					}
				}));
			}
			else if (data.Descriptor == RequestType.ListOfSchedules)
			{
				Schedule[] ? schedules = ((string) data.Data).FromCryJson<Schedule[]>();
				if (schedules is null)
					return;

				this.Dispatcher.Invoke(new Action(() =>
				{
					ScheduleViewModel[] newSchedulesData = new ScheduleViewModel[schedules.Length];
					for (int i = 0; i < schedules.Length; i++)
						newSchedulesData[i] = new ScheduleViewModel(schedules[i]);

					lock (_schedulesLock)
						Schedules = newSchedulesData;
				}));
			}
			else if (data.Descriptor == RequestType.ListOfTasks)
			{
				TaskDataCollection? tasks = ((string) data.Data).FromCryJson<TaskDataCollection>();
				if (tasks is null)
					return;

				this.Dispatcher.Invoke(new Action(() =>
				{
					TaskDataViewModel[] newTasksData = new TaskDataViewModel[tasks.Tasks.Length];
					for (int i = 0; i < tasks.Tasks.Length; i++)
						newTasksData[i] = new TaskDataViewModel(tasks.Tasks[i]);

					lock (_tasksLock)
						Tasks.Tasks = newTasksData;
				}));
			}
			else if (data.Descriptor == RequestType.GetSettings)
			{
				ServiceSettings? settings = ((string) data.Data).FromCryJson<ServiceSettings>();
				if (settings is null)
					return;

				this.Dispatcher.Invoke(new Action(() =>
				{
					lock (_settingsLock)
						Settings = settings;
				}));
			}
		}

		private void _protocol_ProtocolLog(CommonProtocolBase prot, string info)
		{
		}

		private void _protocol_Disconnected(CommonProtocolBase prot, System.Net.IPEndPoint? remoteEndPoint)
		{
		}
	}
}
