using System.IO;
using System.Text;
using System.Text.Json;

namespace WTT_BundleMaster.Services;

public class ConfigurationService
{
    public event Action OnConfigUpdated;
    private readonly string _configPath = "userSettings.json";
    private AppConfig _config = new();

    public AppConfig Config => _config;

    public async Task InitializeAsync()
    {
        await LoadConfigurationAsync();
    
        OnConfigUpdated?.Invoke();
    }
    public async Task SaveConfigurationAsync()
    {
        var json = JsonSerializer.Serialize(_config);
        await using var fileStream = new FileStream(
            _configPath, 
            FileMode.Create, 
            FileAccess.Write, 
            FileShare.None, 
            bufferSize: 4096, 
            useAsync: true
        );
            
        var bytes = Encoding.UTF8.GetBytes(json);
        await fileStream.WriteAsync(bytes);
        OnConfigUpdated?.Invoke();
    }

    public async Task LoadConfigurationAsync()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                _config = new AppConfig();
                return;
            }

            await using var fileStream = new FileStream(
                _configPath, 
                FileMode.Open, 
                FileAccess.Read, 
                FileShare.Read, 
                bufferSize: 4096, 
                useAsync: true
            );

            var result = await JsonSerializer.DeserializeAsync<AppConfig>(fileStream);
            _config = result ?? new AppConfig();
            ValidatePaths();
        }
        catch
        {
            _config = new AppConfig();
        }
    }

    private void ValidatePaths()
    {
        if (!Directory.Exists(_config.LastBundlePath))
            _config.LastBundlePath = string.Empty;
        
        if (!Directory.Exists(_config.LastOutputPath))
            _config.LastOutputPath = string.Empty;
    }
    public async Task UpdateConfigAsync(Action<AppConfig> updateAction)
    {
        updateAction(_config);
        await SaveConfigurationAsync(); 
    }
}