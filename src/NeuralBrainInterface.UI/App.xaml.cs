using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.IO;
using System.Reflection;
using NeuralBrainInterface.Core.Configuration;
using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Services;
using NeuralBrainInterface.UI.Services;

namespace NeuralBrainInterface.UI;

/// <summary>
/// Main application class that handles initialization, configuration, and graceful shutdown
/// Implements Requirements 4.1, 4.2, 4.3, 5.1, 7.1, 7.4, 8.1, 9.1, 10.7, 13.6, 13.7
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private ApplicationStartupService? _startupService;
    private ILogger<App>? _logger;
    private bool _isShuttingDown = false;

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            // Set up unhandled exception handlers for better error reporting
            SetupExceptionHandlers();
            
            // Create application directories if they don't exist
            await CreateApplicationDirectoriesAsync();
            
            // Build and configure the host
            var builder = Host.CreateDefaultBuilder(e.Args);
            
            builder.ConfigureAppConfiguration((context, config) =>
            {
                // Add configuration from multiple sources
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
                config.AddEnvironmentVariables("NEURALBRAIN_");
                config.AddCommandLine(e.Args);
            });
            
            builder.ConfigureServices((context, services) =>
            {
                // Add core services
                services.AddNeuralBrainInterfaceCore(context.Configuration);
                
                // Add UI services
                services.AddSingleton<UIManager>();
                services.AddSingleton<IUIManager>(provider => provider.GetRequiredService<UIManager>());
                services.AddSingleton<MainWindow>();
                
                // Add logging configuration
                services.AddLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                    // File logging would require additional NuGet package
                    // logging.AddFile("Logs/neuralbrain-{Date}.log");
                });
            });

            _host = builder.Build();
            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            
            _logger.LogInformation("Neural Brain Interface application starting...");
            _logger.LogInformation($"Version: {Assembly.GetExecutingAssembly().GetName().Version}");
            _logger.LogInformation($"Environment: {_host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName}");
            
            // Start the host
            await _host.StartAsync();
            
            // Validate system requirements before showing configuration
            var systemValidation = await ValidateSystemRequirementsAsync();
            if (!systemValidation.IsValid)
            {
                ShowSystemRequirementsError(systemValidation);
                Shutdown(1);
                return;
            }
            
            // Show startup configuration dialog
            _logger.LogInformation("Showing startup configuration dialog...");
            var configWindow = new StartupConfigurationWindow(_host.Services);
            var configResult = configWindow.ShowDialog();
            
            if (configResult != true)
            {
                _logger.LogInformation("User cancelled configuration, exiting application");
                Shutdown(0);
                return;
            }
            
            _logger.LogInformation("Configuration completed, initializing application...");
            
            // Get the startup service and initialize the application
            _startupService = _host.Services.GetRequiredService<ApplicationStartupService>();
            var initializationSuccessful = await _startupService.InitializeApplicationAsync();
            
            if (!initializationSuccessful)
            {
                _logger.LogError("Application initialization failed");
                ShowInitializationError();
                Shutdown(1);
                return;
            }
            
            _logger.LogInformation("Application initialization completed successfully");
            
            // Show main window after successful initialization
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            
            _logger.LogInformation("Neural Brain Interface application started successfully");
            
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Critical error during application startup");
            ShowCriticalError(ex);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_isShuttingDown)
            return;
            
        _isShuttingDown = true;
        
        try
        {
            _logger?.LogInformation("Neural Brain Interface application shutting down...");
            
            // Graceful shutdown through startup service
            if (_startupService != null)
            {
                await _startupService.ShutdownApplicationAsync();
            }
            
            _logger?.LogInformation("Application shutdown completed successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during application shutdown");
            // Don't prevent shutdown, but log the error
        }
        finally
        {
            try
            {
                if (_host != null)
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                    _host.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing host: {ex.Message}");
            }
            
            base.OnExit(e);
        }
    }

    /// <summary>
    /// Set up global exception handlers for better error reporting
    /// </summary>
    private void SetupExceptionHandlers()
    {
        // Handle unhandled exceptions in the main UI thread
        DispatcherUnhandledException += (sender, e) =>
        {
            _logger?.LogCritical(e.Exception, "Unhandled exception in UI thread");
            ShowCriticalError(e.Exception);
            e.Handled = true;
            Shutdown(1);
        };
        
        // Handle unhandled exceptions in background threads
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            _logger?.LogCritical(exception, "Unhandled exception in background thread");
            
            if (e.IsTerminating)
            {
                // Application is terminating, show error and exit
                Dispatcher.Invoke(() =>
                {
                    ShowCriticalError(exception);
                    Shutdown(1);
                });
            }
        };
        
        // Handle task exceptions
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            _logger?.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved(); // Prevent the process from terminating
        };
    }

    /// <summary>
    /// Create necessary application directories
    /// </summary>
    private async Task CreateApplicationDirectoriesAsync()
    {
        try
        {
            var directories = new[]
            {
                "Data",
                "BrainFiles", 
                "Memory",
                "Logs",
                "Temp",
                "Backups"
            };
            
            foreach (var directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create application directories: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validate system requirements before initialization
    /// </summary>
    private async Task<SystemValidationResult> ValidateSystemRequirementsAsync()
    {
        try
        {
            var result = new SystemValidationResult { IsValid = true };
            
            // Check minimum memory requirements (Requirements 4.1, 4.2)
            var totalMemory = GC.GetTotalMemory(false);
            // Use a simple approximation for available memory check
            var workingSet = Environment.WorkingSet;
            
            if (workingSet > 2L * 1024 * 1024 * 1024) // More than 2GB working set indicates low memory
            {
                result.Warnings.Add("System memory usage is high. Performance may be affected.");
            }
            
            // Check CPU requirements (Requirements 4.3)
            if (Environment.ProcessorCount < 2)
            {
                result.IsValid = false;
                result.Errors.Add("Insufficient CPU cores. At least 2 CPU cores are required.");
            }
            
            // Check disk space requirements
            var currentDrive = new DriveInfo(Directory.GetCurrentDirectory());
            if (currentDrive.AvailableFreeSpace < 500 * 1024 * 1024) // Less than 500MB
            {
                result.IsValid = false;
                result.Errors.Add("Insufficient disk space. At least 500MB of free disk space is required.");
            }
            
            // Check .NET version requirements
            var dotnetVersion = Environment.Version;
            if (dotnetVersion.Major < 8)
            {
                result.IsValid = false;
                result.Errors.Add($"Unsupported .NET version {dotnetVersion}. .NET 8.0 or higher is required.");
            }
            
            // Check Windows version for hardware access (Requirements 7.1, 7.4, 8.1)
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var version = Environment.OSVersion.Version;
                if (version.Major < 10) // Windows 10 or higher required for modern hardware APIs
                {
                    result.Warnings.Add("Windows 10 or higher is recommended for optimal hardware device support.");
                }
            }
            
            await Task.CompletedTask;
            return result;
        }
        catch (Exception ex)
        {
            return new SystemValidationResult
            {
                IsValid = false,
                Errors = { $"Error validating system requirements: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Show system requirements error dialog
    /// </summary>
    private void ShowSystemRequirementsError(SystemValidationResult validation)
    {
        var message = "System Requirements Not Met:\n\n";
        message += string.Join("\n", validation.Errors);
        
        if (validation.Warnings.Any())
        {
            message += "\n\nWarnings:\n";
            message += string.Join("\n", validation.Warnings);
        }
        
        MessageBox.Show(
            message,
            "System Requirements Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Show initialization error dialog
    /// </summary>
    private void ShowInitializationError()
    {
        var message = "Failed to initialize the Neural Brain Interface application.\n\n" +
                     "This could be due to:\n" +
                     "• Insufficient system resources\n" +
                     "• Missing device permissions\n" +
                     "• Corrupted configuration files\n" +
                     "• Hardware compatibility issues\n\n" +
                     "Please check the log files in the Logs directory for detailed error information.\n" +
                     "Try restarting the application or adjusting the configuration settings.";
        
        MessageBox.Show(
            message,
            "Initialization Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Show critical error dialog
    /// </summary>
    private void ShowCriticalError(Exception? ex)
    {
        var message = "A critical error occurred in the Neural Brain Interface application.\n\n";
        
        if (ex != null)
        {
            message += $"Error: {ex.Message}\n\n";
            
            if (ex.InnerException != null)
            {
                message += $"Details: {ex.InnerException.Message}\n\n";
            }
        }
        
        message += "The application will now close. Please check the log files for more information.";
        
        MessageBox.Show(
            message,
            "Critical Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// System validation result
    /// </summary>
    private class SystemValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}