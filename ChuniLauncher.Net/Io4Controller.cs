using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ChuniLauncher.Net
{
    // Reads the Sega IO4 (LKICK firmware) through the winmm joystick API
    public class Io4Controller : IDisposable
    {
        public event Action? OnTest;
        public event Action? OnService;

        private const ushort IoVendorId = 0x0CA3;
        private const ushort IoProductId = 0x0021;
        private const uint JoyReturnButtons = 0x80;

        private readonly uint _testMask;
        private readonly uint _serviceMask;
        private int _joyId = -1;
        private Thread? _pollThread;
        private bool _isRunning;

        // Button numbers are 1-based, as shown in the Windows game controller test
        public Io4Controller(int testButton, int serviceButton)
        {
            _testMask = 1u << (testButton - 1);
            _serviceMask = 1u << (serviceButton - 1);

            uint count = joyGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                var caps = new JOYCAPS();
                if (joyGetDevCapsW((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<JOYCAPS>()) != 0) continue;

                if ((caps.wMid == IoVendorId && caps.wPid == IoProductId) || caps.szPname.Contains("I/O CONTROL"))
                {
                    _joyId = i;
                    break;
                }
            }

            if (_joyId < 0) return;

            _isRunning = true;
            _pollThread = new Thread(PollLoop);
            _pollThread.IsBackground = true;
            _pollThread.Start();
        }

        private void PollLoop()
        {
            uint previous = 0;
            var info = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags = JoyReturnButtons };

            while (_isRunning)
            {
                if (joyGetPosEx((UIntPtr)_joyId, ref info) == 0)
                {
                    // Only fire on the press, not while the button is held
                    uint pressed = info.dwButtons & ~previous;
                    previous = info.dwButtons;

                    if (_isRunning && (pressed & _testMask) != 0) OnTest?.Invoke();
                    if (_isRunning && (pressed & _serviceMask) != 0) OnService?.Invoke();
                }
                Thread.Sleep(10);
            }
        }

        public void Dispose()
        {
            // No join here: the poll thread marshals events to the UI thread, which may be the caller
            _isRunning = false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwXpos;
            public uint dwYpos;
            public uint dwZpos;
            public uint dwRpos;
            public uint dwUpos;
            public uint dwVpos;
            public uint dwButtons;
            public uint dwButtonNumber;
            public uint dwPOV;
            public uint dwReserved1;
            public uint dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPS
        {
            public ushort wMid;
            public ushort wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin;
            public uint wXmax;
            public uint wYmin;
            public uint wYmax;
            public uint wZmin;
            public uint wZmax;
            public uint wNumButtons;
            public uint wPeriodMin;
            public uint wPeriodMax;
            public uint wRmin;
            public uint wRmax;
            public uint wUmin;
            public uint wUmax;
            public uint wVmin;
            public uint wVmax;
            public uint wCaps;
            public uint wMaxAxes;
            public uint wNumAxes;
            public uint wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }

        [DllImport("winmm.dll")]
        private static extern uint joyGetNumDevs();

        [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW", CharSet = CharSet.Unicode)]
        private static extern uint joyGetDevCapsW(UIntPtr uJoyID, ref JOYCAPS pjc, uint cbjc);

        [DllImport("winmm.dll")]
        private static extern uint joyGetPosEx(UIntPtr uJoyID, ref JOYINFOEX pji);
    }
}
