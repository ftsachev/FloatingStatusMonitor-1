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
        public int WindowWidth { get; set; } = 330;
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
        private Label? statusLabel;
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
                // Start hidden in tray after form handle is created
                windowVisible = false;
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "Floating Status Monitor", "Running in system tray — left-click to show window", ToolTipIcon.Info);
            };

            config = LoadConfigFromDisk();

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Magenta;
            this.MouseDown += Form1_MouseDown;
            this.KeyDown += Form1_KeyDown;

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

            watcher = new FileSystemWatcher(".", ConfigPath)
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
            statusLabel = new Label
            {
                AutoSize = false,
                Size = new Size(320, 24),
                Location = new Point(5, 0),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            statusLabel.MouseDown += Form1_MouseDown;
            this.Controls.Add(statusLabel);
        }

        private void Form1_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
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
            this.Size = new Size(config.WindowWidth, config.WindowHeight);
            this.Location = new Point(config.WindowX, config.WindowY);
            this.BackColor = ColorTranslator.FromHtml(config.BackColor);
            
            if (statusLabel != null) statusLabel.Font = font;
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

        private Color GetBackgroundForRamOrTemp(float value)
        {
            if (value >= 99) return ColorTranslator.FromHtml(config.RamTempBgColor99);
            if (value >= 98) return ColorTranslator.FromHtml(config.RamTempBgColor98);
            return Color.Transparent;
        }

        private void UpdateStats(object? sender, EventArgs e)
        {
            // Read stats on background thread so UI stays responsive
            Task.Run(() =>
            {
                float cpu = GetCpuUsage();
                float ram = GetRamViaGlobalMemoryStatusEx();
                float disk = GetDiskActivity();
                
                this.Invoke((Action)(() => UpdateUI(cpu, ram, disk)));
            });
        }

        private void UpdateUI(float cpu, float ram, float disk)
        {
            string statusText = $"CPU {cpu,5:F1}%  | RAM {ram,5:F1}%  | DSK {disk,5:F1}%";

            Color bgColor = ColorTranslator.FromHtml(config.BackColor);
            Color textColor = ColorTranslator.FromHtml(config.TextColor);

            if (cpu >= 99)
            {
                bgColor = ColorTranslator.FromHtml(config.CpuCriticalBgColor);
                textColor = ColorTranslator.FromHtml(config.CpuCriticalTextColor);
            }
            else if (cpu > 95)
            {
                bgColor = ColorTranslator.FromHtml(config.CpuHighBgColor);
                textColor = ColorTranslator.FromHtml(config.CpuHighTextColor);
            }

            if (statusLabel != null)
            {
                statusLabel.Text = statusText;
                statusLabel.ForeColor = textColor;
            }

            this.BackColor = bgColor;
            trayIcon.Text = $"CPU {cpu:F1}% | RAM {ram:F1}% | DSK {disk:F1}%";
        }

        private void ToggleWindow()
        {
            windowVisible = !windowVisible;
            if (windowVisible)
            {
                // Clamp window to screen bounds
                var screen = Screen.GetWorkingArea(this);
                this.Location = new Point(
                    Math.Max(0, Math.Min(this.Location.X, screen.Width - this.Width)),
                    Math.Max(0, Math.Min(this.Location.Y, screen.Height - this.Height))
                );

                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = true;
                this.Show();
                this.BringToFront();
                (trayIcon.ContextMenuStrip.Items[0] as ToolStripMenuItem).Text = "Hide Window";
            }
            else
            {
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Hide();
                (trayIcon.ContextMenuStrip.Items[0] as ToolStripMenuItem).Text = "Show Window";
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
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();
            System.Threading.Thread.Sleep(250);
            return cpuCounter.NextValue();
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
                config.WindowX = this.Location.X;
                config.WindowY = this.Location.Y;
                config.WindowWidth = this.Size.Width;
                config.WindowHeight = this.Size.Height;

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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
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
