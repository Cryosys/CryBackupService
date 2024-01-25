namespace CryBackup.CommonData
{
	public static class RequestType
	{
		public static readonly string
			TaskUpdated        = nameof(TaskUpdated),
			ListOfTasks        = nameof(ListOfTasks),
			GetSettings        = nameof(GetSettings),
			SetSettings        = nameof(SetSettings),
			InvalidNewSettings = nameof(InvalidNewSettings),
			RestoreBackup      = nameof(RestoreBackup),
			ListOfSchedules    = nameof(ListOfSchedules),
			AlteredSchedule    = nameof(AlteredSchedule),
			Update             = nameof(Update);
	}
}
