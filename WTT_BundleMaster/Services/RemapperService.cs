using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using WTT_BundleMaster.Services;

namespace WTT_BundleMaster
{
    public class RemapperService : IDisposable
    {
        private readonly LogService _logService;
        private readonly BundleMapper _mapper = new BundleMapper();
        private readonly IFileDialogService _fileDialogService;
        private readonly ConfigurationService _config;
        private string _gamePath;
        
        /// <summary>
        /// The path to the root of the tarkov installation
        /// </summary>
        public string GamePath
        {
            get => _gamePath;
            set
            {
                if (_gamePath != value)
                {
                    _gamePath = value;
                    NotifyStateChanged();
                }
            }
        }
        
        /// <summary>
        /// The path to the `EscapeFromTarkov_Data` directory
        /// </summary>
        public string GameDataPath => Path.Combine(_gamePath, "EscapeFromTarkov_Data");
        
        /// <summary>
        /// The path to the `EscapeFromTarkov_Data/StreamingAssets/Windows` directory
        /// </summary>
        public string WindowsPath => Path.Combine(GameDataPath, "StreamingAssets", "Windows");
        
        private string _sdkPath;
        public string SdkPath
        {
            get => _sdkPath;
            set
            {
                if (_sdkPath != value)
                {
                    _sdkPath = value;
                    NotifyStateChanged();
                }
            }
        }
        private string _outputPath;
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (_outputPath != value)
                {
                    _outputPath = value;
                    NotifyStateChanged();
                }
            }
        }
        public bool IsProcessing { get; private set; }
        public double Progress { get; private set; }
        public string CurrentFile { get; private set; }
        
        private bool _isRemapLoaded;
        public bool IsRemapLoaded
        {
            get => _isRemapLoaded;
            set
            {
                if (_isRemapLoaded != value)
                {
                    _isRemapLoaded = value;
                    NotifyStateChanged();
                }
            }
        }
        
        public void Dispose()
        {
        }

        public event Action OnStateChanged;

        public RemapperService(IFileDialogService fileDialogService, 
            LogService logService, 
            ConfigurationService config)
        {
            _config = config;
            _fileDialogService = fileDialogService;
            _logService = logService;
            GamePath = _config.Config.LastRemapGamePath;
            SdkPath = _config.Config.LastRemapSdkPath;
            OutputPath = _config.Config.LastRemapOutputPath;
            config.OnConfigUpdated += () => 
            {

                config.SaveConfigurationAsync();
            };
            Task.Run(async () => {
                await Task.Delay(250);
                await LoadDefaultRemapAsync();
            });
        }
        
        public async Task LoadDefaultRemapAsync()
        {
            try 
            {
                if (!_config.Config.LoadLastRemapOnStart) return;
        
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("REMAP2019-2022.json", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(resourceName))
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    using var reader = new StreamReader(stream);
                    var json = await reader.ReadToEndAsync();
            
                    RemapEntries = JsonConvert.DeserializeObject<List<BundleRemapEntry>>(json);
                    IsRemapLoaded = true;
                    _logService.Log("Default remap loaded successfully", LogLevel.Success);
                }
            }
            catch (Exception ex)
            {
                _logService.Log($"Remap load error: {ex.Message}", LogLevel.Error);
            }
        }
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            _logService.Log(message, level);
            NotifyStateChanged();
        }


        public async Task PickGamePath()
        {
            var newPath = await _fileDialogService.PickDirectoryAsync("Select Game Path");
            if (!string.IsNullOrEmpty(newPath))
            {
                GamePath = newPath; 
                await _config.UpdateConfigAsync(c => c.LastRemapGamePath = newPath);
                NotifyStateChanged();
            }
        }

        public async Task PickSdkPath()
        {
            var newPath = await _fileDialogService.PickDirectoryAsync("Select SDK Path");
            if (!string.IsNullOrEmpty(newPath))
            {
                SdkPath = newPath;
                await _config.UpdateConfigAsync(c => c.LastRemapSdkPath = newPath);
                NotifyStateChanged();
            }
        }

        public async Task PickOutputPath()
        {
            OutputPath = await _fileDialogService.PickSaveFileAsync("JSON Files|*.json", "Save Remap File");
            if (!string.IsNullOrEmpty(OutputPath))
            {
                await _config.UpdateConfigAsync(c => {
                    c.LastRemapOutputPath = OutputPath;
                });
                NotifyStateChanged();
            }
        }
    public List<BundleRemapEntry>? RemapEntries { get; private set; }

    public async Task LoadRemapAsync(string path)
    {
        const int maxRetries = 5;
        const int delayMs = 100;
        int retryCount = 0;
        bool success = false;

        try
        {
            while (retryCount <= maxRetries)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path);
                    RemapEntries = JsonConvert.DeserializeObject<List<BundleRemapEntry>>(json);
                    Log($"Loaded remap file with {RemapEntries.Sum(r => r.AssetRemaps.Count)} entries");
                    IsRemapLoaded = true;
                    success = true;
                    return;
                }
                catch (IOException ex) when (ex.HResult == -2147024864 && retryCount < maxRetries)
                {
                    retryCount++;
                    await Task.Delay(delayMs);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error loading remap file: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (!success)
            {
                RemapEntries = null;
                IsRemapLoaded = false;
            }
            NotifyStateChanged();
        }
    }

    public async Task GenerateRemapAsync()
    {
        IsProcessing = true;
        Progress = 0;
        _logService.Clear();
        NotifyStateChanged();

        try
        {
            var modifiedFiles = Directory.EnumerateFiles(SdkPath, "*", SearchOption.AllDirectories)
                .AsParallel()
                .Where(_mapper.IsValidBundle)
                .ToList();

            var total = modifiedFiles.Count;
            var remapData = new ConcurrentBag<BundleRemapEntry>();
            var processed = 0;
            var lastUpdate = DateTime.MinValue;
            
            var parallelOptions = new ParallelOptions 
            {
                MaxDegreeOfParallelism = Math.Max(Environment.ProcessorCount - 1, 1),
                TaskScheduler = TaskScheduler.Default
            };

            var batches = modifiedFiles
                .Select((x, i) => new { Index = i, Value = x })
                .GroupBy(x => x.Index / 100)
                .Select(g => g.Select(x => x.Value).ToList());

            await Task.Run(async () =>
            {
                foreach (var batch in batches)
                {
                    await Parallel.ForEachAsync(batch, parallelOptions, async (modifiedPath, ct) =>
                    {
                        try
                        {
                            var entry = ProcessFileInternal(modifiedPath);
                            if (entry != null)
                            {
                                remapData.Add(entry);
                            }
                        }
                        finally
                        {
                            var newProcessed = Interlocked.Increment(ref processed);
                            
                            if (DateTime.Now - lastUpdate > TimeSpan.FromMilliseconds(250))
                            {
                                Progress = (double)newProcessed / total * 100;
                                NotifyStateChanged();
                                lastUpdate = DateTime.Now;
                            }
                        }
                    });
                }
            });


            string tempFilePath = Path.GetTempFileName();
            try
            {
                await using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var streamWriter = new StreamWriter(tempFileStream))
                using (var jsonWriter = new JsonTextWriter(streamWriter) { Formatting = Formatting.Indented })
                {
                    var serializer = new JsonSerializer();
                    serializer.Serialize(jsonWriter, remapData);
                    await jsonWriter.FlushAsync(); 
                }

                if (File.Exists(OutputPath))
                    File.Delete(OutputPath);
                File.Move(tempFilePath, OutputPath);
            }
            finally
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }

            Log($"Processed {modifiedFiles.Count} files, {remapData.Sum(r => r.AssetRemaps.Count)} assets remapped", LogLevel.Success);
            if (!string.IsNullOrEmpty(OutputPath))
            {
                await LoadRemapAsync(OutputPath);
            }
        }
        finally
        {
            IsProcessing = false;
            Progress = 0;
            NotifyStateChanged();
        }
    }


        private void NotifyStateChanged()
        {
            OnStateChanged?.Invoke();
        }


        private BundleRemapEntry? ProcessFileInternal(string modifiedPath)
        {
            var currentFile = Path.GetRelativePath(SdkPath, modifiedPath);
            try
            {
                var modifiedData = _mapper.ProcessBundle(modifiedPath);
                var originalPath = Path.Combine(WindowsPath, currentFile);

                if (!File.Exists(originalPath))
                {
                    Log($"Skipping {currentFile} - original not found", LogLevel.Warning);
                    return null;
                }

                var originalData = _mapper.ProcessBundle(originalPath);

                if (modifiedData.Assets == null || originalData?.Assets == null)
                {
                    Log($"Invalid bundle data in {currentFile}", LogLevel.Warning);
                    return null;
                }

                return CreateRemapEntry(currentFile, modifiedData, originalData);
            }
            catch (Exception ex)
            {
                Log($"Error processing {currentFile}: {ex.Message}", LogLevel.Error);
                return null;
            }
        }
        private BundleRemapEntry CreateRemapEntry(string currentFile, BundleData modifiedData, BundleData originalData)
        {

            if (modifiedData.Assets.Count == 0 || originalData.Assets.Count == 0)
                return null;

            var originalAssetsDict = new Dictionary<(string Name, string Type), AssetData>(originalData.Assets.Count);
    
            foreach (var a in originalData.Assets)
            {
                if (string.IsNullOrEmpty(a.Name) || a.PathId == 0) continue;
                var key = (a.Name, a.Type);
                if (!originalAssetsDict.ContainsKey(key))
                {
                    originalAssetsDict[key] = a;
                }
            }

            var assetRemaps = new List<AssetRemap>(Math.Min(modifiedData.Assets.Count, originalData.Assets.Count));
    
            foreach (var modifiedAsset in modifiedData.Assets)
            {
                if (string.IsNullOrEmpty(modifiedAsset.Name) || modifiedAsset.PathId == 0) continue;
        
                var key = (modifiedAsset.Name, modifiedAsset.Type);
                if (originalAssetsDict.TryGetValue(key, out var originalAsset))
                {
                    assetRemaps.Add(new AssetRemap
                    {
                        AssetName = modifiedAsset.Name,
                        AssetType = modifiedAsset.Type,
                        OldPathId = modifiedAsset.PathId,
                        NewPathId = originalAsset.PathId
                    });
                }
            }

            return (assetRemaps.Count > 0 
                ? new BundleRemapEntry
                {
                    OriginalBundlePath = currentFile,
                    OldCabId = modifiedData.CabId,
                    NewCabId = originalData.CabId,
                    AssetRemaps = assetRemaps
                }
                : null) ?? throw new InvalidOperationException();
        }

    }
    
        public class BundleMapper
    {
        private readonly Dictionary<string, string> _typeCache = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();
        
        public bool IsValidBundle(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var reader = new AssetsFileReader(stream);

                return reader.ReadStringLength(7) == "UnityFS";
            }
            catch
            {
                return false;
            }
        }
        
        private bool IsValidAssetName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   name != "unnamed" &&
                   name != "unnamed_shader" &&
                   name != "error_getting_name";
        }

        public BundleData ProcessBundle(string path)
        {
            if (!IsValidBundle(path))
                return new BundleData { CabId = "invalid", Assets = new List<AssetData>() };

            var am = new AssetsManager();
            try
            {
                var bundle = am.LoadBundleFile(path);
                if (bundle?.file == null)
                    return new BundleData { CabId = "invalid", Assets = new List<AssetData>() };

                var data = new BundleData
                {
                    CabId = GetBundleCabId(bundle, am),
                    Assets = new List<AssetData>()
                };

                for (int i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                {
                    var assetsFile = am.LoadAssetsFileFromBundle(bundle, i);
                    if (assetsFile?.file?.AssetInfos == null) continue;

                    foreach (var asset in assetsFile.file.AssetInfos)
                    {
                        var baseField = am.GetBaseField(assetsFile, asset);
                        if (baseField == null) continue;

                        var assetName = GetAssetName(baseField);
                        if (!IsValidAssetName(assetName)) continue;

                        data.Assets.Add(new AssetData
                        {
                            PathId = asset.PathId,
                            Name = assetName,
                            Type = GetAssetType(baseField)
                        });
                    }
                }

                am.UnloadAll();
                return data;
            }
            finally
            {
                am.UnloadAll();
            }
        }
        private string GetBundleCabId(BundleFileInstance bundle, AssetsManager am)
        {

            AssetsFileInstance assetsFile = am.LoadAssetsFileFromBundle(bundle, 0);
            return assetsFile.name;

        }
        private string GetAssetName(AssetTypeValueField asset)
        {
            try 
            {
                if (GetAssetType(asset) == "Shader")
                {
                    var parsedForm = asset["m_ParsedForm"];
                    if (parsedForm != null && !parsedForm.IsDummy)
                    {
                        return parsedForm["m_Name"]?.AsString ?? "unnamed_shader";
                    }
                }
                return asset["m_Name"]?.AsString ?? "unnamed";
            }
            catch
            {
                return "error_getting_name";
            }
        }

        private string GetAssetType(AssetTypeValueField asset)
        {
            var key = asset.TemplateField.Type + asset.TemplateField.ValueType;
            if (!_typeCache.TryGetValue(key, out var type))
            {
                type = !string.IsNullOrEmpty(asset.TemplateField.Type) 
                    ? asset.TemplateField.Type
                    : asset.TemplateField.ValueType.ToString();
                _typeCache[key] = type;
            }
            return type;
        }
    }
    
}
