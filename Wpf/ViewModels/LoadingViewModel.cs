using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using Wpf.Services;

namespace Wpf.ViewModels
{
    public partial class LoadingViewModel : ObservableObject
    {
        private readonly PythonEnvironmentService _envService;

        [ObservableProperty] private string _statusMessage = "Checking Python environment...";
        [ObservableProperty] private bool _isIndeterminate = true;
        [ObservableProperty] private double _progressValue = 0;
        [ObservableProperty] private bool _isCompleted = false;
        [ObservableProperty] private bool _hasError = false;
        [ObservableProperty] private string _errorMessage = string.Empty;
        [ObservableProperty] private bool _isGpuSupported = false;
        [ObservableProperty] private bool _requiresManualCudaToolkit = false;

        public LoadingViewModel(PythonEnvironmentService envService)
        {
            _envService = envService;
        }

        public async Task StartInitializationAsync()
        {
            try
            {
                StatusMessage = "Checking virtual environment folder...";
                bool createdVenv = _envService.EnsureVenvExists();

                if (createdVenv)
                {
                    StatusMessage = "Virtual environment created.";
                    await Task.Delay(1000);
                }

                StatusMessage = "Detecting CUDA & GPU hardware...";
                var (isGpuAvailable, cupyPackage, requiresToolkit) = _envService.DetectCudaCapabilities();

                IsGpuSupported = isGpuAvailable;
                RequiresManualCudaToolkit = requiresToolkit;

                if (isGpuAvailable)
                {
                    StatusMessage = $"NVIDIA CUDA detected. Matching package: {cupyPackage}";
                    await Task.Delay(1000);
                }
                else
                {
                    StatusMessage = "No compatible NVIDIA GPU found (CUDA 11+ required). Defaulting to CPU mode.";
                    await Task.Delay(800);
                }

                StatusMessage = "Checking required Python packages...";
                bool packagesReady = await _envService.ArePackagesInstalledAsync(cupyPackage);

                if (!packagesReady)
                {
                    StatusMessage = "Installing required Python packages...";
                    await Task.Delay(1000);

                    await _envService.InstallPackagesAsync(cupyPackage, msg =>
                    {
                        StatusMessage = msg;
                    });
                }

                StatusMessage = "Python environment is ready!";
                ProgressValue = 100;
                IsIndeterminate = false;
                await Task.Delay(500);

                IsCompleted = true;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = ex.Message;
                StatusMessage = "Initialization failed.";
            }
        }
    }
}