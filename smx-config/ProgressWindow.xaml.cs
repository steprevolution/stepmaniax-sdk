using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace smx_config
{
    public partial class ProgressWindow: Window
    {
        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public ProgressWindow()
        {
            InitializeComponent();

            // Hide the window close button, since we can't easily cancel.
            Loaded += delegate(object sender, RoutedEventArgs e) {
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
            };
        }

        public void SetTotal(int total)
        {
            ProgressBar.Maximum = total;
        }

        public void SetProgress(int progress)
        {
            ProgressBar.Value = progress;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
        }
    }
} 
