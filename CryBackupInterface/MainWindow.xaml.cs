using CryLib;
using CryLib.MVVM;
using CryLib.WPF;
using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace CryBackupInterface
{
	public partial class MainWindow : CryWindowDesignable
	{
		private FileExplorer _explorer;

		public MainWindow()
		{
			Globals.Logger = new MvvMLogHandler();

			InitializeComponent();

			this.Title += " " + Assembly.GetExecutingAssembly().GetName().Version;

			try
			{
				Globals.InteractionModel = new InteractionModel();
			}
			catch (Exception ex)
			{
				Globals.Logger.Add("Error in " + nameof(MainWindow), "Error while creating the interaction model..." + Environment.NewLine + ex.ToString());
				CryMessagebox.Create(ex.ToString());
			}

			_explorer = new FileExplorer();
			_explorer.SetPath(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

			MainFrame.Content = _explorer;
		}

		private void MainFrame_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
		{
			try
			{
				if (e.NavigationMode == System.Windows.Navigation.NavigationMode.Back || e.NavigationMode == System.Windows.Navigation.NavigationMode.Forward)
				{
					e.Cancel = true;
				}
			}
			catch (Exception ex)
			{
				Globals.Logger.Add("Error in " + nameof(MainWindow), "Error while switching frames..." + Environment.NewLine + ex.ToString());
				CryMessagebox.Create(ex.ToString());
			}
		}

		private void Menu_ItemPressed(object sender, RoutedEventArgs e)
		{
			try
			{
				if (e.OriginalSource is not CryLib.WPF.Menu.CryMenuItem item)
					return;

				switch (item.Title)
				{
					case "Explorer":
					{
						MainFrame.Content = _explorer;
						break;
					}
					case "Tasks":
					{
						MainFrame.Content = new TasksPage();
						break;
					}
					case "Service Settings":
					{
						MainFrame.Content = new ServiceSettingsPage();
						break;
					}
					case "Schedules":
					{
						MainFrame.Content = new SchedulesPage();
						break;
					}
					case "Restore":
					{
						MainFrame.Content = new RestorePage();
						break;
					}
					default:
					{
						CryMessagebox.Create("Requested menu entry not recognized!");
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Globals.Logger.Add("Error in " + nameof(MainWindow), "Error while switching menu items..." + Environment.NewLine + ex.ToString());
			}
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			Task.Run(() =>
			{
				try
				{
					Globals.InteractionModel.Connect();
				}
				catch (Exception ex)
				{
					Globals.Logger.Add("Error in " + nameof(MainWindow), "Could not connect to the service... " + Environment.NewLine + ex.ToString());
				}
			});
		}

		private void CryWindowDesignable_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			try
			{
				Globals.InteractionModel.Disconnect();
			}
			catch (Exception ex)
			{
				Globals.Logger.Add("Error in " + nameof(MainWindow), "Could not connect to the service... " + Environment.NewLine + ex.ToString());
			}
		}
	}
}
