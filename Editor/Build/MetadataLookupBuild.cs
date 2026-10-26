using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Remembers which scenes a build is for; the preprocess step is not told
public class MetadataLookupPrepareBuild : BuildPlayerProcessor
{
    public override int callbackOrder => 0;

    public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
    {
        MetadataLookupPreprocessBuild.BuildScenes = buildPlayerContext.BuildPlayerOptions.scenes;
    }
}

// This is a bit of a hack, but we need to ensure that our metadata is not stripped on build,
// so at build time we clone them into a temporary resources directory. Cloning works since 
// each metadata object holds a reference to the thing it belongs to and we can just use the 
// clone at runtime instead. After we've created the build we delete the directory.
// Only metadata of assets the build includes anyway is cloned, since a clone pulls its asset into the build.
public class MetadataLookupPreprocessBuild : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }

    public static bool HadResourcesDirectory = false;

    internal static string[] BuildScenes;

    CustomAssetMetadata CloneCustomAssetMetadata(CustomAssetMetadata assetMetadata, string basePath, ref int index)
    {
        var clone = UnityEngine.Object.Instantiate(assetMetadata);
        var name = $"{index}"; index++;
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

    const string kResourcesPath = "Assets/Resources";
    const string kBasePath = kResourcesPath + "/" + MetadataLookup.kResourcePath;
    const string kCreatedResourcesKey = "CustomAssetMetadata.CreatedResourcesDirectory";

    public void OnPreprocessBuild(BuildReport report)
    {
        RemoveBuildResources();

        HadResourcesDirectory = System.IO.Directory.Exists(kResourcesPath);
        // Survives domain reloads, unlike a static
        SessionState.SetBool(kCreatedResourcesKey, !HadResourcesDirectory);

        // The postprocess step is skipped when a build fails
        EditorApplication.delayCall -= RemoveBuildResources;
        EditorApplication.delayCall += RemoveBuildResources;

        AssetDatabase.StartAssetEditing();
        try
        {
            System.IO.Directory.CreateDirectory(kBasePath);
            var list = ScriptableObject.CreateInstance<MetadataLookupAsset>();

            var builtAssets = BuiltAssetPaths(BuildScenes ?? EnabledBuildSettingsScenes());
            var allMetadata = new List<CustomAssetMetadata>();
            allMetadata.AddRange(FindMetadata(MetadataPaths().Where(builtAssets.Contains)));

            int index = 0;
            for (int i = 0; i < allMetadata.Count; i++)
                allMetadata[i] = CloneCustomAssetMetadata(allMetadata[i], kBasePath, ref index);

            list.allMetadata = allMetadata.ToArray();
            AssetDatabase.CreateAsset(list, $"{kBasePath}/{MetadataLookup.kAssetName}.asset");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            BuildScenes = null;
        }
    }

    static string[] EnabledBuildSettingsScenes()
    {
        return EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
    }

    /// <summary>
    /// Returns the asset paths a player build with these scenes includes: everything the scenes, the
    /// Resources folders and the preloaded assets depend on.
    /// </summary>
    internal static HashSet<string> BuiltAssetPaths(IEnumerable<string> scenes)
    {
        var roots = new List<string>(scenes);
        foreach (var path in AssetDatabase.GetAllAssetPaths())
        {
            if (path.Contains("/Resources/") && !path.StartsWith(kBasePath + "/"))
                roots.Add(path);
        }
        foreach (var asset in PlayerSettings.GetPreloadedAssets())
        {
            if (asset != null)
                roots.Add(AssetDatabase.GetAssetPath(asset));
        }
        return new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
    }

    [InitializeOnLoadMethod]
    static void RemoveLeftoversOnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (!BuildPipeline.isBuildingPlayer)
                RemoveBuildResources();
        };
    }

    /// <summary>
    /// Removes the metadata copies a build puts in Resources, and the Resources directory when the build
    /// created it and nothing else was put in it.
    /// </summary>
    internal static void RemoveBuildResources()
    {
        if (AssetDatabase.IsValidFolder(kBasePath))
            AssetDatabase.DeleteAsset(kBasePath);
        else if (System.IO.Directory.Exists(kBasePath))
        {
            System.IO.Directory.Delete(kBasePath, true);
            System.IO.File.Delete(kBasePath + ".meta");
        }

        if (SessionState.GetBool(kCreatedResourcesKey, false) &&
            System.IO.Directory.Exists(kResourcesPath) &&
            System.IO.Directory.GetFileSystemEntries(kResourcesPath).Length == 0)
        {
            AssetDatabase.DeleteAsset(kResourcesPath);
        }
        SessionState.EraseBool(kCreatedResourcesKey);
    }
}


public class MetadataLookupPostprocessBuild : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        MetadataLookupPreprocessBuild.RemoveBuildResources();
    }
}
