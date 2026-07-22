using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Detects the highest supported CUDA version on the host machine via nvidia-smi.
        /// Returns (isSupported, cupyPackageName).
        /// </summary>
        public (bool IsGpuAvailable, string? CupyPackageName, bool RequiresManualToolkit) DetectCudaCapabilities()
        {
            string? nvidiaSmiPath = ResolveNvidiaSmiPath();
            if (string.IsNullOrEmpty(nvidiaSmiPath)) return (false, null, false);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = nvidiaSmiPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return (false, null, false);

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return (false, null, false);

                var match = Regex.Match(output, @"CUDA\s+Version:\s*(\d+)\.(\d+)", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int majorVersion))
                {
                    int minorVersion = 0;
                    if (match.Groups.Count > 2)
                    {
                        int.TryParse(match.Groups[2].Value, out minorVersion);
                    }

                    if (majorVersion >= 11)
                    {
                        // CUDA 11+ installs bundled runtime dependencies automatically via [ctk]
                        return (true, $"cupy-cuda{majorVersion}x[ctk]", false);
                    }

                    // CUDA versions below 11 (e.g., CUDA 10) are explicitly unsupported
                    return (false, null, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CUDA Detection Error: {ex.Message}");
            }

            return (false, null, false);
        }
        public async Task<bool> ArePackagesInstalledAsync(string? cupyPackageName = null)
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

            // Check core dependencies
            foreach (var pkg in RequiredPackages)
            {
                if (!Regex.IsMatch(output, $@"\b{Regex.Escape(pkg)}\b", RegexOptions.IgnoreCase))
                {
                    return false;
                }
            }

            // Check CuPy if CUDA is present
            if (!string.IsNullOrEmpty(cupyPackageName))
            {
                // Clean "cupy-cuda12x[ctk]" -> "cupy-cuda12x" for pip list checking
                string cleanPackageName = Regex.Replace(cupyPackageName, @"\[.*?\]", "").Trim();

                bool isCupyInstalled = Regex.IsMatch(output, @"\bcupy\b", RegexOptions.IgnoreCase) ||
                                      Regex.IsMatch(output, $@"\b{Regex.Escape(cleanPackageName)}\b", RegexOptions.IgnoreCase);

                if (!isCupyInstalled)
                {
                    return false;
                }
            }

            return true;
        }

        public async Task InstallPackagesAsync(string? cupyPackageName, Action<string> statusCallback)
        {
            string pipPath = GetVenvExecutable("pip");

            var packagesToInstall = new List<string>(RequiredPackages);
            if (!string.IsNullOrEmpty(cupyPackageName))
            {
                packagesToInstall.Add(cupyPackageName);
            }

            foreach (var pkg in packagesToInstall)
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

        private static string? ResolveNvidiaSmiPath()
        {
            string[] knownPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
                @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe"
            };

            foreach (var path in knownPaths)
            {
                if (File.Exists(path)) return path;
            }

            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string folder in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string fullPath = Path.Combine(folder.Trim(), "nvidia-smi.exe");
                        if (File.Exists(fullPath)) return fullPath;
                    }
                    catch { }
                }
            }

            return null;
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