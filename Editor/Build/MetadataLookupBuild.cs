using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// This is a bit of a hack, but we need to ensure that our metadata is not stripped on build,
// so at build time we clone them into a temporary resources directory. Cloning works since 
// each metadata object holds a reference to the thing it belongs to and we can just use the 
// clone at runtime instead. After we've created the build we delete the directory.
public class MetadataLookupPreprocessBuild : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }

    public static bool HadResourcesDirectory = false;

    CustomAssetMetadata CloneCustomAssetMetadata(CustomAssetMetadata assetMetadata, string basePath, ref int index)
    {
        var clone = UnityEngine.Object.Instantiate(assetMetadata);
        var name = $"{UnityEngine.EntityId.ToULong(assetMetadata.GetEntityId())}-{index}"; index++;
        AssetDatabase.CreateAsset(clone, $"{basePath}/{name}.asset");
        return clone;
    }

    /// <summary>Returns the asset path of every material in the project.</summary>
    public static List<string> MaterialPaths()
    {
        var paths = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        return paths;
    }

    /// <summary>
    /// Returns the asset paths that can hold metadata: every labelled asset, plus every material
    /// since materials were given metadata before assets were labelled.
    /// </summary>
    public static List<string> MetadataPaths()
    {
        var paths = new HashSet<string>(MaterialPaths());
        foreach (var guid in AssetDatabase.FindAssets("l:" + AssetMetadataUtility.kAssetLabel))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        return new List<string>(paths);
    }

    /// <summary>Returns all metadata sub-assets stored at the supplied asset paths.</summary>
    public static List<CustomAssetMetadata> FindMetadata(IEnumerable<string> assetPaths)
    {
        var metadata = new List<CustomAssetMetadata>();
        foreach (var assetPath in assetPaths)
        {
            foreach (var item in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (item is CustomAssetMetadata found)
                    metadata.Add(found);
            }
        }
        return metadata;
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        const string basePath = "Assets/Resources/" + MetadataLookup.kResourcePath;
        AssetDatabase.StartAssetEditing();
        try
        {
            HadResourcesDirectory = System.IO.Directory.Exists("Assets/Resources/");
            if (System.IO.Directory.Exists(basePath))
                System.IO.Directory.Delete(basePath, true);
            System.IO.Directory.CreateDirectory(basePath);
            var list = ScriptableObject.CreateInstance<MetadataLookupAsset>();

            var allMetadata = new List<CustomAssetMetadata>();
            allMetadata.AddRange(FindMetadata(MetadataPaths()));

            int index = 0;
            for (int i = 0; i < allMetadata.Count; i++)
                allMetadata[i] = CloneCustomAssetMetadata(allMetadata[i], basePath, ref index);

            list.allMetadata = allMetadata.ToArray();
            AssetDatabase.CreateAsset(list, $"{basePath}/{MetadataLookup.kAssetName}.asset");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

}


public class MetadataLookupPostprocessBuild : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        AssetDatabase.StartAssetEditing();
        try
        {
            if (!MetadataLookupPreprocessBuild.HadResourcesDirectory)
            {
                const string basePath = "Assets/Resources";
                if (System.IO.Directory.Exists(basePath))
                    System.IO.Directory.Delete(basePath, true);

                const string metaFile = basePath + ".meta";
                if (System.IO.File.Exists(metaFile))
                    System.IO.File.Delete(metaFile);
            } else
            {
                const string basePath = "Assets/Resources/" + MetadataLookup.kResourcePath;
                if (System.IO.Directory.Exists(basePath))
                    System.IO.Directory.Delete(basePath, true);

                const string metaFile = basePath + ".meta";
                if (System.IO.File.Exists(metaFile))
                    System.IO.File.Delete(metaFile);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }
}
