using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
using System.Data;
using System.Windows;
using Wpf.Services;
using Wpf.ViewModels;
using Wpf.Services.IService;

namespace Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private PythonBackendService? _pythonService;
        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. Always call the base method first to let WPF fire its built-in startup logic
            base.OnStartup(e);

            // 2. Build the ServiceCollection right here
            var services = new ServiceCollection();

            // Initialize and spin up the API background script automatically
            _pythonService = new PythonBackendService();
            try
            {
                _pythonService.StartBackend();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to launch Python API background process: {ex.Message}", "Backend Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // 3. Register your Onion Architecture dependencies
            services.AddHttpClient<IImageProcessingService, ImageProcessingService>();
            services.AddSingleton<IRoiProcessorService, RoiProcessorService>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();

            // 4. Create the service provider local container
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            // 5. Ask the container to resolve the Window and ViewModel
            var viewModel = serviceProvider.GetRequiredService<MainViewModel>();
            var mainWindow = serviceProvider.GetRequiredService<MainWindow>();

            // 6. Bind them together and show the UI
            mainWindow.DataContext = viewModel;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Forces the port connection shut and terminates the process when the user closes the WPF Window
            _pythonService?.StopBackend();
            _pythonService?.Dispose();
            base.OnExit(e);
        }
    }

}
