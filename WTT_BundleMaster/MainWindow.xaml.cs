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
    
        _ = InitializeServices();
    }

    private async Task InitializeServices()
    {
        var serviceCollection = new ServiceCollection();
        var syncContext = new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher);
        serviceCollection.AddSingleton<SynchronizationContext>(syncContext);

        await _configService.InitializeAsync();
        serviceCollection.AddWpfBlazorWebView();
        serviceCollection.AddBlazorWebViewDeveloperTools();
        serviceCollection.AddSingleton<LogService>();
        serviceCollection.AddSingleton<IFileDialogService, WpfFileDialogService>();
        serviceCollection.AddScoped<RemapperService>();
        serviceCollection.AddScoped<ReplacerService>();
        serviceCollection.AddScoped<FileSearcherService>();
        serviceCollection.AddMudServices(config => 
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopCenter;
            config.SnackbarConfiguration.MaxDisplayedSnackbars = 5;
        });

        serviceCollection.AddSingleton(_configService);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        Resources["services"] = serviceProvider;
    }
}