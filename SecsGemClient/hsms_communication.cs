
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SecsGemClient
{
    // HSMS Connection Manager - Core SEMI E30 Implementation
    public class HsmsConnection
    {
        private readonly TcpClient? tcpClient;
        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly ConcurrentQueue<SecsMessage> messageQueue;
        private readonly SemaphoreSlim sendSemaphore;
        private uint systemBytes = 0;

        public NetworkStream? TcpStream { get; set; }
        public bool IsConnected { get; private set; }
        public IPAddress? RemoteEndpoint { get; private set; }
        public int? Port { get; private set; }
        public int DeviceId { get; private set; }

        // HSMS Session Management
        private System.Threading.Timer? linkTestTimer;

        private DateTime lastMessageTime;
        private const int LINK_TEST_INTERVAL = 30000; // 30 seconds
        private const int T3_TIMEOUT = 45000; // 45 seconds
        private const int T5_TIMEOUT = 10000; // 10 seconds
        private const int T6_TIMEOUT = 5000;  // 5 seconds
        private const int T7_TIMEOUT = 10000; // 10 seconds
        private const int T8_TIMEOUT = 6000;  // 6 seconds

        public event EventHandler<ConnectionStateEventArgs> ConnectionStateChanged;
        public event EventHandler<SecsMessageEventArgs> MessageReceived;

        public HsmsConnection()
        {
            messageQueue = new ConcurrentQueue<SecsMessage>();
            sendSemaphore = new SemaphoreSlim(1, 1);
            IsConnected = false;
            tcpClient = new TcpClient();
            IPAddress defaultIpAddress = new([0, 0, 0, 0]);
            RemoteEndpoint = defaultIpAddress;
            linkTestTimer = null;
            ConnectionStateChanged = HsmsConnection_ConnectionStateChanged;
            MessageReceived = HsmsConnection_MessageReceived;
            cancellationTokenSource = new CancellationTokenSource();
            TcpStream = null;
        }

        private void HsmsConnection_MessageReceived(object? sender, SecsMessageEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void HsmsConnection_ConnectionStateChanged(object? sender, ConnectionStateEventArgs e)
        {
            return;
            throw new NotImplementedException();
        }

        public async Task<bool> ConnectAsync(IPAddress ipAddress, int port, int deviceId)
        {
            try
            {
                if (IsConnected)
                    await DisconnectAsync();

                RemoteEndpoint = ipAddress;
                Port = port;
                DeviceId = deviceId;


                OnConnectionStateChanged(ConnectionState.Connecting);

                await tcpClient.ConnectAsync(ipAddress, port);

                TcpStream = tcpClient.GetStream();


                IsConnected = true;
                lastMessageTime = DateTime.Now;

                // Start message processing tasks
                _ = Task.Run(ReceiveMessagesAsync, cancellationTokenSource.Token);
                _ = Task.Run(ProcessMessageQueueAsync, cancellationTokenSource.Token);

                // Start link test timer
                linkTestTimer = new System.Threading.Timer(SendLinkTestMessage, null, LINK_TEST_INTERVAL, LINK_TEST_INTERVAL);

                OnConnectionStateChanged(ConnectionState.Connected);

                // Send Select.req to establish HSMS session
                await SendSelectRequest();

                return true;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                OnConnectionStateChanged(ConnectionState.Disconnected);
                throw new HsmsException($"Failed to connect: {ex.Message}", ex);
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                IsConnected = false;

                linkTestTimer?.Dispose();
                cancellationTokenSource?.Cancel();


                await SendDeselectRequest();
                await Task.Delay(1000); // Allow time for deselect response


                TcpStream.Close();
                tcpClient.Close();

                OnConnectionStateChanged(ConnectionState.Disconnected);
            }
            catch (Exception ex)
            {
                throw new HsmsException($"Error during disconnection: {ex.Message}", ex);
            }
        }

        private async Task SendSelectRequest()
        {
            var selectMessage = new HsmsMessage
            {
                SessionId = (ushort)DeviceId,
                HeaderByte2 = 0x00, // W-bit = 0, Stream = 0
                HeaderByte3 = 0x00, // Function = 0
                PType = HsmsPType.SelectReq,
                SType = HsmsSType.SelectReq,
                SystemBytes = GetNextSystemBytes()
            };

            await SendHsmsMessageAsync(selectMessage, GetNetworkStream());
        }

        private async Task SendDeselectRequest()
        {
            var deselectMessage = new HsmsMessage
            {
                SessionId = (ushort)DeviceId,
                HeaderByte2 = 0x00,
                HeaderByte3 = 0x00,
                PType = HsmsPType.DeselectReq,
                SType = HsmsSType.DeselectReq,
                SystemBytes = GetNextSystemBytes()
            };

            await SendHsmsMessageAsync(deselectMessage, GetNetworkStream());
        }

        // Update the SendLinkTestMessage method parameter to accept a nullable object to match TimerCallback delegate
        private void SendLinkTestMessage(object? state)
        {
            if (!IsConnected || (DateTime.Now - lastMessageTime).TotalMilliseconds < LINK_TEST_INTERVAL)
                return;

            try
            {
                var linkTestMessage = new HsmsMessage
                {
                    SessionId = (ushort)DeviceId,
                    HeaderByte2 = 0x00,
                    HeaderByte3 = 0x00,
                    PType = HsmsPType.LinktestReq,
                    SType = HsmsSType.LinktestReq,
                    SystemBytes = GetNextSystemBytes()
                };

                Task.Run(async () => await SendHsmsMessageAsync(linkTestMessage, GetNetworkStream()));
            }
            catch (Exception ex)
            {
                // Log link test failure
                Console.WriteLine($"Link test failed: {ex.Message}");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[4096];

            try
            {
                while (IsConnected && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // Read HSMS header (10 bytes)
                    var headerBuffer = new byte[10];
                    var bytesRead = 0;

                    while (bytesRead < 10 && IsConnected)
                    {
                        var read = await TcpStream.ReadAsync(headerBuffer.AsMemory(bytesRead, 10 - bytesRead), cancellationTokenSource.Token);
                        if (read == 0)
                        {
                            IsConnected = false;
                            OnConnectionStateChanged(ConnectionState.Disconnected);
                            return;
                        }
                        bytesRead += read;
                    }

                    var hsmsHeader = ParseHsmsHeader(headerBuffer);
                    byte[]? messageData = null;

                    // Read message data if length > 10
                    if (hsmsHeader.Length > 10)
                    {
                        var dataLength = hsmsHeader.Length - 10;
                        messageData = new byte[dataLength];
                        bytesRead = 0;

                        while (bytesRead < dataLength && IsConnected)
                        {
                            var read = await TcpStream.ReadAsync(messageData.AsMemory(bytesRead, (int)(dataLength - bytesRead)), cancellationTokenSource.Token);
                            if (read == 0)
                            {
                                IsConnected = false;
                                OnConnectionStateChanged(ConnectionState.Disconnected);
                                return;
                            }
                            bytesRead += read;
                        }
                    }

                    lastMessageTime = DateTime.Now;
                    if (messageData != null)
                    {
                        await ProcessReceivedMessage(hsmsHeader, messageData);
                    }
                    else
                    {
                        // Handle the null case - log error, throw exception, etc.
                        Console.WriteLine("Message data is null");
                    }
                }
            }
            catch (Exception ex)
            {
                if (IsConnected)
                {
                    IsConnected = false;
                    OnConnectionStateChanged(ConnectionState.Disconnected);
                    throw new HsmsException($"Error receiving messages: {ex.Message}", ex);
                }
            }
        }

        private HsmsHeader ParseHsmsHeader(byte[] headerBuffer)
        {
            return new HsmsHeader
            {
                Length = BitConverter.ToUInt32(new byte[] { headerBuffer[3], headerBuffer[2], headerBuffer[1], headerBuffer[0] }, 0),
                SessionId = BitConverter.ToUInt16(new byte[] { headerBuffer[5], headerBuffer[4] }, 0),
                HeaderByte2 = headerBuffer[6],
                HeaderByte3 = headerBuffer[7],
                PType = (HsmsPType)headerBuffer[8],
                SType = (HsmsSType)headerBuffer[9]
            };
        }

        private async Task ProcessReceivedMessage(HsmsHeader header, byte[] messageData)
        {
            // Handle HSMS control messages
            if (header.SType != HsmsSType.DataMessage)
            {
                await HandleControlMessage(header);
                return;
            }

            // Parse SECS-II message
            var secsMessage = ParseSecsMessage(header, messageData);
            OnMessageReceived(secsMessage);
        }

        private async Task HandleControlMessage(HsmsHeader header)
        {
            switch (header.SType)
            {
                case HsmsSType.SelectReq:
                    await SendSelectResponse(header.SystemBytes);
                    break;
                case HsmsSType.SelectRsp:
                    // Handle select response
                    break;
                case HsmsSType.DeselectReq:
                    await SendDeselectResponse(header.SystemBytes);
                    break;
                case HsmsSType.DeselectRsp:
                    // Handle deselect response
                    break;
                case HsmsSType.LinktestReq:
                    await SendLinktestResponse(header.SystemBytes);
                    break;
                case HsmsSType.LinktestRsp:
                    // Handle linktest response
                    break;
                case HsmsSType.RejectReq:
                    // Handle reject
                    break;
                case HsmsSType.SeparateReq:
                    // Handle separate
                    break;
            }
        }

        private async Task SendSelectResponse(uint systemBytes)
        {
            var response = new HsmsMessage
            {
                SessionId = (ushort)DeviceId,
                HeaderByte2 = 0x00,
                HeaderByte3 = 0x00,
                PType = HsmsPType.SelectRsp,
                SType = HsmsSType.SelectRsp,
                SystemBytes = systemBytes // Echo back the system bytes
            };

            await SendHsmsMessageAsync(response, GetNetworkStream());
        }

        private async Task SendDeselectResponse(uint systemBytes)
        {
            var response = new HsmsMessage
            {
                SessionId = (ushort)DeviceId,
                HeaderByte2 = 0x00,
                HeaderByte3 = 0x00,
                PType = HsmsPType.DeselectRsp,
                SType = HsmsSType.DeselectRsp,
                SystemBytes = systemBytes
            };

            await SendHsmsMessageAsync(response, GetNetworkStream());
        }

        private async Task SendLinktestResponse(uint systemBytes)
        {
            var response = new HsmsMessage
            {
                SessionId = (ushort)DeviceId,
                HeaderByte2 = 0x00,
                HeaderByte3 = 0x00,
                PType = HsmsPType.LinktestRsp,
                SType = HsmsSType.LinktestRsp,
                SystemBytes = systemBytes
            };

            await SendHsmsMessageAsync(response, GetNetworkStream());
        }

        private SecsMessage ParseSecsMessage(HsmsHeader header, byte[] messageData)
        {
            var stream = (byte)((header.HeaderByte2 & 0x7F));
            var function = header.HeaderByte3;
            var wBit = (header.HeaderByte2 & 0x80) != 0;

            var secsMessage = new SecsMessage
            {
                Stream = stream,
                Function = function,
                WBit = wBit,
                SessionId = header.SessionId,
                SystemBytes = header.SystemBytes,
                Data = messageData
            };

            return secsMessage;
        }

        public void SendMessage(SecsMessage message)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected");

            message.SystemBytes = GetNextSystemBytes();
            messageQueue.Enqueue(message);
        }

        private async Task ProcessMessageQueueAsync()
        {
            while (IsConnected && !cancellationTokenSource.Token.IsCancellationRequested)
            {
                if (messageQueue.TryDequeue(out var message))
                {
                    await sendSemaphore.WaitAsync(cancellationTokenSource.Token);
                    try
                    {
                        await SendSecsMessageInternal(message);
                    }
                    finally
                    {
                        sendSemaphore.Release();
                    }
                }
                else
                {
                    await Task.Delay(10, cancellationTokenSource.Token);
                }
            }
        }

        private async Task SendSecsMessageInternal(SecsMessage message)
        {
            var hsmsMessage = new HsmsMessage
            {
                SessionId = message.SessionId,
                HeaderByte2 = (byte)(message.Stream | (message.WBit ? 0x80 : 0x00)),
                HeaderByte3 = message.Function,
                PType = HsmsPType.DataMessage,
                SType = HsmsSType.DataMessage,
                SystemBytes = message.SystemBytes,
                Data = message.Data
            };

            await SendHsmsMessageAsync(hsmsMessage, GetNetworkStream());
        }

        private NetworkStream GetNetworkStream()
        {
            return TcpStream;
        }

        private async Task SendHsmsMessageAsync(HsmsMessage message, NetworkStream tcpStream)
        {
            if (!IsConnected || tcpStream == null)
                return;

            var messageBytes = SerializeHsmsMessage(message);
            await tcpStream.WriteAsync(messageBytes, cancellationTokenSource.Token);
            await tcpStream.FlushAsync(cancellationTokenSource.Token);
        }

        private static byte[] SerializeHsmsMessage(HsmsMessage message)
        {
            var dataLength = message.Data?.Length ?? 0;
            var totalLength = (uint)(10 + dataLength);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            // Length (4 bytes, big-endian)
            var lengthBytes = BitConverter.GetBytes(totalLength);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);
            writer.Write(lengthBytes);

            // Session ID (2 bytes, big-endian)
            var sessionBytes = BitConverter.GetBytes(message.SessionId);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(sessionBytes);
            writer.Write(sessionBytes);

            // Header bytes
            writer.Write(message.HeaderByte2);
            writer.Write(message.HeaderByte3);

            // PType and SType
            writer.Write((byte)message.PType);
            writer.Write((byte)message.SType);

            // System bytes (4 bytes, big-endian)
            var systemBytes = BitConverter.GetBytes(message.SystemBytes);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(systemBytes);
            writer.Write(systemBytes);

            // Data
            if (message.Data != null && message.Data.Length > 0)
            {
                writer.Write(message.Data);
            }

            return stream.ToArray();
        }

        private uint GetNextSystemBytes()
        {
            return ++systemBytes;
        }

        protected virtual void OnConnectionStateChanged(ConnectionState state)
        {
            ConnectionStateChanged?.Invoke(this, new ConnectionStateEventArgs(state));
        }

        protected virtual void OnMessageReceived(SecsMessage message)
        {
            MessageReceived?.Invoke(this, new SecsMessageEventArgs(message));
        }

        public NetworkStream? GetTcpStream()
        {
            return TcpStream;
        }

        public void Dispose(NetworkStream? tcpStream)
        {
            linkTestTimer?.Dispose();
            cancellationTokenSource.Dispose();
            tcpStream.Dispose();
            tcpClient?.Dispose();
            sendSemaphore?.Dispose();
        }
    }

    // HSMS Message Structure
    public class HsmsMessage
    {
        public ushort SessionId { get; set; }
        public byte HeaderByte2 { get; set; }
        public byte HeaderByte3 { get; set; }
        public HsmsPType PType { get; set; }
        public HsmsSType SType { get; set; }
        public uint SystemBytes { get; set; }
        public byte[]? Data { get; set; }
    }

    public class HsmsHeader
    {
        public uint Length { get; set; }
        public ushort SessionId { get; set; }
        public byte HeaderByte2 { get; set; }
        public byte HeaderByte3 { get; set; }
        public HsmsPType? PType { get; set; }
        public HsmsSType? SType { get; set; }
        public uint SystemBytes { get; set; }
    }

    // HSMS Protocol Types (SEMI E30)
    public enum HsmsPType : byte
    {
        DataMessage = 0x00,
        SelectReq = 0x01,
        SelectRsp = 0x02,
        DeselectReq = 0x03,
        DeselectRsp = 0x04,
        LinktestReq = 0x05,
        LinktestRsp = 0x06,
        RejectReq = 0x07,
        SeparateReq = 0x09
    }

    public enum HsmsSType : byte
    {
        DataMessage = 0x00,
        SelectReq = 0x01,
        SelectRsp = 0x02,
        DeselectReq = 0x03,
        DeselectRsp = 0x04,
        LinktestReq = 0x05,
        LinktestRsp = 0x06,
        RejectReq = 0x07,
        SeparateReq = 0x09
    }

    // Connection States
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    // Event Arguments
    public class ConnectionStateEventArgs : EventArgs
    {
        public ConnectionState? State { get; }

        public ConnectionStateEventArgs(ConnectionState state)
        {
            State = state;
        }
    }

    // Replace the record declaration with a standard class that inherits from EventArgs
    public class SecsMessageEventArgs : EventArgs
    {
        public SecsMessage? Message { get; }

        public SecsMessageEventArgs(SecsMessage message)
        {
            Message = message;
        }
    }
}