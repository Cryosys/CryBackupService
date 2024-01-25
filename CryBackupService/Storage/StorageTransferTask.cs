using CryBackup.CommonData;
using CryBackupService.Storage.Metadata;

using CryLib.Core;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Windows.Security.Credentials;

namespace CryBackupService.Storage
{
	internal class StorageTransferTask : TaskScheduler.Task
	{
		internal override event TaskScheduler.TaskEventHandler? TaskStateChanged;

		private PasswordVault _vault      = new PasswordVault();
		private CredentialCache _netCache = new CredentialCache();

		private AdaptiveLogHandler<TransferLog> _logHandler;
		private readonly string _logPath = Path.Combine(Paths.ExecuterPath, "TransferTask Logs", $"{DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss")}");

		private StorageTransferSettings _settings;
		private ulong _filesChecked = 0u;
		private string _currentFile = "";
		private CryBackup.CommonData.TaskInfo _info;
		private readonly DateTime _creationTime = DateTime.UtcNow;

		private RevisionCollection? _revisionCol = null;

		internal StorageTransferTask(StorageTransferSettings settings)
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
					throw new Exception("Paused is not allowed for the transfer task");

				// The task cannot start or pause again if the state is failed or succeeded
				// In this case we ignore the start call
				if (_info.Status == CryBackup.CommonData.TaskStatus.Failed || _info.Status == CryBackup.CommonData.TaskStatus.Succeded)
					return;

				_SetState(CryBackup.CommonData.TaskStatus.Running, "");

				bool? isInitial = AdvanceRevision();

				if (_info.Status == CryBackup.CommonData.TaskStatus.Failed)
					return;

				if (isInitial is null || _revisionCol is null)
				{
					_SetState(CryBackup.CommonData.TaskStatus.Failed, "Could not advance the revision. See the log for more infos: " + _logPath);
					return;
				}

				// It is very important that this bool is always valid and any case that the bool could be null is handled beforehand and it NEVER gets here.
				if (isInitial is not null)
					// We do not save the revision collection if the transfer failed for some reason as it could corrupt the hierarchy.
					// The local data will still be present tho.
					if (!_Transfer(isInitial ?? false))
						return;

				// Delete old revisions that do not fit the data retention settings
				_CleanUpOldRevisions();

				// Save the revision collection even if empty and no revision was added.
				_SaveRevisionCollection();

				_SetState(CryBackup.CommonData.TaskStatus.Succeded, "Finished backup");
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

		/// <summary>
		/// Advances the revision. It is allowed to call this method multi times.
		/// </summary>
		private bool? AdvanceRevision()
		{
			bool isInitialRevision = true;

			// Check if there is a revision collection
			if (_revisionCol is null)
			{
				if (System.IO.File.Exists(_settings.RevisionCollectionMetaDataPath))
				{
					try
					{
						// The file may be really large and exceed the size limit
						using (FileStream fileStream = new FileStream(_settings.RevisionCollectionMetaDataPath, FileMode.Open, FileAccess.Read, FileShare.None))
							_revisionCol = JsonExtensions.FromCryJson<RevisionCollection>(fileStream);

						if (_revisionCol is null)
						{
							LogError(new Exception("The local metadata for the revision collections seems to be corrupt. The files data could not be parsed."));
							return null;
						}

						isInitialRevision = false;
					}
					catch (Exception ex)
					{
						LogError(new Exception("The local metadata for the revision collections seems to be corrupt.", ex));
						return null;
					}
				}
				// Create a collection if it does not exist
				else
					_revisionCol = new RevisionCollection();
			}

			// Do not advance if there is no first revision. That is so that we do not advance by accident more than once.
			// We do not accept "empty" or non existent revisions
			if (_revisionCol.Revisions.Find(rev => rev.Age == 1) is not null)
			{
				// Increment the revision Age to advance the current revision by 1.
				// By doing it like that we actually do not have to move any data locally around to match the revisions
				// and can simply delete revisions that exceed the specified limit.
				foreach (Revision rev in _revisionCol.Revisions)
					rev.Age++;
			}

			return isInitialRevision;
		}

