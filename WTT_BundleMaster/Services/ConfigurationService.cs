using System.IO;
using System.Text;
using System.Text.Json;

namespace WTT_BundleMaster.Services;

public class ConfigurationService
{
    public event Action OnConfigUpdated;
    private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
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
        await _fileLock.WaitAsync();
        try
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
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task LoadConfigurationAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            if (!File.Exists(_configPath))
            {
                _config = new AppConfig();
                return;
            }

            using var fileStream = new FileStream(
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
        finally
        {
            _fileLock.Release();
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