using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SecsGemClient
{
    public enum GemState
    {
        Offline,
        Local,
        Online
    }

    public enum ProcessingState
    {
        Idle,
        Setup,
        Processing,
        ProcessComplete,
        Paused,
        Aborted
    }

    public enum AlarmSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public enum LogType
    {
        SML,
        General,
        EDA
    }
    // GEM State Manager (SEMI E30 Standards)
    public class GemStateManager
    {
        private readonly object stateLock = new();


        // GEM State Variables
        public GemState CommunicationState { get; private set; }
        public GemState ControlState { get; private set; }
        public ProcessingState ProcessingState { get; private set; }
        public Dictionary<string, object> StatusVariables { get; private set; }
        public List<AlarmData> ActiveAlarms { get; private set; }
        public List<EventData> Events { get; private set; }

        public event EventHandler<GemStateEventArgs>? StateChanged;

        public GemStateManager()
        {
            CommunicationState = GemState.Offline;
            ControlState = GemState.Offline;
            ProcessingState = ProcessingState.Idle;
            StatusVariables = [];
            ActiveAlarms = [];
            Events = [];

            InitializeStatusVariables();
            InitializeEvents();
        }

        private void InitializeStatusVariables()
        {
            // Equipment Constants (ECV) - SEMI E30
            StatusVariables["MDLN"] = "VIRTUAL_HOST_EQ";     // Model Name
            StatusVariables["SOFTREV"] = "1.0.0";            // Software Revision
            StatusVariables["ECID"] = new List<uint> { 1, 2, 3, 4, 5 }; // Equipment Constant IDs

            // Status Variables (SV) - SEMI E30
            StatusVariables["CLOCK"] = DateTime.Now;          // Equipment Clock
            StatusVariables["PPID"] = "";                     // Process Program ID
            StatusVariables["RCMD"] = "";                     // Remote Command
            StatusVariables["CPNAME"] = "";                   // Current Process Name
            StatusVariables["PPPARM"] = new List<object>();   // Process Program Parameters

            // Equipment Status Variables (EQSV) - SEMI E87
            StatusVariables["EqState"] = "IDLE";              // Equipment State
            StatusVariables["PreviousTaskName"] = "";         // Previous Task Name
            StatusVariables["PreviousTaskType"] = "";         // Previous Task Type
            StatusVariables["SubstCount"] = 0;                // Substrate Count
            StatusVariables["SubstHistory"] = new List<string>(); // Substrate History

            // Process Job Management (E40)
            StatusVariables["ControlJobId"] = "";             // Control Job ID
            StatusVariables["ProcessJobId"] = "";             // Process Job ID
            StatusVariables["CarrierInputSpec"] = "";         // Carrier Input Specification
        }

        private void InitializeEvents()
        {
            // Collection Event IDs (CEID) - SEMI E30
            Events.Add(new EventData { EventId = 1, EventName = "Equipment Offline", Enabled = true });
            Events.Add(new EventData { EventId = 2, EventName = "Control State Local", Enabled = true });
            Events.Add(new EventData { EventId = 3, EventName = "Control State Remote", Enabled = true });
            Events.Add(new EventData { EventId = 10, EventName = "Process Started", Enabled = true });
            Events.Add(new EventData { EventId = 11, EventName = "Process Completed", Enabled = true });
            Events.Add(new EventData { EventId = 12, EventName = "Process Aborted", Enabled = true });

            // E87 Substrate Events
            Events.Add(new EventData { EventId = 20, EventName = "Substrate Processed", Enabled = true });
            Events.Add(new EventData { EventId = 21, EventName = "Substrate Removed", Enabled = true });
            Events.Add(new EventData { EventId = 22, EventName = "Recipe Changed", Enabled = true });

            // E40 Process Job Events
            Events.Add(new EventData { EventId = 30, EventName = "Process Job Created", Enabled = true });
            Events.Add(new EventData { EventId = 31, EventName = "Process Job Queued", Enabled = true });
            Events.Add(new EventData { EventId = 32, EventName = "Process Job Setup", Enabled = true });
            Events.Add(new EventData { EventId = 33, EventName = "Process Job Executing", Enabled = true });
            Events.Add(new EventData { EventId = 34, EventName = "Process Job Completed", Enabled = true });
            Events.Add(new EventData { EventId = 35, EventName = "Process Job Aborted", Enabled = true });

            // E90 Substrate Tracking Events
            Events.Add(new EventData { EventId = 40, EventName = "Substrate Location Changed", Enabled = true });
            Events.Add(new EventData { EventId = 41, EventName = "Substrate State Changed", Enabled = true });

            // E94 Control Job Events
            Events.Add(new EventData { EventId = 50, EventName = "Control Job Created", Enabled = true });
            Events.Add(new EventData { EventId = 51, EventName = "Control Job Started", Enabled = true });
            Events.Add(new EventData { EventId = 52, EventName = "Control Job Completed", Enabled = true });
        }

        public void GoOnline()
        {
            lock (stateLock)
            {
                if (ControlState != GemState.Online)
                {
                    var previousState = ControlState;
                    ControlState = GemState.Online;
                    CommunicationState = GemState.Online;

                    UpdateStatusVariable("CLOCK", DateTime.Now);

                    OnStateChanged(new GemStateEventArgs(previousState, ControlState));
                    TriggerEvent(3, "Control State Remote"); // CEID 3
                }
            }
        }

        public void GoOffline()
        {
            lock (stateLock)
            {
                if (ControlState != GemState.Offline)
                {
                    var previousState = ControlState;
                    ControlState = GemState.Offline;

                    UpdateStatusVariable("CLOCK", DateTime.Now);

                    OnStateChanged(new GemStateEventArgs(previousState, ControlState));
                    TriggerEvent(1, "Equipment Offline"); // CEID 1
                }
            }
        }

        public void SetLocal()
        {
            lock (stateLock)
            {
                var previousState = ControlState;
                ControlState = GemState.Local;

                UpdateStatusVariable("CLOCK", DateTime.Now);

                OnStateChanged(new GemStateEventArgs(previousState, ControlState));
                TriggerEvent(2, "Control State Local"); // CEID 2
            }
        }

        public void SetProcessingState(ProcessingState newState)
        {
            lock (stateLock)
            {
                if (ProcessingState != newState)
                {
                    ProcessingState = newState;
                    UpdateStatusVariable("EqState", newState.ToString().ToUpper());
                    UpdateStatusVariable("CLOCK", DateTime.Now);

                    // Trigger appropriate events based on state change
                    switch (newState)
                    {
                        case ProcessingState.Processing:
                            TriggerEvent(10, "Process Started");
                            break;
                        case ProcessingState.Idle:
                            TriggerEvent(11, "Process Completed");
                            break;
                    }
                }
            }
        }

        public void UpdateStatusVariable(string name, object value)
        {
            lock (stateLock)
            {
                StatusVariables[name] = value;
            }
        }

        public object? GetStatusVariable(string name)
        {
            lock (stateLock)
            {
                return StatusVariables.TryGetValue(name, out var value) ? value : null;
            }
        }

        public void SetAlarm(uint alarmId, string alarmText, AlarmSeverity severity = AlarmSeverity.Warning)
        {
            lock (stateLock)
            {
                var existingAlarm = ActiveAlarms.Find(a => a.AlarmId == alarmId);
                if (existingAlarm == null)
                {
                    var alarm = new AlarmData
                    {
                        AlarmId = alarmId,
                        AlarmText = alarmText,
                        Severity = severity,
                        SetTime = DateTime.Now,
                        IsSet = true
                    };

                    ActiveAlarms.Add(alarm);
                    TriggerEvent(100 + alarmId, $"Alarm Set: {alarmText}");
                }
            }
        }

        public void ClearAlarm(uint alarmId)
        {
            lock (stateLock)
            {
                var alarm = ActiveAlarms.Find(a => a.AlarmId == alarmId);
                if (alarm != null)
                {
                    ActiveAlarms.Remove(alarm);
                    TriggerEvent(200 + alarmId, $"Alarm Cleared: {alarm.AlarmText}");
                }
            }
        }

        public void TriggerEvent(uint eventId, string description = "")
        {
            var eventData = Events.Find(e => e.EventId == eventId);
            if (eventData?.Enabled == true)
            {
                eventData.LastTriggered = DateTime.Now;
                eventData.TriggerCount++;

                // In a real implementation, this would send S6F11 message
                OnEventTriggered?.Invoke(this, new EventTriggeredEventArgs(eventId, description));
            }
        }

        // Process Job Management (E40 Support)
        public void CreateProcessJob(string processJobId, string recipe, Dictionary<string, object> parameters)
        {
            UpdateStatusVariable("ProcessJobId", processJobId);
            UpdateStatusVariable("PPID", recipe);
            UpdateStatusVariable("PPPARM", new List<object>(parameters.Values));

            TriggerEvent(30, "Process Job Created");
        }

        public void StartProcessJob(string processJobId)
        {
            UpdateStatusVariable("ProcessJobId", processJobId);
            SetProcessingState(ProcessingState.Processing);

            TriggerEvent(33, "Process Job Executing");
        }

        public void CompleteProcessJob(string processJobId)
        {
            SetProcessingState(ProcessingState.Idle);
            UpdateStatusVariable("ProcessJobId", "");

            TriggerEvent(34, "Process Job Completed");
        }

        // Control Job Management (E94 Support)
        public void CreateControlJob(string controlJobId, List<string> processJobIds)
        {
            UpdateStatusVariable("ControlJobId", controlJobId);
            TriggerEvent(50, "Control Job Created");
        }

        public void StartControlJob(string controlJobId)
        {
            TriggerEvent(51, "Control Job Started");
        }

        public void CompleteControlJob(string controlJobId)
        {
            UpdateStatusVariable("ControlJobId", "");
            TriggerEvent(52, "Control Job Completed");
        }

        // Substrate Tracking (E90 Support)
        public void UpdateSubstrateLocation(string substrateId, string newLocation)
        {
            var substHistory = GetStatusVariable("SubstHistory") as List<string> ?? new List<string>();
            substHistory.Add($"{substrateId}:{newLocation}:{DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            if (substHistory.Count > 100) // Keep last 100 entries
                substHistory.RemoveAt(0);

            UpdateStatusVariable("SubstHistory", substHistory);
            TriggerEvent(40, "Substrate Location Changed");
        }

        public void ProcessSubstrate(string substrateId, string recipe)
        {
#pragma warning disable CS8605 // Unboxing a possibly null value.
            var count = (int)GetStatusVariable("SubstCount") + 1;
#pragma warning restore CS8605 // Unboxing a possibly null value.
            UpdateStatusVariable("SubstCount", count);
            UpdateStatusVariable("PreviousTaskName", recipe);
            UpdateStatusVariable("PreviousTaskType", "PROCESS");

            TriggerEvent(20, "Substrate Processed");
        }

        public event EventHandler<EventTriggeredEventArgs>? OnEventTriggered;

        protected virtual void OnStateChanged(GemStateEventArgs e)
        {
            StateChanged?.Invoke(this, e);
        }
    }

    // Log Manager for SML, General, and EDA logs
    public class LogManager
    {
        private readonly string logDirectory;
        private readonly object logLock = new object();


        public LogManager(string logDir = "Logs")
        {
            logDirectory = logDir;

            if (!Directory.Exists(logDirectory))
                Directory.CreateDirectory(logDirectory);
        }

        public void LogMessage(string message, LogType logType)
        {
            lock (logLock)
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var logEntry = $"[{timestamp}] {message}";
                    var fileName = GetLogFileName(logType);
                    var filePath = Path.Combine(logDirectory, fileName);

                    File.AppendAllText(filePath, logEntry + Environment.NewLine);

                    // Keep log files manageable (rotate when > 10MB)
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > 10 * 1024 * 1024) // 10MB
                    {
                        RotateLogFile(filePath);
                    }
                }
                catch (Exception ex)
                {
                    // Log to event log or console if file logging fails
                    Console.WriteLine($"Log error: {ex.Message}");
                }
            }
        }

        private string GetLogFileName(LogType logType)
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd");

            return logType switch
            {
                LogType.SML => $"SML_{date}.log",
                LogType.General => $"General_{date}.log",
                LogType.EDA => $"EDA_{date}.log",
                _ => $"Unknown_{date}.log"
            };
        }

        private static void RotateLogFile(string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);
                var timestamp = DateTime.Now.ToString("HHmmss");

                var rotatedFileName = $"{fileNameWithoutExt}_{timestamp}{extension}";

                string rotatedFilePath = "";

                if (directory != null && rotatedFileName != null)
                {
                    rotatedFilePath = Path.Combine(directory, rotatedFileName);
                }
                else
                {
                    Console.WriteLine($"Log rotation error: directory path or filename is null.");
                }

                File.Move(filePath, rotatedFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Log rotation error: {ex.Message}");
            }
        }

        public List<string> GetRecentLogEntries(LogType logType, int maxEntries = 100)
        {
            lock (logLock)
            {
                try
                {
                    var fileName = GetLogFileName(logType);
                    var filePath = Path.Combine(logDirectory, fileName);

                    if (!File.Exists(filePath))
                        return new List<string>();

                    var allLines = File.ReadAllLines(filePath);
                    var recentLines = allLines.Length > maxEntries
                        ? allLines.Skip(allLines.Length - maxEntries).ToList()
                        : allLines.ToList();

                    return recentLines;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading log: {ex.Message}");
                    return new List<string>();
                }
            }
        }
    }

    // Supporting Data Structures


    public class AlarmData
    {
        public uint AlarmId { get; set; }
        public string? AlarmText { get; set; }
        public AlarmSeverity Severity { get; set; }
        public DateTime SetTime { get; set; }
        public bool IsSet { get; set; }
    }

    public class EventData
    {
        public uint EventId { get; set; }
        public string? EventName { get; set; }
        public bool Enabled { get; set; }
        public DateTime LastTriggered { get; set; }
        public int TriggerCount { get; set; }
    }

    // Event Arguments
    public class GemStateEventArgs(GemState previousState, GemState newState) : EventArgs
    {
        public GemState PreviousState { get; } = previousState;
        public GemState NewState { get; } = newState;
    }

    public class EventTriggeredEventArgs : EventArgs
    {
        public uint EventId { get; }
        public string Description { get; }
        public DateTime Timestamp { get; }

        public EventTriggeredEventArgs(uint eventId, string description)
        {
            EventId = eventId;
            Description = description;
            Timestamp = DateTime.Now;
        }
    }
}