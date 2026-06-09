using System.Diagnostics;

namespace FrpManager.Helpers
{
    /// <summary>
    /// Manages the lifecycle of the frpc (FRP client) process.
    /// Handles starting, stopping, output redirection, and exit notification.
    /// All events fire on background threads — callers must dispatch to UI thread.
    /// </summary>
    public class FrpcProcessManager : IDisposable
    {
        private Process? _proc;
        private bool _disposed;

        /// <summary>Whether the frpc process is currently running.</summary>
        public bool IsRunning => _proc != null && !_proc.HasExited;

        /// <summary>The process ID of the running frpc instance, or null if not running.</summary>
        public int? ProcessId => _proc?.Id;

        /// <summary>
        /// Fires on a background thread when a line is received from frpc stdout/stderr.
        /// Callers MUST dispatch to the UI thread (e.g., via Dispatcher.BeginInvoke).
        /// Parameter: (line text, isStderr flag).
        /// </summary>
        public event Action<string, bool>? LineReceived;

        /// <summary>
        /// Fires on a background thread when the frpc process exits.
        /// Callers MUST dispatch to the UI thread.
        /// Parameter: exit code.
        /// </summary>
        public event Action<int>? ProcessExited;

        /// <summary>
        /// Starts the frpc process with the specified config file.
        /// If a process is already running, it is stopped first.
        /// Output and error streams are redirected and read asynchronously.
        /// </summary>
        /// <param name="frpcPath">Full path to the frpc executable.</param>
        /// <param name="configPath">Full path to the TOML configuration file.</param>
        public void Start(string frpcPath, string configPath)
        {
            Stop(); // Ensure no previous instance is running

            _proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = frpcPath,
                    Arguments = $"-c \"{configPath}\"",
                    UseShellExecute = false,   // Required for output redirection
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,     // No console window for GUI app
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                },
                EnableRaisingEvents = true     // Required for Exited event
            };

            // Wire up async output handlers — each data line fires LineReceived
            _proc.OutputDataReceived += (_, de) =>
            {
                if (de.Data != null) LineReceived?.Invoke(de.Data, false);
            };
            _proc.ErrorDataReceived += (_, de) =>
            {
                if (de.Data != null) LineReceived?.Invoke(de.Data, true);
            };

            // Process exit notification
            _proc.Exited += (_, _) =>
            {
                ProcessExited?.Invoke(_proc?.ExitCode ?? -1);
            };

            _proc.Start();
            _proc.BeginOutputReadLine();  // Begin async stdout reading
            _proc.BeginErrorReadLine();   // Begin async stderr reading
        }

        /// <summary>
        /// Stops the running frpc process.
        /// Uses Process.Kill(entireProcessTree: true) to terminate frpc and all children.
        /// Safe to call when no process is running (no-op).
        /// This is a synchronous blocking call — call from a background thread if called from UI.
        /// </summary>
        public void Stop()
        {
            if (_proc == null) return;
            try
            {
                if (!_proc.HasExited)
                {
                    // Kill the entire process tree to clean up any spawned subprocesses
                    _proc.Kill(true);
                }
            }
            catch { /* Process may have already exited between the check and kill */ }
            finally
            {
                _proc.Dispose();
                _proc = null;
            }
        }

        /// <summary>
        /// Disposes the process manager, stopping any running frpc instance.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
