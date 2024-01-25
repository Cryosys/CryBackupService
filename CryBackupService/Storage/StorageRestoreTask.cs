using CryBackup.CommonData;
using CryBackupService.Storage.Metadata;
using CryBackupService.TaskScheduler;
using CryLib.Core;
using System.Diagnostics;
using System.Net;
using Windows.Security.Credentials;

namespace CryBackupService.Storage
{
	internal class StorageRestoreTask : TaskScheduler.Task
	{
		internal override event TaskEventHandler? TaskStateChanged;

		private PasswordVault _vault      = new PasswordVault();
		private CredentialCache _netCache = new CredentialCache();

		private AdaptiveLogHandler<TransferLog> _logHandler;
		private readonly string _logPath = Path.Combine(Paths.ExecuterPath, "TransferTask Logs", $"{DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss")}");

		private StorageRestoreSettings _settings;
		private CryBackup.CommonData.TaskInfo _info;
		private readonly DateTime _creationTime = DateTime.UtcNow;

		internal StorageRestoreTask(StorageRestoreSettings settings)
		{
			_SetState(CryBackup.CommonData.TaskStatus.AwaitingStart, "");

			_settings   = settings;
			_logHandler = new AdaptiveLogHandler<TransferLog>(_logPath);
			_logHandler.AddLog(TransferLog.Protocol);
			_logHandler.AddLog(TransferLog.Errors);
		}

		internal override void Start()
		{
			try
			{
				if (_info.Status == CryBackup.CommonData.TaskStatus.Paused)
					throw new Exception("Paused is not allowed for the restore task");

				// The task cannot start or pause again if the state is failed or succeeded
				// In this case we ignore the start call
				if (_info.Status == CryBackup.CommonData.TaskStatus.Failed || _info.Status == CryBackup.CommonData.TaskStatus.Succeded)
					return;

				_SetState(CryBackup.CommonData.TaskStatus.Running, "");

				RevisionCollection? revisionCol = null;

				// Get the available revision versions in the source path(the path where the backup was saved) and check if the wanted revision is available
				if (System.IO.File.Exists(_settings.RevisionCollectionMetaDataPath))
				{
					try
					{
						// The file may be really large and exceed the size limit
						using (FileStream fileStream = new FileStream(_settings.RevisionCollectionMetaDataPath, FileMode.Open, FileAccess.Read, FileShare.None))
							revisionCol = JsonExtensions.FromCryJson<RevisionCollection>(fileStream);

						if (revisionCol is null)
						{
							_SetState(CryBackup.CommonData.TaskStatus.Failed, "The local metadata for the revision collections seems to be corrupt. The files data could not be parsed.");
							return;
						}
					}
					catch (Exception ex)
					{
						_SetState(CryBackup.CommonData.TaskStatus.Failed, "The local metadata for the revision collections seems to be corrupt." + Environment.NewLine + ex.ToString());
						return;
					}
				}
				else
				{
					_SetState(CryBackup.CommonData.TaskStatus.Failed, "Revision metadata path is invalid or file does not exist. Please check the path and try again");
					return;
				}

				Revision? revision = null;

				// Check if the revision were saved correctly and check if the revision exists
				// There should be no revision if that is the very first revision
				if (revisionCol.Revisions.Count == 0 || (revision = revisionCol.Revisions.Find(rev => rev.Age == _settings.RevisionVersion)) is null)
				{
					_SetState(CryBackup.CommonData.TaskStatus.Failed, "Either the backup data is invalid or the given revision for the restore procedure could not be found.");
					return;
				}

				// Restore the backup
				_Restore(revision);

				if (_info.Status == CryBackup.CommonData.TaskStatus.Failed)
					return;

				_SetState(CryBackup.CommonData.TaskStatus.Succeded, "Finished restore");
			}
			catch (Exception ex)
			{
				_SetState(CryBackup.CommonData.TaskStatus.Failed, ex.ToString());
			}
		}

		internal override TaskInfo GetInfo()
		{
			return new TaskInfo(_info);
		}