		private bool _Transfer(bool isInitial)
		{
			if (_revisionCol is null)
			{
				LogError("RevisionCollection is null in TransferTask");
				_SetState(CryBackup.CommonData.TaskStatus.Failed, "RevisionCollection is null in TransferTask");
				return false;
			}

			_filesChecked = 0u;
			_currentFile  = "";

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
			else if (!Directory.Exists(_settings.TargetPath))
				Directory.CreateDirectory(_settings.TargetPath);

			_AuthenticateNetwork(_settings.SourcePath);
			_AuthenticateNetwork(_settings.TargetPath);

			Revision? rev          = null;
			string? fullFolderPath = null;

			if (!isInitial)
			{
				rev = new Revision()
				{
					// The ID will always be 1 here.
					Age = 1,
					// The timestamp should always be UTC to be independent of the local time
					Timestamp = DateTime.UtcNow,
				};

				// Get valid folder name
				do
				{
					rev.FolderName = Guid.NewGuid().ToString();
				}
				while (Directory.Exists(fullFolderPath = Path.Combine(_settings.RevisionStoragePath, rev.FolderName)));

				Directory.CreateDirectory(fullFolderPath);

				// Try to always keep the newest revision on top. This is not necessary and not guaranteed.
				_revisionCol.Revisions.Insert(0, rev);
			}

			if (rev is not null && fullFolderPath is null)
			{
				LogError("Something went wrong while trying to get a valid revision folder path");
				_SetState(CryBackup.CommonData.TaskStatus.Failed, "Something went wrong while trying to get a valid revision folder path");
				return false;
			}

			try
			{
				// Does not expect all target files on the source side, but all source files on the target side
				compare(_settings.SourcePath, _settings.TargetPath);
				_currentFile = "";
			}
			catch (Exception ex)
			{
				LogError(ex);
				_SetState(CryBackup.CommonData.TaskStatus.Failed, "Something went wrong while trying to compare source and target");
				return false;
			}

			return true;

			// <summary> Recursively compares two folders to determine if the target does not match the source. </summary>
			// <param name="path"> Full pathname of the file. </param>
			// <returns>   A string[]. </returns>
			void compare(string sourcePath, string targetPath)
			{
				try
				{
					string[] sourceFolders = Directory.GetDirectories(sourcePath);
					MetaData? metaData     = _GetMetaData(targetPath);

					if (metaData is null)
					{
						metaData = new MetaData();
						metaData.BuildMetaData(targetPath);
						_SaveMetaData(targetPath, metaData);
					}

					foreach (string sourceFolder in sourceFolders)
					{
						// Check if the target contains the source folder
						string? sourceFolderName = new DirectoryInfo(sourceFolder).Name ?? throw new Exception("Cannot compare root path");
						string newTargetPath     = Path.Combine(targetPath, sourceFolderName);
						if (!metaData.Directories.Any(targetFolder => targetFolder == sourceFolderName))
						{
							// The folder does not exist and we need to copy it
							var failedFiles = _Copy(sourceFolder, newTargetPath);
							if (failedFiles.Count > 0)
							{
								string[] failedSourceFiles = failedFiles.Select(file => file.Item1).ToArray();
								LogError("Files failed to copy:" + Environment.NewLine + string.Join(Environment.NewLine, failedSourceFiles));
								// #TODO how to handle that
							}

							metaData.Changed = true;
							continue;
						}
						compare(sourceFolder, newTargetPath);
					}

					string[] sourceFiles = Directory.GetFiles(sourcePath);

					List<(string, string)> missingFiles = new List<(string, string)>();

					foreach (string sourceFile in sourceFiles)
					{
						// Update file info
						if (_filesChecked % 100 == 0)
							_currentFile = sourceFile;

						string sourceFileName = Path.GetFileName(sourceFile);
						string targetFile     = Path.Combine(targetPath, sourceFileName);

						if (_settings.IgnoreThumbsDB && sourceFileName == "Thumbs.db")
						{
							_filesChecked++;
							continue;
						}

						if (!metaData.Files.Any(targetFile => targetFile.Name == sourceFileName))
						{
							missingFiles.Add((sourceFile, targetFile));

							_filesChecked++;
							continue;
						}

						byte[] ? sourceFileHash = null;
						byte[] ? targetFileHash = null;

						if (_settings.EnableHashCompare)
						{
							// Cuts time in half
							Task sourceTask = Task.Run(() =>
							{
								try
								{
									sourceFileHash = _GetFileHash(sourceFile);
								}
								catch (Exception ex)
								{
									LogError(ex);
								}
							});

							Task targetTask = Task.Run(() =>
							{
								try
								{
									targetFileHash = _GetFileHash(targetFile);
								}
								catch (Exception ex)
								{
									LogError(ex);
								}
							});

							Task.WaitAll(sourceTask, targetTask);

							if (sourceFileHash is null || targetFileHash is null || !_CompareHash(sourceFileHash, targetFileHash))
								missingFiles.Add((sourceFile, targetFile));
						}
						else
						{
							long sourceFileSize = new FileInfo(sourceFile).Length;
							long targetFileSize = new FileInfo(targetFile).Length;

							if (sourceFileSize != targetFileSize)
								missingFiles.Add((sourceFile, targetFile));
						}

						_filesChecked++;
					}

					if (missingFiles.Count > 0)
					{
						if (rev is not null)
							for (int i = 0; i < missingFiles.Count; i++)
							{
								string revisionSourceFile = missingFiles[i].Item2;
								if (!System.IO.File.Exists(revisionSourceFile))
									continue;

								string fileName      = Path.GetFileName(revisionSourceFile);
								RevisionData revData = new RevisionData(revisionSourceFile, fileName, false, new FileInfo(revisionSourceFile).Length);

								// Get a valid name for the file
								string tempPath;
								do
								{
									revData.TemporalName = Guid.NewGuid().ToString();
								}
								while (System.IO.File.Exists(tempPath = Path.Combine(fullFolderPath, revData.TemporalName)));

								_Copy(revisionSourceFile, tempPath);
								rev.RevisionData.Add(revData);
							}

						// Item1 is source files, Item2 is target files
						List<(string, string)> failedFiles = _Copy(missingFiles.Select(path => path.Item1).ToArray(), missingFiles.Select(path => path.Item2).ToArray());

						if (failedFiles.Count > 0)
						{
							string[] failedSourceFiles = failedFiles.Select(file => file.Item1).ToArray();
							LogError("Files failed to copy:" + Environment.NewLine + string.Join(Environment.NewLine, failedSourceFiles));
							// #TODO how to handle that
						}

						metaData.Changed = true;
					}

					if (!_settings.KeepDeletedFilesAndDirectories)
					{
						// Delete folders that do not exist on source anymore
						foreach (var targetDir in metaData.Directories)
							if (!sourceFolders.Any(dir => new DirectoryInfo(dir).Name == targetDir))
							{
								string combPath = Path.Combine(targetPath, targetDir);
								if (Directory.Exists(combPath))
								{
									if (rev is not null)
									{
										RevisionData revData = new RevisionData(combPath, targetDir, true, 0);

										// Get a valid name for the directory
										string tempPath;
										do
										{
											revData.TemporalName = Guid.NewGuid().ToString();
										}
										while (Directory.Exists(tempPath = Path.Combine(fullFolderPath, revData.TemporalName)));

										_Copy(combPath, tempPath);
										rev.RevisionData.Add(revData);
									}

									Directory.Delete(combPath, true);
								}

								metaData.Changed = true;
							}

						// Delete files that do not exist on source anymore
						foreach (var targetFile in metaData.Files)
							if (!sourceFiles.Any(file => Path.GetFileName(file) == targetFile.Name))
							{
								string combPath = Path.Combine(targetPath, targetFile.Name);
								if (System.IO.File.Exists(combPath))
								{
									if (rev is not null)
									{
										RevisionData revData = new RevisionData(combPath, targetFile.Name, false, new FileInfo(combPath).Length);

										// Get a valid name for the file
										string tempPath;
										do
										{
											revData.TemporalName = Guid.NewGuid().ToString();
										}
										while (System.IO.File.Exists(tempPath = Path.Combine(fullFolderPath, revData.TemporalName)));

										_Copy(combPath, tempPath);
										rev.RevisionData.Add(revData);
									}

									System.IO.File.Delete(combPath);
								}
								metaData.Changed = true;
							}
					}

					// Build the metadata again if it is flagged as changed
					if (metaData.Changed)
					{
						metaData.BuildMetaData(targetPath);
						_SaveMetaData(targetPath, metaData);
					}
				}
				catch (Exception ex)
				{
					LogError(ex);
				}
			}
		}

