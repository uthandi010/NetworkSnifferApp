using Microsoft.Maui.Controls;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpPcap;
using SharpPcap.LibPcap;
using Microsoft.Maui.Storage;

namespace NetworkSnifferApp
{
    public partial class MainPage : ContentPage
    {
        public ObservableCollection<PacketModel> Packets { get; set; } = new ObservableCollection<PacketModel>();
        private string device = "";
        private string[] parts = new string[2];
        private int packetCount = 0;
        private CancellationTokenSource _cancellationTokenSource;
        private CaptureFileWriterDevice _pcapWriter;
        private string pcapFilePath;

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;
            DevicePicker.SelectedIndexChanged += OnDeviceSelected;

            // Set PCAP file path to app's cache directory
            pcapFilePath = Path.Combine(FileSystem.CacheDirectory, "netflow_capture.pcap");
        }

        private void OnFindDevicesClicked(object sender, EventArgs e)
        {
            string[] devices = SharpCapHelper.GetNetworkDevices();
            DevicePicker.ItemsSource = devices;

            if (devices.Length > 0)
                ResultLabel.Text = "Select a network device.";
            else
                ResultLabel.Text = "No network devices found.";
        }

        private void OnDeviceSelected(object sender, EventArgs e)
        {
            if (DevicePicker.SelectedIndex >= 0)
            {
                device = DevicePicker.SelectedItem.ToString().Trim();
                parts = device.Split(" :");
                ResultLabel.Text = "Selected device: " + parts[1];
            }
        }

        private async void OnStartCaptureClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(parts[0]))
            {
                ResultLabel.Text = "Please select a network device first!";
                return;
            }

            Packets.Clear();
            packetCount = 0;
            ResultLabel.Text = "Capturing NetFlow packets...";

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _pcapWriter = new CaptureFileWriterDevice(pcapFilePath, FileMode.Create);

                var selectedDevice = CaptureDeviceList.Instance.FirstOrDefault(dev => dev.Name == parts[0]);

                if (selectedDevice == null)
                {
                    ResultLabel.Text = "Device not found!";
                    return;
                }

                await SharpCapHelper.StartCapture(parts[0], _cancellationTokenSource.Token, (packet) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        packetCount++;
                        Packets.Insert(0, new PacketModel
                        {
                            Number = packetCount,
                            RawData = packet
                        });
                    });
                }, _pcapWriter, selectedDevice);
            }
            catch (Exception ex)
            {
                ResultLabel.Text = $"Error: {ex.Message}";
            }
        }

        private void OnStopCaptureClicked(object sender, EventArgs e)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _pcapWriter?.Close();
            }

            ResultLabel.Text = $"Capture stopped. PCAP saved at: {pcapFilePath}";
        }

        private async void OnSaveCaptureClicked(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(pcapFilePath))
                {
                    ResultLabel.Text = "No capture data available!";
                    return;
                }

                var saveResult = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select a location to save PCAP file",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.WinUI, new[] { ".pcap" } },
                        { DevicePlatform.iOS, new[] { "public.data" } },
                        { DevicePlatform.Android, new[] { "application/vnd.tcpdump.pcap" } }
                    })
                });

                if (saveResult != null)
                {
                    var destinationPath = saveResult.FullPath;
                    File.Copy(pcapFilePath, destinationPath, true);
                    ResultLabel.Text = $"PCAP saved to: {destinationPath}";
                }
                else
                {
                    ResultLabel.Text = "File save canceled.";
                }
            }
            catch (Exception ex)
            {
                ResultLabel.Text = $"Error saving file: {ex.Message}";
            }
        }
    }

    public class PacketModel
    {
        public int Number { get; set; }
        public string RawData { get; set; }
    }
}