using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChuniLauncher.Net
{
    public class SliderController : IDisposable
    {
        public event Action? OnUp;
        public event Action? OnDown;
        public event Action? OnSelect;
        public event Action? OnNextCategory;
        public event Action? OnPrevCategory;
        public event Action? OnExit;
        
        public event Action<int, bool>? OnTouchStateChanged;

        public bool IsAtTop { get; set; }
        public bool IsAtBottom { get; set; }

        private SerialPort? _serialPort;
        private Thread? _readThread;
        private bool _isRunning;
        private int _sensitivity;
        
        // Cooldown timestamps
        private DateTime _lastUp = DateTime.MinValue;
        private DateTime _lastDown = DateTime.MinValue;
        private DateTime _lastSelect = DateTime.MinValue;
        private DateTime _lastNextCategory = DateTime.MinValue;
        private DateTime _lastPrevCategory = DateTime.MinValue;

        private readonly TimeSpan _cooldown = TimeSpan.FromMilliseconds(250);

        private byte[] _leds = new byte[94];
        private DateTime _lastKeepAlive = DateTime.MinValue;
        private bool _isUpTouched;
        private bool _isDownTouched;
        private bool _isSelectTouched;
        private bool _isPrevTouched;
        private bool _isNextTouched;
        private bool _isExitTouched;

        private DateTime _upReleaseTime = DateTime.MinValue;
        private DateTime _downReleaseTime = DateTime.MinValue;
        private DateTime _selectReleaseTime = DateTime.MinValue;
        private DateTime _prevReleaseTime = DateTime.MinValue;
        private DateTime _nextReleaseTime = DateTime.MinValue;
        private DateTime _exitReleaseTime = DateTime.MinValue;

        public SliderController(string portName, int sensitivity)
        {
            _sensitivity = sensitivity;
            
            try
            {
                _serialPort = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 500;
                _serialPort.WriteTimeout = 500;
                _serialPort.Open();

                _isRunning = true;
                _readThread = new Thread(ReadLoop);
                _readThread.IsBackground = true;
                _readThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open slider port {portName}: {ex.Message}");
            }
        }

        private void SendPacket(byte command, byte[] data)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            List<byte> raw = new List<byte>();
            raw.Add(0xFF);
            
            void PushEscaped(byte b)
            {
                if (b == 0xFD) { raw.Add(0xFD); raw.Add(0xFC); }
                else if (b == 0xFF) { raw.Add(0xFD); raw.Add(0xFE); }
                else raw.Add(b);
            }

            PushEscaped(command);
            PushEscaped((byte)data.Length);
            foreach (var b in data)
            {
                PushEscaped(b);
            }

            long sum = (long)0xFF + command + data.Length + data.Sum(x => (long)x);
            byte checksum = (byte)((0x100 - (sum & 0xFF)) & 0xFF);
            PushEscaped(checksum);

            _serialPort.Write(raw.ToArray(), 0, raw.Count);
        }

        private void SetLed(int index, byte r, byte g, byte b, bool isTouched, DateTime releaseTime)
        {
            if (index < 0 || index >= 31) return;
            int wireIndex = 30 - index;
            
            float multiplier = 1.0f;
            if (isTouched)
            {
                multiplier = 0.25f;
            }
            else
            {
                double msSinceRelease = (DateTime.Now - releaseTime).TotalMilliseconds;
                if (msSinceRelease < 200)
                {
                    multiplier = 0.25f + (float)(0.75 * (msSinceRelease / 200.0));
                }
            }

            r = (byte)(r * multiplier);
            g = (byte)(g * multiplier);
            b = (byte)(b * multiplier);

            _leds[1 + wireIndex * 3 + 0] = b;
            _leds[1 + wireIndex * 3 + 1] = r;
            _leds[1 + wireIndex * 3 + 2] = g;
        }

        private void UpdateLeds()
        {
            _leds[0] = 255; // Brightness

            // Keys 1-3 (0, 2, 4) and Sep 1,2 (1, 3) -> Green
            byte upG = (byte)(IsAtTop ? 51 : 255); // 51 is 20% of 255
            for (int i = 0; i <= 4; i++) SetLed(i, 0, upG, 0, _isUpTouched, _upReleaseTime);

            // Keys 4-6 (6, 8, 10) and Sep 4,5 (7, 9) -> Green
            byte downG = (byte)(IsAtBottom ? 51 : 255);
            for (int i = 6; i <= 10; i++) SetLed(i, 0, downG, 0, _isDownTouched, _downReleaseTime);

            // Keys 7-10 (12, 14, 16, 18) and Sep 7-9 (13, 15, 17) -> Orange
            for (int i = 12; i <= 18; i++) SetLed(i, 255, 128, 0, _isSelectTouched, _selectReleaseTime);

            // Keys 11,12 (20, 22) and Sep 11 (21) -> White
            for (int i = 20; i <= 22; i++) SetLed(i, 255, 255, 255, _isPrevTouched, _prevReleaseTime);

            // Keys 13,14 (24, 26) and Sep 13 (25) -> White
            for (int i = 24; i <= 26; i++) SetLed(i, 255, 255, 255, _isNextTouched, _nextReleaseTime);

            // Keys 15,16 (28, 30) and Sep 15 (29) -> Red
            for (int i = 28; i <= 30; i++) SetLed(i, 255, 0, 0, _isExitTouched, _exitReleaseTime);

            SendPacket(0x02, _leds);
            _lastKeepAlive = DateTime.Now;
        }

        private void ReadLoop()
        {
            if (_serialPort == null) return;

            int state = 0; // 0=WaitCmd, 1=WaitLen, 2=WaitData, 3=WaitChecksum
            byte currentCommand = 0;
            byte currentLen = 0;
            List<byte> currentData = new List<byte>();
            long currentChecksum = 0;
            bool escapeNext = false;

            // Bootstrap
            SendPacket(0x10, new byte[0]);
            bool initDone = false;
            bool infoDone = false;
            _lastKeepAlive = DateTime.Now;
            UpdateLeds();

            while (_isRunning && _serialPort.IsOpen)
            {
                try
                {
                    if (infoDone && (DateTime.Now - _lastKeepAlive).TotalMilliseconds > 500)
                    {
                        UpdateLeds();
                    }

                    int readByte = _serialPort.ReadByte();
                    if (readByte == -1) continue;

                    byte b = (byte)readByte;

                    if (b == 0xFF)
                    {
                        state = 0;
                        currentChecksum = 0xFF;
                        escapeNext = false;
                        continue;
                    }
                    if (b == 0xFD)
                    {
                        escapeNext = true;
                        continue;
                    }

                    if (escapeNext)
                    {
                        b = (byte)(b + 1); // 0xFC+1 = 0xFD, 0xFE+1 = 0xFF
                        escapeNext = false;
                    }

                    currentChecksum += b;

                    switch (state)
                    {
                        case 0:
                            currentCommand = b;
                            state = 1;
                            break;
                        case 1:
                            currentLen = b;
                            currentData.Clear();
                            state = currentLen == 0 ? 3 : 2;
                            break;
                        case 2:
                            currentData.Add(b);
                            if (currentData.Count == currentLen)
                            {
                                state = 3;
                            }
                            break;
                        case 3:
                            if ((currentChecksum & 0xFF) == 0)
                            {
                                ProcessPacket(currentCommand, currentData.ToArray(), ref initDone, ref infoDone);
                            }
                            state = -1; // Wait for next 0xFF
                            break;
                    }
                }
                catch (TimeoutException)
                {
                    // Ignore read timeouts
                }
                catch
                {
                    break;
                }
            }
        }

        private void ProcessPacket(byte command, byte[] data, ref bool initDone, ref bool infoDone)
        {
            if (!initDone && command == 0x10 && data.Length == 0)
            {
                initDone = true;
                SendPacket(0xF0, new byte[0]);
                return;
            }
            if (initDone && !infoDone && command == 0xF0)
            {
                infoDone = true;
                SendPacket(0x03, new byte[0]);
                return;
            }
            if (infoDone && command == 0x01 && data.Length == 32)
            {
                ProcessSensors(data);
            }
        }

        private DateTime _lastExit = DateTime.MinValue;

        private void ProcessSensors(byte[] data)
        {
            DateTime now = DateTime.Now;

            // Helper to check if any sensor in range exceeds sensitivity
            bool IsTouched(int startIndex, int endIndex)
            {
                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (i >= 0 && i < data.Length && data[i] >= _sensitivity)
                    {
                        return true;
                    }
                }
                return false;
            }

            // Check current touches
            bool newUp = IsTouched(26, 31);
            bool newDown = IsTouched(20, 25);
            bool newSelect = IsTouched(12, 19);
            bool newPrev = IsTouched(8, 11);
            bool newNext = IsTouched(4, 7);
            bool newExit = IsTouched(0, 3); // Keys 15,16 are indices 0-3

            bool ledsChanged = false;
            if (newUp != _isUpTouched) { _isUpTouched = newUp; if (!newUp) _upReleaseTime = now; ledsChanged = true; OnTouchStateChanged?.Invoke(0, newUp); }
            if (newDown != _isDownTouched) { _isDownTouched = newDown; if (!newDown) _downReleaseTime = now; ledsChanged = true; OnTouchStateChanged?.Invoke(1, newDown); }
            if (newSelect != _isSelectTouched) { _isSelectTouched = newSelect; if (!newSelect) _selectReleaseTime = now; ledsChanged = true; OnTouchStateChanged?.Invoke(2, newSelect); }
            if (newPrev != _isPrevTouched) { _isPrevTouched = newPrev; if (!newPrev) _prevReleaseTime = now; ledsChanged = true; OnTouchStateChanged?.Invoke(3, newPrev); }
            if (newNext != _isNextTouched) { _isNextTouched = newNext; if (!newNext) _nextReleaseTime = now; ledsChanged = true; OnTouchStateChanged?.Invoke(4, newNext); }
            if (newExit != _isExitTouched) { _isExitTouched = newExit; if (!newExit) _exitReleaseTime = now; ledsChanged = true; OnTouchStateChanged?.Invoke(5, newExit); }

            bool isFading = 
                (!_isUpTouched && (now - _upReleaseTime).TotalMilliseconds < 200) ||
                (!_isDownTouched && (now - _downReleaseTime).TotalMilliseconds < 200) ||
                (!_isSelectTouched && (now - _selectReleaseTime).TotalMilliseconds < 200) ||
                (!_isPrevTouched && (now - _prevReleaseTime).TotalMilliseconds < 200) ||
                (!_isNextTouched && (now - _nextReleaseTime).TotalMilliseconds < 200) ||
                (!_isExitTouched && (now - _exitReleaseTime).TotalMilliseconds < 200);

            if (ledsChanged || isFading)
            {
                if ((now - _lastKeepAlive).TotalMilliseconds > 16) // roughly 60 FPS cap
                {
                    UpdateLeds();
                }
            }

            // Map sensors based on user request (0-indexed)
            // Indices 26-31: Scroll Up
            if (_isUpTouched && now - _lastUp > _cooldown)
            {
                _lastUp = now;
                OnUp?.Invoke();
            }
            
            // Indices 20-25: Scroll Down
            if (_isDownTouched && now - _lastDown > _cooldown)
            {
                _lastDown = now;
                OnDown?.Invoke();
            }

            // Indices 12-19: Select
            if (_isSelectTouched && now - _lastSelect > _cooldown)
            {
                _lastSelect = now;
                OnSelect?.Invoke();
            }

            // Indices 8-11: Previous Category
            if (_isPrevTouched && now - _lastPrevCategory > _cooldown)
            {
                _lastPrevCategory = now;
                OnPrevCategory?.Invoke();
            }

            // Indices 4-7: Next Category
            if (_isNextTouched && now - _lastNextCategory > _cooldown)
            {
                _lastNextCategory = now;
                OnNextCategory?.Invoke();
            }

            // Indices 0-3: Exit
            if (_isExitTouched && now - _lastExit > _cooldown)
            {
                _lastExit = now;
                OnExit?.Invoke();
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            if (_serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    SendPacket(0x04, new byte[0]); // Stop packet
                    _serialPort.Close();
                }
                catch { }
            }
        }
    }
}
