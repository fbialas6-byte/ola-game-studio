using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

internal sealed class OlaUpdaterForm : Form
{
    private const string AppName = "OLA Game Studio";
    private const string AppId = "OlaGameStudio";
    private const string ExeName = "OlaGameStudio.exe";
    private static readonly string GitHubOwner = "fbialas6-byte";
    private const string GitHubRepo = "ola-game-studio";
    private readonly string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppId);
    private readonly Label state = new Label();
    private readonly ProgressBar progress = new ProgressBar();
    private readonly Button update = new Button();

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new OlaUpdaterForm());
    }

    private OlaUpdaterForm()
    {
        Text = "Instalator OLA Game Studio";
        ClientSize = new Size(646, 460);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        var banner = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(91, 45, 190) };
        banner.Controls.Add(new Label { Dock = DockStyle.Fill, Text = AppName, ForeColor = Color.White, Font = new Font("Segoe UI", 19F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter });
        Controls.Add(banner);

        var logo = new Panel { Location = new Point(21, 243), Size = new Size(124, 124), BackColor = Color.FromArgb(91, 45, 190) };
        logo.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "OLA\r\nGAME STUDIO", ForeColor = Color.White, Font = new Font("Segoe UI", 13F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter });
        Controls.Add(logo);

        var info = new Panel { Location = new Point(155, 91), Size = new Size(478, 278), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        info.Controls.Add(new Label { Location = new Point(10, 5), Size = new Size(440, 38), Text = "OLA Game Studio", Font = new Font("Segoe UI", 16F), ForeColor = Color.FromArgb(75, 75, 75) });
        info.Controls.Add(new Label { Location = new Point(11, 52), Size = new Size(445, 104), Text = "Instaluje lub aktualizuje OLA Game Studio.\r\n\r\nTwoje konto, projekty .ola i biblioteka pozostana zachowane. Program bedzie zainstalowany tylko dla tego konta Windows.", Font = new Font("Segoe UI", 10F), ForeColor = Color.Black });
        state.Location = new Point(11, 171);
        state.Size = new Size(445, 24);
        state.Text = "Gotowy do instalacji lub aktualizacji.";
        progress.Location = new Point(11, 205);
        progress.Size = new Size(445, 20);
        progress.Style = ProgressBarStyle.Continuous;
        info.Controls.Add(state);
        info.Controls.Add(progress);
        Controls.Add(info);

        update.Text = "Instaluj / aktualizuj";
        update.Location = new Point(388, 414);
        update.Size = new Size(126, 30);
        update.Click += delegate { InstallOrUpdate(); };
        Controls.Add(update);

        var close = new Button { Text = "Zamknij", Location = new Point(518, 414), Size = new Size(98, 30), DialogResult = DialogResult.Cancel };
        Controls.Add(close);
        CancelButton = close;
    }

    private void InstallOrUpdate()
    {
        update.Enabled = false;
        try
        {
            Process[] running = Process.GetProcessesByName("OlaGameStudio");
            if (running.Length > 0)
            {
                DialogResult answer = MessageBox.Show(this, "OLA Game Studio jest uruchomione. Zamknij program przed aktualizacja i sprobuj ponownie.", AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (answer != DialogResult.OK) { update.Enabled = true; return; }
                foreach (Process p in running) { try { p.CloseMainWindow(); p.WaitForExit(5000); } catch { } }
                foreach (Process p in running) p.Dispose();
                running = Process.GetProcessesByName("OlaGameStudio");
                if (running.Length > 0) throw new InvalidOperationException("Nie udalo sie zamknac OLA Game Studio. Zamknij je recznie i uruchom aktualizator ponownie.");
            }

            Directory.CreateDirectory(installDir);
            state.Text = "Pobieranie najnowszej wersji z GitHub...";
            progress.Value = 25;
            string targetExe = Path.Combine(installDir, ExeName);
            string stagedExe = targetExe + ".new";
            string releaseTag = DownloadLatestApp(stagedExe);
            foreach (string oldExe in Directory.GetFiles(installDir, "OlaGameStudio*.exe"))
                if (!String.Equals(oldExe, stagedExe, StringComparison.OrdinalIgnoreCase)) File.Delete(oldExe);
            File.Move(stagedExe, targetExe);
            progress.Value = 65;
            state.Text = "Aktualizowanie skrotow...";
            CreateShortcuts();
            progress.Value = 85;
            state.Text = "Zapisywanie informacji o instalacji...";
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + AppId))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", releaseTag.TrimStart('v', 'V'));
                key.SetValue("Publisher", "OLA Game Studio");
                key.SetValue("InstallLocation", installDir);
                key.SetValue("DisplayIcon", Path.Combine(installDir, ExeName));
                key.SetValue("UninstallString", "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(installDir, "uninstall.ps1") + "\"");
            }
            progress.Value = 100;
            state.Text = "Instalacja lub aktualizacja zakonczona.";
            MessageBox.Show(this, "OLA Game Studio jest gotowe. Skrot znajduje sie na pulpicie i w menu Start.\r\n\r\nTwoje konta i projekty zostaly zachowane.", "Gotowe", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            state.Text = "Instalacja lub aktualizacja nie powiodla sie.";
            MessageBox.Show(this, ex.Message, "Blad aktualizacji", MessageBoxButtons.OK, MessageBoxIcon.Error);
            update.Enabled = true;
        }
    }

    private static string DownloadLatestApp(string target)
    {
        if (GitHubOwner == "PENDING_OWNER") throw new InvalidOperationException("Brak skonfigurowanego wlasciciela repozytorium GitHub.");
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        string api = "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";
        string release;
        using (var client = new WebClient())
        {
            client.Headers[HttpRequestHeader.UserAgent] = "OlaGameStudioUpdater/1.0";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            release = client.DownloadString(api);
        }
        string downloadUrl = FindAssetDownloadUrl(release, ExeName);
        if (String.IsNullOrEmpty(downloadUrl)) throw new InvalidOperationException("Najnowsze wydanie GitHub nie zawiera pliku " + ExeName + ".");
        string tag = JsonString(release, "tag_name");
        if (String.IsNullOrEmpty(downloadUrl) || !downloadUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Niepoprawny adres pliku w GitHub Release.");
        using (var client = new WebClient())
        {
            client.Headers[HttpRequestHeader.UserAgent] = "OlaGameStudioUpdater/1.0";
            client.DownloadFile(downloadUrl, target);
        }
        if (!File.Exists(target) || new FileInfo(target).Length < 10000)
        {
            try { File.Delete(target); } catch { }
            throw new InvalidOperationException("Pobrany plik programu jest niekompletny.");
        }
        return String.IsNullOrEmpty(tag) ? "1.0.0" : tag;
    }

    private static string JsonString(string json, string name)
    {
        Match match = System.Text.RegularExpressions.Regex.Match(json, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string FindAssetDownloadUrl(string json, string expectedName)
    {
        int assets = json.IndexOf("\"assets\"", StringComparison.Ordinal);
        if (assets < 0) return null;
        int index = json.IndexOf('[', assets);
        if (index < 0) return null;
        index++;

        while (index < json.Length)
        {
            while (index < json.Length && (Char.IsWhiteSpace(json[index]) || json[index] == ',')) index++;
            if (index >= json.Length || json[index] == ']') return null;
            if (json[index] != '{') return null;

            int start = index;
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (; index < json.Length; index++)
            {
                char c = json[index];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                }
                else if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                {
                    string asset = json.Substring(start, index - start + 1);
                    index++;
                    if (String.Equals(JsonString(asset, "name"), expectedName, StringComparison.OrdinalIgnoreCase))
                        return JsonString(asset, "browser_download_url");
                    break;
                }
            }
            if (depth != 0) return null;
        }
        return null;
    }

    private void CreateShortcuts()
    {
        string exe = Path.Combine(installDir, ExeName);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", AppName);
        Directory.CreateDirectory(start);
        MakeShortcut(Path.Combine(desktop, AppName + ".lnk"), exe);
        MakeShortcut(Path.Combine(start, AppName + ".lnk"), exe);
        File.WriteAllText(Path.Combine(installDir, "uninstall.ps1"), Uninstaller, System.Text.Encoding.UTF8);
        MakeShortcut(Path.Combine(start, "Odinstaluj.lnk"), "powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(installDir, "uninstall.ps1") + "\"");
    }

    private static void MakeShortcut(string path, string target, string arguments = "")
    {
        var shell = (IShellLink)new ShellLink();
        shell.SetPath(target);
        shell.SetArguments(arguments);
        shell.SetWorkingDirectory(Path.GetDirectoryName(target) ?? Environment.CurrentDirectory);
        ((IPersistFile)shell).Save(path, false);
        Marshal.ReleaseComObject(shell);
    }

    private const string Uninstaller = "$ErrorActionPreference='SilentlyContinue'\n$app='OlaGameStudio'\n$name='OLA Game Studio'\n$dir=Join-Path $env:LOCALAPPDATA ('Programs\\'+$app)\n$desktop=[Environment]::GetFolderPath('DesktopDirectory')\n$start=Join-Path ([Environment]::GetFolderPath('StartMenu')) ('Programs\\'+$name)\nRemove-Item -LiteralPath (Join-Path $desktop ($name+'.lnk')) -Force\nRemove-Item -LiteralPath $start -Recurse -Force\nRemove-Item -LiteralPath ('HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\'+$app) -Recurse -Force\nGet-Process -Name $app | Stop-Process -Force\nRemove-Item -LiteralPath $dir -Recurse -Force\n[System.Windows.Forms.MessageBox]::Show('Odinstalowano OLA Game Studio. Dane kont i projektow pozostaly zachowane.','Odinstalowano') | Out-Null\n";

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink
    {
        void GetPath(IntPtr file, int max, IntPtr find, int flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr name, int max);
        void SetDescription(string name);
        void GetWorkingDirectory(IntPtr dir, int max);
        void SetWorkingDirectory(string dir);
        void GetArguments(IntPtr args, int max);
        void SetArguments(string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int show);
        void SetShowCmd(int show);
        void GetIconLocation(IntPtr icon, int max, out int index);
        void SetIconLocation(string icon, int index);
        void SetRelativePath(string path, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath(string path);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }
}
