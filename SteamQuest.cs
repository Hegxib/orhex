using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Microsoft.Win32;

namespace Orhex
{
    internal struct SteamAppInfo
    {
        public string Name;
        public string InstallDir;
        public string Executable;
        public string DepotId;
    }

    internal static class SteamQuest
    {
        public static string GetSteamPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam"))
                {
                    if (key == null) return null;
                    string path = key.GetValue("SteamPath") as string;
                    if (string.IsNullOrEmpty(path)) return null;
                    return path.Replace('/', '\\');
                }
            }
            catch { return null; }
        }

        public static string GetOwnerId64()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam\\ActiveProcess"))
                {
                    if (key == null) return "0";
                    object v = key.GetValue("ActiveUser");
                    if (v == null) return "0";
                    long activeUser = Convert.ToInt64(v, CultureInfo.InvariantCulture);
                    if (activeUser == 0) return "0";
                    return (activeUser + 76561197960265728L).ToString(CultureInfo.InvariantCulture);
                }
            }
            catch { return "0"; }
        }

        public static SteamAppInfo FetchAppInfo(long appid)
        {
            SteamAppInfo info = new SteamAppInfo();
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                string url = "https://api.steamcmd.net/v1/info/" + appid.ToString(CultureInfo.InvariantCulture);
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 10000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Orhex/1.2";
                string json;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();
                var root = Json.Parse(json) as Dictionary<string, object>;
                if (root == null) return info;
                var data = GetDict(root, "data");
                if (data == null) return info;
                var app = GetDict(data, appid.ToString(CultureInfo.InvariantCulture));
                if (app == null) return info;

                var common = GetDict(app, "common");
                if (common != null)
                    info.Name = Json.GetString(common, "name");

                var config = GetDict(app, "config");
                if (config != null)
                {
                    info.InstallDir = Json.GetString(config, "installdir");
                    var launch = GetDict(config, "launch");
                    if (launch != null)
                    {
                        string fallbackExe = null;
                        string bestExe = null;
                        int bestScore = int.MaxValue;
                        int order = 0;
                        foreach (KeyValuePair<string, object> kv in launch)
                        {
                            var entry = kv.Value as Dictionary<string, object>;
                            if (entry == null) continue;
                            string exe = Json.GetString(entry, "executable");
                            if (string.IsNullOrEmpty(exe)) continue;
                            if (fallbackExe == null) fallbackExe = exe;
                            string flat = exe.Replace('/', '\\');
                            int slash = flat.LastIndexOf('\\');
                            string base_ = slash >= 0 ? flat.Substring(slash + 1) : flat;
                            string desc = (Json.GetString(entry, "description") ?? "").ToLowerInvariant();
                            int score = order;
                            if (DiscordDb.IsExcludedExe(base_)) score += 1000;
                            else if (DiscordDb.IsJunkExe(base_)) score += 100;
                            if (desc.Contains("demo") || desc.Contains("legacy") || desc.Contains("test") ||
                                desc.Contains("workshop") || desc.Contains("download") || desc.Contains("beta") ||
                                desc.Contains("server") || desc.Contains("dedicated") || desc.Contains("editor") ||
                                desc.Contains("benchmark") || desc.Contains("tool"))
                                score += 30;
                            var lc = GetDict(entry, "config");
                            string oslist = lc != null ? (Json.GetString(lc, "oslist") ?? "") : "";
                            if (oslist.ToLowerInvariant().Contains("windows")) score -= 50;
                            if (score < bestScore) { bestScore = score; bestExe = exe; }
                            order++;
                        }
                        info.Executable = !string.IsNullOrEmpty(bestExe) ? bestExe : fallbackExe;
                    }
                }

                var depots = GetDict(app, "depots");
                if (depots != null)
                {
                    foreach (KeyValuePair<string, object> kv in depots)
                    {
                        if (kv.Key == "branches") continue;
                        bool numeric = kv.Key.Length > 0;
                        foreach (char c in kv.Key)
                            if (!char.IsDigit(c)) { numeric = false; break; }
                        if (numeric)
                        {
                            info.DepotId = kv.Key;
                            break;
                        }
                    }
                }
            }
            catch { }
            return info;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object v;
            if (d.TryGetValue(key, out v))
                return v as Dictionary<string, object>;
            return null;
        }

        public static string GenerateManifest(string steamPath, long appid, string name, string installdir, string depotId, string ownerId64)
        {
            try
            {
                if (string.IsNullOrEmpty(steamPath) || string.IsNullOrEmpty(name)) return null;
                if (string.IsNullOrEmpty(installdir)) installdir = name;
                if (string.IsNullOrEmpty(depotId)) depotId = "0";
                if (string.IsNullOrEmpty(ownerId64)) ownerId64 = "0";
                string steamApps = Path.Combine(steamPath, "steamapps");
                Directory.CreateDirectory(steamApps);
                string manifestPath = Path.Combine(steamApps, "appmanifest_" + appid.ToString(CultureInfo.InvariantCulture) + ".acf");
                var sb = new StringBuilder();
                sb.AppendLine("\"AppState\"");
                sb.AppendLine("{");
                sb.AppendLine("\t\"appid\"\t\t\"" + appid.ToString(CultureInfo.InvariantCulture) + "\"");
                sb.AppendLine("\t\"universe\"\t\t\"1\"");
                sb.AppendLine("\t\"LauncherPath\"\t\t\"" + EscapeAcf(Path.Combine(steamPath, "steam.exe")) + "\"");
                sb.AppendLine("\t\"name\"\t\t\"" + EscapeAcf(name) + "\"");
                sb.AppendLine("\t\"StateFlags\"\t\t\"1026\"");
                sb.AppendLine("\t\"installdir\"\t\t\"" + EscapeAcf(installdir) + "\"");
                sb.AppendLine("\t\"LastUpdated\"\t\t\"0\"");
                sb.AppendLine("\t\"LastPlayed\"\t\t\"0\"");
                sb.AppendLine("\t\"SizeOnDisk\"\t\t\"0\"");
                sb.AppendLine("\t\"StagingSize\"\t\t\"1073741824\"");
                sb.AppendLine("\t\"buildid\"\t\t\"0\"");
                sb.AppendLine("\t\"LastOwner\"\t\t\"" + EscapeAcf(ownerId64) + "\"");
                sb.AppendLine("\t\"DownloadType\"\t\t\"1\"");
                sb.AppendLine("\t\"UpdateResult\"\t\t\"4\"");
                sb.AppendLine("\t\"BytesToDownload\"\t\t\"1073741824\"");
                sb.AppendLine("\t\"BytesDownloaded\"\t\t\"27262976\"");
                sb.AppendLine("\t\"BytesToStage\"\t\t\"1073741824\"");
                sb.AppendLine("\t\"BytesStaged\"\t\t\"27262976\"");
                sb.AppendLine("\t\"TargetBuildID\"\t\t\"0\"");
                sb.AppendLine("\t\"AutoUpdateBehavior\"\t\t\"0\"");
                sb.AppendLine("\t\"AllowOtherDownloadsWhileRunning\"\t\t\"0\"");
                sb.AppendLine("\t\"ScheduledAutoUpdate\"\t\t\"0\"");
                sb.AppendLine("\t\"InstalledDepots\"");
                sb.AppendLine("\t{");
                sb.AppendLine("\t}");
                sb.AppendLine("\t\"StagedDepots\"");
                sb.AppendLine("\t{");
                sb.AppendLine("\t\t\"" + EscapeAcf(depotId) + "\"");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\t\"manifest\"\t\t\"0\"");
                sb.AppendLine("\t\t\t\"size\"\t\t\"1073741824\"");
                sb.AppendLine("\t\t\t\"dlcappid\"\t\t\"0\"");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t}");
                sb.AppendLine("\t\"UserConfig\"");
                sb.AppendLine("\t{");
                sb.AppendLine("\t}");
                sb.AppendLine("\t\"MountedConfig\"");
                sb.AppendLine("\t{");
                sb.AppendLine("\t}");
                sb.AppendLine("}");
                File.WriteAllText(manifestPath, sb.ToString(), Encoding.UTF8);
                return manifestPath;
            }
            catch { return null; }
        }

        private static string EscapeAcf(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
