using System.Windows;
using System.Windows.Controls;
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

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.RestoreCommand.CanExecute(null))
            {
                vm.RestoreCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                row.IsSelected = true;
                row.Focus();
            }
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                bool hasSelection = vm.SelectedBackup != null && !vm.IsBusy;
                MenuRestore.IsEnabled = hasSelection;
                MenuOpenExplorer.IsEnabled = vm.SelectedBackup != null;
                MenuCopyPath.IsEnabled = vm.SelectedBackup != null;
                MenuDelete.IsEnabled = hasSelection;
            }
        }

        private void ContextMenu_Restore_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.RestoreCommand.CanExecute(null))
            {
                vm.RestoreCommand.Execute(null);
            }
        }

        private void ContextMenu_OpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.OpenSelectedInExplorerCommand.CanExecute(null))
            {
                vm.OpenSelectedInExplorerCommand.Execute(null);
            }
        }

        private void ContextMenu_CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.CopyBackupPathCommand.CanExecute(null))
            {
                vm.CopyBackupPathCommand.Execute(null);
            }
        }

        private void ContextMenu_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.DeleteBackupCommand.CanExecute(null))
            {
                vm.DeleteBackupCommand.Execute(null);
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
