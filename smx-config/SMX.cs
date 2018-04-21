using System;
using System.Runtime.InteropServices;
using smx_config;

// This is a binding to the native SMX.dll.
namespace SMX
{
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct SMXInfo {
        [MarshalAs(UnmanagedType.I1)]  // work around C# bug: marshals bool as int
        public bool connected;

        // The 32-byte hex serial number of the device, followed by '\0'.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 33)]
        public byte[] m_Serial;

        public Int16 m_iFirmwareVersion;

        // Padding to make this the same size as native, where MSVC adds padding even though
        // we tell it not to:
        private Byte dummy;
    };

    [StructLayout(LayoutKind.Sequential, Pack=1)]  
    public struct SMXConfig {  
        public Byte unused1, unused2;
        public Byte unused3, unused4;
        public Byte unused5, unused6;
        public UInt16 masterDebounceMilliseconds;
        public Byte panelThreshold7Low, panelThreshold7High; // was "cardinal"
        public Byte panelThreshold4Low, panelThreshold4High; // was "center"
        public Byte panelThreshold2Low, panelThreshold2High; // was "corner"
        public UInt16 panelDebounceMicroseconds;
        public UInt16 autoCalibrationPeriodMilliseconds;
        public Byte autoCalibrationMaxDeviation;
        public Byte badSensorMinimumDelaySeconds;
        public UInt16 autoCalibrationAveragesPerUpdate;
        public Byte unused7, unused8;
        public Byte panelThreshold1Low, panelThreshold1High; // was "up"

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public Byte[] enabledSensors;

