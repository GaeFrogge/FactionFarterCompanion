using System.Net;
using System.Web.Script.Serialization;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

class Score {
    public string Path, Name, Faction;
    public List<Record> Records = new List<Record>();
}
class Record {
    public int Id;
    public long Time;
    public string Zone;
}
class SaveReader {
    public static Score Read(string path) {
        var raw = File.ReadAllText(path);
        var ledger = Regex.Match(raw, @"(?m)^[ \t]*\[""ledger""\]\s*=\s*\{([^{}]*)\}");
        if (!ledger.Success) return null;
        string body = ledger.Groups[1].Value;
        var data = Regex.Match(body, @"\[""data""\]\s*=\s*""([a-fA-F0-9:]+)""");
        var checksum = Regex.Match(body, @"\[""checksum""\]\s*=\s*(\d+)");
        if (!Regex.IsMatch(body, @"\[""version""\]\s*=\s*1\s*(?:,|$)") || !data.Success || !checksum.Success)
            throw new FormatException("Unsupported or incomplete save.");
        string payload = data.Groups[1].Value;
        long hash = 5381;
        foreach (char c in payload) hash = (hash * 33 + c) % 2147483647;
        if (hash != long.Parse(checksum.Groups[1].Value)) throw new FormatException("Save checksum mismatch.");
        string[] fields = payload.Split(':').Select(Decode).ToArray();
        if (fields.Length < 3 || (fields.Length - 3) % 5 != 0) throw new FormatException("Invalid save structure.");
        var score = new Score { Path = path, Name = fields[1], Faction = fields[2] };
        for (int i = 3; i < fields.Length; i += 5) {
            if (fields[i + 2] == "") continue;
            long epoch = long.Parse(fields[i + 2]);
            if (epoch < 0 || epoch > 253402300799L) throw new FormatException("Invalid record time.");
            score.Records.Add(new Record { Id = (i - 3) / 5 + 1, Time = epoch, Zone = fields[i + 3] });
        }
        return score;
    }
    static string Decode(string value) {
        if (value.Length % 2 != 0) throw new FormatException("Invalid save encoding.");
        byte[] bytes = new byte[value.Length / 2];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(value.Substring(i * 2, 2), 16);
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
class Window : Form {
    static readonly Color Base = Color.FromArgb(21, 22, 26), Surface = Color.FromArgb(34, 36, 42),
        TextColor = Color.FromArgb(240, 236, 228), Gold = Color.FromArgb(233, 196, 113);
    const string RegistryPath = @"Software\GaeFrogge\FactionFarterCompanion";
    readonly DataGridView grid = new DataGridView();
    readonly Label source = new Label(), activity = new Label();
    readonly ComboBox toon = new ComboBox();
    readonly TextBox stats = new TextBox(), folderBox = new TextBox();
    readonly Panel body = new Panel(), characters = new Panel(), statistics = new Panel(), settings = new Panel();
    readonly CheckBox auto = new CheckBox();
    readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, string> observed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Score> scores = new Dictionary<string, Score>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, DateTime> uploads = new Dictionary<string, DateTime>();
    readonly Label leaderboard = LabelOf("Leaderboard: Not connected", Color.Silver, 10);
    const string Endpoint = "https://ff.gaefrogge.com/";
    string DeviceKey() {
        using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath)) {
            var token = key.GetValue("DeviceKey") as string;
            if (token != null) return token;
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            key.SetValue("DeviceKey", token);
            return token;
        }
    }
    void CheckServer() {
        try {
            var request = (HttpWebRequest)WebRequest.Create(Endpoint + "scores");
            request.Timeout = 600;

            using (request.GetResponse()) leaderboard.Text = "Leaderboard: Connected";
        } catch (WebException) { leaderboard.Text = "Leaderboard: Not connected"; }
    }
    readonly Timer timer = new Timer();
    bool rendering;
    string folder;
    public Window() {
        Text = "Faction Farter Companion";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) {}
        BackColor = Base; ForeColor = TextColor; Font = new Font("Segoe UI", 10);
        Size = new Size(960, 650); MinimumSize = new Size(680, 480);
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath)) {
            folder = key == null ? "" : (string)key.GetValue("Folder", "");
            if (key != null) {
                auto.Checked = (int)key.GetValue("Auto", 0) == 1;
                foreach (string item in (string[])key.GetValue("Selected", new string[0])) selected.Add(item);
            }
        }
        if (folder == "") {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed)) {
                string candidate = Path.Combine(drive.Name, @"World of Warcraft\_forever_");
                if (Directory.Exists(Path.Combine(candidate, @"WTF\Account"))) { folder = candidate; break; }
            }
        }
        var title = LabelOf("Faction Farter Companion", Gold, 20);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 108, Padding = new Padding(12),
            FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Surface };
        top.Controls.Add(title);
        source.AutoSize = true; source.ForeColor = TextColor; top.Controls.Add(source);
        top.Controls.Add(leaderboard);
        var nav = Bar();
        nav.Controls.Add(ButtonOf("Characters", delegate { ShowPage(characters); }));
        nav.Controls.Add(ButtonOf("Stats", delegate { ShowPage(statistics); }));
        nav.Controls.Add(ButtonOf("Settings", delegate { ShowPage(settings); }));
        body.Dock = DockStyle.Fill; body.Padding = new Padding(12, 8, 12, 8);
        foreach (Panel page in new[] { characters, statistics, settings }) { page.Dock = DockStyle.Fill; body.Controls.Add(page); }
        activity.Dock = DockStyle.Bottom; activity.Height = 34; activity.Padding = new Padding(12, 7, 0, 0);
        activity.ForeColor = Color.Silver;
        Controls.Add(body); Controls.Add(activity); Controls.Add(nav); Controls.Add(top);
        BuildCharacters(); BuildStats(); BuildSettings(); ShowPage(characters);
        ReadAll();
        timer.Interval = 2500; timer.Tick += delegate { Watch(); }; timer.Start();
        FormClosed += delegate { timer.Dispose(); };
    }
    static Label LabelOf(string value, Color color, int size) {
        return new Label { Text = value, ForeColor = color, AutoSize = true, Font = new Font("Segoe UI", size) };
    }
    static FlowLayoutPanel Bar() {
        return new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 6, 8, 0), BackColor = Base };
    }
    static Button ButtonOf(string label, Action click) {
        var button = new Button { Text = label, AutoSize = true, Height = 31, BackColor = Surface, ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 0, 8, 0) };
        button.FlatAppearance.BorderColor = Color.FromArgb(71, 72, 78);
        button.Click += delegate { click(); };
        return button;
    }
    void BuildCharacters() {
        var bar = Bar(); var sync = ButtonOf("Sync all", SyncSelected); sync.ForeColor = Gold;
        bar.Controls.Add(sync); bar.Controls.Add(ButtonOf("Refresh characters", delegate { ReadAll(); }));
        grid.Dock = DockStyle.Fill; grid.BackgroundColor = Base; grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false; grid.RowHeadersVisible = false; grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowTemplate.Height = 34;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Gold };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = TextColor,
            SelectionBackColor = Color.FromArgb(56, 59, 69), SelectionForeColor = TextColor };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Selected", FillWeight = 55 });
        foreach (string label in new[] { "Character", "Faction", "Saved tally", "Last upload" })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = label, ReadOnly = true });
        grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Text = "Sync", UseColumnTextForButtonValue = true, FillWeight = 60 });
        grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e) {
            if (rendering || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var row = grid.Rows[e.RowIndex];
            if (Convert.ToBoolean(row.Cells[0].Value)) selected.Add((string)row.Tag); else selected.Remove((string)row.Tag);
            SaveSettings();
        };
        grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e) {
            if (e.RowIndex >= 0 && e.ColumnIndex == 5) SyncOne((string)grid.Rows[e.RowIndex].Tag);
        };
        characters.Controls.Add(grid); characters.Controls.Add(bar);
    }
    void BuildStats() {
        var row = Bar(); toon.Width = 270; toon.DropDownStyle = ComboBoxStyle.DropDownList;
        toon.BackColor = Surface; toon.ForeColor = TextColor;
        row.Controls.Add(toon); toon.SelectedIndexChanged += delegate { RenderStats(); };
        stats.Dock = DockStyle.Fill; stats.Multiline = true; stats.ReadOnly = true; stats.ScrollBars = ScrollBars.Vertical;
        stats.BackColor = Surface; stats.ForeColor = TextColor; stats.Font = new Font("Segoe UI", 12);
        statistics.Controls.Add(stats); statistics.Controls.Add(row);
    }
    void BuildSettings() {
        var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
        list.Controls.Add(LabelOf("WoW Forever folder", Gold, 13));
        folderBox.Width = 490; folderBox.ReadOnly = true; folderBox.Text = folder;
        folderBox.BackColor = Surface; folderBox.ForeColor = TextColor; list.Controls.Add(folderBox);
        list.Controls.Add(ButtonOf("Choose folder", delegate {
            using (var picker = new FolderBrowserDialog { SelectedPath = folder }) {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                if (!Directory.Exists(Path.Combine(picker.SelectedPath, @"WTF\Account"))) {
                    activity.Text = "Choose the WoW Forever folder that contains WTF."; return;
                }
                folderBox.Text = folder = picker.SelectedPath; SaveSettings(); ReadAll();
            }
        }));
        auto.AutoSize = true; auto.Text = "Auto-sync selected characters after WoW saves";
        list.Controls.Add(auto); auto.CheckedChanged += delegate { SaveSettings(); };
        list.Controls.Add(LabelOf("WoW saves on /reload, logout, or normal exit.", Color.Silver, 10));
        settings.Controls.Add(list);
    }
    void SaveSettings() {
        using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath)) {
            key.SetValue("Folder", folder); key.SetValue("Auto", auto.Checked ? 1 : 0);
            key.SetValue("Selected", selected.ToArray(), RegistryValueKind.MultiString);
        }
    }
    void ShowPage(Panel page) { page.BringToFront(); }
    Dictionary<string, string> Discover() {
        if (string.IsNullOrWhiteSpace(folder)) throw new DirectoryNotFoundException("Choose a WoW folder in Settings.");
        var root = Path.Combine(folder, @"WTF\Account");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("WoW save folder is unavailable.");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(root, "FactionFarter.lua", SearchOption.AllDirectories)) {
            var info = new FileInfo(file);
            if (info.Directory.Name == "SavedVariables") result[file] = info.LastWriteTimeUtc.Ticks + ":" + info.Length;
        }
        return result;
    }
    void Watch() {
        CheckServer();
        if (!auto.Checked) return;
        try {
            var files = Discover();
            if (files.Count != observed.Count || files.Any(f => !observed.ContainsKey(f.Key) || observed[f.Key] != f.Value))
            {
                ReadAll();
                if (scores.Keys.Any(selected.Contains)) SyncSelected();
            }
        } catch (Exception e) { source.Text = "WoW save  \u00b7  " + e.Message; }
    }
    void ReadAll() {
        try {
            var files = Discover(); int errors = 0; var next = new Dictionary<string, Score>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in files.Keys) {
                try {
                    var score = SaveReader.Read(path);
                    if (score == null) continue;
                    next.Add(path, score);
                } catch (Exception) { errors++; Score old; if (scores.TryGetValue(path, out old)) next.Add(path, old); }
            }
            scores.Clear(); foreach (var entry in next) scores.Add(entry.Key, entry.Value);
            if (errors == 0) observed = files;
            source.Text = "WoW save  \u00b7  " + (errors > 0 ? errors + " unreadable" : scores.Count == 0 ? "No matching saves yet" : "Readable");
            activity.Text = errors > 0 ? "Some saves could not be read. Cached results may be shown." :
                scores.Count == 0 ? "Run /reload in WoW to write the addon save." : "Local saves ready. Sync uploads to the hosted leaderboard.";
            RenderCharacters();
        } catch (Exception e) { source.Text = "WoW save  \u00b7  Unavailable"; activity.Text = e.Message; }
    }
    bool SyncOne(string path) {
        try {
            var score = SaveReader.Read(path);
            if (score == null) throw new FormatException("No supported ledger in the save.");
            scores[path] = score;
            source.Text = "WoW save  \u00b7  Readable";
            var payload = new { name = score.Name, faction = score.Faction,
                records = score.Records.Select(r => new { id = r.Id, zone = r.Zone, hour = r.Time / 3600 }).ToArray() };
            byte[] data = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(payload));
            var request = (HttpWebRequest)WebRequest.Create(Endpoint + "sync");

            request.Timeout = 1500;
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + DeviceKey();
            request.ContentLength = data.Length;
            using (var stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
            using (var response = (HttpWebResponse)request.GetResponse()) {
                if (response.StatusCode != HttpStatusCode.OK) throw new IOException("Upload not accepted.");
            }
            uploads[path] = DateTime.Now;
            leaderboard.Text = "Leaderboard: Connected";
            activity.Text = "Uploaded " + score.Name + " to the hosted leaderboard.";
            RenderCharacters(); return true;
        } catch (WebException e) { CheckServer(); activity.Text = "Upload failed: " + e.Message; RenderCharacters(); return false; } catch (Exception e) { source.Text = "WoW save  \u00b7  Read failed"; activity.Text = e.Message; return false; }
    }
    void SyncSelected() {
        var paths = scores.Keys.Where(selected.Contains).ToArray();
        if (paths.Length == 0) { activity.Text = "Select at least one character to sync."; return; }
        int count = paths.Count(SyncOne);
        activity.Text = string.Format("Uploaded {0} of {1} selected characters.", count, paths.Length);
        
    }
    void RenderCharacters() {
        string previous = toon.SelectedItem as string;
        rendering = true;
        try {
            grid.Rows.Clear(); toon.Items.Clear();
            foreach (var score in scores.Values.OrderBy(s => s.Name)) {
                var row = grid.Rows[grid.Rows.Add(selected.Contains(score.Path), score.Name, score.Faction, score.Records.Count, uploads.ContainsKey(score.Path) ? uploads[score.Path].ToString("g") : "Never")];
                row.Tag = score.Path;
                row.Cells[2].Style.ForeColor = score.Faction == "Alliance" ? Color.LightSkyBlue : Color.LightSalmon;
                toon.Items.Add(score.Name + " (" + score.Faction + ")  #" + (toon.Items.Count + 1));
            }
            if (toon.Items.Count > 0) toon.SelectedIndex = Math.Max(0, toon.Items.IndexOf(previous ?? ""));
        } finally { rendering = false; }
        RenderStats();
    }
    void RenderStats() {
        if (rendering) return;
        var score = scores.Values.OrderBy(s => s.Name).ElementAtOrDefault(Math.Max(0, toon.SelectedIndex));
        if (score == null) { stats.Text = "No character save loaded."; return; }
        var zones = score.Records.Where(r => r.Zone != "").GroupBy(r => r.Zone).OrderByDescending(g => g.Count()).ToList();
        var hours = score.Records.GroupBy(r => DateTimeOffset.FromUnixTimeSeconds(r.Time).LocalDateTime.Hour).OrderByDescending(g => g.Count()).ToList();
        var output = new StringBuilder();
        output.AppendLine(score.Name + "  |  " + score.Faction).AppendLine();
        output.AppendLine("Players farted on: " + score.Records.Count);
        output.AppendLine("Favorite zones: " + (zones.Count == 0 ? "No activity yet" : string.Join(", ", zones.Where(g => g.Count() == zones[0].Count()).Select(g => g.Key))));
        output.AppendLine("Most active around: " + (hours.Count == 0 ? "No activity yet" : string.Join(", ", hours.Where(g => g.Count() == hours[0].Count()).Select(g => DateTime.Today.AddHours(g.Key).ToString("h tt")))));
        output.AppendLine().AppendLine("Recent activity").AppendLine();
        foreach (var record in score.Records.OrderByDescending(r => r.Time).Take(20))
            output.AppendLine((record.Zone == "" ? "Unknown zone" : record.Zone) + "  |  " + DateTimeOffset.FromUnixTimeSeconds(record.Time).LocalDateTime.ToString("MMM d 'at' h:mm tt"));
        if (score.Records.Count == 0) output.AppendLine("No recorded activity yet.");
        stats.Text = output.ToString();
    }
}
class App {
    [STAThread] static void Main(string[] args) {
        if (args.Length == 2 && args[0] == "--check") {
            var score = SaveReader.Read(args[1]);
            if (score == null) { Environment.ExitCode = 2; return; }
            Console.WriteLine("{0} | {1} | {2}", score.Name, score.Faction, score.Records.Count);
            foreach (var row in score.Records) Console.WriteLine("{0} | {1} | {2}", row.Id, row.Zone, row.Time);
            return;
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Window());
    }
}



