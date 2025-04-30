using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

class Program
{
  // --- Konfiguration ---
  // Hardcoded Folder für Logs (ändere nach Belieben)
  static readonly string LogFolder = @"C:\ActivityLogs";
  // Intervalle
  const int ActiveWindowIntervalMs = 30 * 1000;  // alle 30 Sekunden
  const int HeartbeatIntervalMs    = 5  * 60 * 1000;  // alle 5 Minuten

  // --- Laufzeitvariablen ---
  static DateTime? _unlockTime;
  static long     _totalUnlockedSeconds = 0;
  static bool     _isUnlocked           = false;
  static string   _lastWindowTitle      = "";
  static Timer    _windowTimer, _heartbeatTimer;
  static readonly object _fileLock = new object();

  static void Main()
  {
    Directory.CreateDirectory(LogFolder);
    // Session-Lock/Unlock abonnieren
    SystemEvents.SessionSwitch += OnSessionSwitch;
    LogEvent("ApplicationStart", "");
    
    // Variablen initialisieren, aber kein SessionStart-Log
    _unlockTime = DateTime.Now;
    _isUnlocked = true;

    // Timer für aktives Fenster
    _windowTimer = new Timer(_ => LogActiveWindow(), null, 0, ActiveWindowIntervalMs);
    // Timer für Heartbeat
    _heartbeatTimer = new Timer(_ => WriteHeartbeat(), null, HeartbeatIntervalMs, HeartbeatIntervalMs);

    Console.WriteLine("Tracking läuft. Mit Strg+C beenden.");
    // clean exit on Ctrl+C
    var exitEvent = new ManualResetEvent(false);
    Console.CancelKeyPress += (s,e) =>
    {
      e.Cancel = true;
      exitEvent.Set();
    };
    exitEvent.WaitOne();

    // aufräumen und letzte Zusammenfassung
    _windowTimer.Dispose();
    _heartbeatTimer.Dispose();
    SystemEvents.SessionSwitch -= OnSessionSwitch;
    WriteFinalSummary();
    LogEvent("ApplicationClose", "");
    Console.WriteLine("Beendet.");
  }

  static void OnSessionSwitch(object s, SessionSwitchEventArgs e)
  {
    if (e.Reason == SessionSwitchReason.SessionUnlock)
    {
      _unlockTime   = DateTime.Now;
      _isUnlocked   = true;
      LogEvent("SessionUnlock", "");
    }
    else if (e.Reason == SessionSwitchReason.SessionLock)
    {
      if (_unlockTime.HasValue)
      {
        _unlockTime = null;
      }
      _isUnlocked = false;
      LogEvent("SessionLock", "");
    }
  }

  static void LogActiveWindow()
  {
    if (!_isUnlocked) return;

    string title = GetActiveWindowTitle();
    if (string.IsNullOrEmpty(title) || title == _lastWindowTitle) return;
    _lastWindowTitle = title;
    LogEvent("ActiveWindow", title.Replace("\r"," ").Replace("\n"," "));
  }

  static void WriteHeartbeat()
  {
    TimeSpan ts = TimeSpan.FromSeconds(-1);
    if (_unlockTime.HasValue)
    {
      ts = DateTime.Now - _unlockTime.Value;
    }
    LogEvent("Heartbeat", $"TotalUnlocked={ts:hh\\:mm\\:ss}");
  }

  static void WriteFinalSummary()
  {
    var ts = TimeSpan.FromSeconds(_totalUnlockedSeconds);
    LogEvent("FinalSummary", $"TotalUnlocked={ts:hh\\:mm\\:ss}");
  }

  static void LogEvent(string evt, string detail)
  {
    var line = $"{DateTime.Now:O}\t{evt}\t{detail}";
    var file = Path.Combine(LogFolder, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
    lock(_fileLock)
    {
      File.AppendAllText(file, line + Environment.NewLine);
    }
  }

  // ----------------------------------------------------
  // Win32-Import, um das aktive Fenster auszulesen
  [DllImport("user32.dll")]
  static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxLength);

  static string GetActiveWindowTitle()
  {
    var sb = new System.Text.StringBuilder(1024);
    var hwnd = GetForegroundWindow();
    if (hwnd == IntPtr.Zero) return null;
    int len = GetWindowText(hwnd, sb, sb.Capacity);
    return len > 0 ? sb.ToString() : null;
  }
}