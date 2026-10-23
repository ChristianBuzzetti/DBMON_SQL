using System.ComponentModel;
using System.Data.Odbc;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using AseIsql.Odbc;
using Microsoft.Win32;

namespace AseIsql.Desktop;

internal sealed record LoginSelection(ConnectionProfile Profile);
internal sealed record ConnectedLogin(OdbcSession Session,LoginSelection Selection,TimeSpan Total=default)
{
    // "login 9.8 s (server login 9.6 s · session facts 0.1 s)": where a slow connect spent its time.
    public string TimingText => "login " + Seconds(Total) + " (driver connect and server login " + Seconds(Session.OpenTiming.Login) + " · session facts " + Seconds(Session.OpenTiming.Facts) + ")";
    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
}

public partial class ConnectionWindow : Window
{
    private bool busy;
    private SqlIniCatalog? catalog;
    private ConnectedLogin? connectedLogin;
    public ConnectionProfile? Profile { get; private set; }
    public bool DriverAvailable { get; }
    private List<SavedLogin> savedLogins = [];
    private bool restoring;
    private string? deleteArmed;

    public ConnectionWindow(ConnectionProfile? previous = null)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Themes.ApplyChrome(this, Themes.Current);
        Charset.ItemsSource = new[] { "ServerDefault", "utf8", "iso_1", "cp1252" }; Charset.Text = "ServerDefault";
        LoginLanguage.ItemsSource = new[] { "us_english", "spanish" }; LoginLanguage.Text = "us_english";
        Hostname.ItemsSource = new[] { Environment.MachineName }; Hostname.Text = Environment.MachineName;
        DriverAvailable = IsDriverInstalled(ServerPlatform.Ase);
        PlatformSelector.ItemsSource = Platforms.All.Select(p => new ComboBoxItem { Content = p.Title() + (IsDriverInstalled(p) ? "" : " · driver missing"), Tag = p }).ToList();
        SelectPlatform(previous?.Platform ?? ServerPlatform.Ase);
        string? path = !string.IsNullOrEmpty(previous?.SqlIniPath) ? previous.SqlIniPath : SqlIniFiles.Discover();
        if (path != null) TryLoadCatalog(path);
        else Feedback.Text = "sql.ini was not found. Browse for it or enter host and port in Advanced options.";
        if (previous != null) RestoreProfile(previous);
        ProfilesPath.Text = "Stored in " + ProfileStore.PathName;
        ReloadSavedLogins(null);
        if (previous != null) ProfileName.Text = DefaultProfileName(previous);
    }
    // A 64-bit ODBC driver registered under its SAP name with an existing library file.
    public static bool IsDriverInstalled(ServerPlatform platform)
    {
        using var drivers = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ODBC\ODBCINST.INI\" + OdbcSession.DriverFor(platform));
        return Environment.Is64BitProcess && drivers?.GetValue("Driver") is string driverPath && File.Exists(driverPath);
    }
    public ServerPlatform SelectedPlatform => (PlatformSelector.SelectedItem as ComboBoxItem)?.Tag is ServerPlatform platform ? platform : ServerPlatform.Ase;
    internal void SelectPlatform(ServerPlatform platform) => PlatformSelector.SelectedItem = PlatformSelector.Items.OfType<ComboBoxItem>().First(i => (ServerPlatform)i.Tag == platform);
    // Fields follow the server type: RS has no database, IQ's database and engine name are optional, and the
    // ASE login options hide for IQ. The driver status names the driver this type needs.
    private void PlatformChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Database == null || IqServerRow == null || DriverStatus == null) return;
        var platform = SelectedPlatform; bool installed = IsDriverInstalled(platform);
        Hostname.IsEnabled = platform != ServerPlatform.Iq;
        Database.IsEnabled = platform != ServerPlatform.Rs;
        if (platform == ServerPlatform.Rs) Database.Text = "";
        else if (platform == ServerPlatform.Ase && Database.Text.Length == 0) Database.Text = "master";
        Database.ToolTip = platform switch { ServerPlatform.Iq => "Optional: the IQ database name. Empty uses the server's database.", ServerPlatform.Rs => "Replication Server has no databases.", _ => "Database to open after login" };
        IqServerRow.Visibility = platform == ServerPlatform.Iq ? Visibility.Visible : Visibility.Collapsed;
        IqIdentityRow.Visibility = platform == ServerPlatform.Iq ? Visibility.Visible : Visibility.Collapsed;
        if (platform != ServerPlatform.Iq) { IqIdentityPassword.Clear(); RememberIdentityPassword.IsChecked = false; }
        LanguageRow.Visibility = EncryptPassword.Visibility = platform == ServerPlatform.Iq ? Visibility.Collapsed : Visibility.Visible;
        DriverStatus.Text = installed ? "Driver: " + OdbcSession.DriverFor(platform) + " · 64 bits · " + platform.Title() :
            "The 64-bit " + OdbcSession.DriverFor(platform) + " ODBC driver is missing. Install it with the official SAP installer to connect to " + platform.Title() + ".";
        TestButton.IsEnabled = ConnectButton.IsEnabled = installed && !busy;
        if (!installed) { Feedback.SetResourceReference(TextBlock.ForegroundProperty, "ErrorInk"); Feedback.Text = DriverStatus.Text; }
        ServerChanged(this, null!);
    }
    // ASE uses query entries only. IQ and RS aliases often list just their listener, which is then the endpoint.
    private IReadOnlyList<SqlIniEndpoint> EndpointsFor(SqlIniServer? server) =>
        server == null ? [] : server.Queries.Count > 0 || SelectedPlatform == ServerPlatform.Ase ? server.Queries : server.MasterEntries ?? [];
    private bool UsesMasterEntry(SqlIniServer? server) => server != null && server.Queries.Count == 0 && SelectedPlatform != ServerPlatform.Ase && server.MasterEntries?.Count > 0;
    private static string DefaultProfileName(ConnectionProfile profile) => (profile.User.Length > 0 ? profile.User + " @ " : "") + (profile.ServerAlias.Length > 0 ? profile.ServerAlias : profile.Server);
    private void ReloadSavedLogins(string? select)
    {
        try { savedLogins = ProfileStore.Load(ProfileStore.PathName); }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException) { savedLogins = []; Feedback.Text = "Saved profiles could not be read: " + ex.Message; }
        restoring = true;
        try {
            SavedProfiles.ItemsSource = null; SavedProfiles.ItemsSource = savedLogins;
            ProfileList.ItemsSource = null; ProfileList.ItemsSource = savedLogins.Select(l => new ListBoxItem { Content = l.Name + (l.HasPassword ? "  ·  login password saved" : "") + (l.HasIdentityPassword ? "  ·  identity password saved" : ""), Tag = l }).ToList();
            SavedProfiles.SelectedItem = savedLogins.FirstOrDefault(l => l.Name.Equals(select, StringComparison.OrdinalIgnoreCase));
            SavedProfiles.IsEnabled = savedLogins.Count > 0;
        } finally { restoring = false; }
        UpdateProfileButtons();
    }
    private SavedLogin? ListedLogin => (ProfileList.SelectedItem as ListBoxItem)?.Tag as SavedLogin;
    private void UpdateProfileButtons()
    {
        var login = ListedLogin; LoadProfileButton.IsEnabled = DeleteProfileButton.IsEnabled = login != null; ForgetPasswordButton.IsEnabled = login?.HasPassword == true;
        ForgetIdentityPasswordButton.IsEnabled = login?.HasIdentityPassword == true;
        if (deleteArmed != null && deleteArmed != login?.Name) { deleteArmed = null; DeleteProfileButton.Content = "Delete"; }
    }
    private void ProfileListChanged(object sender, SelectionChangedEventArgs e) => UpdateProfileButtons();
    private void SavedProfileChosen(object sender, SelectionChangedEventArgs e) { if (!restoring && SavedProfiles.SelectedItem is SavedLogin login) ApplySavedLogin(login); }
    private void LoadProfileClick(object sender, RoutedEventArgs e) { if (ListedLogin is { } login) { ApplySavedLogin(login); restoring = true; SavedProfiles.SelectedItem = login; restoring = false; Fields.SelectedIndex = 0; } }
    internal void ApplySavedLogin(SavedLogin login)
    {
        if (login.Profile.SqlIniPath.Length > 0 && login.Profile.SqlIniPath != catalog?.SourcePath) TryLoadCatalog(login.Profile.SqlIniPath);
        RestoreProfile(login.Profile); ProfileName.Text = login.Name;
        Password.Password = login.Password ?? ""; RememberPassword.IsChecked = login.HasPassword;
        IqIdentityPassword.Password = login.IdentityPassword ?? ""; RememberIdentityPassword.IsChecked = login.HasIdentityPassword;
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Feedback.Text = login.HasPassword ? "Profile " + login.Name + " loaded with its saved password." : "Profile " + login.Name + " loaded. Enter your password before connecting.";
        if (login.Profile.IqIdentityFile.Length > 0 && !login.HasIdentityPassword) Feedback.Text += " Enter the client identity password.";
        if (!login.HasPassword) Password.Focus();
    }
    // Saves each secret independently, only when its own Remember checkbox is on.
    internal SavedLogin StoreCurrent(string? name = null)
    {
        ConnectionProfile profile = ReadProfile();
        name = (name ?? ProfileName.Text).Trim(); if (name.Length == 0) name = DefaultProfileName(profile);
        if (name.Length > 120) throw new ArgumentException("Profile names are limited to 120 characters.");
        string? password = RememberPassword.IsChecked == true && Password.Password.Length > 0 ? Password.Password : null;
        if (RememberIdentityPassword.IsChecked == true && IqIdentityPassword.Password.Length > 0 && profile.IqIdentityFile.Length == 0)
            throw new ArgumentException("Select a client identity file before remembering its password.");
        string? identityPassword = profile.Platform == ServerPlatform.Iq && profile.UseTls && profile.IqIdentityFile.Length > 0 && RememberIdentityPassword.IsChecked == true && IqIdentityPassword.Password.Length > 0 ? IqIdentityPassword.Password : null;
        var login = new SavedLogin(name, profile, password, identityPassword);
        var current = ProfileStore.Load(ProfileStore.PathName);
        if (current.Count >= ProfileStore.MaxLogins && !current.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Up to 200 profiles can be saved.");
        ProfileStore.Save(ProfileStore.PathName, ProfileStore.Upsert(current, login));
        ReloadSavedLogins(name); ProfileName.Text = name;
        return login;
    }
    private void ForgetPasswordClick(object sender, RoutedEventArgs e) => ForgetStoredPassword(false);
    private void ForgetIdentityPasswordClick(object sender, RoutedEventArgs e) => ForgetStoredPassword(true);
    private void ForgetStoredPassword(bool identity)
    {
        if (ListedLogin is not { } selected) return;
        try {
            var current = ProfileStore.Load(ProfileStore.PathName);
            var login = current.FirstOrDefault(l => l.Name.Equals(selected.Name, StringComparison.OrdinalIgnoreCase));
            if (login == null || (identity ? !login.HasIdentityPassword : !login.HasPassword)) return;
            ProfileStore.Save(ProfileStore.PathName, ProfileStore.Upsert(current, identity ? login with { IdentityPassword = null } : login with { Password = null }));
            if (SavedProfiles.SelectedItem is SavedLogin loaded && loaded.Name.Equals(login.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (identity) { IqIdentityPassword.Clear(); RememberIdentityPassword.IsChecked = false; }
                else { Password.Clear(); RememberPassword.IsChecked = false; }
            }
            ReloadSavedLogins(login.Name); Feedback.Text = "Saved " + (identity ? "identity " : "login ") + "password removed from " + login.Name + ".";
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException) { Feedback.Text = ex.Message; }
    }
    // Deleting asks for a second click on the same profile.
    private void DeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (ListedLogin is not { } login) return;
        if (deleteArmed != login.Name) { deleteArmed = login.Name; DeleteProfileButton.Content = "Click again to delete"; return; }
        deleteArmed = null; DeleteProfileButton.Content = "Delete";
        try { ProfileStore.Save(ProfileStore.PathName, ProfileStore.Remove(ProfileStore.Load(ProfileStore.PathName), login.Name)); ReloadSavedLogins(null); Feedback.Text = "Profile " + login.Name + " deleted."; }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException) { Feedback.Text = ex.Message; }
    }

    internal void SetCatalog(SqlIniCatalog value)
    {
        catalog = value; IniPath.Text = value.SourcePath; ServerAliases.ItemsSource = value.Servers; ServerAliases.SelectedIndex = -1;
        CatalogInformation.Text = value.SourcePath + "\n" + value.Servers.Count + " alias · " + value.Servers.Count(s => !s.Duplicate && s.Queries.Any(q => q.IsUsable)) + " with a supported endpoint.\n" + string.Join("\n", value.Diagnostics);
    }
    private void TryLoadCatalog(string path)
    {
        try { SetCatalog(SqlIniFiles.Load(path)); Feedback.Text = "Choose a server from sql.ini or a saved profile."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { catalog = null; IniPath.Text = path; ServerAliases.ItemsSource = null; Feedback.Text = ex.Message; }
    }
    private void ServerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Endpoints == null) return;
        var server = ServerAliases.SelectedItem as SqlIniServer;
        var endpoints = EndpointsFor(server);
        Endpoints.ItemsSource = endpoints; Endpoints.SelectedIndex = endpoints.Count > 0 ? 0 : -1;
        Endpoints.Visibility = endpoints.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEndpoint();
    }
    private void EndpointChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EncryptData != null && ManualMode?.IsChecked != true)
            EncryptData.IsChecked = (Endpoints.SelectedItem as SqlIniEndpoint)?.UseTls == true;
        UpdateEndpoint();
    }
    private void UpdateEndpoint()
    {
        if (EndpointSummary == null) return;
        var server = ServerAliases.SelectedItem as SqlIniServer;
        var endpoint = Endpoints.SelectedItem as SqlIniEndpoint;
        EndpointSummary.Text = server == null ? "No server selected" : server.Duplicate ? "Duplicate alias: correct sql.ini before connecting." :
            endpoint == null ? (SelectedPlatform == ServerPlatform.Ase && server.MasterEntries?.Count > 0 ? "This alias has only a master (listener) entry. For SAP ASE choose an alias with a query entry." : "This alias has no query entry.") :
            endpoint.Error ?? endpoint.ToString() + (UsesMasterEntry(server) ? " · master entry (the alias lists no query entry)" : "");
        if (EncryptData != null) { EncryptData.IsEnabled = endpoint?.UseTls != true || ManualMode?.IsChecked == true; if (!EncryptData.IsEnabled) EncryptData.IsChecked = true; }
    }
    private void ManualChanged(object sender, RoutedEventArgs e)
    {
        if (ManualFields == null) return;
        bool manual = ManualMode.IsChecked == true; ManualFields.IsEnabled = manual; ServerAliases.IsEnabled = !manual; Endpoints.IsEnabled = !manual; UpdateEndpoint();
    }
    private void BrowseIni(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Select Sybase sql.ini", Filter = "Sybase sql.ini|sql.ini|INI files|*.ini|All files|*.*" };
        if (picker.ShowDialog(this) == true) TryLoadCatalog(picker.FileName);
    }
    private void ReloadIni(object sender, RoutedEventArgs e)
    {
        string? alias = (ServerAliases.SelectedItem as SqlIniServer)?.Name;
        SqlIniEndpoint? endpoint = Endpoints.SelectedItem as SqlIniEndpoint;
        if (IniPath.Text.Length > 0) { TryLoadCatalog(IniPath.Text); SelectAlias(alias, endpoint?.Host, endpoint?.Port); }
    }
    private void BrowseTrustedFile(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Select the TLS CA certificate file", Filter = "CA certificates|*.pem;*.crt;*.cer;*.txt|All files|*.*" };
        if (picker.ShowDialog(this) == true) TrustedFile.Text = picker.FileName;
    }
    private void BrowseIdentityFile(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Select the SAP IQ client identity file", Filter = "Client identity files|*.id;*.pem|All files|*.*" };
        if (picker.ShowDialog(this) == true) { IqIdentityFile.Text = picker.FileName; IqIdentityPassword.Clear(); EncryptData.IsChecked = true; }
    }
    private void IdentityFileChanged(object sender, TextChangedEventArgs e) { IqIdentityPassword?.Clear(); if (RememberIdentityPassword != null) RememberIdentityPassword.IsChecked = false; }
    private void SelectAlias(string? alias, string? host, int? port)
    {
        ServerAliases.SelectedItem = catalog?.Servers.SingleOrDefault(s => !s.Duplicate && s.Name.Equals(alias, StringComparison.OrdinalIgnoreCase));
        if (ServerAliases.SelectedItem is SqlIniServer server)
            Endpoints.SelectedItem = EndpointsFor(server).FirstOrDefault(q => q.Host == host && q.Port == port);
    }
    private void RestoreProfile(ConnectionProfile profile)
    {
        SelectPlatform(profile.Platform); IqServerName.Text = profile.IqServerName;
        Server.Text = profile.Server; Port.Text = profile.Port.ToString(); Database.Text = profile.Database; UserName.Text = profile.User;
        Charset.Text = profile.EffectiveCharset; LoginLanguage.Text = profile.Language; Hostname.Text = profile.ClientHostname; EncryptPassword.IsChecked = profile.EncryptPassword;
        EncryptData.IsChecked = profile.UseTls; TrustedFile.Text = profile.TrustedFile;
        IqIdentityFile.Text = profile.IqIdentityFile; IqCertificateName.Text = profile.IqCertificateName; IqIdentityPassword.Clear(); RememberIdentityPassword.IsChecked = false;
        LoginTimeout.Text = profile.LoginTimeout.ToString(); QueryTimeout.Text = profile.QueryTimeout.ToString(); MaxRows.Text = profile.MaxRows.ToString();
        ManualMode.IsChecked = string.IsNullOrEmpty(profile.ServerAlias); SelectAlias(profile.ServerAlias, profile.Server, profile.Port);
        EncryptData.IsChecked = profile.UseTls || (ManualMode.IsChecked != true && (Endpoints.SelectedItem as SqlIniEndpoint)?.UseTls == true);
    }
    internal ConnectionProfile ReadProfile()
    {
        if (!int.TryParse(LoginTimeout.Text, out int login) || !int.TryParse(QueryTimeout.Text, out int query) || !int.TryParse(MaxRows.Text, out int rows))
            throw new ArgumentException("Timeouts and row limits must be integers.");
        string host, alias = "", path = ""; int port; bool tls = EncryptData.IsChecked == true;
        if (ManualMode.IsChecked == true)
        { host = Server.Text; if (!int.TryParse(Port.Text, out port)) throw new ArgumentException("The port must be an integer."); }
        else
        {
            if (catalog == null || ServerAliases.SelectedItem is not SqlIniServer server) throw new ArgumentException("Choose a server from sql.ini.");
            if (server.Duplicate) throw new ArgumentException("The alias is duplicated in sql.ini.");
            if (Endpoints.SelectedItem is not SqlIniEndpoint endpoint) throw new ArgumentException(SelectedPlatform == ServerPlatform.Ase ? "The alias has no query entry." : "The alias has no query or master entry.");
            if (!endpoint.IsUsable) throw new ArgumentException(endpoint.Error);
            host = endpoint.Host; port = endpoint.Port!.Value; tls |= endpoint.UseTls; alias = server.Name; path = catalog.SourcePath;
        }
        var platform = SelectedPlatform;
        var profile = new ConnectionProfile(host, port, platform == ServerPlatform.Rs ? "" : Database.Text.Trim(), UserName.Text, tls, TrustedFile.Text, login, query, rows,
            Charset.Text, Hostname.Text, EncryptPassword.IsChecked == true, alias, path, LoginLanguage.Text, platform, platform == ServerPlatform.Iq ? IqServerName.Text.Trim() : "",
            platform == ServerPlatform.Iq ? IqIdentityFile.Text : "", platform == ServerPlatform.Iq ? IqCertificateName.Text.Trim() : "");
        profile.Validate(); return profile;
    }
    internal LoginSelection ReadSelection()=>new(ReadProfile());
    internal ConnectedLogin? TakeConnection()
    {
        var connection=connectedLogin;connectedLogin=null;return connection;
    }
    private void CheckCatalogFreshness()
    {
        if (ManualMode.IsChecked == true || catalog == null) return;
        SqlIniCatalog fresh = SqlIniFiles.Load(catalog.SourcePath);
        if (fresh.Fingerprint != catalog.Fingerprint)
        { SetCatalog(fresh); throw new ArgumentException("sql.ini changed and was reloaded. Select the server again before connecting."); }
    }
    private void SaveProfile(object sender, RoutedEventArgs e)
    {
        try { var login = StoreCurrent(); Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Feedback.Text = "Profile " + login.Name + " saved" + (login.HasPassword || login.HasIdentityPassword ? " with the selected passwords, encrypted for your Windows user." : " without saved passwords."); }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException or UnauthorizedAccessException) { Feedback.Text = ex.Message; }
    }
    private async Task ConnectAsync(bool testOnly)
    {
        if (busy) return;
        string password = "", identityPassword = "";
        try
        {
            if (!IsDriverInstalled(SelectedPlatform)) throw new ArgumentException("The 64-bit " + OdbcSession.DriverFor(SelectedPlatform) + " ODBC driver is unavailable.");
            CheckCatalogFreshness(); var selection=ReadSelection(); Profile = selection.Profile; password = Password.Password;
            identityPassword = SelectedPlatform == ServerPlatform.Iq ? IqIdentityPassword.Password : "";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            busy = true; Fields.IsEnabled = false; TestButton.IsEnabled = ConnectButton.IsEnabled = CloseButton.IsEnabled = false;
            Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Feedback.Text = "Connecting…";
            var profile = selection.Profile;
            if (testOnly)
            {
                QueryOutput output = await Task.Run(() => { using var candidate = OdbcSession.Open(profile, password, identityPassword); return candidate.Execute(OdbcSession.TestSql(profile.Platform)); });
                if (output.Error != null) throw new InvalidOperationException(output.Error);
                if (output.Tables.Count < 1 || output.Tables[0].Rows.Count < 1) throw new InvalidOperationException("The test did not return the expected result.");
                Feedback.Text = profile.Platform.Title() + " connection and " + (profile.Platform == ServerPlatform.Rs ? "admin version" : "SELECT") + " test succeeded in " + clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s. Click Connect to open your session.";
            }
            else
            {
                connectedLogin = new(await Task.Run(() => OdbcSession.Open(profile, password, identityPassword)),selection);
                // A loaded profile follows the login that just worked: its password is stored or dropped per Remember password.
                if (SavedProfiles.SelectedItem is SavedLogin chosen)
                    try { StoreCurrent(chosen.Name); } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine("Profile update: " + ex.Message); }
                connectedLogin = connectedLogin with { Total = clock.Elapsed };
                Password.Clear(); IqIdentityPassword.Clear(); DialogResult = true;
            }
        }
        catch (Exception ex) when (ex is OdbcException or ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { Feedback.Text = OdbcSession.DescribeConnectionFailure(ex, password, identityPassword); Feedback.SetResourceReference(TextBlock.ForegroundProperty, "ErrorInk"); }
        finally { password = identityPassword = ""; busy = false; Fields.IsEnabled = true; TestButton.IsEnabled = ConnectButton.IsEnabled = IsDriverInstalled(SelectedPlatform); CloseButton.IsEnabled = true; }
    }
    private void TestClick(object sender, RoutedEventArgs e) => _ = ConnectAsync(true);
    private void ConnectClick(object sender, RoutedEventArgs e) => _ = ConnectAsync(false);
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnClosing(object? sender, CancelEventArgs e) { if (busy && connectedLogin == null) e.Cancel = true; else { Password.Clear(); IqIdentityPassword.Clear(); } }
}
