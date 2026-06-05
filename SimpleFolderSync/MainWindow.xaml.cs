using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SimpleFolderSync;

public partial class MainWindow : Window
{
    private readonly string[] _actions = new[] { "Choose Source", "Choose Target", "Build Sync Plan" };
    private readonly string[] _features = new[] { "Source and target folder setup", "Copy/update/delete planning", "Conflict review surface", "Local-only sync notes" };

    public MainWindow()
    {
        InitializeComponent();
        AddActivity("Initial Store app scaffold loaded.");
        AddActivity("Configured scope: " + string.Join("; ", _features.Take(2)) + ".");
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        var label = (sender as Button)?.Content?.ToString() ?? "Action";
        var stepNumber = Array.IndexOf(_actions, label) + 1;
        AddActivity(stepNumber > 0
            ? $"{label}: starter workflow step {stepNumber} queued."
            : $"{label}: starter workflow queued.");
    }

    private void AddActivity(string message)
    {
        ActivityList.Items.Insert(0, $"{DateTime.Now:t} - {message}");
        StatusText.Text = message;
    }
}