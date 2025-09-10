using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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

    }
}
