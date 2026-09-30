using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Orhex
{
    internal static class Json
    {
        public static object Parse(string text)
        {
            return new JsonReader(text).Value();
        }

        public static string GetString(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return null;
        }

        public static List<object> GetList(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v))
            {
                List<object> l = v as List<object>;
                if (l != null) return l;
            }
            return new List<object>();
        }
    }

    internal class JsonReader
    {
        private readonly string s;
        private int i;

        public JsonReader(string text) { s = text; i = 0; }

        public object Value()
        {
            SkipWs();
            char c = s[i];
            if (c == '{') return Object();
            if (c == '[') return Array();
            if (c == '"') return String();
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            return Number();
        }

        private Dictionary<string, object> Object()
        {
            var d = new Dictionary<string, object>();
            i++;
            SkipWs();
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs();
                string k = String();
                SkipWs();
                i++;
                d[k] = Value();
                SkipWs();
                char c = s[i++];
                if (c == '}') break;
            }
            return d;
        }

        private List<object> Array()
        {
            var l = new List<object>();
            i++;
            SkipWs();
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value());
                SkipWs();
                char c = s[i++];
                if (c == ']') break;
            }
            return l;
        }

        private string String()
        {
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    char e = s[i++];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'b') sb.Append('\b');
                    else if (e == 'f') sb.Append('\f');
                    else if (e == 'u') { sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; }
                    else sb.Append(e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private double Number()
        {
            int start = i;
            while (i < s.Length && "-+0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double d;
            double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            return d;
        }

        private void SkipWs()
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }

    internal struct ScannedGame
    {
        public string Name;
        public string Exe;
        public string Source;
    }

    internal static class GameScanner
    {
        public static List<ScannedGame> Scan()
        {
            var list = new List<ScannedGame>();
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ScanShortcuts(list, seen);
            ScanEpic(list, seen);
            ScanSteam(list, seen);
            return list;
        }

        private static void Add(List<ScannedGame> list, Dictionary<string, string> seen, string name, string exe, string source)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exe)) return;
            name = name.Trim();
            if (name.Length == 0) return;
            if (!File.Exists(exe)) return;
            if (IsJunk(name, exe)) return;

            string exeKey = Path.GetFileName(exe).ToLowerInvariant();
            string prev;
            if (seen.TryGetValue(exeKey, out prev))
            {
                if (prev.Length <= name.Length) return;
            }
            seen[exeKey] = name;
            list.Add(new ScannedGame { Name = name, Exe = exe, Source = source });
        }

        private static bool IsJunk(string name, string exe)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("uninstall") || n.Contains("unins") || n.Contains("readme") || n.Contains("manual"))
                return true;
            if (n.Contains("launcher") || n.Contains("epic games") || n.Contains("steam") ||
                n.Contains("origin") || n.Contains("gog galaxy") || n.Contains("battle.net") ||
                n.Contains("xbox") || n.Contains("ubisoft connect") || n.Contains("playstation") ||
                n.Contains("rockstar games"))
                return true;
            string p = exe.ToLowerInvariant();
            if (p.StartsWith("c:\\windows", StringComparison.OrdinalIgnoreCase)) return true;
            if (p.Contains("\\system32\\") || p.Contains("\\syswow64\\")) return true;
            return false;
        }

        private static readonly string[] SkipFolders = new string[]
        {
            "accessories", "system tools", "startup", "administrative tools",
            "windows powershell", "windows system", "windows accessories"
        };

        private static void ScanShortcuts(List<ScannedGame> list, Dictionary<string, string> seen)
        {
            string userMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string commonMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
            if (!string.IsNullOrEmpty(userMenu) && Directory.Exists(userMenu))
                ScanShortcutsDir(userMenu, list, seen);
            if (!string.IsNullOrEmpty(commonMenu) && Directory.Exists(commonMenu))
                ScanShortcutsDir(commonMenu, list, seen);
        }

        private static void ScanShortcutsDir(string dir, List<ScannedGame> list, Dictionary<string, string> seen)
        {
            try
            {
                foreach (string lnk in Directory.GetFiles(dir, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    string target = ResolveShortcut(lnk);
                    if (string.IsNullOrEmpty(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Path.GetFileNameWithoutExtension(lnk);
                    Add(list, seen, name, target, "Start Menu");
                }
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string n = Path.GetFileName(sub).ToLowerInvariant();
                    bool skip = false;
                    foreach (string f in SkipFolders) if (n.Contains(f)) { skip = true; break; }
                    if (!skip) ScanShortcutsDir(sub, list, seen);
                }
            }
            catch { }
        }

        private static string ResolveShortcut(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                object shell = Activator.CreateInstance(t);
                object shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                if (shortcut == null) return null;
                return (string)t.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            }
            catch { return null; }
        }

        private static void ScanEpic(List<ScannedGame> list, Dictionary<string, string> seen)
        {
            string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            string[] dirs = new string[] { common, user };
            foreach (string d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                try
                {
                    foreach (string f in Directory.GetFiles(d, "*.item"))
                    {
                        try
                        {
                            var json = Json.Parse(File.ReadAllText(f)) as Dictionary<string, object>;
                            if (json == null) continue;
                            string display = Json.GetString(json, "DisplayName");
                            string loc = Json.GetString(json, "InstallLocation");
                            string le = Json.GetString(json, "LaunchExecutable");
                            if (display != null && loc != null && le != null)
                                Add(list, seen, display, Path.Combine(loc, le), "Epic");
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private static void ScanSteam(List<ScannedGame> list, Dictionary<string, string> seen)
        {
            var apps = new List<string>();
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string steamApps = Path.Combine(programFilesX86, "Steam", "steamapps");
            if (Directory.Exists(steamApps))
                CollectSteamAppDirs(steamApps, apps);

            string lf = Path.Combine(steamApps, "libraryfolders.vdf");
            if (File.Exists(lf))
            {
                try
                {
                    string text = File.ReadAllText(lf);
                    foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
                    {
                        string lib = m.Groups[1].Value.Replace("\\\\", "\\");
                        string sa = Path.Combine(lib, "steamapps");
                        if (Directory.Exists(sa))
                            CollectSteamAppDirs(sa, apps);
                    }
                }
                catch { }
            }

            foreach (string acf in apps)
            {
                try
                {
                    string text = File.ReadAllText(acf);
                    Match mn = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"");
                    Match mi = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"");
                    if (!mn.Success || !mi.Success) continue;
                    string name = mn.Groups[1].Value;
                    string installdir = mi.Groups[1].Value;
                    string gameDir = Path.Combine(Path.GetDirectoryName(acf), "common", installdir);
                    string exe = FindMainExe(gameDir, name);
                    if (exe != null)
                        Add(list, seen, name, exe, "Steam");
                }
                catch { }
            }
        }

        private static void CollectSteamAppDirs(string steamAppsDir, List<string> apps)
        {
            try
            {
                foreach (string f in Directory.GetFiles(steamAppsDir, "appmanifest_*.acf"))
                    apps.Add(f);
            }
            catch { }
        }

        private static string FindMainExe(string gameDir, string name)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) return null;
            string[] exes = null;
            try { exes = Directory.GetFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly); }
            catch { }
            if (exes == null || exes.Length == 0)
            {
                try
                {
                    string[] subs = Directory.GetDirectories(gameDir);
                    if (subs.Length == 1)
                        exes = Directory.GetFiles(subs[0], "*.exe", SearchOption.TopDirectoryOnly);
                }
                catch { }
            }
            if (exes == null || exes.Length == 0) return null;

            string slug = Slug(name);
            string best = null;
            long bestLen = 0;
            int bestScore = -1;
            foreach (string e in exes)
            {
                string b = Path.GetFileNameWithoutExtension(e).ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
                int score = -1;
                if (slug.Length > 3 && b == slug) score = 3;
                else if (slug.Length > 3 && (b.Contains(slug) || slug.Contains(b)) && b.Length >= 3) score = 2;
                long len = 0;
                try { len = new FileInfo(e).Length; } catch { }
                if (score > bestScore || (score == bestScore && len > bestLen))
                {
                    best = e;
                    bestScore = score;
                    bestLen = len;
                }
            }
            return best;
        }

        private static string Slug(string name)
        {
            if (name == null) return "";
            string t = name.Trim();
            if (t.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) t = t.Substring(4);
            var sb = new StringBuilder();
            foreach (char c in t)
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
    }

    internal static class OnlineSearch
    {
        public static List<string[]> Search(string query)
        {
            var results = new List<string[]>();
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                string url = "https://store.steampowered.com/api/storesearch/?term=" +
                             Uri.EscapeDataString(query) + "&cc=US&l=en";
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 12000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Orhex/1.0";
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var json = Json.Parse(sr.ReadToEnd()) as Dictionary<string, object>;
                    if (json != null)
                    {
                        foreach (object it in Json.GetList(json, "items"))
                        {
                            var d = it as Dictionary<string, object>;
                            if (d == null) continue;
                            string name = Json.GetString(d, "name");
                            string id = Json.GetString(d, "id");
                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                                results.Add(new string[] { name, id });
                        }
                    }
                }
            }
            catch { }
            return results;
        }
    }

    internal class DiscordGame
    {
        public string Name;
        public string AppId;
        public string Exe;
        public List<string> Aliases = new List<string>();
        public List<string> Exes = new List<string>();
    }

    internal static class DiscordDb
    {
        private static List<DiscordGame> games;
        private static bool downloading;
        private static bool loaded;

        public static bool IsLoaded { get { return loaded; } }
        public static bool IsDownloading { get { return downloading; } }
        public static int Count { get { return games == null ? 0 : games.Count; } }

        private static string CachePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orhex");
                return Path.Combine(dir, "discord_games_v3.dat");
            }
        }

        private const string BackupUrl =
            "https://gist.githubusercontent.com/Cynosphere/c1e77f77f0e565ddaac2822977961e76/raw/gameslist.json";

        private const string DetectableUrl =
            "https://discord.com/api/v10/applications/detectable";

        private const string ExclusionsUrl =
            "https://discord.com/api/v10/games/detectable/exclusions";

        private static readonly HashSet<string> excludedExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Regex> excludedPatterns = new List<Regex>();

        private static string ExclusionsCachePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orhex");
                return Path.Combine(dir, "discord_exclusions.dat");
            }
        }

        public static bool IsExcludedExe(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            if (excludedExes.Contains(file)) return true;
            string f = file.ToLowerInvariant();
            foreach (Regex re in excludedPatterns)
            {
                try { if (re.IsMatch(f)) return true; }
                catch { }
            }
            return false;
        }

        private static void LoadExclusionsCache()
        {
            try
            {
                excludedExes.Clear();
                excludedPatterns.Clear();
                if (!File.Exists(ExclusionsCachePath)) return;
                foreach (string line in File.ReadAllLines(ExclusionsCachePath, Encoding.UTF8))
                {
                    string t = line.Trim();
                    if (t.Length == 0) continue;
                    if (t.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
                    {
                        string pat = t.Substring(3);
                        if (pat.Length > 0)
                        {
                            try { excludedPatterns.Add(new Regex(pat, RegexOptions.IgnoreCase | RegexOptions.Compiled)); }
                            catch { }
                        }
                    }
                    else excludedExes.Add(t);
                }
            }
            catch { }
        }

        private static bool FetchExclusions()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                var wc = new WebClient();
                wc.Headers[HttpRequestHeader.UserAgent] =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                wc.Headers[HttpRequestHeader.Accept] = "application/json";
                wc.Headers["Referer"] = "https://discord.com/";
                wc.Headers["Origin"] = "https://discord.com";
                string json = wc.DownloadString(ExclusionsUrl);
                if (string.IsNullOrEmpty(json)) return false;
                var root = Json.Parse(json) as Dictionary<string, object>;
                if (root == null) return false;
                var exes = new List<string>();
                var pats = new List<string>();
                foreach (object e in Json.GetList(root, "executables"))
                {
                    string s = Convert.ToString(e, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(s)) exes.Add(s.Trim());
                }
                foreach (object p in Json.GetList(root, "patterns"))
                {
                    string s = Convert.ToString(p, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(s)) pats.Add(s.Trim());
                }
                if (exes.Count == 0 && pats.Count == 0) return false;
                excludedExes.Clear();
                excludedPatterns.Clear();
                foreach (string s in exes) excludedExes.Add(s);
                foreach (string s in pats)
                {
                    try { excludedPatterns.Add(new Regex(s, RegexOptions.IgnoreCase | RegexOptions.Compiled)); }
                    catch { }
                }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ExclusionsCachePath));
                    var sb = new StringBuilder();
                    foreach (string s in exes) { sb.Append(s); sb.Append('\n'); }
                    foreach (string s in pats) { sb.Append("re:"); sb.Append(s); sb.Append('\n'); }
                    File.WriteAllText(ExclusionsCachePath, sb.ToString(), Encoding.UTF8);
                }
                catch { }
                return true;
            }
            catch { return false; }
        }

        private static string DownloadJson()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                var wc = new WebClient();
                wc.Headers[HttpRequestHeader.UserAgent] =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                wc.Headers[HttpRequestHeader.Accept] = "application/json";
                wc.Headers[HttpRequestHeader.AcceptLanguage] = "en-US,en;q=0.9";
                wc.Headers["Referer"] = "https://discord.com/";
                wc.Headers["Origin"] = "https://discord.com";
                string json = null;
                try
                {
                    json = wc.DownloadString(DetectableUrl);
                }
                catch { }
                if (json == null)
                {
                    try
                    {
                        json = wc.DownloadString(BackupUrl);
                    }
                    catch { }
                }
                return json;
            }
            catch { return null; }
        }

        public static void RefreshSilently()
        {
            if (downloading) return;
            downloading = true;
            Thread th = new Thread(delegate()
            {
                bool ok = false;
                try
                {
                    try { FetchExclusions(); }
                    catch { }
                    string json = DownloadJson();
                    ok = json != null && BuildFromJson(json);
                }
                catch { ok = false; }
                downloading = false;
                if (ok) loaded = true;
            });
            th.IsBackground = true;
            th.Start();
        }

        public static bool EnsureLoaded()
        {
            if (loaded) return true;
            if (games != null) { loaded = true; return true; }
            try
            {
                if (File.Exists(CachePath))
                {
                    LoadExclusionsCache();
                    LoadCache();
                    loaded = true;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static void UpdateAsync(Action<bool> done)
        {
            if (downloading) return;
            downloading = true;
            Thread th = new Thread(delegate()
            {
                bool ok = false;
                try
                {
                    try { FetchExclusions(); }
                    catch { }
                    string json = DownloadJson();
                    ok = json != null && BuildFromJson(json);
                }
                catch { ok = false; }
                downloading = false;
                loaded = ok;
                if (done != null)
                {
                    try { done(ok); }
                    catch { }
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        private static bool BuildFromJson(string json)
        {
            var root = Json.Parse(json) as List<object>;
            if (root == null) return false;
            var list = new List<DiscordGame>();
            foreach (object item in root)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                string name = Json.GetString(d, "name");
                if (string.IsNullOrEmpty(name)) continue;

                var g = new DiscordGame { Name = name, AppId = Json.GetString(d, "id") };
                foreach (object a in Json.GetList(d, "aliases"))
                    if (a != null)
                        g.Aliases.Add(Convert.ToString(a, CultureInfo.InvariantCulture));

                var launcherBad = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var seenExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object e in Json.GetList(d, "executables"))
                {
                    var ed = e as Dictionary<string, object>;
                    if (ed == null) continue;
                    string os = (Json.GetString(ed, "os") ?? "").ToLowerInvariant();
                    if (os != "win32" && os != "win64") continue;
                    string exeName = Json.GetString(ed, "name");
                    if (string.IsNullOrEmpty(exeName)) continue;
                    exeName = exeName.Replace('\\', '/');
                    string file = exeName.IndexOf('/') >= 0 ? exeName.Substring(exeName.LastIndexOf('/') + 1) : exeName;
                    if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    while (file.Length > 0 && !char.IsLetterOrDigit(file[0]))
                        file = file.Substring(1);
                    if (file.Length < 4) continue;
                    if (!seenExe.Add(file)) continue;
                    if (ed.ContainsKey("is_launcher") && Convert.ToBoolean(ed["is_launcher"], CultureInfo.InvariantCulture))
                        launcherBad[file] = 30;
                    g.Exes.Add(file);
                }
                if (g.Exes.Count == 0) continue;
                g.Exe = PickBestExe(name, g.Exes, launcherBad);
                if (string.IsNullOrEmpty(g.Exe))
                    g.Exe = g.Exes[0];
                int bIdx = g.Exes.IndexOf(g.Exe);
                if (bIdx > 0)
                {
                    g.Exes.RemoveAt(bIdx);
                    g.Exes.Insert(0, g.Exe);
                }

                list.Add(g);
            }

            games = list;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                var sb = new StringBuilder();
                foreach (DiscordGame g in games)
                {
                    sb.Append(g.Name);
                    sb.Append('\t');
                    sb.Append(g.AppId ?? "");
                    sb.Append('\t');
                    sb.Append(string.Join(";", g.Exes.ToArray()));
                    sb.Append('\t');
                    sb.Append(string.Join(";", g.Aliases.ToArray()));
                    sb.Append('\n');
                }
                File.WriteAllText(CachePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
            return true;
        }

        private static void LoadCache()
        {
            var list = new List<DiscordGame>();
            foreach (string line in File.ReadAllLines(CachePath, Encoding.UTF8))
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 2) continue;
                string name = parts[0].Trim();
                if (name.Length == 0) continue;
                var g = new DiscordGame { Name = name };
                if (parts.Length >= 2 && parts[1].Length > 0) g.AppId = parts[1];
                if (parts.Length >= 3 && parts[2].Length > 0)
                {
                    foreach (string e in parts[2].Split(';'))
                        if (e.Length > 0)
                            g.Exes.Add(e);
                    if (g.Exes.Count > 0) g.Exe = PickBestExe(g.Name, g.Exes, null);
                }
                if (parts.Length >= 4 && parts[3].Length > 0)
                    foreach (string a in parts[3].Split(';'))
                        if (a.Length > 0)
                            g.Aliases.Add(a);
                if (string.IsNullOrEmpty(g.Exe)) continue;
                list.Add(g);
            }
            games = list;
        }

        public static List<string[]> Search(string query)
        {
            var results = new List<string[]>();
            if (!loaded || games == null) return results;
            string q = (query ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return results;
            string qslug = Slug(q);
            int best = 0;
            foreach (DiscordGame g in games)
            {
                int sc = Score(g, q, qslug);
                if (sc > best)
                {
                    best = sc;
                    results.Clear();
                    results.Add(new string[] { g.Name, g.Exe, "Discord" });
                }
                else if (sc == best && best > 0 && results.Count < 5)
                {
                    results.Add(new string[] { g.Name, g.Exe, "Discord" });
                }
            }
            return results;
        }

        public static string FindApplicationId(string nameOrExe)
        {
            DiscordGame g = FindGame(nameOrExe);
            if (g == null || string.IsNullOrEmpty(g.AppId)) return null;
            return g.AppId;
        }

        public static List<string> GetExtraExes(DiscordGame g)
        {
            var result = new List<string>();
            if (g == null || g.Exes == null) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(g.Exe)) seen.Add(g.Exe);
            foreach (string e in g.Exes)
            {
                if (string.IsNullOrEmpty(e)) continue;
                if (!seen.Add(e)) continue;
                if (IsJunkExe(e)) continue;
                if (IsExcludedExe(e)) continue;
                result.Add(e);
                if (result.Count >= 6) break;
            }
            return result;
        }

        public static DiscordGame FindGame(string nameOrExe)
        {
            if (!loaded || games == null) return null;
            string q = (nameOrExe ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return null;
            string qexe = Path.GetFileName(q).ToLowerInvariant();
            string qslug = Slug(q);
            int best = 0;
            DiscordGame bestGame = null;
            foreach (DiscordGame g in games)
            {
                foreach (string e in g.Exes)
                {
                    if (IsExcludedExe(e)) continue;
                    if (e.Equals(qexe, StringComparison.OrdinalIgnoreCase) && best < 100)
                    {
                        best = 100;
                        bestGame = g;
                    }
                }
                int sc = Score(g, q, qslug);
                if (sc > best)
                {
                    best = sc;
                    bestGame = g;
                }
            }
            if (bestGame != null && best < 100)
            {
                string curated = GameDb.TryGetExe(q);
                if (curated != null)
                {
                    string curatedFile = Path.GetFileName(curated).ToLowerInvariant();
                    int cbest = -1;
                    DiscordGame cgame = null;
                    foreach (DiscordGame g in games)
                    {
                        foreach (string e in g.Exes)
                        {
                            if (!e.Equals(curatedFile, StringComparison.OrdinalIgnoreCase)) continue;
                            int sc = Score(g, q, qslug);
                            if (sc > cbest)
                            {
                                cbest = sc;
                                cgame = g;
                            }
                            break;
                        }
                    }
                    if (cgame != null && cbest > 0)
                    {
                        bestGame = cgame;
                        best = 100;
                    }
                }
            }
            return bestGame;
        }

        private static int Score(DiscordGame g, string q, string qslug)
        {
            string n = g.Name.ToLowerInvariant();
            if (n == q) return 100;
            int s = 0;
            if (qslug.Length > 0 && Slug(n) == qslug) s = 90;
            if (qslug.Length >= 3 && Acronym(n) == qslug) s = Math.Max(s, 92);
            if (q.Length >= 3 && (n.Contains(q) || (q.Length > n.Length && q.Contains(n)))) s = Math.Max(s, 60);
            if (g.Aliases != null)
            {
                foreach (string a in g.Aliases)
                {
                    string al = a.ToLowerInvariant();
                    if (al == q) return 98;
                    if (qslug.Length > 0 && Slug(a) == qslug) s = Math.Max(s, 85);
                    if (qslug.Length >= 3 && Acronym(a) == qslug) s = Math.Max(s, 88);
                    if (q.Length >= 3 && (al.Contains(q) || (q.Length > al.Length && q.Contains(al)))) s = Math.Max(s, 55);
                }
            }
            if (s >= 55) return s;
            return 0;
        }

        private static string Acronym(string name)
        {
            string[] words = name.Split(new char[] { ' ', '-', '_', ':', '\'', '.', ',', '(', ')', '!', '&' },
                StringSplitOptions.RemoveEmptyEntries);
            int start = 0;
            if (words.Length > 1 && words[0] == "the") start = 1;
            var sb = new StringBuilder();
            for (int i = start; i < words.Length; i++)
            {
                string w = words[i];
                if (w.Length > 0) sb.Append(char.ToLowerInvariant(w[0]));
            }
            return sb.ToString();
        }

        private static string Slug(string name)
        {
            string t = name.Trim();
            if (t.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) t = t.Substring(4);
            var sb = new StringBuilder();
            foreach (char c in t)
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        public static bool IsJunkExe(string file)
        {
            string f = file.ToLowerInvariant();
            string[] words = new string[]
            {
                "setup", "installer", "launcher", "updater", "unins", "redist",
                "social", "support", "dxsetup", "vulkan", "crash", "report", "register",
                "downloader", "download", "bootstrapper", "bootstrap", "patcher", "helper",
                "service", "scanner", "tool", "bridge", "proxy", "backend", "handler",
                "rageplugin", "scripthook", "fivem", "configurator", "reloader", "injector",
                "cheat", "sdk", "editor", "battleye", "_be", "_eac", "eac", "overlay", "server",
                "update", "install", "start_protected_game", "protected_game"
            };
            foreach (string w in words)
                if (f.Contains(w))
                    return true;
            return false;
        }

        private static string PickBestExe(string gameName, List<string> exes, Dictionary<string, int> extra)
        {
            if (exes == null || exes.Count == 0) return null;
            string gslug = Slug(gameName ?? "");
            string best = null;
            int bestScore = int.MaxValue;
            foreach (string e in exes)
            {
                if (string.IsNullOrEmpty(e)) continue;
                int score = 0;
                if (extra != null && extra.ContainsKey(e)) score += extra[e];
                if (IsJunkExe(e)) score += 50;
                if (IsExcludedExe(e)) score += 1000;
                string stem = e.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? e.Substring(0, e.Length - 4) : e;
                string eslug = Slug(stem);
                if (eslug.Length >= 3 && gslug.Length >= 3 &&
                    (gslug.Contains(eslug) || eslug.Contains(gslug)))
                    score -= 100;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }
    }

    internal static class GameResolver
    {
        private static List<ScannedGame> indexCache;
        private static DateTime cacheTime;

        public static List<ScannedGame> Index()
        {
            if (indexCache == null || (DateTime.Now - cacheTime).TotalMinutes > 10)
            {
                indexCache = GameScanner.Scan();
                cacheTime = DateTime.Now;
            }
            return indexCache;
        }

        public static List<string[]> SearchLocal(string query)
        {
            var results = new List<string[]>();
            string q = query == null ? "" : query.Trim();
            if (q.Length == 0) return results;

            string db = GameDb.TryGetExe(q);
            if (db != null)
                results.Add(new string[] { q, db, "built-in" });

            string slug = GameScannerSlug(q);
            string low = q.ToLowerInvariant();
            foreach (ScannedGame g in Index())
            {
                string n = g.Name.ToLowerInvariant();
                bool match = n == low || n.Contains(low) || low.Contains(n) ||
                             GameScannerSlug(g.Name) == slug;
                if (match)
                    results.Add(new string[] { g.Name, g.Exe, g.Source });
            }
            return results;
        }

        public static ScannedGame? FindBest(string query)
        {
            string q = query == null ? "" : query.Trim();
            if (q.Length == 0) return null;
            string low = q.ToLowerInvariant();
            string slug = GameScannerSlug(q);
            List<ScannedGame> idx = Index();
            ScannedGame? slugFallback = null;
            ScannedGame? subFallback = null;
            foreach (ScannedGame g in idx)
            {
                string n = g.Name.ToLowerInvariant();
                if (n == low) return g;
                if (slug.Length > 0 && GameScannerSlug(g.Name) == slug && !slugFallback.HasValue)
                    slugFallback = g;
                if (!subFallback.HasValue && (n.Contains(low) || low.Contains(n)))
                    subFallback = g;
            }
            if (slugFallback.HasValue) return slugFallback;
            return subFallback;
        }

        private static string GameScannerSlug(string name)
        {
            string t = name.Trim();
            if (t.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) t = t.Substring(4);
            var sb = new StringBuilder();
            foreach (char c in t)
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
    }
}