		private bool _Restore(Revision revision)
		{
			// Ask for the path credentials if necessary
			if (_settings.SourcePath is null)
			{
				LogError("Source path was not set");
				_SetState(CryBackup.CommonData.TaskStatus.Failed, "Source path was not set");
				return false;
			}
			if (_settings.TargetPath is null)
			{
				LogError("Target path was not set");
				_SetState(CryBackup.CommonData.TaskStatus.Failed, "Target path was not set");
				return false;
			}

			_AuthenticateNetwork(_settings.SourcePath);
			_AuthenticateNetwork(_settings.TargetPath);

			// Check if the target path is empty, only allow restore if overwrite is allowed
			if ((Directory.GetDirectories(_settings.TargetPath).Length > 0 || Directory.GetFiles(_settings.TargetPath).Length > 0) && !_settings.AllowOverwrite)
			{
				_SetState(CryBackup.CommonData.TaskStatus.Failed, "Target path is not empty and overwrite is not allowed, canceled restore.");
				return false;
			}

			bool invalidMetaData = false;
			bool fileCopyFailed  = false;
			restore(_settings.SourcePath, _settings.TargetPath);

			// There should only every be one state that is returned.
			// The file copy error takes priority as it could already indicate what was the error
			if (fileCopyFailed)
			{
				_SetState(CryBackup.CommonData.TaskStatus.Failed, "Failed to copy files, see logs for more information.");
				return false;
			}
			else if (invalidMetaData)
			{
				_SetState(CryBackup.CommonData.TaskStatus.Failed, "Metadata in the source path was invalid, that may indicate corrupt data. The restore was canceled.");
				return false;
			}

			// 1 is always the base revision
			if (_settings.RevisionVersion >= 2)
			{
				for (int revVer = 2; revVer < _settings.RevisionVersion; revVer++)
				{
				}

				// Path for the selected revision
				string revisionFolderPath = Path.Combine(_settings.RevisionStoragePath, revision.FolderName);

				// After the restore we need to copy the revision as it could overwrite data
				foreach (RevisionData revisionData in revision.RevisionData)
				{
					string sourcePath = Path.Combine(revisionFolderPath, revisionData.TemporalName);
					string targetPath = Path.Combine(_settings.TargetPath, revisionData.OriginalPath);

					var failedFiles = _Copy(sourcePath, targetPath);
					if (failedFiles.Count > 0)
					{
						string[] failedSourceFiles = failedFiles.Select(file => file.Item1).ToArray();
						fileCopyFailed = true;
						LogError("Files failed to copy:" + Environment.NewLine + string.Join(Environment.NewLine, failedSourceFiles));
						_SetState(CryBackup.CommonData.TaskStatus.Failed, "Failed to copy files, see logs for more information.");
						return false;
					}
				}
			}

			return true;

			void restore(string sourcePath, string targetPath)
			{
				/// With this we make sure that only the data is restored that was actually stored in the backup
				/// and not data that was kept because of <see cref="StorageTransferSettings.KeepDeletedFilesAndDirectories"/>
				MetaData? metaData = _GetMetaData(sourcePath);

				if (metaData is null)
				{
					invalidMetaData = true;
					return;
				}

				foreach (string sourceFolderName in metaData.Directories)
				{
					if (string.IsNullOrEmpty(sourceFolderName))
					{
						invalidMetaData = true;
						return;
					}

					// Only create the folder and go into it to copy
					string newTargetPath = Path.Combine(targetPath, sourceFolderName);
					Directory.CreateDirectory(newTargetPath);

					// var failedFiles      = _Copy(sourceFolder, newTargetPath);
					// if (failedFiles.Count > 0)
					// {
					//	string[] failedSourceFiles = failedFiles.Select(file => file.Item1).ToArray();
					//	LogError("Files failed to copy:" + Environment.NewLine + string.Join(Environment.NewLine, failedSourceFiles));
					//	// #TODO how to handle that
					// }

					restore(Path.Combine(sourcePath, sourceFolderName), newTargetPath);
					if (invalidMetaData || fileCopyFailed)
						return;
				}

				List<string> sourceFiles = new List<string>();
				List<string> targetFiles = new List<string>();

				foreach (File file in metaData.Files)
				{
					sourceFiles.Add(Path.Combine(sourcePath, file.Name));
					targetFiles.Add(Path.Combine(targetPath, file.Name));
				}

				if (sourceFiles.Count > 0)
				{
					List<(string, string)> failedFiles = _Copy(sourceFiles.ToArray(), targetFiles.ToArray());

					if (failedFiles.Count > 0)
					{
						string[] failedSourceFiles = failedFiles.Select(file => file.Item1).ToArray();
						fileCopyFailed = true;
						LogError("Files failed to copy:" + Environment.NewLine + string.Join(Environment.NewLine, failedSourceFiles));
						return;
					}
				}
			}
		}

		private MetaData? _GetMetaData(string targetPath)
		{
			string metaDataPath = Path.Combine(targetPath, GlobalStatics.MetaDataName);
			if (System.IO.File.Exists(metaDataPath))
			{
				string metaDataJson = System.IO.File.ReadAllText(metaDataPath);
				return metaDataJson.FromCryJson<MetaData>();
			}

			return null;
		}

