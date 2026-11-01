using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Timer = System.Windows.Forms.Timer;

namespace FloatingStatusMonitor
{
    public class Config
    {
        public string FontName { get; set; } = "Consolas";
        public float FontSize { get; set; } = 10;
        public string TextColor { get; set; } = "#FFFFFF";
        public string BackColor { get; set; } = "#000000";
        public double Opacity { get; set; } = 0.8;
        public int WindowWidth { get; set; } = 260;
        public int WindowHeight { get; set; } = 24;
        public int WindowX { get; set; } = 690;
        public int WindowY { get; set; } = 4;
        
        // CPU Color Settings
        public string CpuHighBgColor { get; set; } = "#0000FF"; // Blue for >95-99%
        public string CpuCriticalBgColor { get; set; } = "#FF0000"; // Red for >=99%
        public string CpuHighTextColor { get; set; } = "#FFFFFF"; // White
        public string CpuCriticalTextColor { get; set; } = "#FFFFFF"; // White
        
        // RAM/TEMP Color Settings
        public string RamTempColor0 { get; set; } = "#00FF00"; // Green for >=0%
        public string RamTempColor30 { get; set; } = "#00FFFF"; // Cyan for >=30%
        public string RamTempColor50 { get; set; } = "#FFA500"; // Orange for >=50%
        public string RamTempColor70 { get; set; } = "#FFFF00"; // Yellow for >=70%
        public string RamTempColor80 { get; set; } = "#FFFF00"; // Yellow for >=80%
        public string RamTempColor90 { get; set; } = "#FF00FF"; // Magenta for >=90%
        public string RamTempColor98 { get; set; } = "#FFFFFF"; // White for >=98%
        public string RamTempColor99 { get; set; } = "#FFFFFF"; // White for >=99%
        public string RamTempBgColor98 { get; set; } = "#0000FF"; // Blue bg for >=98%
        public string RamTempBgColor99 { get; set; } = "#FF0000"; // Red bg for >=99%
    }

    public class Form1 : Form
    {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        private Timer updateTimer;
        private Config config;
        private FileSystemWatcher watcher;
        private NotifyIcon trayIcon;
        private Label? statsLabel;
        private PerformanceCounter? diskCounter;
        private bool windowVisible;
        private string exeDir = AppContext.BaseDirectory;
        private string ConfigPath;
        // private string path = Path.Combine(exeDir, "config.json");
        // private string ConfigPath = Path.Combine(
        //     // Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty,
        //     // "config.json"
        //     Path.Combine(exeDir, "config.json")
        // );


        public Form1()
        {
            ConfigPath = Path.Combine(exeDir, "config.json");

            this.Load += (s, e) =>
            {
                ApplyConfig();
                // Clamp initial position to visible screen area
                var screen = Screen.GetWorkingArea(this);
                this.Location = new Point(
                    Math.Max(0, Math.Min(this.Location.X, screen.Width - this.Width)),
                    Math.Max(0, Math.Min(this.Location.Y, screen.Height - this.Height))
                );
                // The status bar is the primary UI. Keep it visible while remaining
                // absent from the taskbar; the tray icon can still hide it on demand.
                windowVisible = true;
                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = false;
                this.Show();
                this.BringToFront();
                (trayIcon!.ContextMenuStrip!.Items[0] as ToolStripMenuItem)!.Text = "Hide Window";
            };

            config = LoadConfigFromDisk();

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            this.Padding = new Padding(0);
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Magenta;
            this.MouseDown += Form1_MouseDown;
            this.KeyDown += Form1_KeyDown;
            this.Resize += Form1_Resize;

            CreateLabels();

            Icon trayIconImage = SystemIcons.Information;
            trayIcon = new NotifyIcon
            {
                Icon = trayIconImage,
                Visible = true,
                Text = "Floating Status Monitor"
            };
            var menu = new ContextMenuStrip();
            var toggleItem = new ToolStripMenuItem("Show Window");
            toggleItem.Click += (s, e) => ToggleWindow();
            menu.Items.Add(toggleItem);
            menu.Items.Add("Save Geometry", null, (s, e) => SaveCurrentGeometry());
            menu.Items.Add("Exit", null, (s, e) => 
            {
                SaveCurrentGeometry();
                Application.Exit();
            });
            trayIcon.ContextMenuStrip = menu;
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ToggleWindow();
            };


            updateTimer = new Timer { Interval = 1000 };
            updateTimer.Tick += UpdateStats;
            updateTimer.Start();

