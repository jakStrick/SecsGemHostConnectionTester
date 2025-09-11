using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Secs4Net;



namespace HostConnectionTestor
{
   
    public partial class MainForm : Form
    {
        private TcpListener listener;
        private const int Port = 5000;
        private Label statusLabel;
        TcpClient client;

        public MainForm()
        {
            InitializeComponent();
            InitializeStatusLabel();
            StartListening();
            Reset();
        }

        private void InitializeStatusLabel()
        {
            statusLabel = new()
            {
                AutoSize = true,
                Location = new System.Drawing.Point(20, 20),
                Text = "Waiting for connection..."
            };

            this.Controls.Add(statusLabel);
        }

        private async void StartListening()
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
            statusLabel.Text = $"Listening on port {Port}...";

            client = await listener.AcceptTcpClientAsync();
            ShowConnectionMessage();

            // Start reading messages
            _ = Task.Run(() => ListenForMessages(client));
        }

        private async Task ListenForMessages(TcpClient tcpClient)
        {
            using var stream = tcpClient.GetStream();
            var buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                // Process the message on the UI thread
                Invoke(new Action(() => OnMessageReceived(message)));
            }
        }

        private void OnMessageReceived(string message)
        {
            // Example: If received "S1F1", respond with "S1F2"
            if (message.Contains("S1F1"))
            {
                SendMessageToClient("S1F2");
                statusLabel.Text = "Received S1F1, sent S1F2.";
            }
            else
            {
                statusLabel.Text = $"Received: {message}";
            }
        }

        private void SendMessageToClient(string response)
        {
            if (client?.Connected == true)
            {
                var stream = client.GetStream();
                var data = Encoding.UTF8.GetBytes(response);
                stream.Write(data, 0, data.Length);
            }
        }

        private void ShowConnectionMessage()
        {
            Invoke(new Action(() =>
            {
                statusLabel.Text = "Connection established with SECS/GEM host. " + client.Connected;
            }));

           

        }

        private async void Reset()
        {


            client = await listener.AcceptTcpClientAsync();
            ShowConnectionMessage();

        }

        // Implement the handler
        private void OnMessageReceived(object sender, SecsMessageEventArgs e)
        {
            if (e?.Message == null)
                return;

            var receivedMessage = e.Message;

            // Example: If received S1F1, respond with S1F2
            if (receivedMessage.Stream == 1 && receivedMessage.Function == 1)
            {
                try
                {
                    // Ensure messageProcessor and hsmsConnection are initialized
                    if (messageProcessor != null && hsmsConnection != null)
                    {
                        SecsMessage ack = messageProcessor.CreateS1F2Message();
                        hsmsConnection.SendMessage(ack);
                    }
                    else
                    {
                        // Optionally log or handle the missing dependencies
                        MessageBox.Show("Message processor or HSMS connection is not initialized.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    // Log or display the error
                    MessageBox.Show($"Error sending S1F2 message: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

    }
}
