using System.Windows;
using System.Windows.Controls;

namespace CryBackupInterface
{
	public partial class ServiceSettingsPage : Page
	{
		public ServiceSettingsPage()
		{
			InitializeComponent();
		}

		private void CryButton_ButtonClicked(object sender, RoutedEventArgs e)
		{
			Globals.InteractionModel.SendServiceSettings();
		}
	}
}
