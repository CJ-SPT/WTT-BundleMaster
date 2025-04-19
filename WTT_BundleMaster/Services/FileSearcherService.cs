using System.IO;

namespace WTT_BundleMaster.Services;

public class FileSearcherService(
    LogService logService,
    ConfigurationService config
    )
{
    private readonly List<string> _filesInDirectory = [];
    
    public async Task SearchAssetFiles(string searchToken)
    {
        await GetDirectoryManifest();
    }

    private async Task GetDirectoryManifest()
    {
        _filesInDirectory.AddRange(Directory.GetFiles(""));
        
        foreach (var file in _filesInDirectory)
        {
            
        }
    }
}