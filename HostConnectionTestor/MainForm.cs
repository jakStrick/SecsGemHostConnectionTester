using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Net;
using System.Collections.Generic;
using System.Net.Sockets;

namespace SecsGemClient
{
    public partial class MainForm : Form
    {
        private TcpListener _tcpListener;
        private TcpClient _connectedHost;
        private SecsMessageProcessor _messageProcessor;
        private GemStateManager _stateManager;
        private LogManager _logManager;
        private bool _isListening = false;
        private NetworkStream _hostStream;

        // UI Controls
        private GroupBox connectionGroupBox;
        private GroupBox statusGroupBox;
        private GroupBox messageGroupBox;
        private TextBox ipAddressTextBox;
        private TextBox portTextBox;
        private TextBox deviceIdTextBox;
        private Button startListeningButton;
        private Button stopListeningButton;
        private Label connectionStatusLabel;
        private RadioButton onlineRadio;
        private RadioButton localRadio;
        private RadioButton offlineRadio;
        private ListBox messageLogListBox;
        private Button sendTestButton;
        private Button sendAlarmButton;
        private RichTextBox logTextBox;
        private Button processStartButton;
        private Button processCompleteButton;
        private Label equipmentStateLabel;

        public MainForm()
        {
            InitializeComponent();
            CreateControls();
            InitializeSecsGem();
            SetupEventHandlers();
        }

