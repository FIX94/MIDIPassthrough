using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TobiasErichsen.teVirtualMIDI;
using CannedBytes.Midi;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Configuration;

namespace MIDIPassthrough
{
    public partial class Form1 : Form
    {
        static bool is64BitProcess = (IntPtr.Size == 8);
        private System.Object inLock = new System.Object();
        private System.Object outLock = new System.Object();
        private Icon small, large;
        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;
        private List<int> inDevs, outDevs;
        private String pString = "MIDI Passthrough";
        private string drvPathNative = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Drivers32";
        private string drvPathWOW64 = "HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows NT\\CurrentVersion\\Drivers32";
        private string midi = "midi";
        private string normalDrv = "wdmaud.drv";
        private string ourDrv = "teVirtualMIDI.dll";
        static private TeVirtualMIDI mahPort;
        private MidiInPort inPort;
        private MidiOutPort outPort;
        private Thread pthrough;
        private bool registered = false;
        private Configuration config = null;
        private bool config_changed = false;
        public Form1()
        {
            InitializeComponent();
            small = GetIcon(ShellIconSize.SmallIcon);
            large = GetIcon(ShellIconSize.LargeIcon);
            //set normal icons
            SendMessage(this.Handle, WM_SETICON, SHGFI_LARGEICON, small.Handle);
            SendMessage(this.Handle, WM_SETICON, SHGFI_SMALLICON, large.Handle);

            //fully hide window but at least load it
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;

            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Exit", trayIcon_Close);

            trayIcon = new NotifyIcon();
            trayIcon.Text = this.Text;
            if (SystemInformation.SmallIconSize.Width == 16 && SystemInformation.SmallIconSize.Height == 16) //get 16x16
                trayIcon.Icon = new Icon(small, SystemInformation.SmallIconSize);
            else //just calculate from base 32x32 icon to (hopefully) look better
                trayIcon.Icon = new Icon(large, SystemInformation.SmallIconSize);

            // Add menu to tray icon and show it.
            trayIcon.ContextMenu = trayMenu;
            trayIcon.Visible = true;
            trayIcon.MouseClick += trayIcon_MouseClick;

            devInBox.DropDownStyle = ComboBoxStyle.DropDownList;
            devOutBox.DropDownStyle = ComboBoxStyle.DropDownList;
            
            inDevs = new List<int>();
            outDevs = new List<int>();
            mahPort = new TeVirtualMIDI(pString);
            inPort = new MidiInPort();
            outPort = new MidiOutPort();
            pthrough = new Thread(new ThreadStart(readInput));
            config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            inPort.Successor = new MyReceiver();

            this.Load += onLoad;
            this.FormClosed += onClosed;
            this.Resize += onResize;
        }

