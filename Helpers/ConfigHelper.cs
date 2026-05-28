using FrpManager.Models;
using System.Text;

namespace FrpManager.Helpers
{
    public static class ConfigHelper
    {
        // ── frpc.toml ─────────────────────────────────────────────────────
        public static string GenerateFrpcToml(
            ServerConfig s,
            IEnumerable<ProxyConfig>   proxies,
            IEnumerable<VisitorConfig> visitors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# frpc.toml — 由 FrpManager 生成");
            sb.AppendLine($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine($"serverAddr = \"{s.ServerAddr}\"");
            sb.AppendLine($"serverPort = {s.ServerPort}");
            sb.AppendLine();
            // 仅在选择了认证方式时写入 [auth] 段
            if (s.AuthMethod != "none")
            {
                sb.AppendLine("[auth]");
                sb.AppendLine($"method = \"{s.AuthMethod}\"");
                if (s.AuthMethod == "token" && !string.IsNullOrWhiteSpace(s.Token))
                    sb.AppendLine($"token  = \"{s.Token}\"");
                sb.AppendLine();
            }
            sb.AppendLine("[log]");
            sb.AppendLine($"level = \"{s.LogLevel}\"");
            if (!string.IsNullOrWhiteSpace(s.LogFile))
                sb.AppendLine($"to    = \"{s.LogFile}\"");
            if (s.TlsEnable)
            {
                sb.AppendLine();
                sb.AppendLine("[transport.tls]");
                sb.AppendLine("enable = true");
            }

            // ── Proxies ───────────────────────────────────────────────────
            foreach (var p in proxies)
            {
                sb.AppendLine();
                sb.AppendLine("[[proxies]]");
                sb.AppendLine($"name      = \"{p.Name}\"");
                sb.AppendLine($"type      = \"{p.Type}\"");
                sb.AppendLine($"localIP   = \"{p.LocalIp}\"");
                sb.AppendLine($"localPort = {p.LocalPort}");

                switch (p.Type)
                {
                    case ProxyType.http:
                    case ProxyType.https:
                        if (!string.IsNullOrWhiteSpace(p.CustomDomains))
                            sb.AppendLine($"customDomains = [\"{p.CustomDomains}\"]");
                        if (!string.IsNullOrWhiteSpace(p.Subdomain))
                            sb.AppendLine($"subdomain = \"{p.Subdomain}\"");
                        break;
                    case ProxyType.stcp:
                    case ProxyType.xtcp:
                        if (!string.IsNullOrWhiteSpace(p.Sk))
                            sb.AppendLine($"secretKey = \"{p.Sk}\"");
                        break;
                    default:
                        sb.AppendLine($"remotePort = {p.RemotePort}");
                        break;
                }

                if (p.UseEncryption || p.UseCompression)
                {
                    sb.AppendLine("[proxies.transport]");
                    if (p.UseEncryption)  sb.AppendLine("useEncryption  = true");
                    if (p.UseCompression) sb.AppendLine("useCompression = true");
                }
            }

            // ── Visitors ──────────────────────────────────────────────────
            foreach (var v in visitors)
            {
                sb.AppendLine();
                sb.AppendLine("[[visitors]]");
                sb.AppendLine($"name       = \"{v.Name}\"");
                sb.AppendLine($"type       = \"{v.Type}\"");
                sb.AppendLine($"serverName = \"{v.ServerName}\"");
                if (!string.IsNullOrWhiteSpace(v.ServerUser))
                    sb.AppendLine($"serverUser = \"{v.ServerUser}\"");
                if (!string.IsNullOrWhiteSpace(v.Sk))
                    sb.AppendLine($"secretKey  = \"{v.Sk}\"");
                sb.AppendLine($"bindAddr   = \"{v.BindAddr}\"");
                sb.AppendLine($"bindPort   = {v.BindPort}");
            }

            return sb.ToString();
        }

        // ── frps.toml ─────────────────────────────────────────────────────
        public static string GenerateFrpsToml(ServerConfig s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# frps.toml — 由 FrpManager 生成");
            sb.AppendLine($"# {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine($"bindPort = {s.ServerPort}");
            sb.AppendLine();
            // 仅在选择了认证方式时写入 [auth] 段
            if (s.AuthMethod != "none")
            {
                sb.AppendLine("[auth]");
                sb.AppendLine($"method = \"{s.AuthMethod}\"");
                if (s.AuthMethod == "token" && !string.IsNullOrWhiteSpace(s.Token))
                    sb.AppendLine($"token  = \"{s.Token}\"");
                sb.AppendLine();
            }
            sb.AppendLine("[webServer]");
            sb.AppendLine("addr = \"0.0.0.0\"");
            sb.AppendLine("port = 7500");
            if (s.TlsEnable)
            {
                sb.AppendLine();
                sb.AppendLine("[transport.tls]");
                sb.AppendLine("force = true");
            }
            return sb.ToString();
        }

        // ── Validation ────────────────────────────────────────────────────
        public static (bool Valid, List<string> Errors) Validate(
            ServerConfig s,
            IEnumerable<ProxyConfig>   proxies,
            IEnumerable<VisitorConfig> visitors)
        {
            var errs = new List<string>();

            // Server
            if (string.IsNullOrWhiteSpace(s.ServerAddr))
                errs.Add("❌ 服务器地址不能为空");
            if (s.ServerPort is <= 0 or > 65535)
                errs.Add($"❌ 服务器端口无效（当前：{s.ServerPort}，应为 1-65535）");

            // Proxies
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int pi = 0;
            foreach (var p in proxies)
            {
                pi++;
                string tag = string.IsNullOrWhiteSpace(p.Name) ? $"代理#{pi}" : $"代理[{p.Name}]";

                if (string.IsNullOrWhiteSpace(p.Name))
                    errs.Add($"❌ {tag} 名称不能为空");
                else if (!names.Add(p.Name))
                    errs.Add($"❌ 存在重复的名称「{p.Name}」");

                if (p.LocalPort is <= 0 or > 65535)
                    errs.Add($"❌ {tag} 本地端口无效（{p.LocalPort}）");

                switch (p.Type)
                {
                    case ProxyType.tcp:
                    case ProxyType.udp:
                        if (p.RemotePort is <= 0 or > 65535)
                            errs.Add($"❌ {tag} 远程端口无效（{p.RemotePort}）");
                        break;
                    case ProxyType.http:
                    case ProxyType.https:
                        if (string.IsNullOrWhiteSpace(p.CustomDomains) &&
                            string.IsNullOrWhiteSpace(p.Subdomain))
                            errs.Add($"❌ {tag} HTTP/HTTPS 必须填写「自定义域名」或「子域名」");
                        break;
                }
            }

            // Visitors
            int vi = 0;
            foreach (var v in visitors)
            {
                vi++;
                string tag = string.IsNullOrWhiteSpace(v.Name) ? $"访客#{vi}" : $"访客[{v.Name}]";

                if (string.IsNullOrWhiteSpace(v.Name))
                    errs.Add($"❌ {tag} 名称不能为空");
                else if (!names.Add(v.Name))
                    errs.Add($"❌ 存在重复的名称「{v.Name}」");

                if (string.IsNullOrWhiteSpace(v.ServerName))
                    errs.Add($"❌ {tag} 「对应代理名称」不能为空");

                if (v.BindPort is <= 0 or > 65535)
                    errs.Add($"❌ {tag} 绑定端口无效（{v.BindPort}）");

                if (string.IsNullOrWhiteSpace(v.BindAddr))
                    errs.Add($"❌ {tag} 绑定地址不能为空");
            }

            return (errs.Count == 0, errs);
        }

        // ── Templates ─────────────────────────────────────────────────────
        public static ProxyConfig   TcpTemplate()     => new() { Name = "tcp-proxy",  Type = ProxyType.tcp,   LocalPort = 8080,  RemotePort = 18080 };
        public static ProxyConfig   SshTemplate()     => new() { Name = "ssh",        Type = ProxyType.tcp,   LocalPort = 22,    RemotePort = 6022  };
        public static ProxyConfig   RdpTemplate()     => new() { Name = "rdp",        Type = ProxyType.tcp,   LocalPort = 3389,  RemotePort = 13389 };
        public static ProxyConfig   WebTemplate()     => new() { Name = "web",        Type = ProxyType.http,  LocalPort = 80,    CustomDomains = "yourdomain.com" };
        public static ProxyConfig   HttpsTemplate()   => new() { Name = "web-https",  Type = ProxyType.https, LocalPort = 443,   CustomDomains = "yourdomain.com" };
        public static ProxyConfig   UdpTemplate()     => new() { Name = "udp-proxy",  Type = ProxyType.udp,   LocalPort = 5000,  RemotePort = 15000 };
        public static ProxyConfig   McTemplate()      => new() { Name = "minecraft",  Type = ProxyType.tcp,   LocalPort = 25565, RemotePort = 25565 };
        public static ProxyConfig   StcpTemplate()    => new() { Name = "stcp-server",Type = ProxyType.stcp,  LocalPort = 8080,  RemotePort = 0     };
        public static VisitorConfig VisitorTemplate() => new() { Name = "stcp-visitor", Type = ProxyType.stcp, ServerName = "stcp-server", BindAddr = "127.0.0.1", BindPort = 9000 };
    }
}
