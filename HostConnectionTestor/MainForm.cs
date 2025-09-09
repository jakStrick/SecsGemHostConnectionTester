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

            public MainForm()
            {
                InitializeComponent();
                InitializeStatusLabel();
                StartListening();
            }

            private void InitializeStatusLabel()
            {
                statusLabel = new Label();
                statusLabel.AutoSize = true;
                statusLabel.Location = new System.Drawing.Point(20, 20);
                statusLabel.Text = "Waiting for connection...";
                this.Controls.Add(statusLabel);
            }

            private async void StartListening()
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                statusLabel.Text = $"Listening on port {Port}...";

                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    ShowConnectionMessage(client);
                }
            }

            private void ShowConnectionMessage(TcpClient client)
            {
                Invoke(new Action(() =>
                {
                    statusLabel.Text = "Connection established with SECS/GEM host.";
                }));
            }

        }
    }
