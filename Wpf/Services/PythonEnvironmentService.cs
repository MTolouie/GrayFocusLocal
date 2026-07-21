using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Wpf.Services
{
    public class PythonEnvironmentService
    {
        private static readonly string[] RequiredPackages = new[]
        {
            "opencv-python",
            "numpy"
        };

        public string PyFolderPath { get; }
        public string VenvFolderPath { get; }

        public PythonEnvironmentService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            PyFolderPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\py"));
            VenvFolderPath = Path.Combine(PyFolderPath, "venv");
        }

        public bool EnsureVenvExists()
        {
            if (Directory.Exists(VenvFolderPath)) return false;

            Directory.CreateDirectory(PyFolderPath);

            // Create venv using system python
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"-m venv \"{VenvFolderPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit();

            if (proc?.ExitCode != 0)
            {
                throw new Exception("Failed to create Python virtual environment (venv). Ensure Python is installed in PATH.");
            }

            return true;
        }

        public async Task<bool> ArePackagesInstalledAsync()
        {
            string pipPath = GetVenvExecutable("pip");
            if (!File.Exists(pipPath)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = pipPath,
                Arguments = "list",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            foreach (var pkg in RequiredPackages)
            {
                if (!Regex.IsMatch(output, $@"\b{Regex.Escape(pkg)}\b", RegexOptions.IgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        public async Task InstallPackagesAsync(Action<string> statusCallback)
        {
            string pipPath = GetVenvExecutable("pip");

            foreach (var pkg in RequiredPackages)
            {
                statusCallback?.Invoke($"Installing package: {pkg}...");

                var psi = new ProcessStartInfo
                {
                    FileName = pipPath,
                    Arguments = $"install {pkg}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode != 0)
                    {
                        string err = await proc.StandardError.ReadToEndAsync();
                        throw new Exception($"Failed to install {pkg}: {err}");
                    }
                }
            }
        }

        private string GetVenvExecutable(string exeName)
        {
            bool isWindows = OperatingSystem.IsWindows();
            string binFolder = isWindows ? "Scripts" : "bin";
            string extension = isWindows ? ".exe" : "";
            return Path.Combine(VenvFolderPath, binFolder, $"{exeName}{extension}");
        }
    }
}