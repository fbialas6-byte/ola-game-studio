using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        LocalAccounts.EnsureDefaultAccount();
        while (true)
        {
            if (!LocalAccounts.TryRestoreSession())
            {
                using (var login = new LoginForm())
                    if (login.ShowDialog() != DialogResult.OK) return;
            }
            using (var studio = new StudioForm()) Application.Run(studio);
            if (!StudioForm.LogoutRequested) return;
            LocalAccounts.ClearRememberedSession();
        }
    }
}

internal static class LocalAccounts
{
    private const int Iterations = 80000;
    public static string CurrentUsername { get; private set; }
    private static string RememberedPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OlaGameStudio", "remember.cookie"); }
    }
    private static string SessionPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OlaGameStudio", "remember.session"); }
    }
    private static string AccountsPath
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OlaGameStudio");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "accounts.txt");
        }
    }

    private static Dictionary<string, string[]> ReadAll()
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(AccountsPath)) return result;
        foreach (string line in File.ReadAllLines(AccountsPath, Encoding.UTF8))
        {
            string[] parts = line.Split('|');
            if (parts.Length == 3 && Regex.IsMatch(parts[0], "^[a-z0-9_]{3,20}$")) result[parts[0]] = new[] { parts[1], parts[2] };
        }
        return result;
    }

    public static void EnsureDefaultAccount()
    {
        var accounts = ReadAll();
        if (accounts.ContainsKey("admin")) return;
        byte[] salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
        byte[] hash;
        using (var derive = new Rfc2898DeriveBytes("admin", salt, Iterations)) hash = derive.GetBytes(32);
        accounts.Add("admin", new[] { Convert.ToBase64String(salt), Convert.ToBase64String(hash) });
        File.WriteAllLines(AccountsPath, accounts.Select(kv => kv.Key + "|" + kv.Value[0] + "|" + kv.Value[1]), Encoding.UTF8);
    }

    public static bool Create(string username, string password, out string message)
    {
        username = (username ?? "").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(username, "^[a-z0-9_]{3,20}$")) { message = "Nazwa musi miec 3-20 znakow: male litery, cyfry lub _."; return false; }
        if (password == null || password.Length < 6) { message = "Haslo musi miec co najmniej 6 znakow."; return false; }
        var accounts = ReadAll();
        if (accounts.ContainsKey(username)) { message = "To konto juz istnieje."; return false; }
        byte[] salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
        byte[] hash;
        using (var derive = new Rfc2898DeriveBytes(password, salt, Iterations)) hash = derive.GetBytes(32);
        accounts.Add(username, new[] { Convert.ToBase64String(salt), Convert.ToBase64String(hash) });
        File.WriteAllLines(AccountsPath, accounts.Select(kv => kv.Key + "|" + kv.Value[0] + "|" + kv.Value[1]), Encoding.UTF8);
        message = "Konto utworzone. Mozesz sie zalogowac.";
        return true;
    }

    public static bool Verify(string username, string password)
    {
        username = (username ?? "").Trim().ToLowerInvariant();
        if (password == null) return false;
        string[] entry;
        if (!ReadAll().TryGetValue(username, out entry)) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(entry[0]);
            byte[] expected = Convert.FromBase64String(entry[1]);
            byte[] actual;
            using (var derive = new Rfc2898DeriveBytes(password, salt, Iterations)) actual = derive.GetBytes(expected.Length);
            if (actual.Length != expected.Length) return false;
            int diff = 0;
            for (int i = 0; i < actual.Length; i++) diff |= actual[i] ^ expected[i];
            bool valid = diff == 0;
            if (valid) CurrentUsername = username;
            return valid;
        }
        catch { return false; }
    }

    public static void RememberCurrentUser()
    {
        if (String.IsNullOrEmpty(CurrentUsername)) return;
        string token = CreateSessionToken();
        string expires = DateTime.UtcNow.AddDays(30).Ticks.ToString();
        string tokenHash;
        using (var sha = SHA256.Create()) tokenHash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
        Directory.CreateDirectory(Path.GetDirectoryName(RememberedPath));
        File.WriteAllText(RememberedPath, token, Encoding.ASCII);
        File.WriteAllText(SessionPath, CurrentUsername + "|" + expires + "|" + tokenHash, Encoding.UTF8);
    }

    private static string CreateSessionToken()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var token = new StringBuilder(24);
        byte[] random = new byte[48];
        using (var rng = RandomNumberGenerator.Create())
        {
            while (token.Length < 24)
            {
                rng.GetBytes(random);
                foreach (byte b in random)
                {
                    if (b >= 248) continue;
                    token.Append(chars[b % chars.Length]);
                    if (token.Length == 24) break;
                }
            }
        }
        return token.ToString();
    }

    public static void ClearRememberedSession()
    {
        ClearRememberedFiles();
        CurrentUsername = null;
    }

    public static void ClearRememberedFiles()
    {
        try { if (File.Exists(RememberedPath)) File.Delete(RememberedPath); } catch { }
        try { if (File.Exists(SessionPath)) File.Delete(SessionPath); } catch { }
    }

    public static void SignInGoogle(string subject)
    {
        byte[] digest;
        using (var sha = SHA256.Create()) digest = sha.ComputeHash(Encoding.UTF8.GetBytes(subject));
        string username = "google_" + BitConverter.ToString(digest, 0, 12).Replace("-", "").ToLowerInvariant();
        var accounts = ReadAll();
        if (!accounts.ContainsKey(username))
        {
            byte[] salt = new byte[16];
            byte[] randomPassword = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) { rng.GetBytes(salt); rng.GetBytes(randomPassword); }
            byte[] hash;
            using (var derive = new Rfc2898DeriveBytes(Convert.ToBase64String(randomPassword), salt, Iterations)) hash = derive.GetBytes(32);
            accounts.Add(username, new[] { Convert.ToBase64String(salt), Convert.ToBase64String(hash) });
            File.WriteAllLines(AccountsPath, accounts.Select(kv => kv.Key + "|" + kv.Value[0] + "|" + kv.Value[1]), Encoding.UTF8);
        }
        CurrentUsername = username;
    }

    public static bool TryRestoreSession()
    {
        try
        {
            if (!File.Exists(RememberedPath) || !File.Exists(SessionPath)) return false;
            string token = File.ReadAllText(RememberedPath, Encoding.ASCII).Trim();
            string[] session = File.ReadAllText(SessionPath, Encoding.UTF8).Split('|');
            if (session.Length != 3 || token.Length != 24) { ClearRememberedSession(); return false; }
            string username = session[0];
            long expires = Int64.Parse(session[1]);
            string tokenHash;
            using (var sha = SHA256.Create()) tokenHash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
            if (!String.Equals(tokenHash, session[2], StringComparison.Ordinal)) { ClearRememberedSession(); return false; }
            if (DateTime.UtcNow.Ticks > expires || !ReadAll().ContainsKey(username)) { ClearRememberedSession(); return false; }
            CurrentUsername = username;
            return true;
        }
        catch { ClearRememberedSession(); return false; }
    }

    public static string UserFolder
    {
        get
        {
            string name = String.IsNullOrEmpty(CurrentUsername) ? "guest" : CurrentUsername;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OlaGameStudio", "Users", name);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}

internal sealed class LoginForm : Form
{
    private readonly TextBox username = new TextBox();
    private readonly TextBox password = new TextBox();
    private readonly Label info = new Label();
    private readonly CheckBox remember = new CheckBox();

    public LoginForm()
    {
        Text = "Logowanie - OLA Game Studio";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 315);
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(239, 242, 246);
        Font = new Font("Segoe UI", 9F);

        var heading = new Label { Text = "OLA Game Studio", Font = new Font("Segoe UI", 16F, FontStyle.Bold), Left = 22, Top = 18, Width = 340, Height = 30 };
        var userLabel = new Label { Text = "Nazwa uzytkownika", Left = 24, Top = 65, Width = 140 };
        username.SetBounds(165, 62, 200, 25);
        var passLabel = new Label { Text = "Haslo", Left = 24, Top = 103, Width = 140 };
        password.SetBounds(165, 100, 200, 25);
        password.UseSystemPasswordChar = true;
        remember.Text = "Zapamietaj logowanie na 30 dni";
        remember.SetBounds(24, 137, 300, 24);
        var login = new Button { Text = "Zaloguj", Left = 165, Top = 166, Width = 95, Height = 32 };
        var create = new Button { Text = "Utworz konto", Left = 270, Top = 166, Width = 95, Height = 32 };
        var google = new Button { Text = "Zaloguj przez Google", Left = 165, Top = 205, Width = 200, Height = 32 };
        info.SetBounds(24, 244, 380, 60);
        info.ForeColor = Color.FromArgb(65, 72, 82);
        info.Text = "Nie masz konta? Wybierz Utworz konto.";
        login.Click += delegate { TryLogin(); };
        create.Click += delegate { CreateAccount(); };
        google.Click += delegate { TryGoogleLogin(); };
        AcceptButton = login;
        Controls.AddRange(new Control[] { heading, userLabel, username, passLabel, password, remember, login, create, google, info });
    }

    private void TryLogin()
    {
        if (LocalAccounts.Verify(username.Text, password.Text))
        {
            if (remember.Checked) LocalAccounts.RememberCurrentUser(); else LocalAccounts.ClearRememberedFiles();
            DialogResult = DialogResult.OK;
        }
        else info.Text = "Niepoprawna nazwa lub haslo.";
    }

    private void CreateAccount()
    {
        string message;
        try
        {
            if (LocalAccounts.Create(username.Text, password.Text, out message))
            {
                info.Text = message;
                password.Clear();
            }
            else info.Text = message;
        }
        catch (Exception ex) { info.Text = "Nie udalo sie utworzyc konta: " + ex.Message; }
    }

    private void TryGoogleLogin()
    {
        info.Text = "Otwieram logowanie Google w przegladarce...";
        info.Refresh();
        try
        {
            GoogleProfile profile = GoogleOAuth.SignIn();
            LocalAccounts.SignInGoogle(profile.Subject);
            if (remember.Checked) LocalAccounts.RememberCurrentUser(); else LocalAccounts.ClearRememberedFiles();
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            info.Text = "Logowanie Google nie powiodlo sie.";
            MessageBox.Show(this, ex.Message, "Logowanie przez Google", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class GoogleProfile
{
    public string Email;
    public string Subject;
}

internal static class GoogleOAuth
{
    private const string ClientId = "433050553116-bpv2k93ans9uaaojr0cqga2nbok0akn6.apps.googleusercontent.com";
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

    public static GoogleProfile SignIn()
    {
        string clientId = ClientId;
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        string verifier = RandomUrlToken(48);
        string state = RandomUrlToken(32);
        string challenge;
        using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = "http://127.0.0.1:" + port + "/";
            string url = AuthEndpoint + "?client_id=" + Enc(clientId) + "&redirect_uri=" + Enc(redirect) +
                "&response_type=code&scope=" + Enc("openid email profile") + "&state=" + Enc(state) +
                "&code_challenge=" + Enc(challenge) + "&code_challenge_method=S256";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            string code = null, returnedState = null, oauthError = null;
            var timeout = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < timeout && code == null && oauthError == null)
            {
                Application.DoEvents();
                if (!listener.Pending()) { System.Threading.Thread.Sleep(100); continue; }
                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII))
                {
                    string first = reader.ReadLine() ?? "";
                    string[] request = first.Split(' ');
                    string target = request.Length > 1 ? request[1] : "/";
                    string line;
                    while (!String.IsNullOrEmpty(line = reader.ReadLine())) { }
                    Uri callback = new Uri("http://127.0.0.1:" + port + target);
                    Dictionary<string, string> values = ParseQuery(callback.Query);
                    values.TryGetValue("code", out code);
                    values.TryGetValue("state", out returnedState);
                    values.TryGetValue("error", out oauthError);
                    byte[] body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>OLA Game Studio</title><p>Logowanie Google zakonczone. Wroc do OLA Game Studio.</p>");
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    stream.Write(body, 0, body.Length);
                }
            }
            if (oauthError != null) throw new InvalidOperationException("Google nie zaakceptowal logowania: " + oauthError);
            if (code == null) throw new TimeoutException("Nie otrzymalem odpowiedzi z Google w ciagu 3 minut.");
            if (!String.Equals(state, returnedState, StringComparison.Ordinal)) throw new InvalidOperationException("Niepoprawny token state odpowiedzi Google.");

            string tokenResponse = PostForm(TokenEndpoint, new Dictionary<string, string> {
                { "client_id", clientId }, { "code", code }, { "code_verifier", verifier },
                { "grant_type", "authorization_code" }, { "redirect_uri", redirect }
            });
            string accessToken = JsonString(tokenResponse, "access_token");
            if (String.IsNullOrEmpty(accessToken)) throw new InvalidOperationException("Google nie zwrocilo tokenu logowania.");
            string userInfo = GetUserInfo(accessToken);
            string email = JsonString(userInfo, "email");
            string subject = JsonString(userInfo, "sub");
            if (String.IsNullOrEmpty(email) || String.IsNullOrEmpty(subject) || !Regex.IsMatch(userInfo, "\\\"email_verified\\\"\\s*:\\s*true"))
                throw new InvalidOperationException("Nie udalo sie potwierdzic adresu e-mail na koncie Google.");
            return new GoogleProfile { Email = email, Subject = subject };
        }
        finally { listener.Stop(); }
    }

    private static string PostForm(string endpoint, Dictionary<string, string> values)
    {
        string form = String.Join("&", values.Select(x => Enc(x.Key) + "=" + Enc(x.Value)));
        byte[] data = Encoding.UTF8.GetBytes(form);
        var request = (HttpWebRequest)WebRequest.Create(endpoint);
        request.Method = "POST"; request.ContentType = "application/x-www-form-urlencoded"; request.ContentLength = data.Length;
        request.Timeout = 20000;
        using (Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
        try
        {
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }
        catch (WebException ex) { throw new InvalidOperationException(DescribeGoogleError(ex), ex); }
    }

    private static string GetUserInfo(string accessToken)
    {
        var request = (HttpWebRequest)WebRequest.Create(UserInfoEndpoint);
        request.Method = "GET"; request.Timeout = 20000;
        request.Headers[HttpRequestHeader.Authorization] = "Bearer " + accessToken;
        try
        {
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }
        catch (WebException ex) { throw new InvalidOperationException(DescribeGoogleError(ex), ex); }
    }

    private static string DescribeGoogleError(WebException ex)
    {
        string body = "";
        try
        {
            if (ex.Response != null)
                using (var reader = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8)) body = reader.ReadToEnd();
        }
        catch { }
        string error = JsonString(body, "error");
        string description = JsonString(body, "error_description");
        string detail = String.IsNullOrEmpty(description) ? error : error + ": " + description;
        if (String.IsNullOrEmpty(detail)) detail = ex.Message;
        return "Google OAuth zwrocil blad: " + detail;
    }

    private static string JsonString(string json, string name)
    {
        Match match = Regex.Match(json, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"");
        return match.Success ? Regex.Unescape(match.Groups[1].Value) : null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>();
        foreach (string part in query.TrimStart('?').Split('&'))
        {
            int equals = part.IndexOf('=');
            if (equals < 0) continue;
            string key = Uri.UnescapeDataString(part.Substring(0, equals).Replace("+", " "));
            string value = Uri.UnescapeDataString(part.Substring(equals + 1).Replace("+", " "));
            result[key] = value;
        }
        return result;
    }

    private static string RandomUrlToken(int byteCount)
    {
        byte[] bytes = new byte[byteCount];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return Base64Url(bytes);
    }
    private static string Base64Url(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
    private static string Enc(string value) { return Uri.EscapeDataString(value); }
}

internal sealed class StudioForm : Form
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private static bool IsKeyDown(Keys key) { return (GetAsyncKeyState((int)key) & 0x8000) != 0; }
    public static bool LogoutRequested { get; private set; }
    private const int GridW = 24;
    private const int GridH = 14;
    private readonly int[,] map = new int[GridH, GridW];
    private readonly Board board;
    private readonly TextBox titleBox = new TextBox();
    private readonly TextBox urlBox = new TextBox();
    private readonly ComboBox tilePicker = new ComboBox();
    private readonly Label status = new Label();
    private readonly ListBox library = new ListBox();
    private readonly List<string> libraryItems = new List<string>();
    private readonly Timer gameTimer = new Timer();
    private bool playing;
    private bool leftHeld, rightHeld, jumpQueued;
    private bool jumpWasDown;
    private float playerX = 1, playerY = 1, velocityY;
    private bool grounded;
    private int lastJumpAt = -1000;
    private int startX = 1, startY = 1;
    private string projectPath;

    public StudioForm()
    {
        LogoutRequested = false;
        Text = "OLA Game Studio";
        MinimumSize = new Size(900, 660);
        Size = new Size(1100, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(239, 242, 246);
        Font = new Font("Segoe UI", 9F);
        KeyPreview = true;
        KeyDown += HandleGameKeyDown;
        KeyUp += HandleGameKeyUp;
        gameTimer.Interval = 40;
        gameTimer.Tick += StepGame;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var editorTab = new TabPage("Kreator .OLA") { BackColor = BackColor };
        var libraryTab = new TabPage("Biblioteka gier") { BackColor = BackColor };
        tabs.TabPages.Add(editorTab);
        tabs.TabPages.Add(libraryTab);
        Controls.Add(tabs);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        editorTab.Controls.Add(editor);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        AddButton(actions, "Nowa", delegate { NewProject(); });
        AddButton(actions, "Otworz .ola", delegate { OpenProject(); });
        AddButton(actions, "Zapisz .ola", delegate { SaveProject(); });
        AddButton(actions, "Graj / edytuj", delegate { TogglePlay(); });
        AddButton(actions, "Restart poziomu", delegate { RestartLevel(); });
        AddButton(actions, "Wyloguj", delegate { LogoutRequested = true; Close(); });
        editor.Controls.Add(actions, 0, 0);

        var meta = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(0, 4, 0, 4) };
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        meta.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        meta.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        meta.Controls.Add(MetaLabel("Nazwa gry"), 0, 0);
        titleBox.Dock = DockStyle.Fill;
        titleBox.Text = "Moja gra";
        meta.Controls.Add(titleBox, 1, 0);
        meta.Controls.Add(MetaLabel("Link online"), 2, 0);
        urlBox.Dock = DockStyle.Fill;
        meta.Controls.Add(urlBox, 3, 0);
        meta.Controls.Add(MetaLabel("Kafelek"), 0, 1);
        tilePicker.Dock = DockStyle.Left;
        tilePicker.Width = 180;
        tilePicker.DropDownStyle = ComboBoxStyle.DropDownList;
        tilePicker.Items.AddRange(new object[] { "Puste", "Sciana", "Gracz", "Meta", "Lawa" });
        tilePicker.SelectedIndex = 1;
        meta.Controls.Add(tilePicker, 1, 1);
        var help = new Label { Text = "Klikaj plansze, aby budowac poziom. Skok: spacja. Lawa resetuje poziom.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
        meta.Controls.Add(help, 2, 1);
        meta.SetColumnSpan(help, 2);
        editor.Controls.Add(meta, 0, 1);

        board = new Board(map) { Dock = DockStyle.Fill, Margin = new Padding(4), BackColor = Color.FromArgb(25, 29, 38) };
        board.CellClicked += OnCellClick;
        editor.Controls.Add(board, 0, 2);
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Text = "Gotowe. Projekt .ola dziala lokalnie; link online otworzysz z biblioteki.";
        editor.Controls.Add(status, 0, 3);

        BuildLibrary(libraryTab);
        LoadLibrary();
        NewProject();
    }

    private void BuildLibrary(Control parent)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        parent.Controls.Add(root);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddButton(bar, "Dodaj plik gry", AddGameFile);
        AddButton(bar, "Dodaj link Roblox / online", AddOnlineGame);
        AddButton(bar, "Usun z biblioteki", RemoveGame);
        AddButton(bar, "Uruchom", LaunchSelected);
        root.Controls.Add(bar, 0, 0);
        library.Dock = DockStyle.Fill;
        library.Font = new Font("Segoe UI", 11F);
        library.DoubleClick += delegate { LaunchSelected(); };
        root.Controls.Add(library, 0, 1);
        var note = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray, Text = "Obsluga: .ola, aplikacje .exe, projekty Roblox Studio .rbxl/.rbxlx oraz adresy stron gier." };
        root.Controls.Add(note, 0, 2);
    }

    private static Label MetaLabel(string text)
    {
        return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(65, 72, 82) };
    }

    private static void AddButton(FlowLayoutPanel panel, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34, Margin = new Padding(0, 3, 8, 3), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        button.Click += delegate { action(); };
        panel.Controls.Add(button);
    }

    private void NewProject()
    {
        playing = false;
        projectPath = null;
        for (int y = 0; y < GridH; y++)
            for (int x = 0; x < GridW; x++) map[y, x] = 0;
        for (int x = 0; x < GridW; x++) map[GridH - 1, x] = 1;
        map[GridH - 2, 1] = 2;
        map[GridH - 2, GridW - 2] = 3;
        titleBox.Text = "Moja gra";
        urlBox.Text = "";
        startX = 1; startY = GridH - 2;
        playerX = startX; playerY = startY;
        board.Invalidate();
        status.Text = "Nowy projekt. Zmien kafelki, potem zapisz jako .ola.";
    }

    private void OnCellClick(int x, int y)
    {
        if (playing) return;
        int tile = tilePicker.SelectedIndex;
        if (tile == 2 || tile == 3)
        {
            for (int yy = 0; yy < GridH; yy++)
                for (int xx = 0; xx < GridW; xx++)
                    if (map[yy, xx] == tile) map[yy, xx] = 0;
        }
        map[y, x] = tile;
        board.Invalidate();
    }

    private void TogglePlay()
    {
        playing = !playing;
        if (playing)
        {
            var p = FindTile(2);
            if (p == null) { playing = false; MessageBox.Show(this, "Dodaj kafelek Gracz na planszy."); return; }
            startX = p[0]; startY = p[1];
            RestartLevel();
            status.Text = "Tryb gry: lewo/prawo i spacja (skok co 1 sek.). Esc wylacza gre.";
            gameTimer.Start();
            Activate();
            board.Select();
            board.Focus();
        }
        else { gameTimer.Stop(); status.Text = "Tryb edycji."; }
        board.Playing = playing;
        board.PlayerPosition = new PointF(playerX, playerY);
        board.Invalidate();
    }

    private void RestartLevel()
    {
        int[] p = FindTile(2);
        if (p != null) { startX = p[0]; startY = p[1]; }
        playerX = startX;
        playerY = startY;
        velocityY = 0;
        grounded = false;
        jumpQueued = false;
        jumpWasDown = false;
        leftHeld = rightHeld = false;
        lastJumpAt = unchecked(Environment.TickCount - 1000);
        board.PlayerPosition = new PointF(playerX, playerY);
        board.Invalidate();
        if (playing) status.Text = "Poziom zrestartowany. Skok spacją, maksymalnie raz na sekunde.";
    }

    private void HandleGameKeyDown(object sender, KeyEventArgs e)
    {
        if (!playing) return;
        if (e.KeyCode == Keys.Space) jumpQueued = true;
        if (e.KeyCode == Keys.R) RestartLevel();
        e.Handled = true;
    }

    private void HandleGameKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.A) leftHeld = false;
        if (e.KeyCode == Keys.Right || e.KeyCode == Keys.D) rightHeld = false;
    }

    private void StepGame(object sender, EventArgs e)
    {
        if (!playing) return;
        if (!board.Focused) { leftHeld = rightHeld = false; jumpWasDown = false; return; }
        const float dt = 0.04f;
        leftHeld = IsKeyDown(Keys.Left) || IsKeyDown(Keys.A);
        rightHeld = IsKeyDown(Keys.Right) || IsKeyDown(Keys.D);
        float move = (rightHeld ? 1f : 0f) - (leftHeld ? 1f : 0f);
        MoveHorizontal(move * 5f * dt);

        bool jumpDown = IsKeyDown(Keys.Space) || IsKeyDown(Keys.Up) || IsKeyDown(Keys.W);
        if (jumpDown && !jumpWasDown) jumpQueued = true;
        jumpWasDown = jumpDown;

        int now = Environment.TickCount;
        if (jumpQueued && grounded && unchecked(now - lastJumpAt) >= 1000)
        {
            velocityY = -8.5f;
            grounded = false;
            lastJumpAt = now;
        }
        jumpQueued = false;

        velocityY = Math.Min(velocityY + 20f * dt, 12f);
        MoveVertical(velocityY * dt);
        if (playerY > GridH + 1) { RestartLevel(); return; }

        board.PlayerPosition = new PointF(playerX, playerY);
        board.Invalidate();
        if (TouchesTile(4))
        {
            RestartLevel();
            status.Text = "Dotknales lawy. Poziom zrestartowany.";
            return;
        }
        if (TouchesTile(3)) ShowWin();
    }

    private void MoveHorizontal(float amount)
    {
        float next = Math.Max(0.4f, Math.Min(GridW - 0.6f, playerX + amount));
        float edge = amount > 0 ? next + 0.35f : next - 0.35f;
        int col = (int)Math.Floor(edge);
        int top = (int)Math.Floor(playerY - 0.35f);
        int bottom = (int)Math.Floor(playerY + 0.35f);
        if (IsWall(col, top) || IsWall(col, bottom)) return;
        playerX = next;
    }

    private void MoveVertical(float amount)
    {
        float next = playerY + amount;
        int left = (int)Math.Floor(playerX - 0.3f);
        int right = (int)Math.Floor(playerX + 0.3f);
        grounded = false;
        if (amount > 0)
        {
            int row = (int)Math.Floor(next + 0.4f);
            if (IsWall(left, row) || IsWall(right, row))
            {
                playerY = row - 0.4f;
                velocityY = 0;
                grounded = true;
                return;
            }
        }
        else if (amount < 0)
        {
            int row = (int)Math.Floor(next - 0.4f);
            if (IsWall(left, row) || IsWall(right, row))
            {
                playerY = row + 1.4f;
                velocityY = 0;
                return;
            }
        }
        playerY = next;
    }

    private bool IsWall(int x, int y) { return x < 0 || x >= GridW || y >= GridH || (y >= 0 && map[y, x] == 1); }

    private bool TouchesTile(int tile)
    {
        int left = Math.Max(0, (int)Math.Floor(playerX - 0.3f));
        int right = Math.Min(GridW - 1, (int)Math.Floor(playerX + 0.3f));
        int top = Math.Max(0, (int)Math.Floor(playerY - 0.35f));
        int bottom = Math.Min(GridH - 1, (int)Math.Floor(playerY + 0.35f));
        for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++) if (map[y, x] == tile) return true;
        return false;
    }

    private void ShowWin()
    {
        gameTimer.Stop();
        playing = false;
        board.Playing = false;
        status.Text = "WYGRANA! Dotarles do mety.";
        var result = MessageBox.Show(this, "WYGRANA!\r\nDotarles do mety.", "Poziom ukonczony", MessageBoxButtons.RetryCancel, MessageBoxIcon.Information);
        if (result == DialogResult.Retry) { playing = true; board.Playing = true; RestartLevel(); gameTimer.Start(); board.Focus(); }
        else { status.Text = "Wygrana! Mozesz edytowac plansze albo uruchomic ja ponownie."; board.Invalidate(); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (playing)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { TogglePlay(); return true; }
            if (key == Keys.R) { RestartLevel(); return true; }
            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down ||
                key == Keys.A || key == Keys.D || key == Keys.W || key == Keys.S || key == Keys.Space) return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private int[] FindTile(int tile)
    {
        for (int y = 0; y < GridH; y++) for (int x = 0; x < GridW; x++) if (map[y, x] == tile) return new int[] { x, y };
        return null;
    }

    private void SaveProject()
    {
        string path = projectPath;
        if (String.IsNullOrEmpty(path))
        {
            Directory.CreateDirectory(ProjectsPath);
            using (var dialog = new SaveFileDialog { Filter = "Gra OLA (*.ola)|*.ola", FileName = SafeName(titleBox.Text) + ".ola", InitialDirectory = ProjectsPath })
                if (dialog.ShowDialog(this) != DialogResult.OK) return; else path = dialog.FileName;
        }
        try
        {
            var lines = new List<string> { "OLA1", B64(titleBox.Text), B64(urlBox.Text), GridW.ToString(), GridH.ToString() };
            for (int y = 0; y < GridH; y++)
            {
                var row = new string[GridW];
                for (int x = 0; x < GridW; x++) row[x] = map[y, x].ToString();
                lines.Add(String.Join(",", row));
            }
            File.WriteAllLines(path, lines, Encoding.UTF8);
            projectPath = path;
            AddLibraryItem(path);
            SaveLibrary();
            status.Text = "Zapisano: " + Path.GetFileName(path);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Nie udalo sie zapisac"); }
    }

    private void OpenProject()
    {
        Directory.CreateDirectory(ProjectsPath);
        using (var dialog = new OpenFileDialog { Filter = "Gra OLA (*.ola)|*.ola|Wszystkie pliki (*.*)|*.*", InitialDirectory = ProjectsPath })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string[] lines = File.ReadAllLines(dialog.FileName, Encoding.UTF8);
                if (lines.Length < GridH + 5 || lines[0].Trim('\uFEFF') != "OLA1") throw new InvalidDataException("To nie jest poprawny plik OLA1.");
                int w = Int32.Parse(lines[3]), h = Int32.Parse(lines[4]);
                if (w != GridW || h != GridH) throw new InvalidDataException("Nieobslugiwany rozmiar planszy.");
                var loaded = new int[GridH, GridW];
                for (int y = 0; y < GridH; y++)
                {
                    string[] row = lines[y + 5].Split(',');
                    if (row.Length != GridW) throw new InvalidDataException("Niepoprawny wiersz planszy.");
                    for (int x = 0; x < GridW; x++) { loaded[y, x] = Int32.Parse(row[x]); if (loaded[y, x] < 0 || loaded[y, x] > 4) throw new InvalidDataException("Nieznany kafelek."); }
                }
                Array.Copy(loaded, map, loaded.Length);
                titleBox.Text = UnB64(lines[1]);
                urlBox.Text = UnB64(lines[2]);
                projectPath = dialog.FileName;
                board.Invalidate();
                status.Text = "Otwarto: " + Path.GetFileName(projectPath);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Nie udalo sie otworzyc pliku"); }
        }
    }

    private void AddGameFile()
    {
        using (var d = new OpenFileDialog { Filter = "Gry i projekty (*.ola;*.exe;*.rbxl;*.rbxlx)|*.ola;*.exe;*.rbxl;*.rbxlx|Wszystkie pliki (*.*)|*.*" })
            if (d.ShowDialog(this) == DialogResult.OK) { AddLibraryItem(d.FileName); SaveLibrary(); }
    }

    private void AddOnlineGame()
    {
        using (var form = new LinkForm())
        {
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                Uri uri;
                if (!Uri.TryCreate(form.UrlValue, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) { MessageBox.Show(this, "Wpisz poprawny adres http lub https."); return; }
                AddLibraryItem(form.NameValue + "|" + uri.AbsoluteUri);
                SaveLibrary();
            }
        }
    }

    private void AddLibraryItem(string value)
    {
        if (!libraryItems.Contains(value)) libraryItems.Add(value);
        RefreshLibrary();
    }

    private void RemoveGame()
    {
        int i = library.SelectedIndex;
        if (i < 0) return;
        libraryItems.RemoveAt(i);
        RefreshLibrary();
        SaveLibrary();
    }

    private void LaunchSelected()
    {
        int i = library.SelectedIndex;
        if (i < 0) return;
        string value = libraryItems[i];
        Uri uri;
        try
        {
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Process.Start(value);
            else if (value.Contains("|") && Uri.TryCreate(value.Substring(value.IndexOf('|') + 1), UriKind.Absolute, out uri)) Process.Start(uri.AbsoluteUri);
            else if (File.Exists(value)) Process.Start(new ProcessStartInfo { FileName = value, UseShellExecute = true });
            else MessageBox.Show(this, "Nie znaleziono pliku. Usun wpis i dodaj go ponownie.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Nie udalo sie uruchomic"); }
    }

    private string ProjectsPath { get { return Path.Combine(LocalAccounts.UserFolder, "Projects"); } }
    private string LibraryPath { get { return Path.Combine(LocalAccounts.UserFolder, "library.txt"); } }
    private void LoadLibrary()
    {
        try { if (File.Exists(LibraryPath)) libraryItems.AddRange(File.ReadAllLines(LibraryPath, Encoding.UTF8).Where(x => !String.IsNullOrWhiteSpace(x))); }
        catch { }
        RefreshLibrary();
    }
    private void SaveLibrary()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath)); File.WriteAllLines(LibraryPath, libraryItems, Encoding.UTF8); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Nie udalo sie zapisac biblioteki"); }
    }
    private void RefreshLibrary()
    {
        library.Items.Clear();
        foreach (string item in libraryItems)
        {
            int bar = item.IndexOf('|');
            library.Items.Add(bar >= 0 ? item.Substring(0, bar) : (item.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? item : Path.GetFileName(item)));
        }
    }

    private static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }
    private static string UnB64(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
    private static string SafeName(string s) { foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return String.IsNullOrWhiteSpace(s) ? "Moja_gra" : s; }
}

