using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace RRM_SM.UI.Views
{
    public partial class MergeCampaignDialog : Window
    {
        public string? SelectedTargetCampaign { get; private set; }

        public MergeCampaignDialog(string sourceCampaignName, IEnumerable<string> availableCampaigns)
        {
            InitializeComponent();

            SourceInfoText.Text = $"Merge all saves currently in '{sourceCampaignName}' into:";

            var targets = availableCampaigns
                .Where(c => !string.IsNullOrWhiteSpace(c) && 
                            !c.Equals("All Campaigns", StringComparison.OrdinalIgnoreCase) && 
                            !c.Equals(sourceCampaignName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();

            TargetCampaignCombo.ItemsSource = targets;
            if (targets.Count > 0)
            {
                TargetCampaignCombo.SelectedIndex = 0;
            }
        }

        private void MergeButton_Click(object sender, RoutedEventArgs e)
        {
            if (TargetCampaignCombo.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            {
                SelectedTargetCampaign = selected;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a target campaign to merge into.", "Select Campaign", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
