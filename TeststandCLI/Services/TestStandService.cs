using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.TestStand.Interop.API;
using NationalInstruments.TestStand.Interop.SequenceAnalyzer;
using TestStandCLI.Logging;
using Thread = System.Threading.Thread;

namespace TestStandCLI.Services;

public static class TestStandService
{
    private static Engine _engine;
    private static Project _project { get; set; }
    private static bool _analysisComplete = false;
    private static IAnalyzer _analyzer;
    private static System.Windows.Forms.Timer _messageUpdateTimer;

    
    public static void ProtectDirectory(string projectDir, string format = null)
    {
        ConsoleLogger.Information("Encrypting sequences in directory: {0}", projectDir);
        var files = Directory.GetFiles(projectDir, "*.seq", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            ConsoleLogger.Information("No sequences to encrypt");
            return;
        }
        
        StartEngine();
        
        var tokenSource = new CancellationTokenSource();
        var token = tokenSource.Token;
        var monitor = new Task(o =>
        {
            while (!token.IsCancellationRequested)
            {
                if (!_engine.IsUIMessageQueueEmpty)
                {
                    UIMessage msg = _engine.GetUIMessage();
                    ConsoleLogger.Debug("TestStand message callback: {0}", msg.Event);
                    msg.Acknowledge();
                }
            }

        }, token);

        monitor.Start();
        
        
        for (var i = 0; i < files.Length; i++)
        {
            var seqPath = files[i];
            var sequence = _engine.GetSequenceFileEx(seqPath,GetSeqFileOptions.GetSeqFile_DoNotRunLoadCallback);
            var seqProperty = sequence.AsPropertyObjectFile();
            if (seqProperty.Protection != 0)
            {
                ConsoleLogger.Information("Skipping already protected: {0} ({1}/{2})", Path.GetFileName(seqPath), i + 1, files.Length);
                _engine.ReleaseSequenceFileEx(sequence, 0x4);
                continue;
            }
            ConsoleLogger.Information("Protecting: {0} ({1}/{2})", Path.GetFileName(seqPath), i + 1, files.Length);
            if (format is not null)
            {
                seqProperty.FileWritingFormat = format == "xml"
                    ? FileWritingFormats.FileWritingFormat_Xml
                    : FileWritingFormats.FileWritingFormat_Binary;
            }
            seqProperty.Protection = 1;
            seqProperty.Lock(Guid.NewGuid().ToString());
            sequence.Save(seqPath);
            _engine.ReleaseSequenceFileEx(sequence, 0x4);
        }

        tokenSource.Cancel();
        tokenSource.Token.WaitHandle.WaitOne(10000);
        monitor.Wait();
        monitor.Dispose();
        
        ConsoleLogger.Information("Sequence encryption done");
        StopEngine();
    }

    public static void ExecuteSequenceFile(string sequenceFile, string entryPoint)
    {
        ConsoleLogger.Information("Executing sequence file: {0}", sequenceFile);
        var seqPath = Path.GetFullPath(sequenceFile);

        if (!File.Exists(sequenceFile))
        {
            ConsoleLogger.Error("Sequence file {0} not found", seqPath);
            Environment.ExitCode = 1;
            return;
        }
        StartEngine();

        var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        _engine.SearchDirectories.Insert(appDir, 0);
        
        
        var sequence = _engine.GetSequenceFileEx(seqPath,GetSeqFileOptions.GetSeqFile_DoNotRunLoadCallback);

        sequence.LoadModules(0x30, null);

        var execution = _engine.NewExecution(sequence, entryPoint, null, false, 0x1);
        ConsoleLogger.Information("Sequence started");


        var sequenceDone = false;
        var sequenceError = false;
        while (!sequenceDone && !sequenceError)
        {
            if (!_engine.IsUIMessageQueueEmpty)
            {
                UIMessage msg = _engine.GetUIMessage();
                switch (msg.Event)
                {
                    case UIMessageCodes.UIMsg_EndExecution:
                        sequenceDone = true;
                        break;
                    case UIMessageCodes.UIMsg_RuntimeError:
                        sequenceError = true;
                        sequenceDone = true;
                        break;
                    default:
                        break;
                }

                ConsoleLogger.Debug("TestStand message callback: {0}", msg.Event);
                msg.Acknowledge();
            }
        }

        _engine.ReleaseSequenceFileEx(sequence, 0x4);
        StopEngine();

        ConsoleLogger.Information("Sequence execution done");
        if (sequenceError)
        {
            Environment.ExitCode = 1;
        }
    }
    
    
    private static void StartEngine()
    {
        ConsoleLogger.Debug("Sequence engine create started");

        _engine = new Engine();
        _engine.UIMessagePollingEnabled = true;
        _engine.StationOptions.DebugOptions = 0x0;
        _engine.StationOptions.TracingEnabled = false;
        _engine.StationOptions.AllowAutomaticTypeConflictResolution = AllowAutomaticTypeConflictResolutionOptions
            .AllowAutomaticTypeConflictResolutionOption_Always;
        
        _engine.BreakpointsEnabled = false;
        _engine.StationOptions.BreakOnSequenceFailure = false;
        _engine.StationOptions.BreakOnStepFailure = false;

        ConsoleLogger.Debug("Sequence engine create finished");
    }
    
    private static void StopEngine()
    {
        try
        {
            ConsoleLogger.Debug("Stopping sequence engine");
            _engine.UnloadAllModules();
            _engine.ShutDown(true);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(_engine);
            _engine =  null;
            ConsoleLogger.Debug("Sequence engine stopped");
            
        }
        catch
        {
            //
        }
    }

    public static string ReadActiveTestStand()
    {
        var version = "Unknown";
        try
        {
            StartEngine();
            var versionString = _engine.ApplicationVersionString;
            if (versionString is null)
            {
                versionString = _engine.VersionString;
            }
            
            if (versionString is not null)
            {
                var match = Regex.Match(versionString, @"(\d+\.\d+)");
                if (match.Success)
                {
                    version = match.Groups[1].Value;
                }
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            try
            {
                StopEngine();
            }
            catch
            {
                // ignore
            }
        }
        return version;
    }

    public static string ReadLicense()
    {
        StartEngine();
        _engine.AcquireLicense(ApplicationLicenses.ApplicationLicense_SequenceEditor,AcquireLicenseOptions.AcquireLicenseOption_SuppressStartupDialog);
        var license = _engine.GetLicenseDescription();
        StopEngine();
        return license;
    }
}
