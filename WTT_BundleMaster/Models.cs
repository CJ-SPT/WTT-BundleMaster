namespace WTT_BundleMaster;

public class BundleRemapEntry
{
    public string OriginalBundlePath { get; set; }
    public string OldCabId { get; set; }
    public string NewCabId { get; set; }
    public List<AssetRemap> AssetRemaps { get; set; } = new List<AssetRemap>();
}

public class AssetRemap
{
    public string AssetName { get; set; }
    public string AssetType { get; set; }
    public long OldPathId { get; set; }
    public long NewPathId { get; set; }
}

public class BundleData
{
    public string CabId { get; set; }
    public List<AssetData> Assets { get; set; } = new List<AssetData>();
}

public class AssetData
{
    public long PathId { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
}

public class AppConfig
{
    public bool DarkMode { get; set; } = true;
    public bool LoadLastRemapOnStart { get; set; } = true;
    public string LastBundlePath { get; set; } = string.Empty;
    public string LastOutputPath { get; set; } = string.Empty;
    public bool Overwrite { get; set; } = true;
    public string LastRemapOutputPath { get; set; } = string.Empty;
    public string LastRemapGamePath { get; set; } = string.Empty;
    public string LastRemapSdkPath { get; set; } = string.Empty;
    public bool CompressBundles { get; set; } = true;
    
    public LogLevel LogLevel { get; set; } = LogLevel.Success;
}