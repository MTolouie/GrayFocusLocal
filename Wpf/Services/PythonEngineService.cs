using System;
using System.IO;
using System.Linq;
using Python.Runtime;

namespace Wpf.Services
{
    public class PythonEngineService : IDisposable
    {
        private bool _isInitialized;
        private IntPtr _threadState; // <-- CRITICAL: Stores the GIL thread state for background workers

        public bool IsInitialized => _isInitialized;
        public dynamic Processor { get; private set; } = null!;

        /// <summary>
        /// Re-initializes the GrayscaleProcessor with the requested hardware accelerator preference.
        /// Must be called from threads where GIL management is handled.
        /// </summary>
        public void ReinitializeProcessor(bool? useGpu)
        {
            using (Py.GIL())
            {
                dynamic clr = Py.Import("grayscale_clr");
                Processor = clr.GrayscaleProcessor(use_gpu: useGpu);
            }
        }

        public void StartBackend()
        {
            if (_isInitialized) return;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Destek: Hem geliştirme ortamı (..\..\..\py) hem de yayınlanmış (publish) sürüm (.\py)
            string pyFolderPath = Directory.Exists(Path.Combine(baseDir, "py"))
                ? Path.GetFullPath(Path.Combine(baseDir, "py"))
                : Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\py"));

            string embeddedPythonPath = Path.Combine(pyFolderPath, "python-runtime", "python-3.13.14-embed-amd64");
            string basePythonHome;

            // 1. Öncelik: Gömülü (Embedded) Python
            if (Directory.Exists(embeddedPythonPath))
            {
                basePythonHome = embeddedPythonPath;
            }
            // 2. Öncelik: Sanal Ortam (venv) üzerinden sistemdeki Python (Eski yöntem)
            else
            {
                string venvPath = Path.Combine(pyFolderPath, "venv");
                basePythonHome = ResolveBasePythonHome(venvPath);

                if (string.IsNullOrEmpty(basePythonHome) || !Directory.Exists(basePythonHome))
                {
                    throw new DirectoryNotFoundException(
                        $"Could not parse the base Python installation directory from: {Path.Combine(venvPath, "pyvenv.cfg")} and no embedded Python was found at {embeddedPythonPath}.");
                }
            }

            string pythonDll = FindPythonDll(basePythonHome);

            if (string.IsNullOrEmpty(pythonDll) || !File.Exists(pythonDll))
            {
                throw new FileNotFoundException(
                    $"Could not locate the python3X.dll inside the base Python path: {basePythonHome}");
            }

            Runtime.PythonDLL = pythonDll;
            PythonEngine.PythonHome = basePythonHome;

            // 1. Initialize CPython interpreter
            PythonEngine.Initialize();
            _isInitialized = true;

            // 2. Load setup requirements inside the GIL
            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                sys.path.append(pyFolderPath);

                string sitePackages = Path.Combine(venvPath, @"Lib\site-packages");
                if (Directory.Exists(sitePackages))
                {
                    sys.path.append(sitePackages);
                }

                dynamic clr = Py.Import("grayscale_clr");
                Processor = clr.GrayscaleProcessor();
            }

            // 3. CRITICAL: Release the GIL from the UI thread!
            // This allows task-pool background threads (e.g. Task.Run) to cleanly acquire 
            // the lock using `using (Py.GIL())` when you click "Scan Target Region".
            _threadState = PythonEngine.BeginAllowThreads();
        }

        private static string ResolveBasePythonHome(string venvPath)
        {
            string cfgPath = Path.Combine(venvPath, "pyvenv.cfg");
            if (!File.Exists(cfgPath))
                return string.Empty;

            string? homeDir = File.ReadLines(cfgPath)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2 && parts[0].Trim().Equals("home", StringComparison.OrdinalIgnoreCase))
                .Select(parts => parts[1].Trim())
                .FirstOrDefault();

            return !string.IsNullOrEmpty(homeDir) && Directory.Exists(homeDir)
                ? homeDir
                : string.Empty;
        }

        private static string FindPythonDll(string baseInstallHome)
        {
            var dllFiles = Directory.EnumerateFiles(baseInstallHome, "python3*.dll");
            foreach (var file in dllFiles)
            {
                string fileName = Path.GetFileName(file).ToLower();
                if (fileName != "python3.dll" && fileName != "python33.dll")
                {
                    return file;
                }
            }
            return string.Empty;
        }

        public void StopBackend()
        {
            if (!_isInitialized) return;

            try
            {
                // Re-acquire/restore the thread state pointer before shut down
                PythonEngine.EndAllowThreads(_threadState);
                PythonEngine.Shutdown();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error shutting down Python engine: {ex.Message}");
            }
            finally
            {
                _isInitialized = false;
            }
        }

        public void Dispose()
        {
            StopBackend();
        }
    }
}