		private List<(string, string)> _Copy(string[] sourcePaths, string[] targetPaths)
		{
			string sourcePath = string.Join(ITask.Separator, sourcePaths);
			string targetPath = string.Join(ITask.Separator, targetPaths);

			return _Copy(sourcePath, targetPath);
		}

		private List<(string, string)> _Copy(string sourcePaths, string targetPaths)
		{
			var task = new CryLib.Core.Tasks.DirectCopyTask();
			task.AwaitStart(new object[] { sourcePaths, targetPaths }).Wait();
			return task.GetFailedFiles();
		}

		private void _AuthenticateNetwork(string path)
		{
			if (path.StartsWith("\\\\"))
			{
				string sTemp = path;

				string[] sDNSSplit = sTemp.Substring("\\\\".Length).Split("\\");
				if (sDNSSplit.Length == 0)
					throw new Exception("Cannot get the IP address of the path");

				string sDNS = sDNSSplit[0];

				IPAddress[] axAddresses;

				try
				{
					axAddresses = Dns.GetHostAddresses(sDNS);
				}
				catch (System.Net.Sockets.SocketException sockEx)
				{
					throw new Exception("Something went wrong while getting the host addresses", sockEx);
				}

				if (axAddresses.Length == 0)
					throw new Exception("The path does not seem to be correct, it cannot be accessed.");

				// Check if the entered credentials are valid and returns if not
				if (!_Authenticate(axAddresses.First(), sDNS, path) ?? false)
					throw new Exception("The credentials are either wrong or the path is incorrect." +
							"This can also indicate that you do not have permissions to access this path.");
			}
		}

		private bool? _Authenticate(IPAddress xAddress, string domain, string sTestPath)
		{
			try
			{
				NetworkCredential? xCred = _netCache.GetCredential("\\\\" + xAddress.ToString(), 445, "Negotiate");

				if (xCred == null)
				{
					if (!string.IsNullOrWhiteSpace(domain))
						xCred = TryGetCredentials(domain);

					if (xCred == null)
						xCred = TryGetCredentials(xAddress.ToString());

					string[] asUsernameSplit;

					if (xCred == null)
					{
						throw new Exception("Credentials for the connection are not save in the Windows Credential Manager. Use the interface application to input the credentials.");
					}
					else
					{
						asUsernameSplit = xCred.UserName.Split("\\");
						xCred           = new NetworkCredential(asUsernameSplit[1], xCred.Password, asUsernameSplit[0]);
					}

					_netCache.Add("\\\\" + xAddress.ToString(), 445, "Negotiate", xCred);

					try
					{
						System.IO.Directory.GetDirectories(sTestPath);
					}
					catch
					{
						_netCache.Remove("\\\\" + xAddress.ToString(), 445, "Negotiate");
						throw;
					}
				}

				return true;
			}
			catch
			{
				return false;
			}
		}

		private NetworkCredential? TryGetCredentials(string domain)
		{
			try
			{
				IEnumerable<PasswordCredential> lxFoundCredentials = _vault.FindAllByResource(domain);

				if (lxFoundCredentials?.Count() > 0)
				{
					// Check if there are more than one user and if check if the current logged in user has credentials stored in it??????
					PasswordCredential? xFoundCredential;
					if ((xFoundCredential = lxFoundCredentials.FirstOrDefault()) != null)
					{
						xFoundCredential.RetrievePassword();
						return new NetworkCredential(xFoundCredential.UserName, xFoundCredential.Password);
					}
				}

				return null;
			}
			catch
			{
				return null;
			}
		}

		private void _SetState(CryBackup.CommonData.TaskStatus state, string info)
		{
			_info = new CryBackup.CommonData.TaskInfo(state, info, _creationTime, DateTime.UtcNow);
			TaskStateChanged?.Invoke(this, new CryBackup.CommonData.TaskInfo(_info));
		}

		// This function is not allowed to throw an exception
		private void LogError(string error)
		{
			try
			{
				_logHandler.Log(TransferLog.Protocol, error);
				_logHandler.Log(TransferLog.Errors, error);
			}
			catch
			{
			}
		}

		// This function is not allowed to throw an exception
		private void LogError(Exception? error)
		{
			try
			{
				if (error is null)
					return;

				LogError(error.ToString());
			}
			catch
			{
			}
		}

		// This function is not allowed to throw an exception
		private void Log(string info)
		{
			try
			{
				_logHandler.Log(TransferLog.Protocol, info);
			}
			catch
			{
			}
		}
	}
}
