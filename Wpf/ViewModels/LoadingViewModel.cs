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
                    StatusMessage = "Virtual environment created. Packages will be downloaded.";
                    await Task.Delay(1000);
                }

                StatusMessage = "Checking required Python packages...";
                bool packagesReady = await _envService.ArePackagesInstalledAsync();

                if (!packagesReady)
                {
                    StatusMessage = "Required packages missing in venv. Starting download via pip...";
                    await Task.Delay(1200); // Give user time to read status message

                    await _envService.InstallPackagesAsync(msg =>
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