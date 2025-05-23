﻿using System.IO;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using WTT_BundleMaster.Services;

namespace WTT_BundleMaster;

public class ReplacerService : IDisposable
{
    private readonly LogService _logger;
    private readonly ConfigurationService _config;
    
    public ReplacerService(LogService logger, ConfigurationService config)
    {
        _logger = logger;
        _config = config;
    }
    
    public void Dispose()
    {
    }
    
    private bool IsBundleFile(string filePath)
    {
        const int signatureLength = 7;
        if (new FileInfo(filePath).Length < signatureLength) return false;

        Span<byte> buf = stackalloc byte[signatureLength];
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Read(buf);

        var sig = System.Text.Encoding.ASCII.GetString(buf);
        return sig.StartsWith("UnityFS", StringComparison.Ordinal)
               || sig.StartsWith("UnityWeb", StringComparison.Ordinal)
               || sig.StartsWith("UnityRaw", StringComparison.Ordinal);
    }


public async Task ProcessBundlesAsync(string inputDir, string outputDir, List<BundleRemapEntry>? remapEntries)
{
    var cabMap = remapEntries?
        .GroupBy(r => r.OldCabId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            g => g.Key,
            g => g.First().NewCabId,
            StringComparer.OrdinalIgnoreCase
        ) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var pathMap = remapEntries?
        .SelectMany(r => r.AssetRemaps)
        .GroupBy(a => a.OldPathId)
        .ToDictionary(g => g.Key, g => g.First().NewPathId) ?? new Dictionary<long, long>();

    try
    {
        var options = new ParallelOptions
        { 
            MaxDegreeOfParallelism = Environment.ProcessorCount 
        };

        var bundlePaths = Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories)
            .Where(IsBundleFile)
            .ToList();

        var total = bundlePaths.Count;
        var processed = 0;

        await Parallel.ForEachAsync(bundlePaths, options, async (bundlePath, ct) =>
        {
            try
            {
                var outputPath = Path.Combine(outputDir, Path.GetRelativePath(inputDir, bundlePath));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                var assetsManager = new AssetsManager();
                await Task.Run(() => ProcessSingleBundle(assetsManager, bundlePath, outputPath, cabMap, pathMap));

                var current = Interlocked.Increment(ref processed);
                var progress = (int)((double)current / total * 100);
                _logger.Log($"Processed {Path.GetFileName(bundlePath)} ({progress}%)");
            }
            catch (Exception ex)
            {
                _logger.Log($"Error processing {bundlePath}: {ex.Message}", LogLevel.Error);
            }
        });
    }
    catch (Exception ex)
    {
        _logger.Log($"Error processing bundle: {ex.Message}", LogLevel.Error);
    }
    finally
    {
        _logger.Log("Bundle processing completed successfully!", LogLevel.Success);
        Thread.Sleep(100);
    }
}
    
    public async Task ProcessSingleBundle(AssetsManager assetsManager, string inputPath, string outputPath, 
        Dictionary<string, string> cabMap, Dictionary<long, long> pathMap)
    {
        string tempOutputPath = Path.GetTempFileName();
        string finalTempPath = Path.GetTempFileName();

        try
        {
            var bundle = assetsManager.LoadBundleFile(inputPath);
            var assetsFile = assetsManager.LoadAssetsFileFromBundle(bundle, 0);

            bool modified = ProcessCabDependencies(assetsManager, assetsFile, cabMap);
            modified |= ProcessAssetsFile(assetsManager, assetsFile, pathMap);

            if (modified)
            {
                SaveModifiedBundle(bundle, assetsFile, tempOutputPath);

                assetsManager.UnloadBundleFile(bundle);
                assetsManager.UnloadAssetsFile(assetsFile);

                if (_config.Config.CompressBundles)
                {
                    CompressModifiedBundle(tempOutputPath, finalTempPath);
                    File.Move(finalTempPath, outputPath, overwrite: true);
                }
                else
                {
                    File.Move(tempOutputPath, outputPath, overwrite: true);
                }
            }
            else
            {
                File.Copy(inputPath, outputPath, overwrite: true);
            }
            assetsManager.UnloadAssetsFile(assetsFile);
            assetsManager.UnloadBundleFile(bundle);
        }
        catch (Exception ex)
        {
            _logger.Log($"Error processing budnle: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            SafeDelete(tempOutputPath);
            SafeDelete(finalTempPath);
        }
    }


    private bool ProcessAssetsFile(AssetsManager am, AssetsFileInstance assetsFile, Dictionary<long, long> pathMap)
    {
        bool modified = false;
        foreach (AssetFileInfo asset in assetsFile.file.AssetInfos)
        {
            AssetTypeValueField baseField = am.GetBaseField(assetsFile, asset);
            bool assetModified = false;

            if (pathMap.TryGetValue(asset.PathId, out long newPathId))
            {
                asset.PathId = newPathId;
                assetModified = true;
            }

            assetModified |= ReplacePathIdsInFields(baseField, pathMap);

            if (assetModified)
            {
                byte[] newAssetData = baseField.WriteToByteArray();
                asset.SetNewData(newAssetData);
                modified = true;
            }
        }
        return modified;
    }

    private bool ReplacePathIdsInFields(AssetTypeValueField field, Dictionary<long, long> pathMap)
    {
        bool modified = false;

        if (field.TemplateField.Children?.Count == 2 &&
            field.TemplateField.Children[0].Name == "m_FileID" &&
            field.TemplateField.Children[1].Name == "m_PathID")
        {
            AssetTypeValueField pathIdField = field.Get("m_PathID");
            long originalPathId = pathIdField.Value.AsLong;
            if (pathMap.TryGetValue(originalPathId, out long newPathId))
            {
                pathIdField.Value.AsLong = newPathId;
                modified = true;
            }
        }

        foreach (AssetTypeValueField child in field.Children)
        {
            modified |= ReplacePathIdsInFields(child, pathMap);
        }

        return modified;
    }

    private bool ProcessCabDependencies(AssetsManager assetsManager,AssetsFileInstance assetsFile, Dictionary<string, string> cabMap)
    {
        bool modified = false;

        var escapedKeys = cabMap.Keys.Select(Regex.Escape)
            .OrderByDescending(k => k.Length)
            .ToArray();
        string cabPattern = string.Join("|", escapedKeys);

        foreach (var extDep in assetsFile.file.Metadata.Externals)
        {
            string originalPath = extDep.PathName;
            string newPath = Regex.Replace(originalPath, cabPattern,
                m => cabMap[m.Value], 
                RegexOptions.IgnoreCase
            );

            if (newPath != originalPath)
            {
                extDep.PathName = newPath;
                modified = true;
            }
        }

        foreach (var asset in assetsFile.file.AssetInfos)
        {
            var baseField = assetsManager.GetBaseField(assetsFile, asset);
            modified |= ReplaceCabIdsInStrings(baseField, cabMap);
        }

        return modified;
    }
    private bool ReplaceCabIdsInStrings(AssetTypeValueField field, Dictionary<string, string> cabMap)
    {
        bool modified = false;

        if (field.TemplateField.ValueType == AssetValueType.String)
        {
            var original = field.Value.AsString;
            var escapedKeys = cabMap.Keys.Select(Regex.Escape)
                .OrderByDescending(k => k.Length)
                .ToArray();
            string pattern = string.Join("|", escapedKeys);
            var updated = Regex.Replace(original, pattern,
                m => cabMap[m.Value],
                RegexOptions.IgnoreCase
            );

            if (updated != original)
            {
                field.Value = new AssetTypeValue(AssetValueType.String, updated);
                modified = true;
            }
        }

        foreach (var child in field.Children)
        {
            modified |= ReplaceCabIdsInStrings(child, cabMap);
        }

        return modified;
    }
    private void SaveModifiedBundle(BundleFileInstance bundle, AssetsFileInstance assetsFile, string outputPath)
    {
        bundle.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(assetsFile.file);
    
        using var stream = File.Create(outputPath);
        using var writer = new AssetsFileWriter(stream);
        bundle.file.Write(writer);
    }

    private void CompressModifiedBundle(string inputPath, string outputPath)
    {
        var compressManager = new AssetsManager();
        var bundle = compressManager.LoadBundleFile(inputPath);
    
        try
        {
            using var outputStream = File.Create(outputPath);
            using var outputWriter = new AssetsFileWriter(outputStream);
            bundle.file.Pack(outputWriter, AssetBundleCompressionType.LZMA);
        }
        finally
        {
            compressManager.UnloadBundleFile(bundle);
        }
    }

    private void SafeDelete(string path, int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch (Exception ex)
            {
                _logger.Log($"Exception has occurred {ex.Message}", LogLevel.Error);
            }
        }
    }
}

