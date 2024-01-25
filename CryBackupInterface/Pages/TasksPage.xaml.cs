using System.Windows.Controls;

namespace CryBackupInterface
{
    public partial class TasksPage : Page
    {
        public TasksPage()
        {
            InitializeComponent();
        }

        private void CryButton_ButtonClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            Globals.InteractionModel.RequestTasks();
        }
    }
}
