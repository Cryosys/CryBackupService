using CryLib;
using System.Windows;
using System.Windows.Controls;

namespace CryBackupInterface
{
	public partial class RestorePage : Page
	{
		public RestorePage()
		{
			InitializeComponent();
		}

		private void CryButton_ButtonClicked(object sender, RoutedEventArgs e)
		{
			if (CryMessagebox.Create("Do you really want to start the restore process with the set settings?", MessageboxStyle.YesNo, "Restore") != MessageboxButton.Yes)
				return;

			Globals.InteractionModel.SendRestore();
		}
	}
}
