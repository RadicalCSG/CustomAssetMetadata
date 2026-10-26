
#if !UNITY_EDITOR
#define UNITY_RUNTIME
#endif
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

internal static class MetadataLookup
{
    public const string kResourcePath = "__AssetMetadata";
    public const string kAssetName = "list.asset";

    readonly static Dictionary<EntityId, List<CustomAssetMetadata>> table = new();

#if UNITY_EDITOR
	// Negative editor lookups are cached separately so a miss still returns false.
	readonly static HashSet<EntityId> assetsWithoutMetadata = new();
#endif

	// Remove unloaded objects before comparing EntityIds or returning cached values.
	static bool RemoveUnloaded(List<CustomAssetMetadata> metadataList)
	{
		var removed = false;
		for (int i = metadataList.Count - 1; i >= 0; i--)
		{
			if (metadataList[i] == null)
			{
				metadataList.RemoveAt(i);
				removed = true;
			}
		}
		return removed;
	}

	/// <summary>Clears cached lookup state.</summary>
	internal static void Clear()
	{
		table.Clear();
#if UNITY_EDITOR
		assetsWithoutMetadata.Clear();
#endif
		s_Initialized = false;
	}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IReadOnlyList<CustomAssetMetadata> GetAllMetadata(UnityEngine.Object asset)
    {
        if (!GetAllMetadataForAsset(asset, out var metadataList))
            return null;
        return metadataList;
    }

    public static bool GetAllMetadataOfType<Metadata>(UnityEngine.Object asset, List<Metadata> result)
        where Metadata : CustomAssetMetadata
    {
        result.Clear();
        if (asset == null)
            return false;

        if (!GetAllMetadataForAsset(asset, out var metadataList))
            return false;

        for (int i = 0; i < metadataList.Count; i++)
        {
            if (metadataList[i] is Metadata metadata)
                result.Add(metadata);
        }
        return result.Count > 0;
    }

	public static bool HasMetadataOfType(UnityEngine.Object asset, System.Type type)
	{
		if (asset == null)
			return false;

		if (!GetAllMetadataForAsset(asset, out var metadataList))
			return false;

		for (int i = 0; i < metadataList.Count; i++)
		{
			if (metadataList[i] != null &&
				type.IsInstanceOfType(metadataList[i]))
				return true;
		}
		return false;
	}

	public static bool HasMetadataOfType<Metadata>(UnityEngine.Object asset)
		where Metadata : CustomAssetMetadata
	{
		if (asset == null)
			return false;

		if (!GetAllMetadataForAsset(asset, out var metadataList))
			return false;

		for (int i = 0; i < metadataList.Count; i++)
		{
			if (metadataList[i] is Metadata)
				return true;
		}
		return false;
	}

    public static Metadata GetMetadataOfType<Metadata>(UnityEngine.Object asset)
        where Metadata : CustomAssetMetadata
    {
        if (asset == null)
            return null;

        if (!GetAllMetadataForAsset(asset, out var metadataList))
            return null;

        for (int i = 0; i < metadataList.Count; i++)
        {
            if (metadataList[i] is Metadata metadata)
                return metadata;
        }
        return null;
    }

	internal static bool Register(UnityEngine.LazyLoadReference<UnityEngine.Object> reference, CustomAssetMetadata metadata)
	{
		if (reference.isBroken || !reference.isSet ||
			object.ReferenceEquals(metadata, null))
			return false;

#if UNITY_EDITOR
		// Invalidate an earlier negative lookup.
		assetsWithoutMetadata.Remove(reference.entityId);
#endif

		if (!table.TryGetValue(reference.entityId, out var metadataList))
		{
			metadataList = new List<CustomAssetMetadata>();
			table[reference.entityId] = metadataList;
		}

		RemoveUnloaded(metadataList);
		if (!metadataList.Contains(metadata))
			metadataList.Add(metadata);
		return true;
	}

