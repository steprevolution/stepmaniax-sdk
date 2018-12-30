using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Resources;
using System.Windows.Threading;
using SMXJSON;

namespace smx_config
{
    // Track whether we're configuring one pad or both at once.
    static class ActivePad
    {
        public enum SelectedPad {
            P1,
            P2,
            Both,
        };
        
        // The actual pad selection.  This defaults to both, and doesn't change if
        // only one pad is selected.  We don't actually show "both" in the dropdown
        // unless two pads are connected, but the underlying setting remains.
        public static SelectedPad selectedPad = SelectedPad.Both;

        // A shortcut for when a LoadFromConfigDelegateArgs isn't available:
        public static IEnumerable<Tuple<int, SMX.SMXConfig>> ActivePads()
        {
            // In case we're called in design mode, just return an empty list.
            if(CurrentSMXDevice.singleton == null)
                return new List<Tuple<int, SMX.SMXConfig>>();

            return ActivePads(CurrentSMXDevice.singleton.GetState());
        }

        // Yield each connected pad which is currently active for configuration.
        public static IEnumerable<Tuple<int, SMX.SMXConfig>> ActivePads(LoadFromConfigDelegateArgs args)
        {
            bool Pad1Connected = args.controller[0].info.connected;
            bool Pad2Connected = args.controller[1].info.connected;

            // If both pads are connected and a single pad is selected, ignore the deselected pad.
            if(Pad1Connected && Pad2Connected)
            {
                if(selectedPad == SelectedPad.P1)
                    Pad2Connected = false;
                if(selectedPad == SelectedPad.P2)
                    Pad1Connected = false;
            }

            if(Pad1Connected)
                yield return Tuple.Create(0, args.controller[0].config);
            if(Pad2Connected)
                yield return Tuple.Create(1, args.controller[1].config);
        }

        // We know the selected pads are synced if there are two active, and when refreshing a
        // UI we just want one of them to set the UI to.  For convenience, return the first one.
        public static SMX.SMXConfig GetFirstActivePadConfig(LoadFromConfigDelegateArgs args)
        {
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePads(args))
                return activePad.Item2;

            // There aren't any pads connected.  Just return a dummy config, since the UI
            // isn't visible.
            return SMX.SMXConfig.Create();
        }