            watcher = new FileSystemWatcher(exeDir, Path.GetFileName(ConfigPath))
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            watcher.Changed += (s, e) =>
            {
                Task.Delay(300).ContinueWith(_ =>
                {
                    try
                    {
                        config = LoadConfigFromDisk();
                        ApplyConfig();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Reload config error: " + ex.Message);
                    }
                });
            };
        }

        private void CreateLabels()
        {
            statsLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0),
                Margin = new Padding(0)
            };
            statsLabel.ForeColor = Color.White;
            statsLabel.Text = "Starting...";

            statsLabel.MouseDown += Form1_MouseDown;
            this.Controls.Add(statsLabel);
            ForceWhiteTextColor();
        }

        private void Form1_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void Form1_Resize(object? sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                windowVisible = false;
                this.Hide();
                (trayIcon.ContextMenuStrip!.Items[0] as ToolStripMenuItem)!.Text = "Show Window";
            }
        }

        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Q)
            {
                SaveCurrentGeometry();
                Application.Exit();
            }
            else if (e.KeyCode == Keys.S)
            {
                SaveCurrentGeometry();
            }
        }

        // private Config LoadConfigFromDisk()
        // {
        //     if (File.Exists(ConfigPath))
        //     {
        //         try
        //         {
        //             string json = File.ReadAllText(ConfigPath);
        //             return JsonConvert.DeserializeObject<Config>(json) ?? new Config();
        //         }
        //         catch { }
        //     }
        //     return new Config();
        // }

        private Config LoadConfigFromDisk()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    var jObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json);
                    var node = jObj?["FloatingStatusMonitor"];
                    if (node != null)
                        return node.ToObject<Config>() ?? new Config();
                }
                catch { }
            }
            return new Config();
        }

        private void ApplyConfig()
        {
            if (this.IsHandleCreated)
            {
                this.Invoke((MethodInvoker)(() => ApplyConfigValues()));
            }
            else
            {
                ApplyConfigValues();
            }
        }

        private void ApplyConfigValues()
        {
            var font = new Font(config.FontName, config.FontSize);
            this.Font = font;
            this.Opacity = config.Opacity;
            this.Location = new Point(config.WindowX, config.WindowY);

            // Set initial window size from config
            this.Size = new Size(config.WindowWidth, config.WindowHeight);
            this.BackColor = ColorTranslator.FromHtml(config.BackColor);

            if (statsLabel != null) statsLabel.Font = font;

            // Don't call FitToText() here - text is still empty (called after first UpdateStats)
        }

        private void FitToText()
        {
            if (statsLabel == null) return;
            var textSize = TextRenderer.MeasureText(statsLabel.Text, statsLabel.Font);
            int w = textSize.Width + statsLabel.Padding.Left + 6;
            int h = textSize.Height;
            this.Size = new Size(w, h);
            statsLabel.Size = new Size(w, h);
        }

        private Color GetColorForCpu(float value)
        {
            if (value >= 99) return ColorTranslator.FromHtml(config.CpuCriticalTextColor);
            if (value > 95) return ColorTranslator.FromHtml(config.CpuHighTextColor);
            return ColorTranslator.FromHtml(config.TextColor);
        }

        private Color GetColorForRamOrTemp(float value)
        {
            if (value >= 99) return ColorTranslator.FromHtml(config.RamTempColor99);
            if (value >= 98) return ColorTranslator.FromHtml(config.RamTempColor98);
            if (value >= 90) return ColorTranslator.FromHtml(config.RamTempColor90);
            if (value >= 80) return ColorTranslator.FromHtml(config.RamTempColor80);
            if (value >= 70) return ColorTranslator.FromHtml(config.RamTempColor70);
            if (value >= 50) return ColorTranslator.FromHtml(config.RamTempColor50);
            if (value >= 30) return ColorTranslator.FromHtml(config.RamTempColor30);
            return ColorTranslator.FromHtml(config.RamTempColor0);
        }

        private void UpdateStats(object? sender, EventArgs e)
        {
            // Read stats on background thread so UI stays responsive
            Task.Run(() =>
            {
                try
                {
                    float cpu = GetCpuUsage();
                    float ram = GetRamViaGlobalMemoryStatusEx();
                    float disk = GetDiskActivity();

                    this.Invoke((Action)(() => UpdateUI(cpu, ram, disk)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("UpdateStats error: " + ex.Message);
                }
            });
        }

        private void UpdateUI(float cpu, float ram, float disk)
        {
            if (statsLabel == null) return;

            statsLabel.Text = $"C:{cpu:F0}% R:{ram:F0}% D:{disk:F0}%";
            FitToText();

            // Force white text color to stay constant - override any urgent colors
            statsLabel.ForeColor = Color.White;

            this.BackColor = ColorTranslator.FromHtml(config.BackColor);
            trayIcon.Text = $"CPU {cpu:F1}% | RAM {ram:F1}% | DSK {disk:F1}%";
        }

        private static Color GetMostUrgentColor(params Color[] colors)
        {
            Color result = colors[0];
            foreach (var c in colors)
            {
                if (c.R > result.R || (c.R == result.R && c.G < result.G))
                    result = c;
            }
            return result;
        }

        private void ForceWhiteTextColor()
        {
            if (statsLabel != null)
            {
                statsLabel.ForeColor = Color.White;
            }
        }

        private void ToggleWindow()
        {
            windowVisible = !windowVisible;
            if (windowVisible)
            {
                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = false;
                this.Show();
                this.BringToFront();
                (trayIcon.ContextMenuStrip!.Items[0] as ToolStripMenuItem)!.Text = "Hide Window";
            }
            else
            {
                this.Hide();
                (trayIcon.ContextMenuStrip!.Items[0] as ToolStripMenuItem)!.Text = "Show Window";
            }
        }

        private float GetRamViaGlobalMemoryStatusEx()
        {
            try
            {
                MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    float used = (float)(mem.ullTotalPhys - mem.ullAvailPhys) / mem.ullTotalPhys * 100;
                    return used;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GlobalMemoryStatusEx Error: " + ex.Message);
            }
            return 0;
        }

        private float GetCpuUsage()
        {
            try
            {
                using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();
                System.Threading.Thread.Sleep(250);
                return cpuCounter.NextValue();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetCpuUsage error: " + ex.Message);
                return 0;
            }
        }

        private float GetMemoryUsage()
        {
            using var memCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
            return memCounter.NextValue();
        }

        private float GetDiskActivity()
        {
            try
            {
                if (diskCounter == null)
                {
                    diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
                    diskCounter.NextValue();
                    System.Threading.Thread.Sleep(100);
                }
                float val = diskCounter.NextValue();
                return val > 100 ? 100 : val;
            }
            catch
            {
                return 0;
            }
        }

        // private void SaveCurrentGeometry()
        // {
        //     try
        //     {
        //         config.WindowX = this.Location.X;
        //         config.WindowY = this.Location.Y;
        //         config.WindowWidth = this.Size.Width;
        //         config.WindowHeight = this.Size.Height;
                
        //         string json = JsonConvert.SerializeObject(config, Formatting.Indented);
        //         File.WriteAllText(ConfigPath, json);
                
        //         Debug.WriteLine($"Geometry saved: X={config.WindowX}, Y={config.WindowY}, W={config.WindowWidth}, H={config.WindowHeight}");
        //     }
        //     catch (Exception ex)
        //     {
        //         Debug.WriteLine("Save geometry error: " + ex.Message);
        //     }
        // }

        private void SaveCurrentGeometry()
        {
            try
            {
                // A minimized WinForms window reports sentinel coordinates such as
                // -32000. Preserve the last visible geometry instead of persisting it.
                if (windowVisible && this.WindowState == FormWindowState.Normal)
                {
                    config.WindowX = this.Location.X;
                    config.WindowY = this.Location.Y;
                    config.WindowWidth = this.Size.Width;
                    config.WindowHeight = this.Size.Height;
                }

                JObject jObj;
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    jObj = JsonConvert.DeserializeObject<JObject>(json) ?? new JObject();
                }
                else
                {
                    jObj = new JObject();
                }

                jObj["FloatingStatusMonitor"] = JObject.FromObject(config);

                File.WriteAllText(ConfigPath, jObj.ToString(Formatting.Indented));
                Debug.WriteLine($"Geometry saved: X={config.WindowX}, Y={config.WindowY}, W={config.WindowWidth}, H={config.WindowHeight}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Save geometry error: " + ex.Message);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveCurrentGeometry();
            trayIcon.Dispose();
            base.OnFormClosed(e);
        }

        [STAThread]
        public static void Main()
        {
            using var instanceMutex = new System.Threading.Mutex(
                initiallyOwned: true,
                name: @"Local\FloatingStatusMonitor",
                createdNew: out bool createdNew);

            if (!createdNew)
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
            GC.KeepAlive(instanceMutex);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    }
}
