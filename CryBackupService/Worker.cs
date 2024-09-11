using CryBackup.CommonData;
using CryLib.Core;
using CryLib.Network;
using CryLib.Network.Protocols;
using NetFwTypeLib;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Principal;

namespace CryBackupService
{
	public class Worker : BackgroundService
	{
		private readonly ILogger<Worker> _logger;
		private TcpListenerServer? _tcpListener;

		private List<CommonProtocolBase> _clients = new List<CommonProtocolBase>();
		private object _clientsLock               = new object();

		private ServiceSettings _serviceSettings;
		private object _serviceSettingsLock          = new object();
		private readonly string _serviceSettingsPath = Path.Combine(CryLib.Core.Paths.ExecuterPath, "settings");

		private readonly ScheduleService _schedulerService = new ScheduleService();

		private AdaptiveLogHandler<LogType> _adaptiveLogHandler;

		private enum LogType
		{
			// Logs all logs written into a singular log file
			All,
			// Logs as the windows service
			WindowsService,
			// Logs as the task scheduler
			Scheduler,
			// Logs as the storage controller which controls the file writing and reading of the backups
			Storage,
			// Logs as the network communicator which handles communication between service and client as well as communication between the service and remote storage solution which to back up
			Network
		}

		/// <summary>   Constructor. </summary>
		/// <param name="logger"> The logger. </param>
		public Worker(ILogger<Worker> logger)
		{
			try
			{
				_logger             = logger;
				_adaptiveLogHandler = _adaptiveLogHandler = new AdaptiveLogHandler<LogType>(Path.Combine(Paths.ExecuterPath, "Logs"));
				_adaptiveLogHandler.AddLog(LogType.All);
				_adaptiveLogHandler.AddLog(LogType.WindowsService);
				_adaptiveLogHandler.AddLog(LogType.Scheduler);
				_adaptiveLogHandler.AddLog(LogType.Storage);
				_adaptiveLogHandler.AddLog(LogType.Network);
				_adaptiveLogHandler.LoggedEntry += _adaptiveLogHandler_LoggedEntry;

				try
				{
					bool bRuleExists = false;
					Type? policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
					if (policyType != null)
					{
						string firewallRuleName  = Assembly.GetExecutingAssembly().GetName().Name + " inbound rule";
						string port              = "61321";
						INetFwPolicy2? fwPolicy2 = Activator.CreateInstance(policyType) as INetFwPolicy2;
						if (fwPolicy2 != null)
						{
							foreach (INetFwRule rule in fwPolicy2.Rules)
							{
								// Add rule to list
								// RuleList.Add(rule);
								// Console.WriteLine(rule.Version);
								if (rule.Name == firewallRuleName && rule.LocalPorts.Contains(port))
								{
									bRuleExists = true;
									break;
								}
							}

							if (!bRuleExists)
								if (IsAdministrator())
									AddFirewallRule(fwPolicy2, firewallRuleName, port);
								else
								{
									Log("Failed to access firewall to request rule adding. The service is not running under administrator privileges. Restart the service once as an administrator to ensure that the firewall is properly configured.", LogType.WindowsService);
								}
						}
					}
					else
					{
						Log("Failed to access firewall to request rule adding. It seems like that the windows firewall is either not running or corrupted. Please check the running state of the firewall and restart the server.", LogType.WindowsService);
					}
				}
				catch (Exception ex)
				{
					Log("Failed to add to firewall" + Environment.NewLine + ex.ToString(), LogType.WindowsService);
				}

				ProtocolSettings.AllowEmptySends  = true;
				ProtocolSettings.CallDataTransfer = false;
				ProtocolSettings.TcpNoDelay       = true;

				// Will be overwritten anyway
				lock (_serviceSettingsLock)
					_serviceSettings = new ServiceSettings();

				TaskScheduler.TaskScheduler.HistoryChanged += TaskScheduler_HistoryChanged;
				_schedulerService.ScheduleTriggered        += _schedulerService_ScheduleTriggered;
			}
			catch (Exception ex)
			{
				// Do not reference the local variable here as we cannot assure that the logger is set
				logger.LogError(ex.ToString());
				throw;
			}
		}

