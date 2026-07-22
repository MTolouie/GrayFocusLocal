using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Windows;
using Wpf.Services;
using Wpf.Services.IService;
using Wpf.ViewModels;
using Wpf.Views;

namespace Wpf
{
    public partial class App : System.Windows.Application
    {
        private PythonEngineService? _pythonEngineService;
        private ServiceProvider? _serviceProvider;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Prevent WPF from closing the app when LoadingWindow closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var services = new ServiceCollection();

            // Core & Environment Services
            services.AddSingleton<PythonEnvironmentService>();
            services.AddSingleton<PythonEngineService>();
            services.AddSingleton<IImageProcessingService, ImageProcessingService>();
            services.AddSingleton<IRoiProcessorService, RoiProcessorService>();

            // Factories & ViewModels
            services.AddSingleton<IResultWindowFactory, ResultWindowFactory>();
            services.AddTransient<LoadingViewModel>();

            // CHANGED: Made MainViewModel a Singleton so its GPU state persists cleanly
            services.AddSingleton<MainViewModel>();

            // Views
            services.AddTransient<LoadingWindow>();
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            // 2. Show Loading Window & Execute Venv/Package Validation Routine
            var loadingVm = _serviceProvider.GetRequiredService<LoadingViewModel>();
            var loadingWindow = _serviceProvider.GetRequiredService<LoadingWindow>();
            loadingWindow.DataContext = loadingVm;
            loadingWindow.Show();

            await loadingVm.StartInitializationAsync();

            if (loadingVm.HasError)
            {
                MessageBox.Show($"Failed to setup environment:\n{loadingVm.ErrorMessage}",
                                "Environment Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // 3. Start Embedded Python Engine
            _pythonEngineService = _serviceProvider.GetRequiredService<PythonEngineService>();
            try
            {
                _pythonEngineService.StartBackend();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start the embedded Python engine: {ex.Message}",
                                "Backend Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // 4. Instantiate Main Window & Pass CUDA / GPU state
            var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();

            
            // Transfer the detected GPU support state from LoadingViewModel to MainViewModel
            viewModel.IsGpuSupported = loadingVm.IsGpuSupported;

            // Optional: Auto-select GPU (1) in the combobox if available, otherwise stay on CPU (0)
            if (viewModel.IsGpuSupported)
            {
                viewModel.SelectedDevice = 1; // GPU
            }
            else
            {
                viewModel.SelectedDevice = 0; // CPU
            }

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.DataContext = viewModel;

            // 5. Close Loading Window and hand over Main Window to WPF
            loadingWindow.Close();

            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose; // Restores standard close behavior
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _pythonEngineService?.StopBackend();
            _pythonEngineService?.Dispose();
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}