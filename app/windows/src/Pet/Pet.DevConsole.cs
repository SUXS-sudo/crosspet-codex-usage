using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CrossPet
{
    sealed partial class Pet
    {
        // ---------------- 开发者控制台 ----------------
        void OpenDev()
        {
            if (dev == null) { dev = new DevConsole(this, env); dev.Closed += () => { dev = null; Paused = false; Js("devRelease()"); }; }
            dev.Show();
        }
    }
}
