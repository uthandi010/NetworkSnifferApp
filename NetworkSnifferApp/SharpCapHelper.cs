using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpPcap;
using SharpPcap.LibPcap;
using PacketDotNet;

namespace NetworkSnifferApp
{
    public class SharpCapHelper
    {
        public static string[] GetNetworkDevices()
        {
            var devices = CaptureDeviceList.Instance;
            var deviceList = new List<string>();

            foreach (var dev in devices)
            {
                deviceList.Add($"{dev.Name} : {dev.Description}");
            }

            return deviceList.ToArray();
        }

        public static async Task StartCapture(
            string deviceName,
            CancellationToken token,
            Action<string> onPacketReceived,
            CaptureFileWriterDevice pcapWriter,
            ILiveDevice selectedDevice)
        {
            try
            {
                // Open the device in promiscuous mode if it's a LibPcapLiveDevice
                if (selectedDevice is LibPcapLiveDevice liveDevice)
                {
                    liveDevice.Open(new DeviceConfiguration { Mode = DeviceModes.Promiscuous });
                }
                else
                {
                    selectedDevice.Open();
                }

                pcapWriter.Open();
                onPacketReceived($"✅ Capturing started on {deviceName}...");

                selectedDevice.OnPacketArrival += (sender, e) =>
                {
                    if (token.IsCancellationRequested)
                    {
                        onPacketReceived("🛑 Capture stopping...");
                        selectedDevice.StopCapture();
                        selectedDevice.Close();
                        pcapWriter.Close();
                        return;
                    }

                    var rawPacket = e.GetPacket();
                    pcapWriter.Write(rawPacket); // Save packet to PCAP file

                    var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
                    var udpPacket = packet.Extract<UdpPacket>();

                    if (udpPacket != null)
                    {
                        string packetHex = BitConverter.ToString(rawPacket.Data);
                        onPacketReceived($"📦 UDP Packet Captured [Src: {udpPacket.SourcePort}, Dst: {udpPacket.DestinationPort}]: {packetHex}");
                    }
                };

                // Run packet capturing in a separate thread
                await Task.Run(() =>
                {
                    selectedDevice.StartCapture();
                    while (!token.IsCancellationRequested)
                    {
                        Thread.Sleep(100);
                    }
                }, token);
            }
            catch (Exception ex)
            {
                onPacketReceived($"❌ Error: {ex.Message}");
            }
            finally
            {
                // Ensure proper cleanup
                if (selectedDevice.Started)
                {
                    selectedDevice.StopCapture();
                }
                selectedDevice.Close();
                pcapWriter.Close();
            }
        }
    }
}