using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrpManager.Models
{
    public enum ProxyType { tcp, udp, http, https, stcp, xtcp }

    // ── Server ────────────────────────────────────────────────────────────
    public class ServerConfig : INotifyPropertyChanged
    {
        private string _addr = "your-server.com";
        private int    _port = 7000;
        private string _token = "";
        private string _authMethod = "token";
        private string _logLevel = "info";
        private string _logFile  = "";
        private bool   _tls = false;

        public string ServerAddr { get => _addr;       set => Set(ref _addr, value); }
        public int    ServerPort { get => _port;       set => Set(ref _port, value); }
        public string Token      { get => _token;      set => Set(ref _token, value); }
        public string AuthMethod { get => _authMethod; set => Set(ref _authMethod, value); }
        public string LogLevel   { get => _logLevel;   set => Set(ref _logLevel, value); }
        public string LogFile    { get => _logFile;    set => Set(ref _logFile, value); }
        public bool   TlsEnable  { get => _tls;        set => Set(ref _tls, value); }

        public event PropertyChangedEventHandler? PropertyChanged;
        void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        { if (!EqualityComparer<T>.Default.Equals(f, v)) { f = v; PropertyChanged?.Invoke(this, new(n!)); } }
    }

    // ── Proxy ─────────────────────────────────────────────────────────────
    public class ProxyConfig : INotifyPropertyChanged
    {
        private string    _name = "new-proxy";
        private ProxyType _type = ProxyType.tcp;
        private string    _localIp = "127.0.0.1";
        private int       _localPort = 80;
        private int       _remotePort = 8080;
        private string    _customDomains = "";
        private string    _subdomain = "";
        private string    _sk = "";
        private bool      _encrypt = false;
        private bool      _compress = false;

        public string    Name           { get => _name;          set => Set(ref _name, value); }
        public ProxyType Type           { get => _type;          set => Set(ref _type, value); }
        public string    LocalIp        { get => _localIp;       set => Set(ref _localIp, value); }
        public int       LocalPort      { get => _localPort;     set => Set(ref _localPort, value); }
        public int       RemotePort     { get => _remotePort;    set => Set(ref _remotePort, value); }
        public string    CustomDomains  { get => _customDomains; set => Set(ref _customDomains, value); }
        public string    Subdomain      { get => _subdomain;     set => Set(ref _subdomain, value); }
        public string    Sk             { get => _sk;            set => Set(ref _sk, value); }
        public bool      UseEncryption  { get => _encrypt;       set => Set(ref _encrypt, value); }
        public bool      UseCompression { get => _compress;      set => Set(ref _compress, value); }

        public string TypeLabel => _type.ToString().ToUpperInvariant();
        public string Summary   => _type switch
        {
            ProxyType.http or ProxyType.https =>
                string.IsNullOrWhiteSpace(_customDomains)
                    ? $":{_localPort} → {_subdomain}.domain"
                    : $":{_localPort} → {_customDomains}",
            _ => $"127.0.0.1:{_localPort} → :{_remotePort}"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        {
            if (!EqualityComparer<T>.Default.Equals(f, v))
            {
                f = v;
                PropertyChanged?.Invoke(this, new(n!));
                PropertyChanged?.Invoke(this, new(nameof(TypeLabel)));
                PropertyChanged?.Invoke(this, new(nameof(Summary)));
            }
        }
    }

    // ── Visitor [vis] ────────────────────────────────────────────────────
    public class VisitorConfig : INotifyPropertyChanged
    {
        private string    _name = "new-visitor";
        private ProxyType _type = ProxyType.stcp;   // only stcp / xtcp
        private string    _serverName = "";           // proxy name on frps
        private string    _serverUser = "";           // optional: frpc user who owns the proxy
        private string    _sk = "";                   // optional shared secret
        private string    _bindAddr = "127.0.0.1";
        private int       _bindPort = 9000;

        public string    Name       { get => _name;       set => Set(ref _name, value); }
        public ProxyType Type       { get => _type;       set => Set(ref _type, value); }
        public string    ServerName { get => _serverName; set => Set(ref _serverName, value); }
        public string    ServerUser { get => _serverUser; set => Set(ref _serverUser, value); }
        public string    Sk         { get => _sk;         set => Set(ref _sk, value); }
        public string    BindAddr   { get => _bindAddr;   set => Set(ref _bindAddr, value); }
        public int       BindPort   { get => _bindPort;   set => Set(ref _bindPort, value); }

        public string TypeLabel => "[vis]";
        public string Summary   => string.IsNullOrWhiteSpace(_serverName)
            ? $":{_bindPort}"
            : $"{_serverName} → :{_bindPort}";

        public event PropertyChangedEventHandler? PropertyChanged;
        void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        {
            if (!EqualityComparer<T>.Default.Equals(f, v))
            {
                f = v;
                PropertyChanged?.Invoke(this, new(n!));
                PropertyChanged?.Invoke(this, new(nameof(TypeLabel)));
                PropertyChanged?.Invoke(this, new(nameof(Summary)));
            }
        }
    }

    // ── App Settings ─────────────────────────────────────────────────────
    public class AppSettings
    {
        public string       FrpcPath        { get; set; } = "";
        public List<string> RecentFrpcPaths { get; set; } = new();
    }

    // ── GitHub ────────────────────────────────────────────────────────────
    public class GitHubRelease
    {
        public string tag_name     { get; set; } = "";
        public string name         { get; set; } = "";
        public string published_at { get; set; } = "";
        public string html_url     { get; set; } = "";
        public List<GitHubAsset> assets { get; set; } = new();
    }

    public class GitHubAsset
    {
        public string name                 { get; set; } = "";
        public string browser_download_url { get; set; } = "";
        public long   size                 { get; set; }
        public string SizeLabel => size < 1024 * 1024
            ? $"{size / 1024.0:F1} KB"
            : $"{size / 1024.0 / 1024.0:F1} MB";
    }
}
