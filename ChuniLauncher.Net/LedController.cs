using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Windows.Media;

namespace ChuniLauncher.Net
{
    public class LedController : IDisposable
    {
        private List<SerialPort> _serialPorts = new List<SerialPort>();
        private Thread? _updateThread;
        private bool _isRunning;
        private Color _currentColor = Colors.Black;
        private Color _targetColor = Colors.Black;
        private DateTime _colorChangedTime = DateTime.MinValue;
        private double _brightness;
        
        public LedController(string led1Port, string led2Port, double brightness)
        {
            _brightness = brightness;
            
            Action<string> openPort = (portName) => 
            {
                if (string.IsNullOrWhiteSpace(portName)) return;
                try {
                    var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
                    port.WriteTimeout = 500;
                    port.Open();
                    _serialPorts.Add(port);
                } catch (Exception ex) {
                    Debug.WriteLine($"Failed to open LED port {portName}: {ex.Message}");
                }
            };
            
            openPort(led1Port);
            openPort(led2Port);

            if (_serialPorts.Count > 0)
            {
                _isRunning = true;
                _updateThread = new Thread(UpdateLoop);
                _updateThread.IsBackground = true;
                _updateThread.Start();
            }
        }
        
        public void SetColor(Color color)
        {
            if (_targetColor != color)
            {
                _currentColor = GetInterpolatedColor();
                _targetColor = color;
                _colorChangedTime = DateTime.Now;
            }
        }

        private Color GetInterpolatedColor()
        {
            double elapsedMs = (DateTime.Now - _colorChangedTime).TotalMilliseconds;
            double progress = elapsedMs / 300.0; // 300ms fade
            if (progress >= 1.0) return _targetColor;
            if (progress <= 0) return _currentColor;
            
            byte r = (byte)(_currentColor.R + (_targetColor.R - _currentColor.R) * progress);
            byte g = (byte)(_currentColor.G + (_targetColor.G - _currentColor.G) * progress);
            byte b = (byte)(_currentColor.B + (_targetColor.B - _currentColor.B) * progress);
            return Color.FromRgb(r, g, b);
        }

        private void SendPacket(SerialPort port, byte dest, byte src, byte command, byte[] data)
        {
            if (!port.IsOpen) return;
            
            List<byte> raw = new List<byte>();
            raw.Add(0xE0);

            void PushEscaped(byte b)
            {
                if (b == 0xE0 || b == 0xD0) {
                    raw.Add(0xD0);
                    raw.Add((byte)(b - 1));
                } else {
                    raw.Add(b);
                }
            }

            int checksum = 0;
            void PushData(byte b) {
                checksum += b;
                PushEscaped(b);
            }

            PushData(dest);
            PushData(src);
            PushData((byte)(data.Length + 1));
            PushData(command);
            foreach (var b in data) {
                PushData(b);
            }

            raw.Add((byte)(checksum % 0x100));

            try {
                port.Write(raw.ToArray(), 0, raw.Count);
            } catch {
                // Ignore write timeouts/errors
            }
        }

        private void UpdateLoop()
        {
            foreach (var port in _serialPorts)
            {
                SendPacket(port, 0x01, 0x02, 0x10, new byte[] { 0xD9 }); // Reset
                Thread.Sleep(50);
                SendPacket(port, 0x01, 0x02, 0x14, new byte[] { 0x01 }); // Disable response
                Thread.Sleep(50);
            }

            while (_isRunning)
            {
                var color = GetInterpolatedColor();
                byte r = (byte)(color.R * _brightness);
                byte g = (byte)(color.G * _brightness);
                byte b = (byte)(color.B * _brightness);
                
                byte[] pixels = new byte[66 * 3];
                // Left air (50-52)
                for (int i = 50; i <= 52; i++) {
                    pixels[i * 3 + 0] = r;
                    pixels[i * 3 + 1] = g;
                    pixels[i * 3 + 2] = b;
                }
                // Right air (60-62)
                for (int i = 60; i <= 62; i++) {
                    pixels[i * 3 + 0] = r;
                    pixels[i * 3 + 1] = g;
                    pixels[i * 3 + 2] = b;
                }
                
                foreach (var port in _serialPorts)
                {
                    SendPacket(port, 0x01, 0x02, 0x82, pixels);
                }
                
                Thread.Sleep(16); // ~60fps
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            if (_updateThread != null && _updateThread.IsAlive)
            {
                _updateThread.Join(500);
            }
            foreach (var port in _serialPorts)
            {
                if (port.IsOpen) port.Close();
            }
        }
    }
}
