using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using RRM_SM.Models;

namespace RRM_SM.UI.Views
{
    public partial class EditSaveDialog : Window
    {
        public string SaveTitle { get; private set; } = string.Empty;
        public string Notes { get; private set; } = string.Empty;
        public List<string> Tags { get; private set; } = new();
        public bool IsPinned { get; private set; }

        public EditSaveDialog(BackupEntry backup)
        {
            InitializeComponent();

            string turnInfo = backup.Turn.HasValue ? $" • Turn {backup.Turn.Value}" : "";
            FileInfoText.Text = $"{backup.OriginalGameFileName} (Campaign: {backup.CampaignName}{turnInfo})";

            TitleBox.Text = backup.Name != backup.OriginalGameFileName ? backup.Name : "";
            NotesBox.Text = backup.Notes ?? "";
            TagsBox.Text = backup.Tags != null ? string.Join(", ", backup.Tags) : "";
            PinnedCheckBox.IsChecked = backup.IsPinned;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveTitle = TitleBox.Text.Trim();
            Notes = NotesBox.Text.Trim();
            Tags = TagsBox.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            IsPinned = PinnedCheckBox.IsChecked == true;

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

