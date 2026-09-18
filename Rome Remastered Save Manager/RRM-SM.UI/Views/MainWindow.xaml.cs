using System.Windows;
using Microsoft.Win32;
using RRM_SM.UI.ViewModels;

namespace RRM_SM.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var viewModel = new MainViewModel();

            // Wire up dialog callbacks (keeps ViewModel free of WPF UI dependencies)
            viewModel.InputDialogRequested = ShowInputDialog;
            viewModel.FolderBrowserRequested = ShowFolderBrowser;

            DataContext = viewModel;
        }

        private string? ShowInputDialog(string title, string message)
        {
            var dialog = new InputDialog(title, message)
            {
                Owner = this
            };
            bool? result = dialog.ShowDialog();
            return result == true ? dialog.ResponseText : null;
        }

        private string? ShowFolderBrowser(string title, string initialDir)
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
                InitialDirectory = initialDir
            };

            bool? result = dialog.ShowDialog(this);
            return result == true ? dialog.FolderName : null;
        }
    }
}

