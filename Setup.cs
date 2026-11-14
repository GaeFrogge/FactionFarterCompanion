using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
[assembly:System.Reflection.AssemblyTitle("Faction Farter Companion Setup")]
[assembly:System.Reflection.AssemblyVersion("0.3.0.0")]

class Setup {
    const string Name = "Faction Farter Companion";
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FactionFarterCompanion";
    static string Shortcut(Environment.SpecialFolder folder) {
        return Path.Combine(Environment.GetFolderPath(folder), Name + ".url");
    }
    static void Link(Environment.SpecialFolder folder, string app) {
        File.WriteAllText(Shortcut(folder), "[InternetShortcut]\r\nURL=" + new Uri(app).AbsoluteUri +
            "\r\nIconFile=" + app + "\r\nIconIndex=0\r\n");
    }
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name);
        using (var key = Registry.CurrentUser.OpenSubKey(Key)) {
            if (key != null) {
                string previous = key.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(previous)) {
                    string command = key.GetValue("UninstallString") as string;
                    if (!string.IsNullOrEmpty(command) && command.EndsWith("Maintenance.exe\"", StringComparison.OrdinalIgnoreCase))
                        previous = Path.GetDirectoryName(command.Trim('"'));
                }
                if (!string.IsNullOrEmpty(previous)) folder = previous;
            }
        }
        try {
            if (Array.IndexOf(args, "/uninstall") >= 0) {
                if (MessageBox.Show("Remove the companion? Your WoW saves and companion preferences will be kept.", Name,
                    MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                File.Delete(Path.Combine(folder, "FactionFarterCompanion.exe"));
                File.Delete(Shortcut(Environment.SpecialFolder.Programs));
                File.Delete(Shortcut(Environment.SpecialFolder.DesktopDirectory));
                Registry.CurrentUser.DeleteSubKeyTree(Key, false);
                MessageBox.Show("Companion removed.", Name);
                return;
            }
            using (var form = new Form { Text = Name + " Setup", ClientSize = new Size(570, 235),
                StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false }) {
                form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                var title = new Label { Text = "Install Faction Farter Companion", Left = 20, Top = 20, AutoSize = true, Font = new Font("Segoe UI", 15) };
                var detail = new Label { Text = "Choose an installation folder. A Start menu shortcut is included.", Left = 22, Top = 62, AutoSize = true };
                var path = new TextBox { Text = folder, Left = 22, Top = 90, Width = 438 };
                var browse = new Button { Text = "Browse...", Left = 468, Top = 88, Width = 80 };
                var desktop = new CheckBox { Text = "Create a desktop shortcut", Left = 22, Top = 128, AutoSize = true, Checked = File.Exists(Shortcut(Environment.SpecialFolder.DesktopDirectory)) };
                var install = new Button { Text = "Install", Left = 374, Top = 181, Width = 80 };
                var cancel = new Button { Text = "Cancel", Left = 468, Top = 181, Width = 80, DialogResult = DialogResult.Cancel };
                browse.Click += delegate {
                    using (var picker = new FolderBrowserDialog { SelectedPath = path.Text })
                        if (picker.ShowDialog(form) == DialogResult.OK) path.Text = picker.SelectedPath;
                };
                install.Click += delegate {
                    try {
                        if (!Path.IsPathRooted(path.Text.Trim())) throw new IOException("Choose a full installation path.");
                        string location = Path.GetFullPath(path.Text.Trim());
                        string app = Path.Combine(location, "FactionFarterCompanion.exe");
                        string maintenance = Path.Combine(location, "Maintenance.exe");
                        Directory.CreateDirectory(location);
                        using (var payload = typeof(Setup).Assembly.GetManifestResourceStream("Companion"))
                        using (var file = File.Create(app)) payload.CopyTo(file);
                        if (!string.Equals(Application.ExecutablePath, maintenance, StringComparison.OrdinalIgnoreCase))
                            File.Copy(Application.ExecutablePath, maintenance, true);
                        Link(Environment.SpecialFolder.Programs, app);
                        if (desktop.Checked) Link(Environment.SpecialFolder.DesktopDirectory, app);
                        else File.Delete(Shortcut(Environment.SpecialFolder.DesktopDirectory));
                        using (var entry = Registry.CurrentUser.CreateSubKey(Key)) {
                            entry.SetValue("DisplayName", Name);
                            entry.SetValue("DisplayVersion", "0.3");
                            entry.SetValue("InstallLocation", location);
                            entry.SetValue("DisplayIcon", app);
                            entry.SetValue("UninstallString", "\"" + maintenance + "\" /uninstall");
                        }
                        form.Controls.Clear();
                        title.Text = "Installation complete";
                        detail.Text = "Find Faction Farter Companion in your Start menu.";
                        var launch = new CheckBox { Text = "Launch Faction Farter Companion", Left = 22, Top = 112, AutoSize = true };
                        var finish = new Button { Text = "Finish", Left = 468, Top = 181, Width = 80 };
                        finish.Click += delegate {
                            try { if (launch.Checked) Process.Start(app); form.Close(); }
                            catch (Exception ex) { MessageBox.Show(form, ex.Message, Name); }
                        };
                        form.Controls.AddRange(new Control[] { title, detail, launch, finish });
                        form.AcceptButton = finish;
                    } catch (Exception ex) { MessageBox.Show(form, ex.Message, Name); }
                };
                form.Controls.AddRange(new Control[] { title, detail, path, browse, desktop, install, cancel });
                form.AcceptButton = install;
                form.CancelButton = cancel;
                form.ShowDialog();
            }
        } catch (Exception ex) { MessageBox.Show(ex.Message, Name); }
    }
}

