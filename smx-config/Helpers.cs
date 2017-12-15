using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;

namespace smx_config
{
    static class Helpers
    {
        // Return true if we're in debug mode.
        public static bool GetDebug()
        {
            foreach(string arg in Environment.GetCommandLineArgs())
            {
                if(arg == "-d")
                    return true;
            }
            return false;
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

            // Don't wait for an interval to send the first update.
            //AutoLightsColorRefreshColor();

            LightsTimer.Start();
        }

        public void Stop()
        {
            LightsTimer.Stop();

            // Reenable auto-lights immediately, without waiting for lights to time out.
            SMX.SMX.ReenableAutoLights();
        }

        private void AutoLightsColorRefreshColor()
        {
            byte[] lights = new byte[864];
            CommandBuffer cmd = new CommandBuffer();

            for(int pad = 0; pad < 2; ++pad)
            {
                SMX.SMXConfig config;
                if(!SMX.SMX.GetConfig(pad, out config))
                    continue;

                byte[] color = config.stepColor;
                for( int iPanel = 0; iPanel < 9; ++iPanel )
                {
                    for( int i = 0; i < 16; ++i )
                    {
                        cmd.Write( color[iPanel*3+0] );
                        cmd.Write( color[iPanel*3+1] );
                        cmd.Write( color[iPanel*3+2] );
                    }
                }
            }
            SMX.SMX.SetLights(cmd.Get());
        }
    };
}