		public override Task StartAsync(CancellationToken cancellationToken)
		{
			try
			{
				_adaptiveLogHandler.StartHandler();
				Log("Attempting to start service", LogType.WindowsService);
			}
			// Do not escalate this at all
			catch { }

			if (File.Exists(_serviceSettingsPath))
			{
				string settingsJson = File.ReadAllText(_serviceSettingsPath);
				lock (_serviceSettingsLock)
					_serviceSettings = settingsJson.FromCryJson<ServiceSettings>() ?? new ServiceSettings();
			}
			else
			{
				lock (_serviceSettingsLock)
					_serviceSettings = new ServiceSettings();
			}

			if (_tcpListener is null)
			{
				_tcpListener                  = new TcpListenerServer(8080);
				_tcpListener.ClientConnected += _tcpListener_ClientConnected;
				_tcpListener.Log             += _tcpListener_Log;

				// We only allow connections to the localhost.
				// It could be extended to external access, but would require more setup.
				if (_tcpListener.Start(System.Net.IPAddress.Parse("127.0.0.1")).Result == false)
				{
					Log("Could not start interface listener", LogType.WindowsService, 0);
					Environment.Exit(ErrorCodes.InterfaceListenerFailed);
				}
			}

			TaskScheduler.TaskScheduler.StartScheduler();
			string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
			Storage.StorageTransferTask transferTask = new Storage.StorageTransferTask(new StorageTransferSettings(Path.Combine(desktopPath, "A"), Path.Combine(desktopPath, "B\\Inner B"), Path.Combine("B\\~revisionmeta.data"), Path.Combine(desktopPath, "B\\Revisions"), false, true, false, 2));
			TaskScheduler.TaskScheduler.AddTaskToQueue(transferTask);

			_schedulerService.LoadSchedule();
			_schedulerService.StartScheduler();

			// Add test schedule
			// _schedulerService.AddToSchedule(new Schedule(
			//		Guid.NewGuid(),
			//		"Test Schedule",
			//		new List<DayOfWeek>()
			// {
			//	DayOfWeek.Monday,
			//	DayOfWeek.Wednesday,
			//	DayOfWeek.Friday,
			//	DayOfWeek.Saturday,
			//	DayOfWeek.Sunday,
			// },
			//		new DateTime(1970, 1, 1, 14, 30, 0, DateTimeKind.Utc),
			//		ScheduleType.StorageTransfer
			//		));

			try
			{
				Log("Started service", LogType.WindowsService);
			}
			// Do not escalate this at all
			catch { }

			return base.StartAsync(cancellationToken);
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			// We do not actually do anything here as everything is event based or time-based with the scheduler.
			// We could wait infinite here for the token to be canceled.
			while (!stoppingToken.IsCancellationRequested)
			{
				// _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
				await Task.Delay(10000, stoppingToken);
			}
		}

		private void _SaveSettings()
		{
			try
			{
				string tmp = _serviceSettings.ToCryJson();
				File.WriteAllText(_serviceSettingsPath, tmp);
			}
			catch (Exception ex)
			{
				try
				{
					Log(ex.ToString(), LogType.WindowsService, 0);
				}
				// Do not escalate this at all
				catch { }

				throw;
			}
		}

