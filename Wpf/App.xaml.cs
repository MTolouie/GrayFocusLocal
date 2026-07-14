using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using Wpf.Services;
using Wpf.ViewModels;
using Wpf.Services.IService;
using Wpf.Views;

namespace Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        // CHANGED: was PythonBackendService (subprocess launcher).
        // PythonEngineService owns the embedded Python.NET runtime instead,
        // and now lives in the DI container as a singleton so the same
        // GrayscaleProcessor instance is shared by ImageProcessingService.
        private PythonEngineService? _pythonEngineService;
        private ServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();

            // CHANGED: no more services.AddHttpClient<IImageProcessingService, ...>()
            // — there's no HttpClient involved anymore. PythonEngineService is
            // registered as a singleton (one embedded interpreter, one
            // GrayscaleProcessor for the app's lifetime) and injected into
            // ImageProcessingService the normal DI way.
            services.AddSingleton<PythonEngineService>();
            services.AddSingleton<IImageProcessingService, ImageProcessingService>();
            services.AddSingleton<IRoiProcessorService, RoiProcessorService>();

            // NEW: ResultWindow/ResultViewModel need a runtime value
            // (the batch's preview references) that the container can't
            // supply on its own, so they're built through this factory
            // instead of being registered directly.
            services.AddSingleton<IResultWindowFactory, ResultWindowFactory>();

            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            // Start the embedded Python engine through the same instance the
            // container will hand to ImageProcessingService.
            _pythonEngineService = _serviceProvider.GetRequiredService<PythonEngineService>();
            try
            {
                _pythonEngineService.StartBackend();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start the embedded Python engine: {ex.Message}", "Backend Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

            mainWindow.DataContext = viewModel;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Shuts down the embedded Python interpreter cleanly instead of
            // killing a subprocess.
            _pythonEngineService?.StopBackend();
            _pythonEngineService?.Dispose();
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}