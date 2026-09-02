using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using TwitchSubscriberPictures.Core.Abstractions;

namespace TwitchSubscriberPictures.ViewModels;

/// <summary>
/// Writes log entries into an ObservableCollection on the WPF UI thread. This is
/// intentionally non-modal: errors never surface as message boxes or popups.
/// </summary>
public sealed class UiLogSink : IAppLogger
{
    private const int MaxEntries = 1000;
    private readonly object _fileLock = new();
    private readonly string _filePath;

    public UiLogSink(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultFilePath();
        RotateExistingLog();
    }

    public ObservableCollection<LogEntryViewModel> Entries { get; } = new();

    public string FilePath => _filePath;

    public void Log(AppLogLevel level, string message)
    {
        var entry = new LogEntryViewModel(
            DateTimeOffset.Now,
            level.ToString().ToUpperInvariant(),
            message);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            AddEntry(entry);
        }
        else
        {
            dispatcher.InvokeAsync(() => AddEntry(entry));
        }
    }

    private void AddEntry(LogEntryViewModel entry)
    {
        Entries.Add(entry);
        AppendToFile(entry);

        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }

    private void AppendToFile(LogEntryViewModel entry)
    {
        try
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.AppendAllText(_filePath, entry.DisplayText + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the app or surface a popup.
        }
    }

    private static string GetDefaultFilePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TwitchSubscriberPictures",
            "logs");

        return Path.Combine(root, "app.log");
    }

    private void RotateExistingLog()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var rotatedPath = Path.Combine(directory, $"app-{timestamp}.log");
            File.Move(_filePath, rotatedPath, overwrite: true);

            Directory
                .EnumerateFiles(directory, "app-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(5)
                .ToList()
                .ForEach(File.Delete);
        }
        catch
        {
            // Log rotation must never prevent the app from starting.
        }
    }
}