internal sealed class Board : Control
{
    private readonly int[,] map;
    public event Action<int, int> CellClicked;
    public bool Playing { get; set; }
    public PointF PlayerPosition { get; set; }
    public Board(int[,] source) { map = source; DoubleBuffered = true; TabStop = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int cw = Math.Max(1, ClientSize.Width / 24), ch = Math.Max(1, ClientSize.Height / 14);
        for (int y = 0; y < 14; y++) for (int x = 0; x < 24; x++)
        {
            Rectangle r = new Rectangle(x * cw, y * ch, cw, ch);
            int tile = Playing && map[y, x] == 2 ? 0 : map[y, x];
            Color c = tile == 1 ? Color.FromArgb(92, 104, 122) : tile == 2 ? Color.FromArgb(58, 170, 112) : tile == 3 ? Color.FromArgb(236, 179, 63) : tile == 4 ? Color.FromArgb(231, 82, 46) : Color.FromArgb(35, 40, 50);
            using (var b = new SolidBrush(c)) e.Graphics.FillRectangle(b, r);
            using (var p = new Pen(Color.FromArgb(50, 255, 255, 255))) e.Graphics.DrawRectangle(p, r);
        }
        if (Playing)
        {
            RectangleF p = new RectangleF((PlayerPosition.X - 0.3f) * cw, (PlayerPosition.Y - 0.35f) * ch, 0.6f * cw, 0.7f * ch);
            using (var b = new SolidBrush(Color.DeepSkyBlue)) e.Graphics.FillEllipse(b, p);
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Playing || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        int x = e.X / Math.Max(1, ClientSize.Width / 24), y = e.Y / Math.Max(1, ClientSize.Height / 14);
        if (x >= 0 && x < 24 && y >= 0 && y < 14 && CellClicked != null) CellClicked(x, y);
    }
}

internal sealed class LinkForm : Form
{
    private readonly TextBox name = new TextBox();
    private readonly TextBox url = new TextBox();
    public string NameValue { get { return String.IsNullOrWhiteSpace(name.Text) ? url.Text : name.Text; } }
    public string UrlValue { get { return url.Text.Trim(); } }
    public LinkForm()
    {
        Text = "Dodaj gre online"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(420, 145); MaximizeBox = false; MinimizeBox = false;
        var n = new Label { Text = "Nazwa", Left = 12, Top = 15, Width = 90 };
        name.SetBounds(105, 12, 295, 24);
        var u = new Label { Text = "Adres URL", Left = 12, Top = 52, Width = 90 };
        url.SetBounds(105, 49, 295, 24);
        var ok = new Button { Text = "Dodaj", Left = 228, Top = 95, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Anuluj", Left = 320, Top = 95, Width = 80, DialogResult = DialogResult.Cancel };
        Controls.AddRange(new Control[] { n, name, u, url, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
    }
}