		private void _SaveRevisionCollection()
		{
			if (_revisionCol is null)
				return;

			// The file may be really large and exceed the size limit
			using (FileStream fileStream = new FileStream(_settings.RevisionCollectionMetaDataPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
				_revisionCol.ToCryJson(fileStream);
		}

		private void _CleanUpOldRevisions()
		{
			if (_revisionCol is null)
				return;

			List<Revision> revisions = _revisionCol.Revisions;
			revisions = revisions.OrderBy(rev => rev.Age).ToList();

			for (int i = revisions.Count - 1; i >= 0; i--)
			{
				// THE AGE IS NOT 0 BASED, NEITHER IS THE DATA RETENSION VALUE
				if (revisions[i].Age > _settings.DataRetention)
				{
					// Delete the folder if the revision exceeds the data retention
					string revisionPath = Path.Combine(_settings.RevisionStoragePath, revisions[i].FolderName);

					if (Directory.Exists(revisionPath))
						Directory.Delete(revisionPath, true);
					revisions.RemoveAt(i);
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

		private void _SaveMetaData(string targetPath, MetaData metaData)
		{
			if (!metaData.Changed)
				return;

			string metaDataPath = Path.Combine(targetPath, GlobalStatics.MetaDataName);
			string metaDataJson = metaData.ToCryJson();
			System.IO.File.WriteAllText(metaDataPath, metaDataJson);
			metaData.Changed = false;
		}

		private byte[] _GetFileHash(string path)
		{
			using (FileStream xFileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
				return SHA1.Create().ComputeHash(xFileStream);
		}

		private bool _CompareHash(byte[] sourceHash, byte[] targetHash) => sourceHash.SequenceEqual(targetHash);

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

		private bool SaveUserCreds(string domain, string sUserName, string sPW)
		{
			if (HasUserData(domain))
			{
				DeleteCredential(domain, sUserName);
			}

			PasswordCredential xCredentials;
			IntPtr pPtr = IntPtr.Zero;
			try
			{
				xCredentials = new PasswordCredential(resource: domain,
						userName: sUserName,
						password: sPW.Trim());
			}
			finally
			{
				Marshal.ZeroFreeGlobalAllocUnicode(pPtr);
			}

			_vault.Add(xCredentials);

			return true;
		}

		private void DeleteCredential(string domain, string user)
		{
			PasswordCredential xCredentials = _vault.Retrieve(domain, user);
			_vault.Remove(xCredentials);
		}

		private bool HasUserData(string domain) => TryGetCredentials(domain) != null ? true : false;

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

		private void _SetState(CryBackup.CommonData.TaskStatus state, string info)
		{
			_info = new CryBackup.CommonData.TaskInfo(state, info, _creationTime, DateTime.UtcNow);
			TaskStateChanged?.Invoke(this, new CryBackup.CommonData.TaskInfo(_info));
		}
	}

	internal enum TransferLog
	{
		Protocol,
		Errors
	}
}