        private void CreateControls()
        {
            // Connection Group Box
            connectionGroupBox = new GroupBox
            {
                Text = "Equipment Network Settings",
                Location = new Point(10, 10),
                Size = new Size(400, 150),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            var ipLabel = new Label { Text = "Listen IP Address:", Location = new Point(10, 30), Size = new Size(110, 23) };
            ipAddressTextBox = new TextBox { Location = new Point(120, 27), Size = new Size(120, 23), Text = "127.0.0.1" };

            var portLabel = new Label { Text = "Listen Port:", Location = new Point(10, 60), Size = new Size(80, 23) };
            portTextBox = new TextBox { Location = new Point(120, 57), Size = new Size(120, 23), Text = "5000" };

            var deviceLabel = new Label { Text = "Device ID:", Location = new Point(10, 90), Size = new Size(80, 23) };
            deviceIdTextBox = new TextBox { Location = new Point(120, 87), Size = new Size(120, 23), Text = "1" };

            startListeningButton = new Button
            {
                Text = "Start Listening",
                Location = new Point(250, 30),
                Size = new Size(130, 35),
                BackColor = Color.LightGreen
            };

            stopListeningButton = new Button
            {
                Text = "Stop Listening",
                Location = new Point(250, 70),
                Size = new Size(130, 35),
                BackColor = Color.LightCoral,
                Enabled = false
            };

            connectionGroupBox.Controls.AddRange(new Control[] {
                ipLabel, ipAddressTextBox, portLabel, portTextBox,
                deviceLabel, deviceIdTextBox, startListeningButton, stopListeningButton
            });

            // Status Group Box
            statusGroupBox = new GroupBox
            {
                Text = "Equipment State Control",
                Location = new Point(10, 170),
                Size = new Size(400, 150),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            onlineRadio = new RadioButton
            {
                Text = "Online (Remote)",
                Location = new Point(20, 30),
                Size = new Size(120, 25)
            };

            localRadio = new RadioButton
            {
                Text = "Local",
                Location = new Point(150, 30),
                Size = new Size(100, 25)
            };

            offlineRadio = new RadioButton
            {
                Text = "Offline",
                Location = new Point(260, 30),
                Size = new Size(100, 25),
                Checked = true
            };

            connectionStatusLabel = new Label
            {
                Text = "Status: Not Listening",
                Location = new Point(20, 60),
                Size = new Size(250, 23),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.Red
            };

            equipmentStateLabel = new Label
            {
                Text = "Equipment State: IDLE",
                Location = new Point(20, 85),
                Size = new Size(200, 23),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.Blue
            };

            processStartButton = new Button
            {
                Text = "Start Process",
                Location = new Point(20, 110),
                Size = new Size(100, 30),
                Enabled = false
            };

            processCompleteButton = new Button
            {
                Text = "Complete Process",
                Location = new Point(130, 110),
                Size = new Size(120, 30),
                Enabled = false
            };

            statusGroupBox.Controls.AddRange(new Control[] {
                onlineRadio, localRadio, offlineRadio, connectionStatusLabel,
                equipmentStateLabel, processStartButton, processCompleteButton
            });

            // Message Group Box
            messageGroupBox = new GroupBox
            {
                Text = "Host Communication",
                Location = new Point(430, 10),
                Size = new Size(450, 310),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            messageLogListBox = new ListBox
            {
                Location = new Point(20, 25),
                Size = new Size(400, 200),
                Font = new Font("Consolas", 8F)
            };

            sendTestButton = new Button
            {
                Text = "Send S1F1 (Are You There)",
                Location = new Point(20, 240),
                Size = new Size(180, 30),
                Enabled = false
            };

            sendAlarmButton = new Button
            {
                Text = "Send S5F1 (Test Alarm)",
                Location = new Point(210, 240),
                Size = new Size(180, 30),
                Enabled = false
            };

            messageGroupBox.Controls.AddRange(new Control[] {
                messageLogListBox, sendTestButton, sendAlarmButton
            });

            // Log Text Box
            var logLabel = new Label
            {
                Text = "Equipment Log:",
                Location = new Point(10, 335),
                Size = new Size(100, 23),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            logTextBox = new RichTextBox
            {
                Location = new Point(10, 360),
                Size = new Size(870, 290),
                Font = new Font("Consolas", 8F),
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LimeGreen
            };

            this.Controls.AddRange(new Control[] {
                connectionGroupBox, statusGroupBox, messageGroupBox, logLabel, logTextBox
            });
        }

        private void SetupEventHandlers()
        {
            startListeningButton.Click += StartListeningButton_Click;
            stopListeningButton.Click += StopListeningButton_Click;
            sendTestButton.Click += SendTestButton_Click;
            sendAlarmButton.Click += SendAlarmButton_Click;
            processStartButton.Click += ProcessStartButton_Click;
            processCompleteButton.Click += ProcessCompleteButton_Click;
            onlineRadio.CheckedChanged += OnlineRadio_CheckedChanged;
            localRadio.CheckedChanged += LocalRadio_CheckedChanged;
            offlineRadio.CheckedChanged += OfflineRadio_CheckedChanged;
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeSecsGem()
        {
            _logManager = new LogManager();
            _stateManager = new GemStateManager();
            _messageProcessor = new SecsMessageProcessor();

            // Subscribe to events
            _messageProcessor.MessageProcessed += OnMessageProcessed;
            _stateManager.StateChanged += OnGemStateChanged;
            _stateManager.OnEventTriggered += OnEventTriggered;

            LogMessage("SECS/GEM Virtual Equipment initialized - Automatic Response Mode", LogType.General);

            // Start automatic equipment simulation
            _ = Task.Run(AutomaticEquipmentSimulation);
        }

        private async void StartListeningButton_Click(object sender, EventArgs e)
        {
            try
            {
                var ipAddress = IPAddress.Parse(ipAddressTextBox.Text);
                var port = int.Parse(portTextBox.Text);

                _tcpListener = new TcpListener(ipAddress, port);
                _tcpListener.Start();
                _isListening = true;

                startListeningButton.Enabled = false;
                stopListeningButton.Enabled = true;
                connectionStatusLabel.Text = $"Status: Listening on {ipAddress}:{port}";
                connectionStatusLabel.ForeColor = Color.Orange;

                LogMessage($"Equipment listening for host connection on {ipAddress}:{port}", LogType.General);

                // Wait for host connection in background
                _ = Task.Run(WaitForHostConnection);
            }
            catch (Exception ex)
            {
                LogMessage($"Error starting listener: {ex.Message}", LogType.General);
                connectionStatusLabel.Text = "Status: Listen Error";
                connectionStatusLabel.ForeColor = Color.Red;
            }
        }

        private async Task WaitForHostConnection()
        {
            try
            {
                while (_isListening)
                {
                    LogMessage("Waiting for host to connect...", LogType.General);

                    _connectedHost = await _tcpListener.AcceptTcpClientAsync();
                    _hostStream = _connectedHost.GetStream();

                    var remoteEndpoint = _connectedHost.Client.RemoteEndPoint as IPEndPoint;
                    LogMessage($"Host connected from {remoteEndpoint?.Address}:{remoteEndpoint?.Port}", LogType.General);

                    this.Invoke((MethodInvoker)delegate
                    {
                        connectionStatusLabel.Text = $"Status: Host Connected ({remoteEndpoint?.Address})";
                        connectionStatusLabel.ForeColor = Color.Green;
                        sendTestButton.Enabled = true;
                        sendAlarmButton.Enabled = true;
                        processStartButton.Enabled = true;
                        processCompleteButton.Enabled = true;
                    });

                    // Start message processing
                    _ = Task.Run(ProcessMessages);

                    // Automatically go to local state when host connects
                    await Task.Delay(1000);
                    AutomaticallyGoToLocal();

                    // Start automatic behaviors
                    _ = Task.Run(SendPeriodicStatusUpdates);

                    // Wait for this connection to end before accepting new ones
                    while (_connectedHost?.Connected == true && _isListening)
                    {
                        await Task.Delay(1000);
                    }

                    // Connection ended
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (_isListening)
                        {
                            connectionStatusLabel.Text = "Status: Listening (Host Disconnected)";
                            connectionStatusLabel.ForeColor = Color.Orange;
                        }
                        sendTestButton.Enabled = false;
                        sendAlarmButton.Enabled = false;
                        processStartButton.Enabled = false;
                        processCompleteButton.Enabled = false;

                        // Reset to offline
                        offlineRadio.Checked = true;
                        _stateManager.GoOffline();
                    });

                    LogMessage("Host disconnected", LogType.General);
                    _connectedHost = null;
                    _hostStream = null;
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal when stopping listener
            }
            catch (Exception ex)
            {
                LogMessage($"Error in host connection handler: {ex.Message}", LogType.General);
            }
        }

        private async Task ProcessMessages()
        {
            var buffer = new byte[4096];

            try
            {
                while (_connectedHost?.Connected == true && _isListening)
                {
                    // Read HSMS header (10 bytes)
                    var headerBuffer = new byte[10];
                    var bytesRead = 0;

                    while (bytesRead < 10 && _connectedHost.Connected)
                    {
                        var read = await _hostStream.ReadAsync(headerBuffer, bytesRead, 10 - bytesRead);
                        if (read == 0) break;
                        bytesRead += read;
                    }

                    if (bytesRead < 10) break;

                    // Parse header
                    var length = BitConverter.ToUInt32(new byte[] { headerBuffer[3], headerBuffer[2], headerBuffer[1], headerBuffer[0] }, 0);
                    var sessionId = BitConverter.ToUInt16(new byte[] { headerBuffer[5], headerBuffer[4] }, 0);
                    var headerByte2 = headerBuffer[6];
                    var headerByte3 = headerBuffer[7];
                    var systemBytes = BitConverter.ToUInt32(new byte[] { headerBuffer[9], headerBuffer[8], headerBuffer[7], headerBuffer[6] }, 0);

                    // Read message data if present
                    byte[]? messageData = null;
                    if (length > 10)
                    {
                        var dataLength = length - 10;
                        messageData = new byte[dataLength];
                        bytesRead = 0;

                        while (bytesRead < dataLength && _connectedHost.Connected)
                        {
                            var read = await _hostStream.ReadAsync(messageData, bytesRead, (int)(dataLength - bytesRead));
                            if (read == 0) break;
                            bytesRead += read;
                        }
                    }

                    // Create SECS message
                    var stream = (byte)(headerByte2 & 0x7F);
                    var function = headerByte3;
                    var wBit = (headerByte2 & 0x80) != 0;

                    var secsMessage = new SecsMessage
                    {
                        Stream = stream,
                        Function = function,
                        WBit = wBit,
                        SessionId = sessionId,
                        SystemBytes = systemBytes,
                        Data = messageData
                    };

                    // Parse the message data if available
                    if (messageData != null && messageData.Length > 0)
                    {
                        try
                        {
                            var parsedMessage = _messageProcessor.ParseMessage(messageData);
                            secsMessage.RootItem = parsedMessage.RootItem;
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Error parsing message data: {ex.Message}", LogType.General);
                        }
                    }

                    // Handle the message
                    await HandleIncomingMessage(secsMessage);
                }
            }
            catch (Exception ex)
            {
                if (_connectedHost?.Connected == true)
                {
                    LogMessage($"Error in message processing: {ex.Message}", LogType.General);
                }
            }
        }

        private async Task HandleIncomingMessage(SecsMessage message)
        {
            var messageText = $"S{message.Stream}F{message.Function}{(message.WBit ? "W" : "")}";

            this.Invoke((MethodInvoker)delegate
            {
                AddMessageToLog($"RX: {messageText}");
            });

            LogMessage($"Received: {messageText}", LogType.SML);

            switch ($"S{message.Stream}F{message.Function}")
            {
                case "S1F1": // Are You There Request from Host - Auto respond
                    var s1f2 = _messageProcessor.CreateS1F2Message();
                    s1f2.SystemBytes = message.SystemBytes;
                    s1f2.SessionId = message.SessionId;
                    SendSecsMessage(s1f2);
                    AddMessageToLog("TX: S1F2 - Online Data (Auto)");
                    LogMessage("Auto-sent S1F2 - Online Data", LogType.SML);
                    break;

                case "S1F13": // Establish Communications Request from Host - Auto respond
                    var s1f14 = _messageProcessor.CreateS1F14Message();
                    s1f14.SystemBytes = message.SystemBytes;
                    s1f14.SessionId = message.SessionId;
                    SendSecsMessage(s1f14);
                    AddMessageToLog("TX: S1F14 - Establish Comm Ack (Auto)");
                    LogMessage("Auto-sent S1F14 - Establish Communications Acknowledge", LogType.SML);

                    // Automatically go online after establishing communications
                    await Task.Delay(1000);
                    AutomaticallyGoOnline();
                    break;

                case "S2F17": // Date/Time Request from Host - Auto respond
                    var s2f18 = _messageProcessor.CreateS2F18Message();
                    s2f18.SystemBytes = message.SystemBytes;
                    s2f18.SessionId = message.SessionId;
                    SendSecsMessage(s2f18);
                    AddMessageToLog($"TX: S2F18 - Date/Time: {DateTime.Now} (Auto)");
                    LogMessage($"Auto-sent S2F18 - Date/Time: {DateTime.Now}", LogType.SML);
                    break;

                case "S1F2": // Host's response to our S1F1
                    LogMessage("Host responded to Are You There request", LogType.SML);
                    break;

                case "S1F14": // Host's response to our S1F13
                    LogMessage("Communications established with host", LogType.SML);
                    break;

                case "S2F18": // Host's response to our S2F17
                    LogMessage("Received date/time from host", LogType.SML);
                    break;

                default:
                    LogMessage($"Unhandled message from host: S{message.Stream}F{message.Function}", LogType.General);
                    // Auto-send a generic acknowledge for W-bit messages we don't specifically handle
                    if (message.WBit)
                    {
                        SendGenericAcknowledge(message);
                    }
                    break;
            }
        }

        private void SendSecsMessage(SecsMessage message)
        {
            try
            {
                if (_connectedHost?.Connected != true || _hostStream == null) return;

                var serializedData = _messageProcessor.SerializeMessage(message);
                var dataLength = serializedData?.Length ?? 0;
                var totalLength = (uint)(10 + dataLength);

                // Create HSMS header
                var header = new byte[10];

                // Length (4 bytes, big-endian)
                var lengthBytes = BitConverter.GetBytes(totalLength);
                if (BitConverter.IsLittleEndian) Array.Reverse(lengthBytes);
                Array.Copy(lengthBytes, 0, header, 0, 4);

                // Session ID (2 bytes, big-endian)
                var sessionBytes = BitConverter.GetBytes(message.SessionId);
                if (BitConverter.IsLittleEndian) Array.Reverse(sessionBytes);
                Array.Copy(sessionBytes, 0, header, 4, 2);

                // Header bytes
                header[6] = (byte)(message.Stream | (message.WBit ? 0x80 : 0x00));
                header[7] = message.Function;

                // PType and SType (data message)
                header[8] = 0x00; // PType = DataMessage
                header[9] = 0x00; // SType = DataMessage

                // Write header
                _hostStream.Write(header, 0, header.Length);

                // Write data if present
                if (serializedData != null && serializedData.Length > 0)
                {
                    _hostStream.Write(serializedData, 0, serializedData.Length);
                }

                _hostStream.Flush();
            }
            catch (Exception ex)
            {
                LogMessage($"Error sending SECS message: {ex.Message}", LogType.General);
            }
        }

        private async void StopListeningButton_Click(object sender, EventArgs e)
        {
            await StopListening();
        }

        private async Task StopListening()
        {
            try
            {
                _isListening = false;

                if (_connectedHost?.Connected == true)
                {
                    _connectedHost.Close();
                }

                _tcpListener?.Stop();

                startListeningButton.Enabled = true;
                stopListeningButton.Enabled = false;
                sendTestButton.Enabled = false;
                sendAlarmButton.Enabled = false;
                processStartButton.Enabled = false;
                processCompleteButton.Enabled = false;

                connectionStatusLabel.Text = "Status: Not Listening";
                connectionStatusLabel.ForeColor = Color.Red;

                // Reset to offline state
                offlineRadio.Checked = true;
                _stateManager.GoOffline();

                LogMessage("Stopped listening for host connections", LogType.General);
            }
            catch (Exception ex)
            {
                LogMessage($"Error stopping listener: {ex.Message}", LogType.General);
            }
        }

        private void SendTestButton_Click(object sender, EventArgs e)
        {
            if (_connectedHost?.Connected == true)
            {
                try
                {
                    var s1f1Message = SecsMessageProcessor.CreateS1F1Message();
                    s1f1Message.SessionId = (ushort)int.Parse(deviceIdTextBox.Text);
                    SendSecsMessage(s1f1Message);

                    LogMessage("Sent S1F1 - Are You There", LogType.SML);
                    AddMessageToLog("TX: S1F1 - Are You There");
                }
                catch (Exception ex)
                {
                    LogMessage($"Error sending test message: {ex.Message}", LogType.General);
                }
            }
        }

        private void SendAlarmButton_Click(object sender, EventArgs e)
        {
            if (_connectedHost?.Connected == true)
            {
                try
                {
                    _stateManager.SetAlarm(101, "Test Equipment Alarm", AlarmSeverity.Warning);

                    var s5f1Message = _messageProcessor.CreateS5F1Message(101, "Test Equipment Alarm");
                    s5f1Message.SessionId = (ushort)int.Parse(deviceIdTextBox.Text);
                    SendSecsMessage(s5f1Message);

                    LogMessage("Sent S5F1 - Test Alarm", LogType.SML);
                    AddMessageToLog("TX: S5F1 - Test Alarm Set");
                }
                catch (Exception ex)
                {
                    LogMessage($"Error sending alarm message: {ex.Message}", LogType.General);
                }
            }
        }

        private void ProcessStartButton_Click(object sender, EventArgs e)
        {
            try
            {
                _stateManager.StartProcessJob("PROC_001");
                equipmentStateLabel.Text = "Equipment State: PROCESSING";
                equipmentStateLabel.ForeColor = Color.Green;

                // Send process started event
                var eventData = new Dictionary<string, object>
                {
                    ["ProcessJobId"] = "PROC_001",
                    ["Recipe"] = "TestRecipe_001",
                    ["StartTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                if (_connectedHost?.Connected == true)
                {
                    var s6f11Message = _messageProcessor.CreateS6F11Message(10, eventData);
                    s6f11Message.SessionId = (ushort)int.Parse(deviceIdTextBox.Text);
                    SendSecsMessage(s6f11Message);
                    AddMessageToLog("TX: S6F11 - Process Started");
                }

                LogMessage("Process started - PROC_001", LogType.General);
            }
            catch (Exception ex)
            {
                LogMessage($"Error starting process: {ex.Message}", LogType.General);
            }
        }

        private void ProcessCompleteButton_Click(object sender, EventArgs e)
        {
            try
            {
                _stateManager.CompleteProcessJob("PROC_001");
                equipmentStateLabel.Text = "Equipment State: IDLE";
                equipmentStateLabel.ForeColor = Color.Blue;

                // Send process completed event
                var eventData = new Dictionary<string, object>
                {
                    ["ProcessJobId"] = "PROC_001",
                    ["CompletionStatus"] = "SUCCESS",
                    ["EndTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                if (_connectedHost?.Connected == true)
                {
                    var s6f11Message = _messageProcessor.CreateS6F11Message(11, eventData);
                    s6f11Message.SessionId = (ushort)int.Parse(deviceIdTextBox.Text);
                    SendSecsMessage(s6f11Message);
                    AddMessageToLog("TX: S6F11 - Process Completed");
                }

                LogMessage("Process completed - PROC_001", LogType.General);
            }
            catch (Exception ex)
            {
                LogMessage($"Error completing process: {ex.Message}", LogType.General);
            }
        }

        private void OnlineRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (onlineRadio.Checked)
            {
                _stateManager.GoOnline();
                statusGroupBox.Text = "Equipment State Control - ONLINE";
            }
        }

        private void LocalRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (localRadio.Checked)
            {
                _stateManager.SetLocal();
                statusGroupBox.Text = "Equipment State Control - LOCAL";
            }
        }

        private void OfflineRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (offlineRadio.Checked)
            {
                _stateManager.GoOffline();
                statusGroupBox.Text = "Equipment State Control - OFFLINE";
            }
        }

        private void AutomaticallyGoToLocal()
        {
            try
            {
                this.Invoke((MethodInvoker)delegate
                {
                    localRadio.Checked = true;
                });

                _stateManager.SetLocal();
                LogMessage("Equipment automatically went to LOCAL state", LogType.General);
            }
            catch (Exception ex)
            {
                LogMessage($"Error going to local automatically: {ex.Message}", LogType.General);
            }
        }

        private void AutomaticallyGoOnline()
        {
            try
            {
                this.Invoke((MethodInvoker)delegate
                {
                    onlineRadio.Checked = true;
                });

                _stateManager.GoOnline();
                LogMessage("Equipment automatically went ONLINE", LogType.General);
            }
            catch (Exception ex)
            {
                LogMessage($"Error going online automatically: {ex.Message}", LogType.General);
            }
        }

        private async Task AutomaticEquipmentSimulation()
        {
            // Wait for host connection to be established
            while (_connectedHost?.Connected != true)
            {
                await Task.Delay(1000);
            }

            await Task.Delay(10000); // Wait 10 seconds after connection

            int processCount = 0;

            while (_connectedHost?.Connected == true && _isListening)
            {
                // Simulate a process every 3 minutes
                await Task.Delay(180000);

                if (_connectedHost?.Connected == true)
                {
                    processCount++;
                    await AutomaticallyRunProcess(processCount);
                }
            }
        }

        private async Task AutomaticallyRunProcess(int processNumber)
        {
            try
            {
                string processJobId = $"AUTO_PROC_{processNumber:D3}";

                // Start process automatically
                _stateManager.StartProcessJob(processJobId);

                this.Invoke((MethodInvoker)delegate
                {
                    equipmentStateLabel.Text = "Equipment State: PROCESSING";
                    equipmentStateLabel.ForeColor = Color.Green;
                });

                var startEventData = new Dictionary<string, object>
                {
                    ["ProcessJobId"] = processJobId,
                    ["Recipe"] = $"AutoRecipe_{processNumber}",
                    ["StartTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                var s6f11Start = _messageProcessor.CreateS6F11Message(10, startEventData);
                s6f11Start.SessionId = (ushort)int.Parse(deviceIdTextBox.Text);
                SendSecsMessage(s6f11Start);

                this.Invoke((MethodInvoker)delegate
                {
                    AddMessageToLog($"TX: S6F11 - Process {processJobId} Started (Auto)");
                });

                LogMessage($"Auto-started process {processJobId}", LogType.General);

                // Simulate process time (45 seconds)
                await Task.Delay(45000);

                // Complete process automatically
                _stateManager.CompleteProcessJob(processJobId);

                this.Invoke((MethodInvoker)delegate
                {
                    equipmentStateLabel.Text = "Equipment State: IDLE";
                    equipmentStateLabel.ForeColor = Color.Blue;
                });

                var completeEventData = new Dictionary<string, object>
                {
                    ["ProcessJobId"] = processJobId,
                    ["CompletionStatus"] = "SUCCESS",
                    ["EndTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                var s6f11Complete = _messageProcessor.CreateS6F11Message(11, completeEventData);
                s6f11Complete.SessionId = (ushort)int.Parse(deviceIdTextBox.Text);
                SendSecsMessage(s6f11Complete);

                this.Invoke((MethodInvoker)delegate
                {
                    AddMessageToLog($"TX: S6F11 - Process {processJobId} Completed (Auto)");
                });

                LogMessage($"Auto-completed process {processJobId}", LogType.General);
            }
            catch (Exception ex)
            {
                LogMessage($"Error in automatic process simulation: {ex.Message}", LogType.General);
            }
        }

        private async Task SendPeriodicStatusUpdates()
        {
            await Task.Delay(15000); // Wait 15 seconds before starting status updates

            while (_connectedHost?.Connected == true && _isListening)
            {
                await Task.Delay(60000); // Every 60 seconds

                if (_connectedHost?.Connected == true)
                {
                    // Send status update
                    var statusData = new Dictionary<string, object>
                    {
                        ["EquipmentState"] = _stateManager.ProcessingState.ToString(),
                        ["CommunicationState"] = _stateManager.CommunicationState.ToString(),
                        ["ActiveAlarms"] = _stateManager.ActiveAlarms.Count,
                        ["Timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["Uptime"] = DateTime.Now.ToString("HH:mm:ss")
                    };

                    var s6f11Status = _messageProcessor.CreateS6F11Message(999, statusData);
                    s6f11Status.SessionId = (ushort)int.Parse(deviceIdTextBox.Text);
                    SendSecsMessage(s6f11Status);

                    this.Invoke((MethodInvoker)delegate
                    {
                        AddMessageToLog("TX: S6F11 - Status Update (Auto)");
                    });

                    LogMessage("Auto-sent status update to host", LogType.SML);
                }
            }
        }

        private void SendGenericAcknowledge(SecsMessage originalMessage)
        {
            try
            {
                // Send a generic function+1 acknowledge with ACKC = 0
                var ackMessage = new SecsMessage
                {
                    Stream = originalMessage.Stream,
                    Function = (byte)(originalMessage.Function + 1),
                    WBit = false,
                    SystemBytes = originalMessage.SystemBytes,
                    SessionId = originalMessage.SessionId,
                    RootItem = new SecsBinaryItem(new byte[] { 0x00 }) // Generic ACK = 0 (accepted)
                };

                SendSecsMessage(ackMessage);
                AddMessageToLog($"TX: S{ackMessage.Stream}F{ackMessage.Function} - Generic Ack (Auto)");
                LogMessage($"Auto-sent S{ackMessage.Stream}F{ackMessage.Function} - Generic Acknowledge", LogType.SML);
            }
            catch (Exception ex)
            {
                LogMessage($"Error sending generic acknowledge: {ex.Message}", LogType.General);
            }
        }

        private void OnMessageProcessed(object sender, MessageProcessedEventArgs e)
        {
            LogMessage($"Processed: {e.ProcessingResult}", LogType.General);
        }

        private void OnGemStateChanged(object sender, GemStateEventArgs e)
        {
            this.Invoke((MethodInvoker)delegate
            {
                switch (e.NewState)
                {
                    case GemState.Online:
                        onlineRadio.Checked = true;
                        statusGroupBox.Text = "Equipment State Control - ONLINE";
                        break;
                    case GemState.Local:
                        localRadio.Checked = true;
                        statusGroupBox.Text = "Equipment State Control - LOCAL";
                        break;
                    case GemState.Offline:
                        offlineRadio.Checked = true;
                        statusGroupBox.Text = "Equipment State Control - OFFLINE";
                        break;
                }
            });

            LogMessage($"Equipment state changed to: {e.NewState}", LogType.General);
        }

        private void OnEventTriggered(object sender, EventTriggeredEventArgs e)
        {
            LogMessage($"Event triggered: {e.EventId} - {e.Description}", LogType.General);
        }

        private void AddMessageToLog(string message)
        {
            if (messageLogListBox.InvokeRequired)
            {
                messageLogListBox.Invoke((MethodInvoker)delegate
                {
                    messageLogListBox.Items.Add($"{DateTime.Now:HH:mm:ss} - {message}");
                    if (messageLogListBox.Items.Count > 100)
                        messageLogListBox.Items.RemoveAt(0);
                    messageLogListBox.SelectedIndex = messageLogListBox.Items.Count - 1;
                });
            }
            else
            {
                messageLogListBox.Items.Add($"{DateTime.Now:HH:mm:ss} - {message}");
                if (messageLogListBox.Items.Count > 100)
                    messageLogListBox.Items.RemoveAt(0);
                messageLogListBox.SelectedIndex = messageLogListBox.Items.Count - 1;
            }
        }

        private void LogMessage(string message, LogType logType)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] {message}";

            if (logTextBox.InvokeRequired)
            {
                logTextBox.Invoke((MethodInvoker)delegate
                {
                    logTextBox.AppendText(logEntry + Environment.NewLine);
                    logTextBox.ScrollToCaret();
                });
            }
            else
            {
                logTextBox.AppendText(logEntry + Environment.NewLine);
                logTextBox.ScrollToCaret();
            }

            _logManager?.LogMessage(message, logType);
        }

        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            await StopListening();
        }
    }
}