	internal static void Unregister(UnityEngine.LazyLoadReference<UnityEngine.Object> reference, CustomAssetMetadata metadata)
	{
		if (reference.isBroken || !reference.isSet ||
			object.ReferenceEquals(metadata, null)) 
			return;

		if (!table.TryGetValue(reference.entityId, out var metadataList))
			return;

		metadataList.Remove(metadata);
        if (metadataList.Count == 0)
        {
			table.Remove(reference.entityId);
        }
	}

#if UNITY_EDITOR
	/// <param name="anyMetadataAtPath">Whether the path contains metadata, including metadata whose reference is not currently resolvable.</param>
	static bool RegisterMetadataForAsset(string assetPath, out bool anyMetadataAtPath)
	{
		anyMetadataAtPath = false;
		if (Application.isEditor && string.IsNullOrEmpty(assetPath))
			return false;

		bool foundAny = false;
		// Metadata uses HideInHierarchy and is omitted by LoadAllAssetRepresentationsAtPath.
		var subAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
		foreach (var item in subAssets)
		{
			if (item is not CustomAssetMetadata metadata)
				continue;

			anyMetadataAtPath = true;
			// Register by EntityId without forcing the referenced asset to load.
			foundAny = MetadataLookup.Register(metadata.reference, metadata) || foundAny;
		}
		return foundAny;
	}
#endif

	static bool GetAllMetadataForAsset(UnityEngine.Object asset, out List<CustomAssetMetadata> result)
    {
		if (asset)
		{
			EnsureInitialized();
			var entityId = asset.GetEntityId();
			if (table.TryGetValue(entityId, out result))
			{
				if (!RemoveUnloaded(result))
					return true;
				// Preserve live entries and let the editor search restore unloaded ones.
				if (result.Count == 0)
					table.Remove(entityId);
#if !UNITY_EDITOR
				else
					return true;
#endif
			}
#if UNITY_EDITOR
			if (Application.isEditor)
			{
				if (assetsWithoutMetadata.Contains(entityId))
				{
					result = null;
					return false;
				}

				var assetPath = UnityEditor.AssetDatabase.GetAssetPath(asset);
				RegisterMetadataForAsset(assetPath, out var anyMetadataAtPath);
				if (table.TryGetValue(entityId, out result))
					return true;

				// Do not cache a negative result while metadata references are temporarily unresolved.
				if (!anyMetadataAtPath)
					assetsWithoutMetadata.Add(entityId);
			}
#endif
		}
		result = null;
        return false;
    }

    [System.Diagnostics.Conditional("UNITY_RUNTIME")]
    static void InitializeRuntime()
	{
#if UNITY_EDITOR
		if (!Application.isEditor)
#endif
		{
            var assetpath = $"{kResourcePath}/{kAssetName}";
			var lookupAsset = Resources.Load(assetpath) as MetadataLookupAsset;
			// Keep the resource and its metadata loaded.
			s_RuntimeLookupAsset = lookupAsset;
            if (lookupAsset == null)
			{
				Debug.LogError($"Failed to load {assetpath}");
                return;
			}

			foreach (var metadata in lookupAsset.allMetadata)
			{
				if (metadata == null)
					continue;
				MetadataLookup.Register(metadata.reference, metadata);
            }
        }
    }

	[System.Diagnostics.Conditional("UNITY_EDITOR")]
	static void InitializeEditor()
	{
#if UNITY_EDITOR
        if (Application.isEditor)
		{
			var allMetadata = Resources.FindObjectsOfTypeAll<CustomAssetMetadata>();
			foreach (var metadata in allMetadata)
			{
				if (!metadata)
					continue;
				var assetPath = UnityEditor.AssetDatabase.GetAssetPath(metadata);
				RegisterMetadataForAsset(assetPath, out _);
			}
        }
#endif
    }

    static bool s_Initialized = false;
    static MetadataLookupAsset s_RuntimeLookupAsset;
    static void EnsureInitialized()
    {
        if (s_Initialized) 
            return;
        s_Initialized = true;
        InitializeRuntime();
        InitializeEditor();
	}
}
