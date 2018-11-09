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

    // Bits for SMXConfig::flags.
    public enum SMXConfigFlags {
        SMXConfigFlags_AutoLightingUsePressedAnimations = 1 << 0,
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

        // Master delay debouncing (version >= 3).  If enabled, this will add a
        // corresponding delay to inputs, which the game needs to compensate for.
        // This is disabled by default.
        public UInt16 debounceDelayMs;

        // Packed flags (SMXConfigFlags).
        public Byte flags;

        // It would be simpler to set flags to [MarshalAs(UnmanagedType.U8)], but
        // that doesn't work.
        public SMXConfigFlags configFlags {
            get {
                return (SMXConfigFlags) flags;
            }

            set {
                flags = (Byte) value;
            }
        }

        // Pad this struct to exactly 250 bytes.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 166)]

        public Byte[] padding;

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

        // The layout of this structure (and the underlying C struct) matches the firmware configuration
        // data.  This is a bit inconvenient for the panel thresholds which aren't contiguous, so these
        // helpers just convert them to and from arrays.
        public Byte[] GetLowThresholds()
        {
            return new Byte[] {
                panelThreshold0Low,
                panelThreshold1Low,
                panelThreshold2Low,
                panelThreshold3Low,
                panelThreshold4Low,
                panelThreshold5Low,
                panelThreshold6Low,
                panelThreshold7Low,
                panelThreshold8Low,
            };
        }

        public Byte[] GetHighThresholds()
        {
            return new Byte[] {
                panelThreshold0High,
                panelThreshold1High,
                panelThreshold2High,
                panelThreshold3High,
                panelThreshold4High,
                panelThreshold5High,
                panelThreshold6High,
                panelThreshold7High,
                panelThreshold8High,
            };
        }

        public void SetLowThresholds(Byte[] values)
        {
            panelThreshold0Low = values[0];
            panelThreshold1Low = values[1];
            panelThreshold2Low = values[2];
            panelThreshold3Low = values[3];
            panelThreshold4Low = values[4];
            panelThreshold5Low = values[5];
            panelThreshold6Low = values[6];
            panelThreshold7Low = values[7];
            panelThreshold8Low = values[8];
        }
        
        public void SetHighThresholds(Byte[] values)
        {
            panelThreshold0High = values[0];
            panelThreshold1High = values[1];
            panelThreshold2High = values[2];
            panelThreshold3High = values[3];
            panelThreshold4High = values[4];
            panelThreshold5High = values[5];
            panelThreshold6High = values[6];
            panelThreshold7High = values[7];
            panelThreshold8High = values[8];
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
        [System.Flags]
        enum LoadLibraryFlags : uint
        {    
              None = 0, 
              DONT_RESOLVE_DLL_REFERENCES = 0x00000001,
              LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,
              LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
              LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,
              LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,
              LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200,
              LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
              LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100,
              LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800,
              LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400,
              LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008
        }

        [DllImport("kernel32", SetLastError=true)]
        static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, LoadLibraryFlags dwFlags);
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
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr SMX_Version();

        public enum LightsType
        {
            LightsType_Released, // animation while panels are released
            LightsType_Pressed, // animation while panel is pressed
        };

        public static string Version()
        {
            if(!DLLAvailable()) return "";

            // I can't find any way to marshal a simple null-terminated string.  Marshalling
            // UnmanagedType.LPStr tries to deallocate the string, which crashes since it's
            // a static string.
            unsafe {
                sbyte *p = (sbyte *) SMX_Version();
                int length = 0;
                while(p[length] != 0)
                    ++length;
                return new string(p, 0, length);
            }
        }

        // Check if the native DLL is available.  This is mostly to avoid exceptions in the designer.
        // This returns false if the DLL doesn't load.
        public static bool DLLAvailable()
        {
            return LoadLibrary("SMX.dll") != IntPtr.Zero;
        }

        // Check if the native DLL exists.  This will return false if SMX.dll is missing entirely,
        // but not if it fails to load for another reason like runtime dependencies.  This just lets
        // us print a more specific error message.
        public static bool DLLExists()
        {
            return LoadLibraryEx("SMX.dll", (IntPtr)0, LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE) != IntPtr.Zero;
        }
        
        public delegate void UpdateCallback(int PadNumber, SMXUpdateCallbackReason reason);

        // The C API allows a user pointer, but we don't use that here.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void InternalUpdateCallback(int PadNumber, int reason, IntPtr user);
        private static InternalUpdateCallback CurrentUpdateCallback;

        public static void Start(UpdateCallback callback)
        {
            if(!DLLAvailable()) return;

            // Sanity check SMXConfig, which should be 250 bytes.  If this is incorrect,
            // check the padding array.
            {
                SMXConfig config = new SMXConfig();
                int bytes = Marshal.SizeOf(config);
                if(bytes != 250)
                    throw new Exception("SMXConfig is " + bytes + " bytes, but should be 250 bytes");
            }

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

            if(buf.Length != 9*16*3*2)
                throw new Exception("SetLights buffer has an invalid length: " + buf.Length);
            SMX_SetLights(buf);
        }

        // SMXPanelAnimation
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return:MarshalAs(UnmanagedType.I1)]
        private static extern bool SMX_LightsAnimation_Load(byte[] buf, int size, int pad, int type, out IntPtr error);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void SMX_LightsAnimation_SetAuto(bool enable);

        public static bool LightsAnimation_Load(byte[] buf, int pad, LightsType type, out string error)
        {
            if(!DLLAvailable())
            {
                error = "SMX.DLL not available";
                return false;
            }

            error = "";
            IntPtr error_pointer;
            bool result = SMX_LightsAnimation_Load(buf, buf.Length, pad, (int) type, out error_pointer);
            if(!result)
            {
                // SMX_LightsAnimation_Load takes a char **error, which is set to the error
                // string.
                error = Marshal.PtrToStringAnsi(error_pointer);
            }

            return result;
        }

        public static void LightsAnimation_SetAuto(bool enable)
        {
            if(!DLLAvailable()) return;
            SMX_LightsAnimation_SetAuto(enable);
        }
        
        // SMXPanelAnimationUpload
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return:MarshalAs(UnmanagedType.I1)]
        private static extern bool SMX_LightsUpload_PrepareUpload(int pad, out IntPtr error);
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern void SMX_LightsUpload_BeginUpload(int pad,
            [MarshalAs(UnmanagedType.FunctionPtr)] InternalLightsUploadCallback callback,
            IntPtr user);

        public static bool LightsUpload_PrepareUpload(int pad, out string error)
        {
            if(!DLLAvailable())
            {
                error = "SMX.DLL not available";
                return false;
            }

            error = "";
            IntPtr error_pointer;
            bool result = SMX_LightsUpload_PrepareUpload(pad, out error_pointer);
            if(!result)
            {
                // SMX_LightsAnimation_Load takes a char **error, which is set to the error
                // string.
                error = Marshal.PtrToStringAnsi(error_pointer);
            }

            return result;
        }

        public delegate void LightsUploadCallback(int progress);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InternalLightsUploadCallback(int reason, IntPtr user);
        public static void LightsUpload_BeginUpload(int pad, LightsUploadCallback callback)
        {
            if(!DLLAvailable())
                return;

            GCHandle handle = new GCHandle();
            InternalLightsUploadCallback wrapper = delegate(int progress, IntPtr user)
            {
                try {
                    callback(progress);
                } finally {
                    // When progress = 100, this is the final call and we can release this
                    // object to GC.
                    if(progress == 100)
                        handle.Free();
                }
            };

            // Pin the callback until we get the last call.
            handle = GCHandle.Alloc(wrapper);

            SMX_LightsUpload_BeginUpload(pad, wrapper, IntPtr.Zero);
        }
    }
}
