using System.Diagnostics;

namespace FrpManager.Helpers
{
    public class FrpcProcessManager : IDisposable
    {
        private Process? _proc;
        private bool _disposed;

        public bool IsRunning => _proc != null && !_proc.HasExited;
        public int? ProcessId => _proc?.Id;

        /// <summary>Fires on background thread — caller must dispatch to UI.</summary>
        public event Action<string, bool>? LineReceived;
        /// <summary>Fires on background thread — caller must dispatch to UI.</summary>
        public event Action<int>? ProcessExited;

        public void Start(string frpcPath, string configPath)
        {
            Stop();

            _proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = frpcPath,
                    Arguments = $"-c \"{configPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                },
                EnableRaisingEvents = true
            };

            _proc.OutputDataReceived += (_, de) =>
            {
                if (de.Data != null) LineReceived?.Invoke(de.Data, false);
            };
            _proc.ErrorDataReceived += (_, de) =>
            {
                if (de.Data != null) LineReceived?.Invoke(de.Data, true);
            };
            _proc.Exited += (_, _) =>
            {
                ProcessExited?.Invoke(_proc?.ExitCode ?? -1);
            };

            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
        }

        public void Stop()
        {
            if (_proc == null) return;
            try
            {
                if (!_proc.HasExited)
                {
                    _proc.Kill(true);
                }
            }
            catch { /* process may have already exited */ }
            finally
            {
                _proc.Dispose();
                _proc = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
