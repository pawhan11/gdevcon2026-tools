using System.Collections.Generic;

namespace TestStandCLI.Services;

using System;
using TestStandCLI.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

public class DirectoryFileMonitor : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _directoryPath;
    private readonly string _fileFilter;
    private readonly ConcurrentDictionary<string, long> _filePositions;
    private bool _disposed;

    // Event that fires when new data is written to any file
    public event EventHandler<FileDataEventArgs> DataWritten;
    
    // Event that fires when a new file is created
    public event EventHandler<FileCreatedEventArgs> FileCreated;

    public DirectoryFileMonitor(string directoryPath, string fileFilter = "*.*", bool deleteExistingFiles = false)
    {
        if (string.IsNullOrEmpty(directoryPath))
            throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        _directoryPath = directoryPath;
        _fileFilter = fileFilter;
        _filePositions = new ConcurrentDictionary<string, long>();

        if (deleteExistingFiles)
        {
            // Clear existing files if specified
            DeleteExistingFiles();
        }

        // Initialize positions for existing files
        _filePositions = new ConcurrentDictionary<string, long>();
        
        // Initialize tracking for existing files
        InitializeExistingFiles();

        // Set up FileSystemWatcher for the directory
        _watcher = new FileSystemWatcher(_directoryPath, _fileFilter)
        {
            NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
            EnableRaisingEvents = false,
            IncludeSubdirectories = false // Set to true if you want to monitor subdirectories
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileCreated;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
    }
    
    private void DeleteExistingFiles()
    {
        try
        {
            var files = Directory.GetFiles(_directoryPath, _fileFilter);
            foreach (var filePath in files)
            {
                try
                {
                    File.Delete(filePath);
                    ConsoleLogger.Debug("Deleted file: {0}", Path.GetFileName(filePath));
                }
                catch (Exception ex)
                {
                    ConsoleLogger.Error("Failed to delete file {0}: {1}", Path.GetFileName(filePath), ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleLogger.Error("Error clearing existing files: {0}", ex.Message);
        }
    }

    private void InitializeExistingFiles()
    {
        try
        {
            var files = Directory.GetFiles(_directoryPath, _fileFilter);
            foreach (var filePath in files)
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Exists)
                {
                    _filePositions[filePath] = fileInfo.Length;
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleLogger.Error("Error initializing existing files: {0}", ex.Message);
        }
    }

    public void StartMonitoring()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DirectoryFileMonitor));

        _watcher.EnableRaisingEvents = true;
    }

    public void StopMonitoring()
    {
        if (!_disposed)
        {
            _watcher.EnableRaisingEvents = false;
        }
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Small delay to ensure file creation is complete
            await Task.Delay(200);

            var filePath = e.FullPath;
            
            // Initialize position tracking for new file
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Exists)
            {
                _filePositions[filePath] = 0; // Start from beginning for new files
                
                // Raise file created event
                FileCreated?.Invoke(this, new FileCreatedEventArgs(filePath, DateTime.Now));
                
                // Check if there's initial content and raise data written event
                var initialData = ReadNewData(filePath);
                if (!string.IsNullOrEmpty(initialData))
                {
                    DataWritten?.Invoke(this, new FileDataEventArgs(filePath, initialData, DateTime.Now));
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleLogger.Error("Error handling file creation: {0}", ex.Message);
        }
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Small delay to ensure the writing process is complete
            
            var filePath = e.FullPath;
            var newData = ReadNewData(filePath);
            
            if (!string.IsNullOrEmpty(newData))
            {
                DataWritten?.Invoke(this, new FileDataEventArgs(filePath, newData, DateTime.Now));
            }
        }
        catch (Exception ex)
        {
            ConsoleLogger.Error("Error reading file changes: {0}", ex.Message);
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        // Remove from tracking when file is deleted
        _filePositions.TryRemove(e.FullPath, out _);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Update tracking when file is renamed
        if (_filePositions.TryRemove(e.OldFullPath, out long position))
        {
            _filePositions[e.FullPath] = position;
        }
    }

    private string ReadNewData(string filePath)
    {
        try
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var currentLength = fileStream.Length;
                var lastPosition = _filePositions.GetValueOrDefault(filePath, 0);
                
                // If file was truncated, reset position
                if (currentLength < lastPosition)
                {
                    lastPosition = 0;
                    _filePositions[filePath] = 0;
                }

                // If no new data, return empty
                if (currentLength <= lastPosition)
                {
                    return string.Empty;
                }

                // Read only the new data
                fileStream.Seek(lastPosition, SeekOrigin.Begin);
                var buffer = new byte[currentLength - lastPosition];
                var bytesRead = fileStream.Read(buffer, 0, buffer.Length);

                // Update position for next read
                _filePositions[filePath] = currentLength;

                // Convert to string (assuming UTF-8, adjust encoding if needed)
                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
        }
        catch (FileNotFoundException)
        {
            // File was deleted while we were trying to read it
            _filePositions.TryRemove(filePath, out _);
            return string.Empty;
        }
        catch (IOException)
        {
            // File might be locked by the writing application
            // Return empty and try again on next change
            return string.Empty;
        }

    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _watcher?.Dispose();
            _filePositions?.Clear();
            _disposed = true;
        }
    }
}

// Event arguments for file data changes
public class FileDataEventArgs : EventArgs
{
    public string FilePath { get; }
    public string NewData { get; }
    public DateTime Timestamp { get; }

    public FileDataEventArgs(string filePath, string newData, DateTime timestamp)
    {
        FilePath = filePath;
        NewData = newData;
        Timestamp = timestamp;
    }
}

// Event arguments for new file creation
public class FileCreatedEventArgs : EventArgs
{
    public string FilePath { get; }
    public DateTime Timestamp { get; }

    public FileCreatedEventArgs(string filePath, DateTime timestamp)
    {
        FilePath = filePath;
        Timestamp = timestamp;
    }

}