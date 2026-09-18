using System.Windows;

namespace RRM_SM.UI.Views
{
    public partial class InputDialog : Window
    {
        public string ResponseText => InputTextBox.Text;

        public InputDialog(string title, string message)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
            Loaded += (s, e) =>
            {
                InputTextBox.Focus();
                InputTextBox.SelectAll();
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