		public override Task StopAsync(CancellationToken cancellationToken)
		{
			try
			{
				Log("Attempting to stop service", LogType.WindowsService);
			}
			// Do not escalate this at all
			catch { }

			try
			{
				_SaveSettings();
			}
			// We do not escalate exception in the stop sequence
			catch
			{
			}

			if (_tcpListener is not null)
			{
				// Wait 60 seconds for the listener to stop
				_tcpListener.Stop().Wait(TimeSpan.FromSeconds(60));
				_tcpListener.ClientConnected -= _tcpListener_ClientConnected;
				_tcpListener.Log             -= _tcpListener_Log;
				_tcpListener                  = null;
			}

			// Thats a deadlock
			// lock (_clientsLock)
			{
				foreach (CommonProtocolBase prot in _clients)
				{
					// Each client can have its own issues, but we want to ensure that all clients get disconnected
					try
					{
						prot.Disconnect();
					}
					catch (Exception ex)
					{
						Log(ex.ToString(), LogType.WindowsService, 0);
					}
				}
			}

			TaskScheduler.TaskScheduler.StopScheduler();

			// Do not escalate this at all
			try
			{
				Log("Stopped service", LogType.WindowsService);
			}
			catch { }

			try
			{
				_adaptiveLogHandler.StopHandler();
			}
			catch { }

			return base.StopAsync(cancellationToken);
		}

		/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary>   Logs the given info to the provided logger type as well as to the windows event system. </summary>
		///
		/// <param name="info">  The information. </param>
		/// <param name="type">  (Optional) The type. </param>
		/// <param name="level"> (Optional) The level. </param>
		/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
		private void Log(string info, LogType type = LogType.Storage, int level = 3)
		{
			if (level == 0)
				_logger.LogError(info);
			else if (level == 1)
				_logger.LogWarning(info);

			_adaptiveLogHandler.Log(type, info);
			_adaptiveLogHandler.Log(LogType.All, info);
		}

