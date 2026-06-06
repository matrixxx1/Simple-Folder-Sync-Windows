using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace SimpleFolderSync;

public partial class MainWindow : Window
{
    private enum SyncMode
    {
        OneWay,
        TwoWay
    }

    private enum PlannedActionType
    {
        AddOrCopy,
        Update,
        Delete,
        Conflict
    }

    private readonly ObservableCollection<string> _activity = new();
    private readonly ObservableCollection<SyncPlanItem> _plans = new();
    private static readonly string[] DefaultExcludeDirs = new[] { ".git", "bin", "obj", ".vs", "node_modules" };

    public MainWindow()
    {
        InitializeComponent();
        PlanGrid.ItemsSource = _plans;
        ActivityList.ItemsSource = _activity;
        AddActivity("Simple Folder Sync loaded.");
        AddActivity("Build a plan before apply to review every change.");
        AddActivity("Backups are stored in: Backups\\SimpleFolderSync\\yyyyMMddHHmmss");
    }

    private void OnChooseSource(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder(SourcePathTextBox.Text);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SourcePathTextBox.Text = folder;
            AddActivity($"Source set to: {folder}");
        }
    }

    private void OnChooseTarget(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder(TargetPathTextBox.Text);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            TargetPathTextBox.Text = folder;
            AddActivity($"Target set to: {folder}");
        }
    }

    private string? PickFolder(string current)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Select folder",
            ShowNewFolderButton = true
        };

        if (Directory.Exists(current))
        {
            dlg.SelectedPath = current;
        }

        return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.SelectedPath : null;
    }

    private async void OnBuildPlan(object sender, RoutedEventArgs e)
    {
        _plans.Clear();
        AddActivity("Building sync plan...");
        var source = SourcePathTextBox.Text;
        var target = TargetPathTextBox.Text;

        if (!Directory.Exists(source) || !Directory.Exists(target))
        {
            AddActivity("Please choose valid source and target folders first.");
            return;
        }

        try
        {
            var plan = await BuildPlanAsync(source, target);
            foreach (var item in plan)
            {
                _plans.Add(item);
            }
            SummaryText.Text = BuildSummary(plan);
            AddActivity($"Plan built with {plan.Count} entries.");
        }
        catch (Exception ex)
        {
            AddActivity($"Failed to build plan: {ex.Message}");
        }
    }

    private async void OnApplyPlan(object sender, RoutedEventArgs e)
    {
        if (_plans.Count == 0)
        {
            AddActivity("No plan to apply. Build a plan first.");
            return;
        }

        if (global::System.Windows.MessageBox.Show(
                "Apply this plan now? Conflicts will be skipped by default unless manually resolved by editing files.",
                "Apply Sync Plan",
                global::System.Windows.MessageBoxButton.YesNo,
                global::System.Windows.MessageBoxImage.Warning) != global::System.Windows.MessageBoxResult.Yes)
        {
            AddActivity("Apply cancelled by user.");
            return;
        }

        var backupRoot = GetOrCreateBackupFolder();
        var executed = 0;
        foreach (var item in _plans.ToList())
        {
            try
            {
                if (item.Action == PlannedActionType.Conflict)
                {
                    AddActivity($"Skipping conflict: {item.RelativePath}");
                    continue;
                }

                ApplyPlanItem(item, backupRoot);
                executed++;
                AddActivity($"Applied: {item.Action} - {item.RelativePath}");
            }
            catch (Exception ex)
            {
                AddActivity($"Failed {item.RelativePath}: {ex.Message}");
            }
        }

        AddActivity($"Apply complete. {executed} operations executed.");
        var refreshed = await BuildPlanAsync(source: SourcePathTextBox.Text, target: TargetPathTextBox.Text);
        _plans.Clear();
        foreach (var item in refreshed)
        {
            _plans.Add(item);
        }
        SummaryText.Text = $"{BuildSummary(refreshed)} (after apply)";
        AddActivity("Plan refreshed after apply.");
    }

    private void OnClearLog(object sender, RoutedEventArgs e)
    {
        _activity.Clear();
        AddActivity("Log cleared.");
    }

    private string BuildSummary(IReadOnlyList<SyncPlanItem> plan)
    {
        var copies = plan.Count(x => x.Action == PlannedActionType.AddOrCopy);
        var updates = plan.Count(x => x.Action == PlannedActionType.Update);
        var deletes = plan.Count(x => x.Action == PlannedActionType.Delete);
        var conflicts = plan.Count(x => x.Action == PlannedActionType.Conflict);
        return $"Plan totals: {copies} copy, {updates} update, {deletes} delete, {conflicts} conflict.";
    }

    private string GetOrCreateBackupFolder()
    {
        if (BackupBeforeWrite?.IsChecked != true)
        {
            return string.Empty;
        }

        var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups", DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        return root;
    }

    private async Task<List<SyncPlanItem>> BuildPlanAsync(string source, string target)
    {
        return await Task.Run(() => BuildPlan(source, target));
    }

    private List<SyncPlanItem> BuildPlan(string source, string target)
    {
        var mode = SyncModeCombo.SelectedIndex == 1 ? SyncMode.TwoWay : SyncMode.OneWay;
        var includeHidden = IncludeHiddenFiles?.IsChecked == true;
        var extensionSpec = ExtensionFilterTextBox.Text;
        var allowedExtensions = ParseExtensions(extensionSpec);

        var sourceFiles = ListFiles(source, includeHidden, allowedExtensions);
        var targetFiles = ListFiles(target, includeHidden, allowedExtensions);
        var sourceByName = sourceFiles.ToDictionary(i => i.RelativePath, StringComparer.OrdinalIgnoreCase);
        var targetByName = targetFiles.ToDictionary(i => i.RelativePath, StringComparer.OrdinalIgnoreCase);
        var allKeys = new HashSet<string>(sourceByName.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var key in targetByName.Keys)
        {
            allKeys.Add(key);
        }

        var results = new List<SyncPlanItem>();
        foreach (var key in allKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            if (!sourceByName.TryGetValue(key, out var sourceItem))
            {
                if (mode == SyncMode.OneWay)
                {
                    if (DeleteMissingTarget?.IsChecked == true)
                    {
                        results.Add(new SyncPlanItem(key, PlannedActionType.Delete, "Delete target-only", targetPath: Path.Combine(target, key), details: "Only in target; delete to mirror source."));
                    }
                    else
                    {
                        AddActivity($"Ignored target-only file in one-way mode: {key}");
                    }
                    continue;
                }

                // two-way: copy target file to source
                if (targetByName.TryGetValue(key, out var missingFromSourceInTwoWay))
                {
                    results.Add(new SyncPlanItem(
                        key,
                        PlannedActionType.AddOrCopy,
                        "Copy target → source",
                        targetPath: missingFromSourceInTwoWay.FullPath,
                        sourcePath: Path.Combine(source, key),
                        details: "Item exists only in target."));
                }
                continue;
            }

            if (!targetByName.TryGetValue(key, out var targetItem))
            {
                results.Add(new SyncPlanItem(
                    key,
                    PlannedActionType.AddOrCopy,
                    "Copy source → target",
                    sourcePath: sourceItem.FullPath,
                    targetPath: Path.Combine(target, key),
                    details: "New item in target."));
                continue;
            }

            if (!FilesMatch(sourceItem, targetItem))
            {
                if (mode == SyncMode.TwoWay)
                {
                    var sourceWrite = sourceItem.LastWriteUtc;
                    var targetWrite = targetItem.LastWriteUtc;
                    if (Math.Abs((sourceWrite - targetWrite).TotalSeconds) < 2)
                    {
                        results.Add(new SyncPlanItem(key, PlannedActionType.Conflict, "Conflict", sourcePath: sourceItem.FullPath, targetPath: targetItem.FullPath, details: "Same timestamp but different content."));
                    }
                    else if (sourceWrite > targetWrite)
                    {
                        results.Add(new SyncPlanItem(key, PlannedActionType.Update, "Copy newer source → target", sourcePath: sourceItem.FullPath, targetPath: targetItem.FullPath, details: $"Source is newer ({sourceWrite:u})."));
                    }
                    else
                    {
                        results.Add(new SyncPlanItem(key, PlannedActionType.Update, "Copy newer target → source", sourcePath: targetItem.FullPath, targetPath: sourceItem.FullPath, details: $"Target is newer ({targetWrite:u})."));
                    }
                }
                else
                {
                    var action = sourceItem.LastWriteUtc > targetItem.LastWriteUtc ? "Update target" : "Copy source → target";
                    results.Add(new SyncPlanItem(key, PlannedActionType.Update, action, sourcePath: sourceItem.FullPath, targetPath: targetItem.FullPath, details: "Source/target differ."));
                }
            }
        }

        return results;
    }

    private bool FilesMatch(FileMetadata sourceItem, FileMetadata targetItem)
    {
        if (sourceItem.Length != targetItem.Length)
        {
            return false;
        }

        if (sourceItem.LastWriteUtc == targetItem.LastWriteUtc)
        {
            return true;
        }

        using var sourceHash = SHA256.Create();
        using var targetHash = SHA256.Create();
        var sourceHashString = Convert.ToHexString(sourceHash.ComputeHash(File.ReadAllBytes(sourceItem.FullPath)));
        var targetHashString = Convert.ToHexString(targetHash.ComputeHash(File.ReadAllBytes(targetItem.FullPath)));
        return string.Equals(sourceHashString, targetHashString, StringComparison.Ordinal);
    }

    private void ApplyPlanItem(SyncPlanItem item, string backupRoot)
    {
        switch (item.Action)
        {
            case PlannedActionType.AddOrCopy:
            case PlannedActionType.Update:
                if (item.SourcePath is not null && item.TargetPath is not null)
                {
                    var targetParent = Path.GetDirectoryName(item.TargetPath);
                    if (!Directory.Exists(targetParent))
                    {
                        Directory.CreateDirectory(targetParent!);
                    }

                    if (!string.IsNullOrWhiteSpace(backupRoot) && File.Exists(item.TargetPath))
                    {
                        Backup(item.TargetPath, backupRoot);
                    }

                    File.Copy(item.SourcePath, item.TargetPath, overwrite: true);
                }
                break;
            case PlannedActionType.Delete:
                if (item.SourcePath is not null && File.Exists(item.SourcePath) && !string.IsNullOrWhiteSpace(backupRoot))
                {
                    Backup(item.SourcePath, backupRoot);
                }

                if (item.SourcePath is not null && Directory.Exists(item.SourcePath))
                {
                    Directory.Delete(item.SourcePath, recursive: true);
                }
                else if (item.SourcePath is not null)
                {
                    File.Delete(item.SourcePath);
                }
                break;
        }
    }

    private void Backup(string sourcePath, string backupRoot)
    {
        if (string.IsNullOrWhiteSpace(backupRoot)) return;

        var relativePath = Path.GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, sourcePath)
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');
        var dest = Path.Combine(backupRoot, relativePath);
        var destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrWhiteSpace(destDir))
        {
            Directory.CreateDirectory(destDir);
        }
        File.Copy(sourcePath, dest, overwrite: true);
    }

    private static List<FileMetadata> ListFiles(string root, bool includeHidden, HashSet<string> allowedExtensions)
    {
        var all = new List<FileMetadata>();
        var rootInfo = new DirectoryInfo(root);

        foreach (var file in rootInfo.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (!includeHidden && (file.Attributes.HasFlag(FileAttributes.Hidden) || file.Attributes.HasFlag(FileAttributes.System)))
            {
                continue;
            }

            if (!ShouldProcessPath(file.DirectoryName!))
            {
                continue;
            }

            var extension = file.Extension.ToLowerInvariant();
            if (!allowedExtensions.Contains("*.*") && !allowedExtensions.Contains(extension))
            {
                continue;
            }

            all.Add(new FileMetadata
            {
                FullPath = file.FullName,
                RelativePath = Path.GetRelativePath(root, file.FullName),
                Length = file.Length,
                LastWriteUtc = file.LastWriteTimeUtc
            });
        }

        return all;
    }

    private static bool ShouldProcessPath(string directoryName)
    {
        return !DefaultExcludeDirs.Any(ex => directoryName.Contains(Path.DirectorySeparatorChar + ex, StringComparison.OrdinalIgnoreCase) ||
                                              directoryName.Contains(Path.AltDirectorySeparatorChar + ex, StringComparison.OrdinalIgnoreCase));
    }

    private HashSet<string> ParseExtensions(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*.*" };
        }

        var tokens = input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrWhiteSpace(t));
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (token == "*.*" || token == "*" )
            {
                normalized.Add("*.*");
                continue;
            }

            if (token.StartsWith("*."))
            {
                normalized.Add(token[1..]);
                continue;
            }

            normalized.Add(token.StartsWith('.') ? token : "." + token);
        }

        if (normalized.Count > 0)
        {
            return normalized;
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*.*" };
    }

    private void AddActivity(string message)
    {
        var entry = $"{DateTime.Now:t} {message}";
        _activity.Insert(0, entry);
        if (_activity.Count > 400)
        {
            _activity.RemoveAt(_activity.Count - 1);
        }
    }

    private class FileMetadata
    {
        public string FullPath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public long Length { get; init; }
        public DateTime LastWriteUtc { get; init; }
    }

    private sealed class SyncPlanItem
    {
        public SyncPlanItem(string relativePath, PlannedActionType action, string direction, string? sourcePath = null, string? targetPath = null, string details = "")
        {
            RelativePath = relativePath;
            Action = action;
            Direction = direction;
            SourcePath = sourcePath;
            TargetPath = targetPath;
            Details = details;
        }

        public string RelativePath { get; }
        public PlannedActionType Action { get; }
        public string Direction { get; }
        public string? SourcePath { get; }
        public string? TargetPath { get; }
        public string Details { get; }
    }
}
