using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Orhex
{
    internal struct QueueItem
    {
        public string Name;
        public int DurationSeconds;
        public string Exe;
        public string Manifest;
        public int Minutes { get { return DurationSeconds / 60; } set { DurationSeconds = value * 60; } }
        public QueueItem(string name, int minutes) { Name = name; DurationSeconds = minutes * 60; Exe = null; Manifest = null; }
        public QueueItem(string name, int minutes, string exe) { Name = name; DurationSeconds = minutes * 60; Exe = exe; Manifest = null; }
        public QueueItem(string name, int minutes, string exe, string manifest) { Name = name; DurationSeconds = minutes * 60; Exe = exe; Manifest = manifest; }
        public QueueItem(string name, int durationSeconds, string exe, string manifest, bool isSeconds) { Name = name; DurationSeconds = durationSeconds; Exe = exe; Manifest = manifest; }
        public string Display
        {
            get { return Name + (DurationSeconds > 0 ? "  —  " + Program.FormatTime(DurationSeconds) : "  —  no time limit"); }
        }
        public string EffectiveExe
        {
            get { return string.IsNullOrEmpty(Exe) ? GameDb.ExeForGame(Name) : Exe; }
        }
    }

    internal static class Program
    {
        public const string AppName = "Orhex";
        public const string UrlDonate = "https://hegxib.me/donate";
        public const string UrlSocial = "https://x.hegxib.me";
        public const string UrlSite = "https://hegxib.me";
        public const string UrlGithub = "https://github.com/Hegxib/orhex";

        public static string OwnExe = "";
        public static Icon AppIcon;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            OwnExe = Process.GetCurrentProcess().MainModule.FileName;
            LoadIcon();

            string holdArg = GetArg(args, "hold");
            if (holdArg != null)
            {
                RunHolder(holdArg);
                return;
            }

            DiscordDb.RefreshSilently();

            string queueArg = GetArg(args, "queue");
            if (queueArg != null)
            {
                int index = 0;
                string indexArg = GetArg(args, "index");
                int.TryParse(indexArg, out index);
                List<QueueItem> items = ParseQueue(queueArg);
                if (items.Count == 0) return;
                Application.Run(new MimicForm(items, index));
            }
            else
            {
                Application.Run(new MainForm());
            }
        }

        public static string GetArg(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--" + key)
                    return args[i + 1];
            return null;
        }

        private static void RunHolder(string holdArg)
        {
            Mutex hm = null;
            try
            {
                List<QueueItem> hitems = ParseQueue(holdArg);
                if (hitems.Count == 0) return;
                QueueItem hq = hitems[0];
                bool created;
                hm = new Mutex(true, "Orhex_" + hq.EffectiveExe.ToLowerInvariant(), out created);
                if (!created) return;
                int secs = hq.DurationSeconds;
                if (secs <= 0) Thread.Sleep(Timeout.Infinite);
                else Thread.Sleep(Math.Min(secs, 604800) * 1000);
            }
            catch { }
            finally
            {
                if (hm != null)
                {
                    try { hm.ReleaseMutex(); } catch { }
                    hm.Close();
                }
            }
        }

        private static void LoadIcon()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Orhex.icon.ico"))
                    if (s != null)
                        AppIcon = new Icon(s);
            }
            catch { AppIcon = null; }
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(url); }
            catch { }
        }

        public static List<QueueItem> ParseQueue(string s)
        {
            List<QueueItem> list = new List<QueueItem>();
            if (string.IsNullOrEmpty(s)) return list;
            string[] parts = s.Split(';');
            foreach (string part in parts)
            {
                string[] f = part.Split('|');
                if (f.Length < 2) continue;
                string name = f[0].Trim();
                int duration = 0;
                string ds = f[1].Trim();
                if (ds.Length > 1 && (ds[0] == 's' || ds[0] == 'S'))
                {
                    int.TryParse(ds.Substring(1), out duration);
                }
                else if (ds.Contains(":"))
                {
                    string[] tp = ds.Split(':');
                    int m = 0, sec = 0;
                    if (tp.Length == 2) { int.TryParse(tp[0], out m); int.TryParse(tp[1], out sec); duration = m * 60 + sec; }
                    else if (tp.Length == 3) { int h = 0; int.TryParse(tp[0], out h); int.TryParse(tp[1], out m); int.TryParse(tp[2], out sec); duration = h * 3600 + m * 60 + sec; }
                }
                else
                {
                    int raw = 0;
                    int.TryParse(ds, out raw);
                    if (raw > 0 && raw < 500) duration = raw * 60;
                    else duration = raw;
                }
                if (name.Length == 0) continue;
                string exe = f.Length >= 3 && f[2].Length > 0 ? f[2] : null;
                string manifest = f.Length >= 4 && f[3].Length > 0 ? f[3] : null;
                list.Add(new QueueItem(name, duration, exe, manifest, true));
            }
            return list;
        }

        public static string SerializeQueue(List<QueueItem> items)
        {
            StringBuilder sb = new StringBuilder();
            foreach (QueueItem it in items)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(it.Name.Replace("|", "-").Replace(";", "-"));
                sb.Append('|');
                sb.Append('s');
                sb.Append(it.DurationSeconds);
                sb.Append('|');
                sb.Append(it.Exe ?? "");
                sb.Append('|');
                sb.Append(it.Manifest ?? "");
            }
            return sb.ToString();
        }

        public static string FormatTime(long seconds)
        {
            if (seconds < 0) seconds = 0;
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1)
                return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds);
            return string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
        }
    }

    internal static class MimicEngine
    {
        public static void RelaunchAs(string game, string exe, string queueArg, int index)
        {
            string folder = Path.Combine(Path.GetTempPath(), "Orhex");
            Directory.CreateDirectory(folder);
            LaunchAs(Path.Combine(folder, exe), queueArg, index);
        }

        public static void SpawnAs(string exe, string queueArg, int index)
        {
            string folder = Path.Combine(Path.GetTempPath(), "Orhex");
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, exe);
            string self = Program.OwnExe;
            if (!string.Equals(self, target, StringComparison.OrdinalIgnoreCase))
            {
                bool copied = false;
                for (int i = 0; i < 5 && !copied; i++)
                {
                    try { File.Copy(self, target, true); copied = true; }
                    catch (IOException) { Thread.Sleep(300); }
                }
                if (!copied)
                    throw new IOException("The fake game file is still in use. Close the other window and try again.");
            }
            string args = "--queue \"" + queueArg + "\" --index " + index;
            var psi = new ProcessStartInfo { FileName = target, Arguments = args, UseShellExecute = true };
            Process.Start(psi);
        }

        public static void SpawnHidden(string exe, string queueArg)
        {
            string folder = Path.Combine(Path.GetTempPath(), "Orhex");
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, exe);
            string self = Program.OwnExe;
            if (!string.Equals(self, target, StringComparison.OrdinalIgnoreCase))
            {
                bool copied = false;
                for (int i = 0; i < 5 && !copied; i++)
                {
                    try { File.Copy(self, target, true); copied = true; }
                    catch (IOException) { Thread.Sleep(300); }
                }
                if (!copied) return;
            }
            string args = "--hold \"" + queueArg + "\"";
            var psi = new ProcessStartInfo
            {
                FileName = target,
                Arguments = args,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            try { Process.Start(psi); }
            catch { }
        }

        public static void LaunchNoExit(string target, string queueArg, int index)
        {
            string args = "--queue \"" + queueArg + "\" --index " + index;
            var psi = new ProcessStartInfo { FileName = target, Arguments = args, UseShellExecute = true };
            Process.Start(psi);
        }

        public static bool IsAlreadyRunning(string exe)
        {
            Mutex m = null;
            try
            {
                bool created;
                m = new Mutex(true, "Orhex_" + exe.ToLowerInvariant(), out created);
                if (!created) return true;
                try { m.ReleaseMutex(); }
                catch { }
                return false;
            }
            catch { return false; }
            finally
            {
                if (m != null) m.Close();
            }
        }

        public static void LaunchAs(string target, string queueArg, int index)
        {
            string self = Program.OwnExe;
            if (!string.Equals(self, target, StringComparison.OrdinalIgnoreCase))
            {
                string dir = Path.GetDirectoryName(target);
                if (dir != null) Directory.CreateDirectory(dir);
                bool copied = false;
                for (int i = 0; i < 5 && !copied; i++)
                {
                    try { File.Copy(self, target, true); copied = true; }
                    catch (IOException) { Thread.Sleep(300); }
                }
                if (!copied)
                    throw new IOException("The fake game file is still in use. Close the other window and try again.");
            }

            string args = "--queue \"" + queueArg + "\" --index " + index;
            var psi = new ProcessStartInfo { FileName = target, Arguments = args, UseShellExecute = true };
            Process.Start(psi);
            Thread.Sleep(600);
            Environment.Exit(0);
        }
    }

    internal static class GameDb
    {
        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "elden ring", "eldenring.exe" },
            { "elden ring nightreign", "Nightreign.exe" },
            { "cyberpunk 2077", "Cyberpunk2077.exe" },
            { "grand theft auto v", "GTA5.exe" },
            { "gta v", "GTA5.exe" },
            { "gta 5", "GTA5.exe" },
            { "gta vi", "GTA6.exe" },
            { "red dead redemption 2", "RDR2.exe" },
            { "rdr2", "RDR2.exe" },
            { "counter-strike 2", "cs2.exe" },
            { "cs2", "cs2.exe" },
            { "csgo", "csgo.exe" },
            { "fortnite", "FortniteClient-Win64-Shipping.exe" },
            { "apex legends", "r5apex.exe" },
            { "overwatch 2", "Overwatch.exe" },
            { "valorant", "valorant-win64-shipping.exe" },
            { "dota 2", "dota2.exe" },
            { "league of legends", "lol.exe" },
            { "lol", "lol.exe" },
            { "rocket league", "RocketLeague.exe" },
            { "destiny 2", "destiny2.exe" },
            { "the witcher 3", "witcher3.exe" },
            { "witcher 3", "witcher3.exe" },
            { "baldur's gate 3", "bg3.exe" },
            { "bg3", "bg3.exe" },
            { "starfield", "Starfield.exe" },
            { "hogwarts legacy", "HogwartsLegacy.exe" },
            { "diablo iv", "Diablo IV.exe" },
            { "diablo 4", "Diablo IV.exe" },
            { "sekiro", "sekiro.exe" },
            { "dark souls iii", "DarkSoulsIII.exe" },
            { "dark souls 3", "DarkSoulsIII.exe" },
            { "helldivers 2", "helldivers2.exe" },
            { "doom eternal", "DOOMEternal.exe" },
            { "ghost of tsushima", "GhostOfTsushima.exe" },
            { "god of war", "GoW.exe" },
            { "monster hunter wilds", "MonsterHunterWilds.exe" },
            { "assassin's creed shadows", "ACShadows.exe" },
            { "borderlands 4", "Borderlands4.exe" },
            { "roblox", "RobloxPlayerBeta.exe" }
        };

        public static string TryGetExe(string game)
        {
            string key = game.Trim();
            if (Map.ContainsKey(key))
                return Map[key];
            return null;
        }

        public static string ExeForGame(string game)
        {
            string known = TryGetExe(game);
            if (known != null)
                return known;
            StringBuilder slug = new StringBuilder();
            foreach (char c in game.Trim())
                if (char.IsLetterOrDigit(c))
                    slug.Append(c);
            if (slug.Length == 0)
                slug.Append("game");
            return slug.ToString() + ".exe";
        }
    }

    internal static class MenuBuilder
    {
        public static MenuStrip Build(Form owner, Action stopAction)
        {
            MenuStrip strip = new MenuStrip();
            ToolStripMenuItem file = new ToolStripMenuItem("&File");
            ToolStripMenuItem exit = new ToolStripMenuItem("E&xit");
            exit.Click += delegate { owner.Close(); };
            file.DropDownItems.Add(exit);

            ToolStripMenuItem help = new ToolStripMenuItem("&Help");
            ToolStripMenuItem donate = new ToolStripMenuItem("&Donate");
            donate.Click += delegate { Program.OpenUrl(Program.UrlDonate); };
            ToolStripMenuItem social = new ToolStripMenuItem("&Socials");
            social.Click += delegate { Program.OpenUrl(Program.UrlSocial); };
            ToolStripMenuItem sep = new ToolStripMenuItem("-");
            ToolStripMenuItem aboutHelp = new ToolStripMenuItem("About " + Program.AppName);
            aboutHelp.Click += delegate { ShowAbout(); };
            help.DropDownItems.Add(donate);
            help.DropDownItems.Add(social);
            help.DropDownItems.Add(sep);
            help.DropDownItems.Add(aboutHelp);

            ToolStripMenuItem aboutTop = new ToolStripMenuItem("About");
            aboutTop.Click += delegate { ShowAbout(); };
            ToolStripMenuItem siteTop = new ToolStripMenuItem("hegxib.me");
            siteTop.Click += delegate { Program.OpenUrl(Program.UrlSite); };
            ToolStripMenuItem githubTop = new ToolStripMenuItem("GitHub");
            githubTop.Click += delegate { Program.OpenUrl(Program.UrlGithub); };
            ToolStripMenuItem donateTop = new ToolStripMenuItem("Donate ♥");
            donateTop.ForeColor = Color.FromArgb(220, 80, 120);
            donateTop.Click += delegate { Program.OpenUrl(Program.UrlDonate); };

            strip.Items.Add(file);
            strip.Items.Add(help);
            strip.Items.Add(aboutTop);
            strip.Items.Add(siteTop);
            strip.Items.Add(githubTop);
            strip.Items.Add(donateTop);
            return strip;
        }

        internal static void ShowAbout()
        {
            MessageBox.Show(
                Program.AppName + "  v" + Assembly.GetExecutingAssembly().GetName().Version.ToString() + "\r\n" +
                "Spoof your Discord presence in seconds.\r\n\r\n" +
                "How it works:\r\n" +
                "• Search resolves the real executable name for a game\r\n" +
                "• Rich Presence is pushed over Discord's local IPC pipe (discord-ipc-0…9)\r\n" +
                "• No game files are modified\r\n\r\n" +
                "Connections:\r\n" +
                "• discord.com/api/v10/applications/detectable — detectable games list\r\n" +
                "• discord.com/api/v10/games/detectable/exclusions — exes Discord ignores\r\n" +
                "• gist.githubusercontent.com/.../gameslist.json — backup mirror\r\n" +
                "• store.steampowered.com/api/storesearch — Steam search (fallback)\r\n" +
                "• \\\\.\\pipe\\discord-ipc-* — local Rich Presence only\r\n",
                "About " + Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static NotifyIcon CreateTray(Form owner, string tooltip)
        {
            NotifyIcon ni = new NotifyIcon();
            if (Program.AppIcon != null) ni.Icon = Program.AppIcon;
            ni.Text = tooltip;
            ni.Visible = true;
            ni.DoubleClick += delegate
            {
                owner.Show();
                if (owner.WindowState == FormWindowState.Minimized)
                    owner.WindowState = FormWindowState.Normal;
                owner.Activate();
            };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open " + Program.AppName, null, delegate
            {
                owner.Show();
                if (owner.WindowState == FormWindowState.Minimized)
                    owner.WindowState = FormWindowState.Normal;
                owner.Activate();
            });
            menu.Items.Add("Donate", null, delegate { Program.OpenUrl(Program.UrlDonate); });
            menu.Items.Add("Socials", null, delegate { Program.OpenUrl(Program.UrlSocial); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { owner.Close(); });
            ni.ContextMenuStrip = menu;
            return ni;
        }
    }

    internal class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin { get { return Color.FromArgb(14, 14, 16); } }
        public override Color MenuStripGradientEnd { get { return Color.FromArgb(14, 14, 16); } }
        public override Color MenuBorder { get { return Color.FromArgb(63, 63, 70); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(63, 63, 70); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(63, 63, 70); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(63, 63, 70); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(63, 63, 70); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 24, 27); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(24, 24, 27); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(24, 24, 27); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(24, 24, 27); } }
        public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(63, 63, 70); } }
        public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(63, 63, 70); } }
    }

    internal class ModernCardPanel : Panel
    {
        public ModernCardPanel() { DoubleBuffered = true; BackColor = Color.FromArgb(24, 24, 27); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int r = 10;
                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                path.AddArc(rect.X, rect.Y, r, r, 180, 90);
                path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
                path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
                path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
                path.CloseFigure();
                using (var pen = new Pen(Color.FromArgb(35, 42, 52))) e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal class MainForm : Form
    {
        private readonly TextBox nameBox = new TextBox();
        private readonly TextBox minutesBox = new TextBox();
        private readonly TextBox secondsBox = new TextBox();
        private readonly TextBox exeBox = new TextBox();
        private readonly Label sourceLabel = new Label();
        private readonly ListBox queueList = new ListBox();
        private readonly Label status = new Label();
        private readonly List<QueueItem> items = new List<QueueItem>();
        private NotifyIcon tray;
        private string lastSearchedText = null;

        public MainForm()
        {
            Text = "Orhex - Game Mimic by Hegxib";
            ClientSize = new Size(540, 560);
            MinimumSize = new Size(540, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            if (Program.AppIcon != null) Icon = Program.AppIcon;

            MenuStrip menu = MenuBuilder.Build(this, null);
            Controls.Add(menu);
            MainMenuStrip = menu;

            int y = 34;
            Label title = new Label
            {
                Text = Program.AppName,
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, y),
                BackColor = Color.White
            };
            y += 30;
            Label sub = new Label
            {
                Text = "Spoof your Discord presence in seconds.  Enter = search, Enter again = add. Custom names OK.",
                AutoSize = true,
                Location = new Point(20, y),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = Color.FromArgb(90, 90, 90),
                BackColor = Color.White
            };
            y += 28;

            // — Game row —
            Label nl = new Label { Text = "Game", AutoSize = true, Location = new Point(20, y + 3), Font = new Font("Segoe UI", 9, FontStyle.Bold), BackColor = Color.White };
            nameBox.Location = new Point(70, y);
            nameBox.Width = 210;
            nameBox.Font = new Font("Segoe UI", 10);
            nameBox.TextChanged += delegate { sourceLabel.Text = ""; status.Text = ""; };
            Label timeLabel = new Label { Text = "Time", AutoSize = true, Location = new Point(292, y + 3), Font = new Font("Segoe UI", 9), BackColor = Color.White };
            minutesBox.Location = new Point(330, y);
            minutesBox.Width = 36;
            minutesBox.Font = new Font("Segoe UI", 10);
            minutesBox.TextAlign = HorizontalAlignment.Center;
            minutesBox.Text = "15";
            Label mLabel = new Label { Text = "m", AutoSize = true, Location = new Point(368, y + 4), ForeColor = Color.Gray, BackColor = Color.White };
            secondsBox.Location = new Point(380, y);
            secondsBox.Width = 36;
            secondsBox.Font = new Font("Segoe UI", 10);
            secondsBox.TextAlign = HorizontalAlignment.Center;
            secondsBox.Text = "40";
            Label sLabel = new Label { Text = "s", AutoSize = true, Location = new Point(418, y + 4), ForeColor = Color.Gray, BackColor = Color.White };
            Button searchBtn = new Button { Text = "Search", Location = new Point(438, y - 1), Size = new Size(82, 26), FlatStyle = FlatStyle.System };
            searchBtn.Click += delegate { Search(); };
            y += 34;

            // — Executable row —
            Label el = new Label { Text = "Executable", AutoSize = true, Location = new Point(20, y + 3), Font = new Font("Segoe UI", 9), BackColor = Color.White };
            exeBox.Location = new Point(90, y);
            exeBox.Width = 430;
            exeBox.Font = new Font("Segoe UI", 9.5f);
            exeBox.ForeColor = Color.FromArgb(60, 60, 60);
            y += 30;

            sourceLabel.AutoSize = true;
            sourceLabel.Location = new Point(90, y);
            sourceLabel.ForeColor = Color.FromArgb(120, 120, 120);
            sourceLabel.Font = new Font("Segoe UI", 8);
            sourceLabel.BackColor = Color.White;
            y += 22;

            // — Queue controls —
            Button addBtn = new Button { Text = "Add to queue", Location = new Point(20, y), Size = new Size(110, 30), FlatStyle = FlatStyle.System };
            addBtn.Click += delegate { AddItem(); };
            Button removeBtn = new Button { Text = "Remove", Location = new Point(138, y), Size = new Size(80, 30), FlatStyle = FlatStyle.System };
            removeBtn.Click += delegate { RemoveItem(); };
            Button clearBtn = new Button { Text = "Clear", Location = new Point(226, y), Size = new Size(70, 30), FlatStyle = FlatStyle.System };
            clearBtn.Click += delegate { items.Clear(); RefreshList(); };
            Button upBtn = new Button { Text = "▲", Location = new Point(360, y), Size = new Size(36, 30), FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 7) };
            upBtn.Click += delegate { MoveItem(-1); };
            Button downBtn = new Button { Text = "▼", Location = new Point(400, y), Size = new Size(36, 30), FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 7) };
            downBtn.Click += delegate { MoveItem(1); };
            Label queueHint = new Label { Text = "Queue — runs all at once", AutoSize = true, Location = new Point(445, y + 8), Font = new Font("Segoe UI", 7), ForeColor = Color.Gray, BackColor = Color.White };
            y += 40;

            queueList.Location = new Point(20, y);
            queueList.Size = new Size(500, 150);
            queueList.Font = new Font("Segoe UI", 9.5f);
            queueList.BorderStyle = BorderStyle.FixedSingle;
            queueList.DoubleClick += delegate { RemoveItem(); };
            y += 162;

            Button startBtn = new Button
            {
                Text = "Start Mimicking  —  run all queued games together",
                Location = new Point(20, y),
                Size = new Size(500, 42),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(88, 101, 242),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            startBtn.FlatAppearance.BorderSize = 0;
            startBtn.Click += delegate { StartQueue(); };
            y += 54;

            status.AutoSize = false;
            status.Size = new Size(500, 36);
            status.Location = new Point(20, y);
            status.Font = new Font("Segoe UI", 8.25f);
            status.ForeColor = Color.Gray;
            status.BackColor = Color.White;

            Controls.AddRange(new Control[] { title, sub, nl, nameBox, timeLabel, minutesBox, mLabel, secondsBox, sLabel, searchBtn, el, exeBox, sourceLabel, addBtn, removeBtn, clearBtn, upBtn, downBtn, queueHint, queueList, startBtn, status });

            // Enter flow: first Enter searches, second Enter adds to queue + clears.
            nameBox.KeyDown += EnterKeyDown;
            exeBox.KeyDown += EnterKeyDown;
            minutesBox.KeyDown += EnterKeyDown;
            secondsBox.KeyDown += EnterKeyDown;

            ToolStripMenuItem dbUpdate = new ToolStripMenuItem("Update game database");
            dbUpdate.Click += delegate
            {
                status.ForeColor = Color.Gray;
                status.Text = "Updating Discord's game database...";
                DiscordDb.UpdateAsync(delegate(bool ok)
                {
                    this.BeginInvoke(new Action(delegate()
                    {
                        status.ForeColor = ok ? Color.Green : Color.OrangeRed;
                        status.Text = ok ? "Game database updated (" + DiscordDb.Count + " games)." : "Update failed — try again later.";
                    }));
                });
            };
            if (menu.Items.Count > 1)
            {
                ToolStripDropDownItem help = (ToolStripDropDownItem)menu.Items[1];
                help.DropDownItems.Add(new ToolStripSeparator());
                help.DropDownItems.Add(dbUpdate);
            }

            tray = MenuBuilder.CreateTray(this, Program.AppName + " — setup");
            FormClosed += delegate
            {
                SaveQueue();
                tray.Dispose();
            };

            LoadSavedQueue();
            CleanupStaleTempCopies();
        }

        private void CleanupStaleTempCopies()
        {
            try
            {
                string tempRoot = Path.Combine(Path.GetTempPath(), "Orhex");
                if (Directory.Exists(tempRoot))
                {
                    foreach (string f in Directory.GetFiles(tempRoot, "*.exe"))
                    {
                        try { File.Delete(f); }
                        catch { }
                    }
                }
            }
            catch { }
            try
            {
                if (!File.Exists(SteamFakesFile)) return;
                var kept = new List<string>();
                foreach (string line in File.ReadAllLines(SteamFakesFile))
                {
                    string t = line.Trim();
                    if (t.Length == 0) continue;
                    bool gone = false;
                    try
                    {
                        if (File.Exists(t)) File.Delete(t);
                        gone = true;
                    }
                    catch { }
                    if (!gone) kept.Add(t);
                }
                File.WriteAllLines(SteamFakesFile, kept.ToArray());
            }
            catch { }
        }

        private void MoveItem(int dir)
        {
            int i = queueList.SelectedIndex;
            int j = i + dir;
            if (i < 0 || j < 0 || j >= items.Count) return;
            QueueItem tmp = items[i];
            items[i] = items[j];
            items[j] = tmp;
            RefreshList();
            queueList.SelectedIndex = j;
        }

        private string QueueFile
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orhex");
                return Path.Combine(dir, "queue.dat");
            }
        }

        private void SaveQueue()
        {
            try
            {
                if (items.Count == 0)
                {
                    if (File.Exists(QueueFile)) File.Delete(QueueFile);
                    return;
                }
                string dir = Path.GetDirectoryName(QueueFile);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(QueueFile, Program.SerializeQueue(items));
            }
            catch { }
        }

        private void LoadSavedQueue()
        {
            try
            {
                if (!File.Exists(QueueFile)) return;
                string raw = File.ReadAllText(QueueFile).Trim();
                if (raw.Length == 0) return;
                List<QueueItem> saved = Program.ParseQueue(raw);
                if (saved.Count == 0) return;
                items.Clear();
                items.AddRange(saved);
                RefreshList();
            }
            catch { }
        }

        private int GetDurationSeconds()
        {
            int m = 15, sec = 40;
            int.TryParse(minutesBox.Text.Trim(), out m);
            int.TryParse(secondsBox.Text.Trim(), out sec);
            if (m < 0) m = 0; if (m > 999) m = 999;
            if (sec < 0) sec = 0; if (sec > 59) sec = 59;
            int total = m * 60 + sec;
            if (total <= 0) total = 15 * 60 + 40;
            return total;
        }

        private bool AddItem()
        {
            string name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                status.ForeColor = Color.Red;
                status.Text = "Type a game name first.";
                return false;
            }
            int duration = GetDurationSeconds();
            string exe = exeBox.Text.Trim();
            if (exe.Length == 0)
            {
                ScannedGame? best = GameResolver.FindBest(name);
                if (best.HasValue)
                {
                    FillExe(new string[] { best.Value.Name, best.Value.Exe, best.Value.Source });
                    exe = exeBox.Text.Trim();
                }
            }
            if (exe.Length == 0) exe = null;
            items.Add(new QueueItem(name, duration, exe, null, true));
            RefreshList();
            nameBox.SelectAll();
            nameBox.Focus();
            status.ForeColor = Color.Gray;
            status.Text = "";
            return true;
        }

        // First Enter = search, second Enter = add to queue + clear the boxes.
        // Custom names are allowed: whatever is typed is kept as the display
        // name (queue list + mimic window title), exe is only used for detection.
        private void EnterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                OnEnterPressed();
            }
        }

        private void OnEnterPressed()
        {
            string name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                status.ForeColor = Color.Red;
                status.Text = "Type a game name first.";
                return;
            }
            bool searchedAlready = lastSearchedText != null &&
                string.Equals(lastSearchedText.Trim(), name, StringComparison.OrdinalIgnoreCase);
            if (!searchedAlready)
            {
                Search();
                return;
            }
            // Same text as the last search (exe resolved, or nothing found for a
            // custom name) -> add it and clear the boxes for the next entry.
            if (AddItem())
                ClearEntryBoxes(name);
        }

        private void ClearEntryBoxes(string addedName)
        {
            nameBox.Text = "";
            exeBox.Text = "";
            sourceLabel.Text = "";
            lastSearchedText = null;
            status.ForeColor = Color.Gray;
            status.Text = "Added \"" + addedName + "\" — type the next game.";
            nameBox.Focus();
        }

        private void RemoveItem()
        {
            if (queueList.SelectedIndex >= 0 && queueList.SelectedIndex < items.Count)
            {
                items.RemoveAt(queueList.SelectedIndex);
                RefreshList();
            }
        }

        private void RefreshList()
        {
            queueList.Items.Clear();
            foreach (QueueItem it in items)
                queueList.Items.Add(it.Display);
        }

        private void Search()
        {
            string query = nameBox.Text.Trim();
            if (query.Length == 0)
            {
                status.ForeColor = Color.Red;
                status.Text = "Type a game name first.";
                return;
            }
            lastSearchedText = query;
            if (!DiscordDb.IsLoaded)
            {
                if (!DiscordDb.EnsureLoaded())
                {
                    if (!DiscordDb.IsDownloading)
                    {
                        status.ForeColor = Color.Gray;
                        status.Text = "Downloading Discord's game database (one time)...";
                        DiscordDb.UpdateAsync(delegate(bool ok)
                        {
                            this.BeginInvoke(new Action(delegate()
                            {
                                if (!ok)
                                {
                                    status.ForeColor = Color.OrangeRed;
                                    status.Text = "Couldn't download the game database — using local search only.";
                                }
                                DoSearch(query);
                            }));
                        });
                    }
                    return;
                }
            }
            DoSearch(query);
        }

        private void DoSearch(string query)
        {
            string curated = GameDb.TryGetExe(query);
            if (curated != null)
            {
                FillExe(new string[] { query, curated, "built-in" });
                status.ForeColor = Color.Green;
                status.Text = "Known game — will run as " + curated;
                return;
            }
            ScannedGame? local = GameResolver.FindBest(query);
            if (local.HasValue)
            {
                FillExe(new string[] { local.Value.Name, local.Value.Exe, local.Value.Source });
                status.ForeColor = Color.Green;
                status.Text = "Found installed: " + local.Value.Name + " (" + local.Value.Source + ") — will run as " + System.IO.Path.GetFileName(local.Value.Exe);
                return;
            }
            List<string[]> disc = DiscordDb.Search(query);
            if (disc.Count > 0)
            {
                string[] pick = disc[0];
                if (disc.Count > 1)
                {
                    pick = ListPickerDialog.Show(disc, "Select the Discord game", this);
                    if (pick == null) return;
                }
                FillExe(pick);
                string appId = DiscordDb.FindApplicationId(pick[0]);
                status.ForeColor = Color.Green;
                status.Text = "Found in Discord's game list: " + pick[0]
                    + (appId != null ? "  (app id " + appId + ")" : "")
                    + " — will run as " + exeBox.Text;
                return;
            }
            status.ForeColor = Color.Gray;
            status.Text = "Not in Discord's list — checking Steam...";
            string q = query;
            Thread th = new Thread(delegate()
            {
                List<string[]> res = OnlineSearch.Search(q);
                if (res.Count == 0)
                {
                    this.BeginInvoke(new Action(delegate()
                    {
                        // Skip if the user already queued/cleared this entry.
                        if (!string.Equals(nameBox.Text.Trim(), q, StringComparison.OrdinalIgnoreCase)) return;
                        if (exeBox.Text.Trim().Length == 0)
                        {
                            // Custom name: keep the typed display name, guess an exe
                            // so it can be queued/started right away. The mimic
                            // window will show the custom name.
                            string guessed = GameDb.ExeForGame(q);
                            exeBox.Text = guessed;
                            sourceLabel.Text = "Source: custom  ·  " + q;
                            status.ForeColor = Color.OrangeRed;
                            status.Text = "Custom name — the window will show \"" + q + "\" running as " + guessed + ".";
                        }
                    }));
                    return;
                }
                string canonical = res[0][0];
                this.BeginInvoke(new Action(delegate()
                {
                    // Skip if the user already queued/cleared this entry.
                    if (!string.Equals(nameBox.Text.Trim(), q, StringComparison.OrdinalIgnoreCase)) return;
                    if (nameBox.Text.Trim().Length == 0) nameBox.Text = canonical;
                    if (exeBox.Text.Trim().Length == 0)
                    {
                        ScannedGame? l2 = GameResolver.FindBest(canonical);
                        if (l2.HasValue) FillExe(new string[] { l2.Value.Name, l2.Value.Exe, l2.Value.Source });
                        else exeBox.Text = GameDb.ExeForGame(canonical);
                    }
                    status.ForeColor = Color.Green;
                    status.Text = "Found on Steam: " + canonical + " — will run as " + (exeBox.Text.Trim().Length > 0 ? exeBox.Text : GameDb.ExeForGame(canonical));
                }));
            });
            th.IsBackground = true;
            th.Start();
        }

        private void FillExe(string[] hit)
        {
            exeBox.Text = System.IO.Path.GetFileName(hit[1]);
            sourceLabel.Text = "Source: " + hit[2] + "  ·  " + hit[0];
        }

        private void StartQueue()
        {
            List<QueueItem> run = new List<QueueItem>();
            if (items.Count > 0) run.AddRange(items);
            else
            {
                string name = nameBox.Text.Trim();
                if (name.Length == 0)
                {
                    status.ForeColor = Color.Red;
                    status.Text = "Add a game first (or type one above and Start).";
                    return;
                }
                int duration = GetDurationSeconds();
                string exe = exeBox.Text.Trim();
                if (exe.Length == 0)
                {
                    ScannedGame? best = GameResolver.FindBest(name);
                    if (best.HasValue) { FillExe(new string[] { best.Value.Name, best.Value.Exe, best.Value.Source }); exe = exeBox.Text.Trim(); }
                    else { List<string[]> disc = DiscordDb.Search(name); if (disc.Count > 0) { FillExe(disc[0]); exe = exeBox.Text.Trim(); } }
                }
                run.Add(new QueueItem(name, duration, exe.Length > 0 ? exe : null, null, true));
            }
            status.ForeColor = Color.Green;
            status.Text = "Starting " + run.Count + " game(s) — resolving exes, Steam manifests, holders...";
            Application.DoEvents();
            // Phase 1: resolve every queued game first, so we can pass the FULL
            // queue to each child. Previously each child got a single-item queue,
            // so every MimicForm pushed its own RPC activity independently and
            // Discord only counted/shown one game (quests/Orbs progress stuck at 1).
            var resolved = new List<QueueItem>();
            var steamTargets = new List<string>();
            var resolvedGames = new List<DiscordGame>();
            foreach (QueueItem qi in run)
            {
                try
                {
                    DiscordGame g = null;
                    if (DiscordDb.EnsureLoaded())
                        g = DiscordDb.FindGame(qi.Name) ?? DiscordDb.FindGame(qi.EffectiveExe);
                    string primaryExe = (g != null && !string.IsNullOrEmpty(g.Exe)) ? g.Exe : qi.EffectiveExe;
                    if (MimicEngine.IsAlreadyRunning(primaryExe))
                    {
                        status.ForeColor = Color.OrangeRed;
                        status.Text = qi.Name + " is already running — skipped.";
                        continue;
                    }

                    string manifest = null;
                    string steamTarget = null;
                    try
                    {
                        status.Text = "Resolving Steam info for " + qi.Name + "...";
                        Application.DoEvents();
                        List<string[]> sres = OnlineSearch.Search(qi.Name);
                        if (sres.Count > 0)
                        {
                            long sid = 0;
                            if (long.TryParse(sres[0][1], out sid))
                            {
                                SteamAppInfo info = SteamQuest.FetchAppInfo(sid);
                                string sPath = SteamQuest.GetSteamPath();
                                if (!string.IsNullOrEmpty(info.Name) && !string.IsNullOrEmpty(info.Executable) &&
                                    !string.IsNullOrEmpty(sPath) && Directory.Exists(Path.Combine(sPath, "steamapps")))
                                {
                                    manifest = SteamQuest.GenerateManifest(sPath, sid, info.Name, info.InstallDir, info.DepotId, SteamQuest.GetOwnerId64());
                                    if (manifest != null)
                                    {
                                        steamTarget = Path.Combine(sPath, "steamapps", "common", info.InstallDir, info.Executable);
                                        string sdir = Path.GetDirectoryName(steamTarget);
                                        if (sdir != null) Directory.CreateDirectory(sdir);
                                        try { File.Copy(Program.OwnExe, steamTarget, true); }
                                        catch { steamTarget = null; manifest = null; }
                                        if (steamTarget != null)
                                        {
                                            RecordSteamFake(steamTarget);
                                            try { File.WriteAllText(Path.Combine(sdir, "steam_appid.txt"), sid.ToString()); }
                                            catch { }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { manifest = null; steamTarget = null; }

                    string finalExe = steamTarget != null ? Path.GetFileName(steamTarget) : primaryExe;
                    resolved.Add(new QueueItem(qi.Name, qi.DurationSeconds, finalExe, manifest, true));
                    steamTargets.Add(steamTarget);
                    resolvedGames.Add(g);
                }
                catch (Exception ex) { MessageBox.Show("Couldn't resolve " + qi.Name + ": " + ex.Message, Program.AppName); }
            }
            if (resolved.Count == 0) return;
            // Every child receives the full queue + its own index, so each
            // MimicForm knows "game X of N" and they can take turns in Discord
            // (only one active presence at a time) instead of fighting.
            string fullArg = Program.SerializeQueue(resolved);
            int launched = 0;
            int holders = 0;
            for (int k = 0; k < resolved.Count; k++)
            {
                try
                {
                    string steamTarget = steamTargets[k];
                    if (steamTarget != null)
                    {
                        if (MimicEngine.IsAlreadyRunning(Path.GetFileName(steamTarget)))
                        {
                            try { File.Delete(resolved[k].Manifest); } catch { }
                            ForgetSteamFake(steamTarget);
                        }
                        else
                        {
                            MimicEngine.LaunchNoExit(steamTarget, fullArg, k);
                            launched++;
                        }
                    }
                    else
                    {
                        if (MimicEngine.IsAlreadyRunning(resolved[k].EffectiveExe))
                        {
                            status.ForeColor = Color.OrangeRed;
                            status.Text = resolved[k].Name + " is already running — skipped.";
                            continue;
                        }
                        MimicEngine.SpawnAs(resolved[k].EffectiveExe, fullArg, k);
                        launched++;
                    }
                }
                catch (Exception ex) { MessageBox.Show("Couldn't start " + resolved[k].Name + ": " + ex.Message, Program.AppName); }
            }
            for (int k = 0; k < resolved.Count; k++)
            {
                DiscordGame g = resolvedGames[k];
                if (g != null)
                {
                    foreach (string extra in DiscordDb.GetExtraExes(g))
                    {
                        if (MimicEngine.IsAlreadyRunning(extra)) continue;
                        try
                        {
                            var hqi = new QueueItem(resolved[k].Name, resolved[k].DurationSeconds, extra, null, true);
                            MimicEngine.SpawnHidden(extra, Program.SerializeQueue(new List<QueueItem> { hqi }));
                            holders++;
                        }
                        catch { }
                    }
                }
            }
            if (launched > 0)
            {
                status.ForeColor = Color.Green;
                if (launched > 1)
                    status.Text = launched + " game(s) + " + holders + " helper(s) running — Discord cycles through them (~30s each) so all quests/Orbs progress together. Close each window to stop.";
                else
                    status.Text = launched + " game(s) + " + holders + " helper(s) running — Discord detects them. Close each window to stop.";
            }
        }

        private string SteamFakesFile
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orhex");
                return Path.Combine(dir, "steam_fakes.dat");
            }
        }

        private void RecordSteamFake(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(SteamFakesFile);
                if (dir != null) Directory.CreateDirectory(dir);
                string cur = File.Exists(SteamFakesFile) ? File.ReadAllText(SteamFakesFile) : "";
                if (cur.IndexOf(path, StringComparison.OrdinalIgnoreCase) < 0)
                    File.AppendAllText(SteamFakesFile, path + "\n");
            }
            catch { }
        }

        private void ForgetSteamFake(string path)
        {
            try
            {
                if (!File.Exists(SteamFakesFile)) return;
                var kept = new List<string>();
                foreach (string line in File.ReadAllLines(SteamFakesFile))
                {
                    string t = line.Trim();
                    if (t.Length == 0) continue;
                    if (!t.Equals(path, StringComparison.OrdinalIgnoreCase)) kept.Add(t);
                }
                File.WriteAllLines(SteamFakesFile, kept.ToArray());
            }
            catch { }
        }
    }

    internal class MimicForm : Form
    {
        private readonly List<QueueItem> items;
        private int index;
        private long remaining;
        private readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        private readonly Label nameLabel = new Label();
        private readonly Label progressLabel = new Label();
        private readonly Label countdownLabel = new Label();
        private readonly DiscordRpc rpc = new DiscordRpc();
        private NotifyIcon tray;
        private Mutex instanceMutex;
        // Discord only honours ONE active presence at a time, so parallel
        // MimicForms take turns (wall-clock slots, no IPC needed). Each window
        // knows the full queue + its own index (see StartQueue fullArg).
        private const int RotateSeconds = 30;
        private DateTime lastPushUtc = DateTime.MinValue;
        private bool slotActive = true;
        private bool recognized = true;

        public MimicForm(List<QueueItem> items, int index)
        {
            this.items = items;
            this.index = Math.Max(0, Math.Min(index, items.Count - 1));

            string exeName = Path.GetFileName(Program.OwnExe);
            bool createdNew;
            instanceMutex = new Mutex(true, "Orhex_" + exeName.ToLowerInvariant(), out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("This game is already being mimicked by another window. Use that window instead.", Program.AppName);
                Environment.Exit(0);
            }

            ClientSize = new Size(460, 300);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.White;
            if (Program.AppIcon != null) Icon = Program.AppIcon;

            MenuStrip menu = MenuBuilder.Build(this, null);
            Controls.Add(menu);
            MainMenuStrip = menu;

            Label top = new Label { Text = "Now playing", AutoSize = true, Location = new Point(16, 40), ForeColor = Color.Gray, BackColor = Color.White };
            nameLabel.Font = new Font("Segoe UI", 15, FontStyle.Bold);
            nameLabel.AutoSize = true;
            nameLabel.Location = new Point(16, 64);
            nameLabel.BackColor = Color.White;
            progressLabel.AutoSize = true;
            progressLabel.Location = new Point(16, 96);
            progressLabel.ForeColor = Color.Gray;
            progressLabel.BackColor = Color.White;

            countdownLabel.Font = new Font("Segoe UI Light", 30, FontStyle.Bold);
            countdownLabel.AutoSize = true;
            countdownLabel.Location = new Point(16, 122);
            countdownLabel.BackColor = Color.White;

            Label note = new Label
            {
                Text = "Discord shows you're playing this game.\r\nYou can run several at once — each window is independent.\r\nClose this window to stop this game.",
                AutoSize = true,
                Location = new Point(16, 186),
                ForeColor = Color.Gray,
                BackColor = Color.White
            };

            Button stopBtn = new Button { Text = "Stop mimicking", Location = new Point(16, 244), Size = new Size(428, 34), Font = new Font("Segoe UI", 9, FontStyle.Bold), FlatStyle = FlatStyle.System };
            stopBtn.Click += delegate { Close(); };

            Controls.AddRange(new Control[] { top, nameLabel, progressLabel, countdownLabel, note, stopBtn });

            tick.Interval = 1000;
            tick.Tick += delegate { Tick(); };
            ApplyItem();
            tick.Start();

            tray = MenuBuilder.CreateTray(this, Program.AppName + " — " + CurrentName());
            tray.ContextMenuStrip.Items.Insert(1, new ToolStripSeparator());
            tray.ContextMenuStrip.Items.Insert(1, new ToolStripMenuItem("Stop mimicking", null, delegate { Close(); }));

            FormClosed += delegate
            {
                tick.Stop();
                tick.Dispose();
                rpc.Close();
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                if (instanceMutex != null)
                {
                    try { instanceMutex.ReleaseMutex(); } catch { }
                    instanceMutex.Close();
                    instanceMutex = null;
                }
                CleanupTempCopy();
                CleanupSteamFake();
            };
        }

        private string CurrentName() { return items[index].Name; }

        private void ApplyItem()
        {
            QueueItem cur = items[index];
            Text = cur.Name + " — " + Program.AppName;
            nameLabel.Text = cur.Name;
            remaining = cur.DurationSeconds > 0 ? cur.DurationSeconds : -1;
            recognized = IsRecognized(cur);
            UpdateCountdown();
            UpdateTray();
            UpdateSlot(true);
        }

        private static bool IsRecognized(QueueItem cur)
        {
            try
            {
                if (!DiscordDb.EnsureLoaded()) return true; // unknown yet, don't warn
                if (DiscordDb.FindApplicationId(cur.Name) != null) return true;
                if (DiscordDb.FindApplicationId(cur.EffectiveExe) != null) return true;
                return false;
            }
            catch { return true; }
        }

        private int ActiveIndex()
        {
            if (items.Count <= 1) return 0;
            long secs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            return (int)((secs / RotateSeconds) % items.Count);
        }

        private void UpdateSlot(bool force)
        {
            if (items.Count <= 1)
            {
                slotActive = true;
                // Re-push periodically so a Discord restart doesn't kill presence.
                if (force || !rpc.IsConnected || (DateTime.UtcNow - lastPushUtc).TotalSeconds >= 60)
                {
                    UpdatePresence();
                    lastPushUtc = DateTime.UtcNow;
                }
                UpdateSlotLabel();
                return;
            }
            int active = ActiveIndex();
            bool shouldBeActive = (active == index);
            if (shouldBeActive)
            {
                if (!recognized && DiscordDb.EnsureLoaded())
                    recognized = IsRecognized(items[index]);
                if (force || !rpc.IsConnected || (DateTime.UtcNow - lastPushUtc).TotalSeconds >= 25)
                {
                    UpdatePresence();
                    lastPushUtc = DateTime.UtcNow;
                }
                slotActive = true;
            }
            else
            {
                if (rpc.IsConnected) rpc.Close();
                slotActive = false;
            }
            UpdateSlotLabel();
        }

        private void UpdateSlotLabel()
        {
            QueueItem cur = items[index];
            string baseText;
            if (items.Count > 1)
                baseText = "Queue game " + (index + 1) + " of " + items.Count + "  ·  running as " + cur.EffectiveExe;
            else
                baseText = "Running as " + cur.EffectiveExe;
            if (!recognized)
                baseText += "  ·  ⚠ not in Discord's game list — quests won't progress";
            else if (items.Count > 1)
                baseText += slotActive ? "  ·  ● active in Discord" : "  ·  ○ waiting (active: game " + (ActiveIndex() + 1) + ")";
            progressLabel.Text = baseText;
        }

        private void UpdatePresence()
        {
            QueueItem cur = items[index];
            if (!DiscordDb.EnsureLoaded()) return;
            string appId = DiscordDb.FindApplicationId(cur.Name);
            if (appId == null)
                appId = DiscordDb.FindApplicationId(cur.EffectiveExe);
            if (appId == null)
            {
                rpc.Close();
                return;
            }
            if (!rpc.Connect(appId)) return;
            long now = EpochMs();
            long end = remaining > 0 ? now + remaining * 1000L : 0;
            rpc.SetActivity(cur.Name, remaining < 0 ? "no time limit" : null, now, end);
        }

        private static long EpochMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        private void UpdateCountdown()
        {
            if (remaining < 0)
                countdownLabel.Text = "\u221e  no time limit";
            else
                countdownLabel.Text = Program.FormatTime(remaining);
        }

        private void UpdateTray()
        {
            if (tray == null) return;
            string left = remaining < 0 ? "no limit" : Program.FormatTime(remaining);
            tray.Text = Program.AppName + " \u2022 Playing " + CurrentName() + " \u2022 " + left;
        }

        private void Tick()
        {
            if (remaining < 0)
            {
                // No time limit: no countdown, but still rotate presence slots.
                UpdateSlot(false);
                UpdateTray();
                return;
            }
            remaining--;
            if (remaining <= 0)
            {
                if (tray != null)
                    tray.ShowBalloonTip(3000, Program.AppName, CurrentName() + " — done.", ToolTipIcon.Info);
                Close();
            }
            else
            {
                UpdateCountdown();
                UpdateSlot(false);
                UpdateTray();
            }
        }

        private void CleanupTempCopy()
        {
            try
            {
                string exe = Program.OwnExe;
                string tempRoot = Path.Combine(Path.GetTempPath(), "Orhex");
                if (string.IsNullOrEmpty(exe) || string.IsNullOrEmpty(tempRoot)) return;
                if (!exe.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) return;
                try { File.Delete(exe); }
                catch { }
            }
            catch { }
        }

        private void CleanupSteamFake()
        {
            try
            {
                if (index < 0 || index >= items.Count) return;
                string manifest = items[index].Manifest;
                if (!string.IsNullOrEmpty(manifest))
                {
                    try { File.Delete(manifest); }
                    catch { }
                }
                try
                {
                    string ownDir = Path.GetDirectoryName(Program.OwnExe);
                    if (!string.IsNullOrEmpty(ownDir))
                    {
                        string appidFile = Path.Combine(ownDir, "steam_appid.txt");
                        if (File.Exists(appidFile))
                        {
                            try { File.Delete(appidFile); }
                            catch { }
                        }
                    }
                }
                catch { }
                string fakesFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orhex", "steam_fakes.dat");
                if (File.Exists(fakesFile))
                {
                    string own = Program.OwnExe;
                    var kept = new List<string>();
                    foreach (string line in File.ReadAllLines(fakesFile))
                    {
                        string t = line.Trim();
                        if (t.Length == 0) continue;
                        if (!t.Equals(own, StringComparison.OrdinalIgnoreCase)) kept.Add(t);
                    }
                    File.WriteAllLines(fakesFile, kept.ToArray());
                }
            }
            catch { }
        }
    }

    internal class ListPickerDialog : Form
    {
        public string[] Selected;
        private readonly ListBox list;
        private readonly List<string[]> items;

        public static string[] Show(List<string[]> items, string title, Form owner)
        {
            ListPickerDialog dlg = new ListPickerDialog(items, title);
            dlg.ShowDialog(owner);
            return dlg.Selected;
        }

        private ListPickerDialog(List<string[]> items, string title)
        {
            this.items = items;
            Text = title;
            ClientSize = new Size(400, 320);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;

            list = new ListBox();
            list.Bounds = new Rectangle(10, 10, 380, 250);
            list.Font = new Font("Segoe UI", 9);
            foreach (string[] it in items)
                list.Items.Add(it[0] + "  (" + it[1] + ")");
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.DoubleClick += delegate { Accept(); };

            Button ok = new Button { Text = "Select", Bounds = new Rectangle(10, 272, 100, 30) };
            ok.Click += delegate { Accept(); };
            Button cancel = new Button { Text = "Cancel", Bounds = new Rectangle(120, 272, 90, 30) };
            cancel.Click += delegate { Close(); };

            Controls.Add(list);
            Controls.Add(ok);
            Controls.Add(cancel);
        }

        private void Accept()
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < items.Count)
                Selected = items[list.SelectedIndex];
            Close();
        }
    }
}