        public Byte autoLightsTimeout;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3*9)]
        public Byte[] stepColor;

        public Byte panelRotation;
        public UInt16 autoCalibrationSamplesPerAverage;
        public Byte masterVersion;
        public Byte configVersion;

        // The remaining thresholds (configVersion >= 2).
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public Byte[] unused9;
        public Byte panelThreshold0Low, panelThreshold0High;
        public Byte panelThreshold3Low, panelThreshold3High;
        public Byte panelThreshold5Low, panelThreshold5High;
        public Byte panelThreshold6Low, panelThreshold6High;
        public Byte panelThreshold8Low, panelThreshold8High;

        // enabledSensors is a mask of which panels are enabled.  Return this as an array
        // for convenience.
        public bool[] GetEnabledPanels()
        {
            return new bool[] {
                (enabledSensors[0] & 0xF0) != 0,
                (enabledSensors[0] & 0x0F) != 0,
                (enabledSensors[1] & 0xF0) != 0,
                (enabledSensors[1] & 0x0F) != 0,
                (enabledSensors[2] & 0xF0) != 0,
                (enabledSensors[2] & 0x0F) != 0,
                (enabledSensors[3] & 0xF0) != 0,
                (enabledSensors[3] & 0x0F) != 0,
                (enabledSensors[4] & 0xF0) != 0,
            };
        }

        // Set enabledSensors from an array returned from GetEnabledPanels.
        public void SetEnabledPanels(bool[] panels)
        {
            for(int i = 0; i < 5; ++i)
                enabledSensors[i] = 0;

            if(panels[0]) enabledSensors[0] |= 0xF0;
            if(panels[1]) enabledSensors[0] |= 0x0F;
            if(panels[2]) enabledSensors[1] |= 0xF0;
            if(panels[3]) enabledSensors[1] |= 0x0F;
            if(panels[4]) enabledSensors[2] |= 0xF0;
            if(panels[5]) enabledSensors[2] |= 0x0F;
            if(panels[6]) enabledSensors[3] |= 0xF0;
            if(panels[7]) enabledSensors[3] |= 0x0F;
            if(panels[8]) enabledSensors[4] |= 0xF0;
        }
    };  

    public struct SMXSensorTestModeData
    {
        // If false, sensorLevel[n][*] is zero because we didn't receive a response from that panel.
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType=UnmanagedType.I1, SizeConst = 9)]
        public bool[] bHaveDataFromPanel;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9*4)]
        public Int16[] sensorLevel;

        [MarshalAs(UnmanagedType.ByValArray, ArraySubType=UnmanagedType.I1, SizeConst = 9*4)]
        public bool[] bBadSensorInput;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public int[] iDIPSwitchPerPanel;

        public override bool Equals(object obj)
        {
            SMXSensorTestModeData other = (SMXSensorTestModeData) obj;
            return
                Helpers.SequenceEqual(bHaveDataFromPanel, other.bHaveDataFromPanel) &&
                Helpers.SequenceEqual(sensorLevel, other.sensorLevel) &&
                Helpers.SequenceEqual(bBadSensorInput, other.bBadSensorInput) &&
                Helpers.SequenceEqual(iDIPSwitchPerPanel, other.iDIPSwitchPerPanel);
        }

        // Dummy override to silence a bad warning.  We don't use these in containers to need
        // a hash code implementation.
        public override int GetHashCode() { return base.GetHashCode(); }


        public bool AnySensorsOnPanelNotResponding(int panel)
        {
            if(!bHaveDataFromPanel[panel])
                return false;
            for(int sensor = 0; sensor < 4; ++sensor)
                if(bBadSensorInput[panel*4+sensor])
                    return true;

            return false;
        }
    };

    public static class SMX
    {
        [DllImport("kernel32", SetLastError=true)]
        static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SMX_Start(
            [MarshalAs(UnmanagedType.FunctionPtr)] InternalUpdateCallback callback,
            IntPtr user);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SMX_Stop();
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SMX_GetInfo(int pad, out SMXInfo info);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern UInt16 SMX_GetInputState(int pad);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        [return:MarshalAs(UnmanagedType.I1)]
        private static extern bool SMX_GetConfig(int pad, out SMXConfig config);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SMX_SetConfig(int pad, ref SMXConfig config);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SMX_FactoryReset(int pad);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SMX_ForceRecalibration(int pad);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SMX_SetTestMode(int pad, int mode);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SMX_GetTestData(int pad, out SMXSensorTestModeData data);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SMX_SetLights(byte[] buf);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SMX_ReenableAutoLights();

        // Check if the native DLL is available.  This is mostly to avoid exceptions in the designer.
        private static bool DLLAvailable()
        {
            return LoadLibrary("SMX.dll") != IntPtr.Zero;
        }

        public delegate void UpdateCallback(int PadNumber, SMXUpdateCallbackReason reason);

        // The C API allows a user pointer, but we don't use that here.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void InternalUpdateCallback(int PadNumber, int reason, IntPtr user);
        private static InternalUpdateCallback CurrentUpdateCallback;
        public static void SetUpdateCallback(UpdateCallback callback)
        {
            if(!DLLAvailable()) return;

        }

        public static void Start(UpdateCallback callback)
        {
            if(!DLLAvailable()) return;

            // Make a wrapper to convert from the native enum to SMXUpdateCallbackReason.
            InternalUpdateCallback NewCallback = delegate(int PadNumber, int reason, IntPtr user) {
                SMXUpdateCallbackReason ReasonEnum = (SMXUpdateCallbackReason) Enum.ToObject(typeof(SMXUpdateCallbackReason), reason);
                callback(PadNumber, ReasonEnum);
            };
            if(callback == null)
                NewCallback = null;

            SMX_Start(NewCallback, IntPtr.Zero);

            // Keep a reference to the delegate, so it isn't garbage collected.  Do this last.  Once
            // we do this the old callback may be collected, so we want to be sure that native code
            // has already updated the callback.
            CurrentUpdateCallback = NewCallback;
            
            Console.WriteLine("Struct sizes (C#): " +
                Marshal.SizeOf(typeof(SMXConfig)) + " " +
                Marshal.SizeOf(typeof(SMXInfo)) + " " +
                Marshal.SizeOf(typeof(SMXSensorTestModeData)));
        }

        public static void Stop()
        {
            if(!DLLAvailable()) return;
            SMX_Stop();
        }

        public enum SMXUpdateCallbackReason {
            Updated,
            FactoryResetCommandComplete
        };

        public static void GetInfo(int pad, out SMXInfo info)
        {
            if(!DLLAvailable()) {
                info = new SMXInfo();
                return;
            }
            SMX_GetInfo(pad, out info);
        }

        public static UInt16 GetInputState(int pad)
        {
            if(!DLLAvailable())
                return 0;

            return SMX_GetInputState(pad);
        }
        
        public static bool GetConfig(int pad, out SMXConfig config)
        {
            if(!DLLAvailable()) {
                config = new SMXConfig();
                config.enabledSensors = new Byte[5];
                config.stepColor = new Byte[3*9];
                return false;
            }
            return SMX_GetConfig(pad, out config);
        }
                
        public static void SetConfig(int pad, SMXConfig config)
        {
            if(!DLLAvailable()) return;

            // Always bump the configVersion to the version we support on write.
            config.configVersion = 2;

            SMX_SetConfig(pad, ref config);
        }

        public enum SensorTestMode {
                Off = 0,
                UncalibratedValues = '0',
                CalibratedValues = '1',
                Noise = '2',
                Tare = '3',
        };

        public static void SetTestMode(int pad, SensorTestMode mode)
        {
            if(!DLLAvailable()) return;
            SMX_SetTestMode(pad, (int) mode);
        }

        public static bool GetTestData(int pad, out SMXSensorTestModeData data)
        {
            if(!DLLAvailable()) {
                data = new SMXSensorTestModeData();
                return false;
            }

            return SMX_GetTestData(pad, out data);
        }

        public static void FactoryReset(int pad)
        {
            if(!DLLAvailable()) return;
            SMX_FactoryReset(pad);
        }


        public static void ForceRecalibration(int pad)
        {
            if(!DLLAvailable()) return;
            SMX_ForceRecalibration(pad);
        }

        public static void SetLights(byte[] buf)
        {
            if(!DLLAvailable()) return;
            SMX_SetLights(buf);
        }

        public static void ReenableAutoLights()
        {
            if(!DLLAvailable()) return;
            SMX_ReenableAutoLights();
        }
    }
}
