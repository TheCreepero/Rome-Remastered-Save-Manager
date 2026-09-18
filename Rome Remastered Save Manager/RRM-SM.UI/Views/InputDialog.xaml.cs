using System.Windows;

namespace RRM_SM.UI.Views
{
    public partial class InputDialog : Window
    {
        public string ResponseText => InputTextBox.Text;

        public InputDialog(string title, string message, string defaultText = "")
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
            if (!string.IsNullOrEmpty(defaultText))
            {
                InputTextBox.Text = defaultText;
            }
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