		private void _tcpListener_Log(TcpListenerServer listener, string info, ELogStatus status)
		{
			try
			{
				if (status == ELogStatus.INFO)
					Log(info, LogType.Network, 2);
				else if (status == ELogStatus.WARNING)
					Log(info, LogType.Network, 1);
				else
					Log(info, LogType.Network, 0);
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void _tcpListener_ClientConnected(TcpListenerServer listener, System.Net.Sockets.TcpClient client)
		{
			try
			{
				CommonProtocolBase prot = new CommonProtocolBase();
				prot.ProtocolLog  += Prot_ProtocolLog;
				prot.Disconnected += Prot_Disconnected;
				prot.ReceivedData += Prot_ReceivedData;

				// With that the client should not be accessed anymore outside of the protocol.
				prot.Listen(client);

				lock (_clientsLock)
					_clients.Add(prot);
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void Prot_ReceivedData(CommonProtocolBase prot, System.Net.IPEndPoint? remoteEndPoint, CommonData data)
		{
			try
			{
				if (data.Data is null)
					return;

				if (data.Descriptor == RequestType.ListOfTasks)
					SendListOfTasks(prot);
				else if (data.Descriptor == RequestType.ListOfSchedules)
					SendSchedules(prot);
				else if (data.Descriptor == RequestType.GetSettings)
					SendSettings(prot);
				else if (data.Descriptor == RequestType.SetSettings)
				{
					// We do nothing if the send data is not of type string
					if (data.Data is not string stringData)
						return;

					SetSettings(prot, stringData);
				}
				else if (data.Descriptor == RequestType.RestoreBackup)
				{
					// We do nothing if the send data is not of type string
					if (data.Data is not string stringData)
						return;

					RestoreBackup(prot, stringData);
				}
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void SendTaskUpdate(CommonProtocolBase prot, TaskScheduler.Task updatedTask)
		{
			try
			{
				TaskData data = new TaskData()
				{
					Name = updatedTask.GetType().Name,
					ID   = updatedTask.ID,
					Info = updatedTask.GetInfo()
				};

				// Never block the protocol here to send data back. This is not a request based connection.
				Task.Run(() =>
				{
					try
					{
						if (prot.Connected)
							prot.Send(RequestType.TaskUpdated, data.ToCryJson());
					}
					catch (Exception ex)
					{
						Log(ex.ToString(), LogType.Network, 2);
					}
				});
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void SendListOfTasks(CommonProtocolBase prot)
		{
			try
			{
				TaskDataCollection col = TaskScheduler.TaskScheduler.GetTasks();

				// Never block the protocol here to send data back. This is not a request based connection.
				Task.Run(() =>
				{
					try
					{
						if (prot.Connected)
							prot.Send(RequestType.ListOfTasks, col.ToCryJson());
					}
					catch (Exception ex)
					{
						Log(ex.ToString(), LogType.Network, 2);
					}
				});
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void SendSettings(CommonProtocolBase prot)
		{
			try
			{
				string settingsJson;
				lock (_serviceSettingsLock)
					settingsJson = _serviceSettings.ToCryJson();

				// Never block the protocol here to send data back. This is not a request based connection.
				Task.Run(() =>
				{
					try
					{
						if (prot.Connected)
							prot.Send(RequestType.GetSettings, settingsJson);
					}
					catch (Exception ex)
					{
						Log(ex.ToString(), LogType.Network, 2);
					}
				});
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void SetSettings(CommonProtocolBase prot, string stringData)
		{
			try
			{
				ServiceSettings? settings = stringData.FromCryJson<ServiceSettings>();

				if (settings is null)
				{
					// Never block the protocol here to send data back. This is not a request based connection.
					Task.Run(() =>
					{
						try
						{
							// Resent the new settings, kinda a confirmation
							if (prot.Connected)
								prot.Send(RequestType.InvalidNewSettings, "");
						}
						catch (Exception ex)
						{
							Log(ex.ToString(), LogType.Network, 2);
						}
					});
					return;
				}

				string settingsJson;
				lock (_serviceSettingsLock)
				{
					_serviceSettings = settings;
					settingsJson     = _serviceSettings.ToCryJson();
				}

				// Never block the protocol here to send data back. This is not a request based connection.
				Task.Run(() =>
				{
					try
					{
						// Resent the new settings, kinda a confirmation
						if (prot.Connected)
							prot.Send(RequestType.GetSettings, settingsJson);
					}
					catch (Exception ex)
					{
						Log(ex.ToString(), LogType.Network, 2);
					}
				});
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void RestoreBackup(CommonProtocolBase prot, string stringData)
		{
			try
			{
				StorageRestoreSettings? restoreSettings = stringData.FromCryJson<StorageRestoreSettings>();

				if (restoreSettings is null)
				{
					// Never block the protocol here to send data back. This is not a request based connection.
					Task.Run(() =>
					{
						try
						{
							// Resent the new settings, kinda a confirmation
							if (prot.Connected)
								prot.Send(RequestType.InvalidNewSettings, "");
						}
						catch (Exception ex)
						{
							Log(ex.ToString(), LogType.Network, 2);
						}
					});
					return;
				}

				// Never block the protocol here.
				Task.Run(() =>
				{
					try
					{
						Storage.StorageRestoreTask restoreTask = new Storage.StorageRestoreTask(restoreSettings);
						TaskScheduler.TaskScheduler.AddTaskToQueue(restoreTask);
					}
					catch (Exception ex)
					{
						Log(ex.ToString(), LogType.Network, 2);
					}
				});
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void SendSchedules(CommonProtocolBase prot)
		{
			try
			{
				Schedule[] schedules = _schedulerService.GetSchedules();
				string schedulesJson = schedules.ToCryJson();

				// Never block the protocol here to send data back. This is not a request based connection.
				Task.Run(() =>
				{
					try
					{
						if (prot.Connected)
							prot.Send(RequestType.ListOfSchedules, schedulesJson);
					}
					catch (Exception ex)
					{
						Log(ex.ToString(), LogType.Network, 2);
					}
				});
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void SendUpdate(CommonProtocolBase prot, string info)
		{
			try
			{
				// Never block the protocol here to send data back. This is not a request based connection.
				Task.Run(() =>
				{
					try
					{
						if (prot.Connected)
							prot.Send(RequestType.Update, info);
					}
					catch (Exception ex)
					{
						Log(ex.ToString(), LogType.Network, 2);
					}
				});
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void AddFirewallRule(INetFwPolicy2 fwPolicy2, string firewallRuleName, string port)
		{
			try
			{
				var currentProfiles = fwPolicy2.CurrentProfileTypes;

				Type? ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
				if (ruleType is null)
					return;

				INetFwRule2? xInboundRule = Activator.CreateInstance(ruleType) as INetFwRule2;
				if (xInboundRule is null)
					return;

				xInboundRule.Enabled = true;

				// Configure rule
				xInboundRule.Action     = NET_FW_ACTION_.NET_FW_ACTION_ALLOW;
				xInboundRule.Protocol   = 6;	// TCP
				xInboundRule.LocalPorts = port;
				xInboundRule.Name       = firewallRuleName;
				xInboundRule.Profiles   = currentProfiles;

				// Add the rule
				Type? policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
				if (policyType is null)
					return;

				INetFwPolicy2? xFirewallPolicy = Activator.CreateInstance(policyType) as INetFwPolicy2;
				if (xFirewallPolicy is null)
					return;

				xFirewallPolicy.Rules.Add(xInboundRule);
			}
			catch
			{
				throw;
			}
		}

		private static bool RunAsAdmin()
		{
			if (IsAdministrator() == false)
			{
				// Restart program and run as admin
				var exeName = Process.GetCurrentProcess().MainModule?.FileName;
				if (exeName == null)
					throw new Exception("Could not get main module filename");

				ProcessStartInfo startInfo = new ProcessStartInfo(exeName)
				{
					Verb = "runas"
				};

				Process.Start(startInfo);
				Environment.Exit(0);
				return false;
			}

			return true;
		}

		private static bool IsAdministrator()
		{
			// Check if this process is elevated
			WindowsIdentity identity   = WindowsIdentity.GetCurrent();
			WindowsPrincipal principal = new WindowsPrincipal(identity);
			return principal.IsInRole(WindowsBuiltInRole.Administrator);
		}

		private void Prot_ProtocolLog(CommonProtocolBase prot, string info)
		{
			try
			{
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void Prot_Disconnected(CommonProtocolBase prot, System.Net.IPEndPoint? remoteEndPoint)
		{
			try
			{
				lock (_clientsLock)
					if (_clients.Contains(prot))
						_clients.Remove(prot);
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void TaskScheduler_HistoryChanged(TaskScheduler.Task info)
		{
			try
			{
				lock (_clientsLock)
					foreach (var client in _clients)
					{
						SendTaskUpdate(client, info);
						SendUpdate(client, $"Task updated -> {info.ID}");
					}
			}
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void _schedulerService_ScheduleTriggered(ScheduleService sender, ScheduleType scheduleType, ISettings? settings)
		{
			try
			{
				if (scheduleType == ScheduleType.StorageTransfer)
				{
					if (settings is null || settings is not StorageTransferSettings transferSettings)
						return;

					Storage.StorageTransferTask transferTask = new Storage.StorageTransferTask(transferSettings);
					TaskScheduler.TaskScheduler.AddTaskToQueue(transferTask);
				}
				else
					Log("Unknown schedule type", LogType.Scheduler);
			}
			// Exceptions should not escalate back to the event invoker
			catch (Exception ex)
			{
				Log(ex.ToString(), LogType.Network, 2);
			}
		}

		private void _adaptiveLogHandler_LoggedEntry(LogType type, string entry)
		{
			try
			{
				lock (_clientsLock)
					foreach (var client in _clients)
						SendUpdate(client, entry);
			}
			// Exceptions should not escalate back to the event invoker
			catch (Exception ex)
			{
				_logger.LogError(ex.ToString());
			}
		}
	}
}
