using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrpManager.Models
{
    // ── Proxy Type Enum ──────────────────────────────────────────────────────
    /// <summary>
    /// Enumeration of all supported FRP proxy types.
    /// sudp added for SUDP (Simple UDP) proxy support.
    /// </summary>
    public enum ProxyType { tcp, udp, http, https, stcp, xtcp, sudp }

    // ── Orderable item interface ──────────────────────────────────────────
    /// <summary>
    /// Implemented by config items that support user-defined ordering.
    /// Enables type-safe reordering without reflection in MoveItemUp/Down.
    /// </summary>
    public interface IOrderedItem
    {
        int Order { get; set; }
    }

    // ── Server ────────────────────────────────────────────────────────────
    /// <summary>
    /// Represents the frpc server connection configuration.
    /// Implements INotifyPropertyChanged for WPF data binding.
    /// All properties map to frpc.toml [common] / [auth] / [log] sections.
    /// </summary>
    public class ServerConfig : INotifyPropertyChanged
    {
        private string _addr = "your-server.com";
        private int _port = 7000;
        private string _token = "";
        private string _authMethod = "token";
        private string _logLevel = "info";
        private string _logFile = "";
        private bool _tls = false;
        private string _stunServer = "";

        /// <summary>FRP server address (domain or IP).</summary>
        public string ServerAddr { get => _addr; set => Set(ref _addr, value); }
        /// <summary>FRP server bind port (default 7000).</summary>
        public int ServerPort { get => _port; set => Set(ref _port, value); }
        /// <summary>Authentication token (must match server's token).</summary>
        public string Token { get => _token; set => Set(ref _token, value); }
        /// <summary>Authentication method: token, oidc, or none.</summary>
        public string AuthMethod { get => _authMethod; set => Set(ref _authMethod, value); }
        /// <summary>Log level: trace, debug, info, warn, error.</summary>
        public string LogLevel { get => _logLevel; set => Set(ref _logLevel, value); }
        /// <summary>Optional log file path (empty = console only).</summary>
        public string LogFile { get => _logFile; set => Set(ref _logFile, value); }
        /// <summary>Enable TLS transport encryption.</summary>
        public bool TlsEnable { get => _tls; set => Set(ref _tls, value); }
        /// <summary>STUN server for XTCP NAT hole-punching (empty = built-in).</summary>
        public string NatHoleStunServer { get => _stunServer; set => Set(ref _stunServer, value); }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Generic property setter with change notification.</summary>
        void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        { if (!EqualityComparer<T>.Default.Equals(f, v)) { f = v; PropertyChanged?.Invoke(this, new(n!)); } }
    }

    // ── Proxy ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Represents a single FRP proxy rule ([[proxies]] section in frpc.toml).
    /// Supports all FRP proxy types: tcp, udp, http, https, stcp, xtcp, sudp.
    /// </summary>
    public class ProxyConfig : INotifyPropertyChanged, IOrderedItem
    {
        private int _order;
        private string _name = "new-proxy";
        private ProxyType _type = ProxyType.tcp;
        private string _localIp = "127.0.0.1";
        private int _localPort = 80;
        private int _remotePort = 8080;
        private string _customDomains = "";
        private string _subdomain = "";
        private string _sk = "";
        private bool _encrypt = false;
        private bool _compress = false;

        /// <summary>Display order in the config list (1-based). Lower = first.</summary>
        public int Order { get => _order; set => Set(ref _order, value); }
        /// <summary>Proxy name (unique identifier).</summary>
        public string Name { get => _name; set => Set(ref _name, value); }
        /// <summary>Proxy type: tcp, udp, http, https, stcp, xtcp, sudp.</summary>
        public ProxyType Type { get => _type; set => Set(ref _type, value); }
        /// <summary>Local IP address to forward traffic to.</summary>
        public string LocalIp { get => _localIp; set => Set(ref _localIp, value); }
        /// <summary>Local port to forward traffic to.</summary>
        public int LocalPort { get => _localPort; set => Set(ref _localPort, value); }
        /// <summary>Remote port exposed on the FRP server (TCP/UDP only).</summary>
        public int RemotePort { get => _remotePort; set => Set(ref _remotePort, value); }
        /// <summary>Custom domains for HTTP/HTTPS (comma-separated).</summary>
        public string CustomDomains { get => _customDomains; set => Set(ref _customDomains, value); }
        /// <summary>Subdomain for HTTP/HTTPS (requires subdomain_host on server).</summary>
        public string Subdomain { get => _subdomain; set => Set(ref _subdomain, value); }
        /// <summary>Secret key for STCP/XTCP/SUDP authentication (empty = no auth).</summary>
        public string Sk { get => _sk; set => Set(ref _sk, value); }
        /// <summary>Enable transport-level encryption.</summary>
        public bool UseEncryption { get => _encrypt; set => Set(ref _encrypt, value); }
        /// <summary>Enable transport-level compression.</summary>
        public bool UseCompression { get => _compress; set => Set(ref _compress, value); }

        /// <summary>Label shown in the proxy type badge (e.g., "TCP", "HTTP").</summary>
        public string TypeLabel => _type.ToString().ToUpperInvariant();

        /// <summary>One-line summary displayed in the proxy list.</summary>
        public string Summary => _type switch
        {
            ProxyType.http or ProxyType.https =>
                string.IsNullOrWhiteSpace(_customDomains)
                    ? $":{_localPort} → {_subdomain}.domain"
                    : $":{_localPort} → {_customDomains}",
            ProxyType.stcp or ProxyType.xtcp or ProxyType.sudp =>
                $":{_localPort} (secret)",
            _ => $"127.0.0.1:{_localPort} → :{_remotePort}"
        };

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Generic property setter with change notification for all dependent properties.</summary>
        void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        {
            if (!EqualityComparer<T>.Default.Equals(f, v))
            {
                f = v;
                PropertyChanged?.Invoke(this, new(n!));
                // Also notify derived properties that depend on changed fields
                PropertyChanged?.Invoke(this, new(nameof(TypeLabel)));
                PropertyChanged?.Invoke(this, new(nameof(Summary)));
            }
        }
    }

    // ── Visitor [vis] ────────────────────────────────────────────────────
    /// <summary>
    /// Represents a FRP visitor rule ([[visitors]] section in frpc.toml).
    /// Visitors are the client-side counterparts of STCP/XTCP/SUDP proxies.
    /// </summary>
    public class VisitorConfig : INotifyPropertyChanged, IOrderedItem
    {
        private int _order;
        private string _name = "new-visitor";
        private ProxyType _type = ProxyType.stcp;
        private string _serverName = "";
        private string _serverUser = "";
        private string _sk = "";
        private string _bindAddr = "127.0.0.1";
        private int _bindPort = 9000;
        private bool _keepTunnelOpen = false;
        private string _fallbackTo = "";
        private int _fallbackTimeoutMs = 200;

        /// <summary>Display order in the config list (1-based). Lower = first.</summary>
        public int Order { get => _order; set => Set(ref _order, value); }
        /// <summary>Visitor name (unique identifier).</summary>
        public string Name { get => _name; set => Set(ref _name, value); }
        /// <summary>Visitor type: stcp, xtcp, sudp.</summary>
        public ProxyType Type { get => _type; set => Set(ref _type, value); }
        /// <summary>Name of the server-side proxy to visit.</summary>
        public string ServerName { get => _serverName; set => Set(ref _serverName, value); }
        /// <summary>Server user for multi-tenant setups (optional).</summary>
        public string ServerUser { get => _serverUser; set => Set(ref _serverUser, value); }
        /// <summary>Secret key for STCP/XTCP/SUDP authentication.</summary>
        public string Sk { get => _sk; set => Set(ref _sk, value); }
        /// <summary>Local bind address for the visitor tunnel.</summary>
        public string BindAddr { get => _bindAddr; set => Set(ref _bindAddr, value); }
        /// <summary>Local bind port (-1 = fallback only, no direct connections).</summary>
        public int BindPort { get => _bindPort; set => Set(ref _bindPort, value); }
        /// <summary>Keep XTCP tunnel open for faster reconnection.</summary>
        public bool KeepTunnelOpen { get => _keepTunnelOpen; set => Set(ref _keepTunnelOpen, value); }
        /// <summary>STCP fallback visitor name when XTCP P2P hole-punching fails.</summary>
        public string FallbackTo { get => _fallbackTo; set => Set(ref _fallbackTo, value); }
        /// <summary>Timeout in ms before falling back to STCP.</summary>
        public int FallbackTimeoutMs { get => _fallbackTimeoutMs; set => Set(ref _fallbackTimeoutMs, value); }

        /// <summary>Badge label for visitor identification in lists.</summary>
        public string TypeLabel => "[vis]";

        /// <summary>One-line summary displayed in the visitor list.</summary>
        public string Summary => string.IsNullOrWhiteSpace(_serverName)
            ? $":{_bindPort}"
            : $"{_serverName} → :{_bindPort}";

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Generic property setter with change notification for all dependent properties.</summary>
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
    /// <summary>
    /// Persistent application settings stored as JSON in AppData.
    /// Serialized/deserialized by SettingsHelper using Newtonsoft.Json.
    /// </summary>
    public class AppSettings
    {
        /// <summary>Path to the frpc executable.</summary>
        public string FrpcPath { get; set; } = "";

        /// <summary>Recently used frpc paths (max 8), newest first.</summary>
        public List<string> RecentFrpcPaths { get; set; } = new();

        /// <summary>Current UI language code: "zh-CN" or "en-US".</summary>
        public string Language { get; set; } = "zh-CN";

        /// <summary>Whether Windows auto-start is enabled.</summary>
        public bool AutoStartEnabled { get; set; } = false;

        /// <summary>Whether frpc was running when the app last closed (for auto-resume).</summary>
        public bool FrpcWasRunning { get; set; } = false;

        /// <summary>Last saved/loaded TOML file path for direct overwrite on Save.</summary>
        public string? LastSavedFilePath { get; set; }
    }

    // ── GitHub ────────────────────────────────────────────────────────────
    /// <summary>
    /// GitHub API release response (partial model).
    /// Used by GithubHelper to fetch FRP release metadata.
    /// </summary>
    public class GitHubRelease
    {
        public string tag_name { get; set; } = "";
        public string name { get; set; } = "";
        public string published_at { get; set; } = "";
        public string html_url { get; set; } = "";
        public List<GitHubAsset> assets { get; set; } = new();
    }

    /// <summary>
    /// GitHub API asset response (partial model).
    /// Represents a downloadable file attachment to a release.
    /// </summary>
    public class GitHubAsset
    {
        public string name { get; set; } = "";
        public string browser_download_url { get; set; } = "";
        public long size { get; set; }

        /// <summary>Human-readable file size label (KB or MB).</summary>
        public string SizeLabel => size < 1024 * 1024
            ? $"{size / 1024.0:F1} KB"
            : $"{size / 1024.0 / 1024.0:F1} MB";
    }
}
