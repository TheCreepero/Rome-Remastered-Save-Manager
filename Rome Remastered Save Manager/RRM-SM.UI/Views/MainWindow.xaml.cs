using System.Windows;
using System.Windows.Input;
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private void BackupsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                if (e.Key == Key.Enter && vm.RestoreCommand.CanExecute(null))
                {
                    vm.RestoreCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete && vm.DeleteBackupCommand.CanExecute(null))
                {
                    vm.DeleteBackupCommand.Execute(null);
                    e.Handled = true;
                }
            }
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
