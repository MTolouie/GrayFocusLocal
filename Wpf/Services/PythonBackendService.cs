using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Wpf.Services
{
    public class PythonBackendService : IDisposable
    {
        private Process? _pythonProcess;

        public PythonBackendService()
        {
            // Secondary safety net: ensures cleanup even if the standard WPF OnExit is bypassed
            AppDomain.CurrentDomain.ProcessExit += (s, e) => StopBackend();
        }

        public void StartBackend()
        {
            // 1. Locate your 'py' folder relative to your running executable path
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Adjust the number of ".." based on your build output depth (e.g., bin/Debug/net8.0-windows)
            string pyFolderPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\py"));

            string pythonExe = Path.Combine(pyFolderPath, @"venv\Scripts\python.exe");
            string scriptPath = Path.Combine(pyFolderPath, "grayscale.py");

            if (!File.Exists(pythonExe))
            {
                throw new FileNotFoundException($"Could not find Python executable inside venv: {pythonExe}");
            }

            // 2. Configure the startup process settings
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\"",
                WorkingDirectory = pyFolderPath,
                UseShellExecute = false,
                CreateNoWindow = true, // Set to false if you want to see the console output logs for debugging
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // 3. Start the process
            _pythonProcess = new Process { StartInfo = startInfo };

            // Optional: Listen to console prints from the Python API script
            _pythonProcess.OutputDataReceived += (sender, args) => {
                if (!string.IsNullOrEmpty(args.Data)) Debug.WriteLine($"[Python]: {args.Data}");
            };

            _pythonProcess.Start();
            _pythonProcess.BeginOutputReadLine();
        }

        public void StopBackend()
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                try
                {
                    // Since it's a persistent API server, killing it forces it to release the port connection instantly
                    _pythonProcess.Kill();
                    _pythonProcess.Dispose();
                    _pythonProcess = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping python backend: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            StopBackend();
        }
    }
}