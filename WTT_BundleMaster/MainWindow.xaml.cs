using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using WTT_BundleMaster.Services;

namespace WTT_BundleMaster;


public partial class MainWindow
{
    private readonly ConfigurationService _configService = new ConfigurationService();

    public MainWindow()
    {
        InitializeComponent();
    
        var initialServices = new ServiceCollection();
        initialServices.AddWpfBlazorWebView();
        Resources["services"] = initialServices.BuildServiceProvider();
    
        _ = InitializeServicesAsync();
    }

    private async Task InitializeServicesAsync()
    {
        var serviceCollection = new ServiceCollection();
        
        var syncContext = new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher);
        serviceCollection.AddSingleton<SynchronizationContext>(syncContext);
        
        var configTask = _configService.InitializeAsync();
        
        serviceCollection.AddWpfBlazorWebView();
        serviceCollection.AddBlazorWebViewDeveloperTools();
        serviceCollection.AddSingleton<LogService>();
        serviceCollection.AddSingleton<IFileDialogService, WpfFileDialogService>();
        serviceCollection.AddScoped<RemapperService>();
        serviceCollection.AddScoped<ReplacerService>();

        serviceCollection.AddMudServices(config => 
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopCenter;
            
            config.SnackbarConfiguration.MaxDisplayedSnackbars = 5;
        });
        
        await configTask;
        serviceCollection.AddSingleton(_configService);
        
        await Dispatcher.InvokeAsync(() => 
        {
            Resources["services"] = serviceCollection.BuildServiceProvider();
        });
    }
}