        void devInBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            setInDev(devInBox.SelectedIndex);
        }
        void devOutBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            setOutDev(devOutBox.SelectedIndex);
        }

        public void onLoad(object sender, System.EventArgs e)
        {
            //window loaded, set window back to a normal state
            this.WindowState = FormWindowState.Normal;
            this.Visible = false;
            this.ShowInTaskbar = true;

            pthrough.Start();
            CreateMIDIDevList();
            if (inDevs.Count > 0)
            {
                int def = getInDev();
                if (def < -1 || def > inDevs.Count)
                {
                    def = -1;
                    setInDev(-1);
                }
                else if (def == -1)
                {
                    devInBox.SelectedIndex = inDevs.Count;
                }
                else
                {
                    lock (inLock)
                    {
                        inPort.Open(def);
                        inPort.Start();
                    }
                    devInBox.SelectedIndex = def;
                }
            }
            if (outDevs.Count > 0)
            {
                int def = getOutDev();
                if (def < -1 || def > outDevs.Count)
                {
                    def = -1;
                    setOutDev(-1);
                }
                else if(def == -1)
                {
                    devOutBox.SelectedIndex = outDevs.Count;
                }
                else
                {
                    lock (outLock)
                    {
                        outPort.Open(def);
                    }
                    devOutBox.SelectedIndex = def;
                }
            }
            devInBox.SelectedIndexChanged += devInBox_SelectedIndexChanged;
            devOutBox.SelectedIndexChanged += devOutBox_SelectedIndexChanged;
            SetThreadStatus();
        }
        int getInDev()
        {
            int j;
            if (Int32.TryParse(config.AppSettings.Settings["defMidiIn"].Value, out j))
                return j;
            else
                return 0;
        }
        void setInDev(int val)
        {
            if (val < 0 || val > inDevs.Count)
                val = -1;
            if (getInDev() == val)
                return;
            config.AppSettings.Settings["defMidiIn"].Value = val.ToString();
            config_changed = true;
        }
        int getOutDev()
        {
            int j;
            if (Int32.TryParse(config.AppSettings.Settings["defMidiOut"].Value, out j))
                return j;
            else
                return 0;
        }
        void setOutDev(int val)
        {
            if (val < 0 || val > outDevs.Count)
                val = -1;
            if (getOutDev() == val)
                return;
            config.AppSettings.Settings["defMidiOut"].Value = val.ToString();
            config_changed = true;
        }
        void trayIcon_Close(object sender, System.EventArgs e)
        {
            //just use form close event for everything
            this.Close();
        }

        void trayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            this.Visible = !this.Visible;
            if (this.Visible)
            {
                //set normal icons
                SendMessage(this.Handle, WM_SETICON, SHGFI_LARGEICON, small.Handle);
                SendMessage(this.Handle, WM_SETICON, SHGFI_SMALLICON, large.Handle);
                //put in front
                this.Activate();
                //set status again
                SetThreadStatus();
            }
        }
        private void onResize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
                this.Visible = false;
            }
        }
        void onClosed(object sender, FormClosedEventArgs e)
        {
            mahPort.shutdown();
            pthrough.Join();
            DeregisterOurDevice();
            lock (inLock)
            {
                if (inPort.IsOpen)
                {
                    inPort.Stop();
                    inPort.Close();
                }
            }
            lock(outLock)
            {
                if (outPort.IsOpen)
                    outPort.Close();
            }
            trayIcon.Icon = null;
            trayIcon.Dispose();
            if (config_changed == true)
                config.Save(ConfigurationSaveMode.Full);
        }
        void RegisterOurDevice()
        {
            var midiVal = Registry.GetValue(drvPathNative, midi, null);
            var midiVal2 = is64BitProcess ? Registry.GetValue(drvPathWOW64, midi, null) : null;
            if (midiVal != null && normalDrv.Equals(midiVal) && 
                (is64BitProcess == false || (midiVal2 != null && normalDrv.Equals(midiVal2))))
            {
                Registry.SetValue(drvPathNative, midi, ourDrv);
                if (is64BitProcess)
                    Registry.SetValue(drvPathWOW64, midi, ourDrv);
                registered = true;
            }
            setRegisteredStatus();
        }
        void DeregisterOurDevice()
        {
            var midiVal = Registry.GetValue(drvPathNative, midi, null);
            var midiVal2 = is64BitProcess ? Registry.GetValue(drvPathWOW64, midi, null) : null;
            if (midiVal != null && ourDrv.Equals(midiVal) &&
                (is64BitProcess == false || (midiVal2 != null && ourDrv.Equals(midiVal2))))
            {
                Registry.SetValue(drvPathNative, midi, normalDrv);
                if (is64BitProcess)
                    Registry.SetValue(drvPathWOW64, midi, normalDrv);
            }
        }
        private void SetThreadStatus()
        {  
            if (inPort.IsOpen || outPort.IsOpen)
            {
                label3.Text = "Running";
                label3.ForeColor = Color.FromArgb(0, 128, 0);
                devOutBox.Enabled = false;
                devInBox.Enabled = false;
            }
            else
            {
                label3.Text = "Stopped";
                label3.ForeColor = Color.FromArgb(128, 0, 0);
                devOutBox.Enabled = true;
                devInBox.Enabled = true;
            }
        }
        private void setRegisteredStatus()
        {
            registered = false;
            var midiVal = Registry.GetValue(drvPathNative, midi, null);
            if (midiVal != null && ourDrv.Equals(midiVal))
                registered = true;
            if (registered == true)
            {
                label6.Text = "Registered";
                label6.ForeColor = Color.FromArgb(0, 128, 0);
                PassButton.Enabled = true;
                ResetButton.Enabled = true;
                devOutBox.Enabled = true;
                devInBox.Enabled = true;
            }
            else
            {
                label6.Text = "Not Registered";
                label6.ForeColor = Color.FromArgb(128, 0, 0);
                PassButton.Enabled = false;
                ResetButton.Enabled = false;
                devOutBox.Enabled = false;
                devInBox.Enabled = false;
            }
        }
        private void CreateMIDIDevList()
        {
            DeregisterOurDevice();

            inDevs.Clear();
            devInBox.Items.Clear();
            lock (inLock)
            {
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        inPort.Open(i);
                        if (!pString.Equals(inPort.Capabilities.Name))
                        {
                            inDevs.Add(i); //only add others
                            devInBox.Items.Add(inPort.Capabilities.Name);
                        }
                        inPort.Close();
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }
            devInBox.Items.Add("<NONE>");
            devInBox.SelectedIndex = 0;

            outDevs.Clear();
            devOutBox.Items.Clear();
            lock (outLock)
            {
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        outPort.Open(i);
                        if (!pString.Equals(outPort.Capabilities.Name))
                        {
                            outDevs.Add(i); //only add others
                            devOutBox.Items.Add(outPort.Capabilities.Name);
                        }
                        outPort.Close();
                    }
                    catch (CannedBytes.Midi.MidiOutPortException)
                    {
                        break;
                    }
                }
            }
            devOutBox.Items.Add("<NONE>");
            devOutBox.SelectedIndex = 0;

            RegisterOurDevice();
        }

        private void readInput()
        {
            MidiData midiData = new MidiData();
            try
            {
                while (true)
                {
                    byte[] command = mahPort.getCommand();
                    if (Monitor.TryEnter(outLock))
                    {
                        try
                        {
                            if (outPort.IsOpen)
                            {
                                if (command.Length == 3)
                                {
                                    midiData.Status = command[0];
                                    midiData.Parameter1 = command[1];
                                    midiData.Parameter2 = command[2];
                                    outPort.ShortData(midiData);
                                }
                                else if (command.Length == 2)
                                {
                                    midiData.Status = command[0];
                                    midiData.Parameter1 = command[1];
                                    midiData.Parameter2 = 0; //unused
                                    outPort.ShortData(midiData);
                                }
                            }
                        }
                        finally
                        {
                            Monitor.Exit(outLock);
                        }
                    }
                }
            }
            catch (TeVirtualMIDIException)
            {
                clearOtherMIDIout();
            }
        }
        private static string byteArrayToString(byte[] ba)
        {
            string hex = BitConverter.ToString(ba);
            return hex.Replace("-", ":");
        }
        private void clearOtherMIDIout()
        {
            lock (outLock)
            {
                if (outPort.IsOpen)
                {
                    MidiData midiData = new MidiData();
                    midiData.Parameter2 = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        midiData.Status = (byte)(0xB0 | i);
                        midiData.Parameter1 = 121;
                        outPort.ShortData(midiData);
                        midiData.Parameter1 = 123;
                        outPort.ShortData(midiData);
                    }
                }
            }
         }
        private void PassButton_Click(object sender, EventArgs e)
        {
  
            if (inPort.IsOpen || outPort.IsOpen)
            {
                if (inPort.IsOpen)
                {
                    lock (inLock)
                    {
                        inPort.Stop();
                        inPort.Close();
                    }
                }
                clearOtherMIDIout();
                if (outPort.IsOpen)
                {
                    lock (outLock)
                    {
                        outPort.Close();
                    }
                }
            }
            else
            {
                if (inDevs.Count > 0)
                {
                    if (devInBox.SelectedIndex >= 0 && devInBox.SelectedIndex < inDevs.Count)
                    {
                        lock(inLock)
                        {
                            int devId = inDevs[devInBox.SelectedIndex];
                            try
                            {
                                inPort.Open(devId);
                                inPort.Start();
                                setInDev(devId);
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                }
                if (outDevs.Count > 0)
                {
                    if (devOutBox.SelectedIndex >= 0 && devOutBox.SelectedIndex < outDevs.Count)
                    {
                        lock(outLock)
                        {
                            int devId = outDevs[devOutBox.SelectedIndex];
                            try
                            {
                                outPort.Open(devId);
                                setOutDev(devId);
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                }
            }
            SetThreadStatus();
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            lock (inLock)
            {
                if(inPort.IsOpen)
                {
                    inPort.Stop();
                    inPort.Close();
                }
            }
            clearOtherMIDIout();
            CreateMIDIDevList();
            SetThreadStatus();
        }

        private class MyReceiver : IMidiDataReceiver
        {
            public void ShortData(int data, long timestamp)
            {
                MidiData curData = new MidiData(data);
                byte[] cmd = { curData.Status, curData.Parameter1, curData.Parameter2 };
                try
                {
                    mahPort.sendCommand(cmd);
                }
                catch(Exception)
                {

                }
            }

            public void LongData(MidiBufferStream buffer, long timestamp)
            {
                //not implemented
            }
        }

        //inspired by http://www.brad-smith.info/blog/archives/164
        const int SHGFI_ICON = 0x100;
        const int SHGFI_LARGEICON = 0x0;    // 32x32 pixels
        const int SHGFI_SMALLICON = 0x1;    // 16x16 pixels

        public enum ShellIconSize : int
        {
            SmallIcon = SHGFI_ICON | SHGFI_SMALLICON,
            LargeIcon = SHGFI_ICON | SHGFI_LARGEICON
        }

        public struct SHFILEINFO
        {
            // Handle to the icon representing the file
            public IntPtr hIcon;
            // Index of the icon within the image list
            public int iIcon;
            // Various attributes of the file
            public uint dwAttributes;
            // Path to the file
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szDisplayName;
            // File type
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        };

        [DllImport("shell32.dll")]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbSizeFileInfo,
            uint uFlags
        );

        public static Icon GetIcon(ShellIconSize size)
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            SHGetFileInfo(Application.ExecutablePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), (uint)size);
            return Icon.FromHandle(shinfo.hIcon);
        }

        //http://stackoverflow.com/questions/4048910/setting-a-different-taskbar-icon-to-the-icon-displayed-in-the-titlebar-c
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, IntPtr lParam);

        private const uint WM_SETICON = 0x80u;
    }
}