        public static SMX.SMXConfig GetFirstActivePadConfig()
        {
            return GetFirstActivePadConfig(CurrentSMXDevice.singleton.GetState());
        }
    }

    static class Helpers
    {
        // Return true if arg is in the commandline.
        public static bool HasCommandlineArgument(string arg)
        {
            foreach(string s in Environment.GetCommandLineArgs())
            {
                if(s == arg)
                    return true;
            }
            return false;
        }

        // Return true if we're in debug mode.
        public static bool GetDebug()
        {
            return HasCommandlineArgument("-d");
        }

        // Return true if we were launched on startup.
        public static bool LaunchedOnStartup()
        {
            return HasCommandlineArgument("-s");
        }

        // Return the last Win32 error as a string.
        public static string GetLastWin32ErrorString()
        {
            int error = Marshal.GetLastWin32Error();
            if(error == 0)
                return "";
            return new System.ComponentModel.Win32Exception(error).Message;
        }

        // Work around Enumerable.SequenceEqual not checking if the arrays are null.
        public static bool SequenceEqual<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second)
        {
            if(first == second)
                return true;
            if(first == null || second == null)
                return false;
            return Enumerable.SequenceEqual(first, second);
        }

        public static Color ColorFromFloatRGB(double r, double g, double b)
        {
            byte R = (byte) Math.Max(0, Math.Min(255, r * 255));
            byte G = (byte) Math.Max(0, Math.Min(255, g * 255));
            byte B = (byte) Math.Max(0, Math.Min(255, b * 255));
            return Color.FromRgb(R, G, B);
        }

        // Return a Color as an HTML color code.
        public static string ColorToString(Color color)
        {
            // WPF's Color.ToString() returns #AARRGGBB, which is just wrong.  Alpha is always
            // last in HTML color codes.  We don't need alpha, so just strip it off.
            return "#" + color.ToString().Substring(3);
        }

        // Parse #RRGGBB and return a Color, or white if the string isn't in the correct format.
        public static Color ParseColorString(string s)
        {
            // We only expect "#RRGGBB".
            if(s.Length != 7 || !s.StartsWith("#"))
                return Color.FromRgb(255,255,255);

            try {
                return (Color) ColorConverter.ConvertFromString(s);
            }
            catch(System.FormatException)
            {
                return Color.FromRgb(255,255,255);
            }
        }

        // Light values are actually in the range 0-170 and not 0-255, since higher values aren't
        // any brighter and just draw more power.  The auto-lighting colors that we're configuring
        // need to be scaled to this range too, but show full range colors in the UI.
        readonly static double LightsScaleFactor = 0.666666f;
        static public Byte ScaleColor(Byte c)
        {
            return (Byte) Math.Round(c * LightsScaleFactor);
        }
        static public Byte UnscaleColor(Byte c)
        {
            Byte result = (Byte) Math.Round(Math.Min(255, c / LightsScaleFactor));

            // The color values we output are quantized, since we're scaling an 8-bit value.
            // This doesn't have any real effect, but it causes #FFFFFF in the settings export
            // file to be written out as #FDFDFD (which has the same value in hardware).  Just
            // so the common value of white is clean, snap these values to 0xFF.  The end result
            // will be the same.
            if(result >= 0xFD)
                return 0xFF;
            return result;
        }

        static public Color ScaleColor(Color c)
        {
            return Color.FromRgb(ScaleColor(c.R), ScaleColor(c.G), ScaleColor(c.B));
        }

        static public Color UnscaleColor(Color c)
        {
            return Color.FromRgb(UnscaleColor(c.R), UnscaleColor(c.G), UnscaleColor(c.B));
        }

        public static Color FromHSV(double H, double S, double V)
        {
            H = H % 360;
            S = Math.Max(0, Math.Min(1, S));
            V = Math.Max(0, Math.Min(1, V));
            if(H < 0)
                H += 360;
            H /= 60;
 
            if( S < 0.0001f )
                    return ColorFromFloatRGB(V, V, V);
 
            double C = V * S;
             double X = C * (1 - Math.Abs((H % 2) - 1));

            Color ret;
            switch( (int) Math.Round(Math.Floor(H)) )
            {
            case 0:  ret = ColorFromFloatRGB(C, X, 0); break;
            case 1:  ret = ColorFromFloatRGB(X, C, 0); break;
            case 2:  ret = ColorFromFloatRGB(0, C, X); break;
            case 3:  ret = ColorFromFloatRGB(0, X, C); break;
            case 4:  ret = ColorFromFloatRGB(X, 0, C); break;
            default: ret = ColorFromFloatRGB(C, 0, X); break;
            }

            ret -= ColorFromFloatRGB(C-V, C-V, C-V);
            return ret;
        }
        
        public static void ToHSV(Color c, out double h, out double s, out double v)
        {
            h = s = v = 0;
            if( c.R == 0 && c.G == 0 && c.B == 0 )
                return;

            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;

            double m = Math.Min(Math.Min(r, g), b);
            double M = Math.Max(Math.Max(r, g), b);
            double C = M - m;
            if( Math.Abs(r-g) < 0.0001f && Math.Abs(g-b) < 0.0001f ) // grey
                    h = 0;
            else if( Math.Abs(r-M) < 0.0001f ) // M == R
                    h = ((g - b)/C) % 6;
            else if( Math.Abs(g-M) < 0.0001f ) // M == G
                    h = (b - r)/C + 2;
            else // M == B
                    h = (r - g)/C + 4;

            h *= 60;
            if( h < 0 )
                    h += 360;
 
            s = C / M;
            v = M;
        }

        // Return our settings directory, creating it if it doesn't exist.
        public static string GetSettingsDirectory()
        {
            string result = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/StepManiaX/";
            System.IO.Directory.CreateDirectory(result);
            return result;
        }

        public static byte[] ReadFileFromSettings(string filename)
        {
            string outputFilename = GetSettingsDirectory() + filename;
            try {
                return System.IO.File.ReadAllBytes(outputFilename);
            } catch {
                // If the file doesn't exist or can't be read for some other reason, just
                // return null.
                return null;
            }
        }

        public static void SaveFileToSettings(string filename, byte[] data)
        {
            string outputFilename = GetSettingsDirectory() + filename;
            string directory = System.IO.Path.GetDirectoryName(outputFilename);
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllBytes(outputFilename, data);
        }

        // Read path.  If an error is encountered, return "".
        public static string ReadFile(string path)
        {
            try {
                return System.IO.File.ReadAllText(path);
            }
            catch(System.IO.IOException)
            {
                return "";
            }
        }

        // Read path.  If an error is encountered, return null.
        public static byte[] ReadBinaryFile(string path)
        {
            try {
                return System.IO.File.ReadAllBytes(path);
            }
            catch(System.IO.IOException)
            {
                return null;
            }
        }

        public static Dictionary<SMX.SMX.LightsType, string> LightsTypeNames = new Dictionary<SMX.SMX.LightsType, string>()
        {
            { SMX.SMX.LightsType.LightsType_Pressed, "pressed" },
            { SMX.SMX.LightsType.LightsType_Released, "released" },
        };

        // Load any saved animations from disk.  If useDefault is true, load the default
        // animation even if there's a user animation saved.
        public static void LoadSavedPanelAnimations(bool useDefault=false)
        {
            for(int pad = 0; pad < 2; ++pad)
            {
                foreach(var it in LightsTypeNames)
                    LoadSavedAnimationType(pad, it.Key, useDefault);
            }
        }

        public static void SaveAnimationToDisk(int pad, SMX.SMX.LightsType type, byte[] data)
        {
            string filename = LightsTypeNames[type] + ".gif";
            string path = "Animations/Pad" + (pad+1) + "/" + filename;
            Helpers.SaveFileToSettings(path, data);
        }

        // Read a saved PanelAnimation.
        //
        // Data will always be returned.  If the user hasn't saved anything, we'll return
        // our default animation.
        public static byte[] ReadSavedAnimationType(int pad, SMX.SMX.LightsType type, bool useDefault=false)
        {
            string filename = LightsTypeNames[type] + ".gif";
            string path = "Animations/Pad" + (pad+1) + "/" + filename;
            byte[] gif = Helpers.ReadFileFromSettings(path);
            if(gif == null || useDefault)
            {
                // If the user has never loaded a file, load our default.
                Uri url = new Uri("pack://application:,,,/Resources/" + filename);
                StreamResourceInfo info = Application.GetResourceStream(url);
                gif = new byte[info.Stream.Length];
                info.Stream.Read(gif, 0, gif.Length);
            }
            return gif;
        }

        // Load a PanelAnimation from disk.
        public static void LoadSavedAnimationType(int pad, SMX.SMX.LightsType type, bool useDefault=false)
        {
            byte[] gif = ReadSavedAnimationType(pad, type, useDefault);
            string error;
            SMX.SMX.LightsAnimation_Load(gif, pad, type, out error);
        }

        // Prepare all loaded animations for upload.
        //
        // We do this early, as soon as animations are loaded, so we know whether there are
        // any errors preventing upload to display in the UI.
        public static void PrepareLoadedAnimations()
        {
            PanelLoadErrors = null;
            // Prepare animations for both pads.
            for(int pad = 0; pad < 2; ++pad)
            {
                // Store the first error we get.  Keep loading pad 1 even if pad 0 has
                // an error.
                string error;
                if(!SMX.SMX.LightsUpload_PrepareUpload(pad, out error) && PanelLoadErrors == null)
                    PanelLoadErrors = error;
            }
        }

        // If there was an error preparing animations, we store it here for display.  Otherwise,
        // this is null.
        public static string PanelLoadErrors = null;

        // Create a .lnk.
        public static void CreateShortcut(string outputFile, string targetPath, string arguments)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(outputFile);

            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.WindowStyle = 0;
            shortcut.Save();
        }
    }

    // This class just makes it easier to assemble binary command packets.
    public class CommandBuffer
    {
        public void Write(string s)
        {
            char[] buf = s.ToCharArray();
            byte[] data = new byte[buf.Length];
            for(int i = 0; i < buf.Length; ++i)
                data[i] = (byte) buf[i];
            Write(data);
        }
        public void Write(byte[] s) { parts.AddLast(s); }
        public void Write(byte b) { Write(new byte[] { b }); }
        public void Write(char b) { Write((byte) b); }

        public byte[] Get()
        {
            int length = 0;
            foreach(byte[] part in parts)
                length += part.Length;

            byte[] result = new byte[length];
            int next = 0;
            foreach(byte[] part in parts)
            {
                Buffer.BlockCopy(part, 0, result, next, part.Length);
                next += part.Length;
            }
            return result;
        }

        private LinkedList<byte[]> parts = new LinkedList<byte[]>();
    };

    // Manage launching on startup.
    static class LaunchOnStartup
    {
        public static string GetLaunchShortcutFilename()
        {
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return startupFolder + "/StepManiaX.lnk";
        }

        // Enable or disable launching on startup.
        public static bool Enable
        {
            get {
                return Properties.Settings.Default.LaunchOnStartup;
            }

            set {
                // Remember whether we want to be launched on startup.  This is used as a sanity
                // check in case we're not able to remove our launch shortcut.
                Properties.Settings.Default.LaunchOnStartup = value;
                Properties.Settings.Default.Save();

                string shortcutFilename = GetLaunchShortcutFilename();
                if(value)
                {
                    string filename = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    Helpers.CreateShortcut(shortcutFilename, filename, "-s");
                } else {

                    try {
                        System.IO.File.Delete(shortcutFilename);
                    } catch {
                        // If there's an error deleting the shortcut (most likely it doesn't exist),
                        // don't do anything.
                    }
                }
            }
        }
    };

    // When enabled, periodically set all lights to the current auto-lighting color.  This
    // is enabled while manipulating the step color slider.
    class ShowAutoLightsColor
    {
        private DispatcherTimer LightsTimer;

        public ShowAutoLightsColor()
        {
            LightsTimer = new DispatcherTimer();

            // Run at 30fps.
            LightsTimer.Interval = new TimeSpan(0,0,0,0, 1000 / 33);

            LightsTimer.Tick += delegate(object sender, EventArgs e)
            {
                if(!LightsTimer.IsEnabled)
                    return;

                AutoLightsColorRefreshColor();
            };
        }

        public void Start()
        {
            // To show the current color, send a lights command periodically.  If we stop sending
            // this for a while the controller will return to auto-lights, which we won't want to
            // happen until AutoLightsColorEnd is called.
            if(LightsTimer.IsEnabled)
                return;

            // We normally leave lights animation control enabled while this application is
            // running.  Turn it off temporarily while we're showing the lights sample, or the
            // two will fight.
            SMX.SMX.LightsAnimation_SetAuto(false);

            // Don't wait for an interval to send the first update.
            //AutoLightsColorRefreshColor();

            LightsTimer.Start();
        }

        public void Stop()
        {
            LightsTimer.Stop();

            // Turn lighting control back on.
            SMX.SMX.LightsAnimation_SetAuto(true);
        }

        private void AutoLightsColorRefreshColor()
        {
            CommandBuffer cmd = new CommandBuffer();

            for(int pad = 0; pad < 2; ++pad)
            {
                // Use this panel's color.  If a panel isn't connected, we still need to run the
                // loop below to insert data for the panel.
                byte[] color = new byte[9*3];
                SMX.SMXConfig config;
                if(SMX.SMX.GetConfig(pad, out config))
                    color = config.stepColor;
                for( int iPanel = 0; iPanel < 9; ++iPanel )
                {
                    for( int i = 0; i < 25; ++i )
                    {
                        // Auto-lights colors in the config packet are scaled so the firmware
                        // doesn't have to do it, but here we're setting the panel color to
                        // the auto-light color directly to preview the color.  SetLights
                        // will apply the scaling, so we need to remove it.
                        cmd.Write( Helpers.UnscaleColor(color[iPanel*3+0]) );
                        cmd.Write( Helpers.UnscaleColor(color[iPanel*3+1]) );
                        cmd.Write( Helpers.UnscaleColor(color[iPanel*3+2]) );
                    }
                }
            }

            SMX.SMX.SetLights2(cmd.Get());
        }
    };

    static class SMXHelpers
    {
        // Export configurable values in SMXConfig to a JSON string.
        public static string ExportSettingsToJSON(SMX.SMXConfig config)
        {
            Dictionary<string, Object> dict = new Dictionary<string, Object>();
            List<int> panelLowThresholds = new List<int>();
            panelLowThresholds.Add(config.panelThreshold0Low);
            panelLowThresholds.Add(config.panelThreshold1Low);
            panelLowThresholds.Add(config.panelThreshold2Low);
            panelLowThresholds.Add(config.panelThreshold3Low);
            panelLowThresholds.Add(config.panelThreshold4Low);
            panelLowThresholds.Add(config.panelThreshold5Low);
            panelLowThresholds.Add(config.panelThreshold6Low);
            panelLowThresholds.Add(config.panelThreshold7Low);
            panelLowThresholds.Add(config.panelThreshold8Low);
            dict.Add("panelLowThresholds", panelLowThresholds);

            List<int> panelHighThresholds = new List<int>();
            panelHighThresholds.Add(config.panelThreshold0High);
            panelHighThresholds.Add(config.panelThreshold1High);
            panelHighThresholds.Add(config.panelThreshold2High);
            panelHighThresholds.Add(config.panelThreshold3High);
            panelHighThresholds.Add(config.panelThreshold4High);
            panelHighThresholds.Add(config.panelThreshold5High);
            panelHighThresholds.Add(config.panelThreshold6High);
            panelHighThresholds.Add(config.panelThreshold7High);
            panelHighThresholds.Add(config.panelThreshold8High);
            dict.Add("panelHighThresholds", panelHighThresholds);

            // Store the enabled panel mask as a simple list of which panels are selected.
            bool[] enabledPanels = config.GetEnabledPanels();
            List<int> enabledPanelList = new List<int>();
            for(int panel = 0; panel < 9; ++panel)
            {
                if(enabledPanels[panel])
                    enabledPanelList.Add(panel);
            }
            dict.Add("enabledPanels", enabledPanelList);

            // Store panel colors.
            List<string> panelColors = new List<string>();
            for(int PanelIndex = 0; PanelIndex < 9; ++PanelIndex)
            {
                // Scale colors from the hardware value back to the 0-255 value we use in the UI.
                Color color = Color.FromRgb(config.stepColor[PanelIndex*3+0], config.stepColor[PanelIndex*3+1], config.stepColor[PanelIndex*3+2]);
                color = Helpers.UnscaleColor(color);
                panelColors.Add(Helpers.ColorToString(color));
            }
            dict.Add("panelColors", panelColors);

            return SMXJSON.SerializeJSON.Serialize(dict);
        }

        // Import a saved JSON configuration to an SMXConfig.
        public static void ImportSettingsFromJSON(string json, ref SMX.SMXConfig config)
        {
            Dictionary<string, Object> dict = SMXJSON.ParseJSON.Parse<Dictionary<string, Object>>(json);

            // Read the thresholds.  If any values are missing, we'll leave the value in config alone.
            List<Object> newPanelLowThresholds = dict.Get("panelLowThresholds", new List<Object>());
            config.panelThreshold0Low = newPanelLowThresholds.Get(0, config.panelThreshold0Low);
            config.panelThreshold1Low = newPanelLowThresholds.Get(1, config.panelThreshold1Low);
            config.panelThreshold2Low = newPanelLowThresholds.Get(2, config.panelThreshold2Low);
            config.panelThreshold3Low = newPanelLowThresholds.Get(3, config.panelThreshold3Low);
            config.panelThreshold4Low = newPanelLowThresholds.Get(4, config.panelThreshold4Low);
            config.panelThreshold5Low = newPanelLowThresholds.Get(5, config.panelThreshold5Low);
            config.panelThreshold6Low = newPanelLowThresholds.Get(6, config.panelThreshold6Low);
            config.panelThreshold7Low = newPanelLowThresholds.Get(7, config.panelThreshold7Low);
            config.panelThreshold8Low = newPanelLowThresholds.Get(8, config.panelThreshold8Low);

            List<Object> newPanelHighThresholds = dict.Get("panelHighThresholds", new List<Object>());
            config.panelThreshold0High = newPanelHighThresholds.Get(0, config.panelThreshold0High);
            config.panelThreshold1High = newPanelHighThresholds.Get(1, config.panelThreshold1High);
            config.panelThreshold2High = newPanelHighThresholds.Get(2, config.panelThreshold2High);
            config.panelThreshold3High = newPanelHighThresholds.Get(3, config.panelThreshold3High);
            config.panelThreshold4High = newPanelHighThresholds.Get(4, config.panelThreshold4High);
            config.panelThreshold5High = newPanelHighThresholds.Get(5, config.panelThreshold5High);
            config.panelThreshold6High = newPanelHighThresholds.Get(6, config.panelThreshold6High);
            config.panelThreshold7High = newPanelHighThresholds.Get(7, config.panelThreshold7High);
            config.panelThreshold8High = newPanelHighThresholds.Get(8, config.panelThreshold8High);

            List<Object> enabledPanelList = dict.Get<List<Object>>("enabledPanels", null);
            if(enabledPanelList != null)
            {
                bool[] enabledPanels = new bool[9];
                for(int i = 0; i < enabledPanelList.Count; ++i)
                {
                    int panel = enabledPanelList.Get(i, 0);

                    // Sanity check:
                    if(panel < 0 || panel >= 9)
                        continue;
                    enabledPanels[panel] = true;
                }
                config.SetEnabledPanels(enabledPanels);
            }

            List<Object> panelColors = dict.Get<List<Object>>("panelColors", null);
            if(panelColors != null)
            {
                for(int PanelIndex = 0; PanelIndex < 9 && PanelIndex < panelColors.Count; ++PanelIndex)
                {
                    string colorString = panelColors.Get(PanelIndex, "#FFFFFF");
                    Color color = Helpers.ParseColorString(colorString);
                    color = Helpers.ScaleColor(color);

                    config.stepColor[PanelIndex*3+0] = color.R;
                    config.stepColor[PanelIndex*3+1] = color.G;
                    config.stepColor[PanelIndex*3+2] = color.B;
                }
            }
        }
    };
}
