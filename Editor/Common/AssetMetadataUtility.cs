using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.ComponentModel;
using System.Linq;

public static class AssetMetadataUtility
{
	/// <summary>The label of every asset that holds metadata.</summary>
	public const string kAssetLabel = "AssetMetadata";

	static HashSet<Type> disallowMultipleMetadataLookup;
	static Dictionary<Type, Type[]> restrictedTypesLookup;
	static Dictionary<Type, (string displayName, string menuPath)> metadataNames;
	static List<Type> allCustomAssetMetadataTypes;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void EnsureInitialized()
    {
        if (allCustomAssetMetadataTypes != null)
            return;

        var metadataTypes = TypeCache.GetTypesDerivedFrom<CustomAssetMetadata>();
		metadataNames = new Dictionary<Type, (string displayName, string menuPath)>(metadataTypes.Count);
		allCustomAssetMetadataTypes = metadataTypes.Where(type => !type.IsAbstract && !type.ContainsGenericParameters).ToList();
        restrictedTypesLookup = new Dictionary<Type, Type[]>();
        disallowMultipleMetadataLookup = new HashSet<Type>();
		var menuOrder = new Dictionary<Type, int>();
		foreach (var metadataType in allCustomAssetMetadataTypes)
        {
            string displayName = null;
			string menuName = null;

			var displayNameAttribute = metadataType.GetCustomAttribute<DisplayNameAttribute>();
            if (displayNameAttribute != null)
            {
                displayName = displayNameAttribute.DisplayName;
            }

			var addMetadataMenuAttribute = metadataType.GetCustomAttribute<AddMetadataMenuAttribute>();
			if (addMetadataMenuAttribute != null)
			{
				menuName = addMetadataMenuAttribute.MetadataMenu.Replace('\\', '/');
				menuOrder[metadataType] = addMetadataMenuAttribute.MetadataOrder;
			}
            
            if (displayName == null && menuName != null)
            {
                int index = menuName.LastIndexOf('/');
                displayName = (index == -1) ? menuName : menuName.Substring(index + 1);
			}

			if (displayName == null)
			{
                displayName = ObjectNames.NicifyVariableName(metadataType.Name);
			}

			if (menuName == null && displayName != null)
			{
				menuName = displayName;
			}

            metadataNames[metadataType] = (displayName, menuName);
			var disallowMultipleCustomAssetMetadataAttribute = metadataType.GetCustomAttribute<DisallowMultipleCustomAssetMetadataAttribute>();
            if (disallowMultipleCustomAssetMetadataAttribute != null)
            {
                disallowMultipleMetadataLookup.Add(metadataType);
			}
			var restrictMetadataAssetTypesAttribute = metadataType.GetCustomAttribute<RestrictMetadataAssetTypesAttribute>();
			if (restrictMetadataAssetTypesAttribute != null)
			{
				restrictedTypesLookup[metadataType] = restrictMetadataAssetTypesAttribute.Types;
			}
		}

		allCustomAssetMetadataTypes.Sort((a, b) =>
		{
			menuOrder.TryGetValue(a, out var orderA);
			menuOrder.TryGetValue(b, out var orderB);
			if (orderA != orderB)
				return orderA.CompareTo(orderB);
			return string.CompareOrdinal(metadataNames[a].menuPath, metadataNames[b].menuPath);
		});
    }

    public static bool HaveMetadataTypes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            EnsureInitialized();
            return allCustomAssetMetadataTypes.Count > 0;
        }
    }

    public static List<Type> AllMetadataTypes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            EnsureInitialized();
            return allCustomAssetMetadataTypes;
        }
	}

	public static string GetDisplayName(Type metadataType)
	{
		if (metadataType == null) throw new NullReferenceException(nameof(metadataType));
		EnsureInitialized();
		if (metadataNames.TryGetValue(metadataType, out var value))
			return value.displayName;
		return ObjectNames.NicifyVariableName(metadataType.Name);
	}

	public static string GetMenuName(Type metadataType)
	{
		if (metadataType == null) throw new NullReferenceException(nameof(metadataType));
		EnsureInitialized();
        if (metadataNames.TryGetValue(metadataType, out var value))
            return value.menuPath;
        return ObjectNames.NicifyVariableName(metadataType.Name);
	}

	public static void GetAll(UnityEngine.Object target, List<CustomAssetMetadata> metadata)
	{
		if (target == null || ReferenceEquals(target, null)) throw new NullReferenceException(nameof(target));
		if (metadata == null) throw new NullReferenceException(nameof(metadata));
		EnsureInitialized();
		var assetPath = AssetDatabase.GetAssetPath(target);
        if (assetPath == null || string.IsNullOrEmpty(assetPath))
            return;

        // LoadAllAssetsAtPath throws on scenes, and only native assets can hold metadata
        if (!CanStoreMetadata(target))
            return;

        var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        foreach (var asset in assets)
        {
            if (asset is CustomAssetMetadata additionalDataAsset)
            {
                metadata.Add(additionalDataAsset);
            }
        }
    }

    /// <summary>
    /// Whether metadata added to <paramref name="target"/> is kept. Sub-assets added to imported assets
    /// (textures, models, prefabs, scenes) or folders are lost on the next import.
    /// </summary>
    public static bool CanStoreMetadata(UnityEngine.Object target)
    {
        if (target == null || target is CustomAssetMetadata)
            return false;
        return AssetDatabase.IsNativeAsset(target);
    }

    public static CustomAssetMetadata Add(UnityEngine.Object target, Type type)
	{
		if (target == null || ReferenceEquals(target, null)) throw new NullReferenceException(nameof(target));
		if (type == null) throw new NullReferenceException(nameof(type));
		EnsureInitialized();
		if (!CanAddMetadataType(target, type))
            return null;

		var assetPath = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrWhiteSpace(assetPath))
		{
			Debug.LogError("AssetMetadataUtility.Add: Could not find asset path for target", target);
			return null;
        }

		// TODO: could try to make non unity assets work by putting a file next to it?

		var instance = ScriptableObject.CreateInstance(type);
        instance.hideFlags = HideFlags.HideInHierarchy;
        if (instance is CustomAssetMetadata assetMetadata)
        {
			assetMetadata.name = type.Name;
            assetMetadata.reference = target;
            assetMetadata.OnReset();
			AssetDatabase.AddObjectToAsset(assetMetadata, assetPath);
            Undo.RegisterCreatedObjectUndo(assetMetadata, "Add " + GetDisplayName(type));
            undoAssetPaths.Add(assetPath);
            SetMetadataLabel(assetPath, true);
            SaveAndImport(assetPath);
            // OnEnable runs before the reference is assigned, so register explicitly.
            MetadataLookup.Register(target, assetMetadata);
            return assetMetadata;
        }

        UnityEngine.Object.DestroyImmediate(instance);
        return null;
	}

	public static bool CanAddMetadataType(UnityEngine.Object target, Type type)
	{
		if (type == null) throw new NullReferenceException(nameof(type));
		if (target == null || ReferenceEquals(target, null)) throw new NullReferenceException(nameof(target));
		EnsureInitialized();
        if (!CanStoreMetadata(target))
            return false;
        if (disallowMultipleMetadataLookup.Contains(type))
        {
            if (MetadataLookup.HasMetadataOfType(target, type))
                return false;
        }
        if (!restrictedTypesLookup.TryGetValue(type, out var restrictedTypes))
			return true;

        var targetType = target.GetType();
		foreach (var restrictedType in restrictedTypes)
        {
            if (restrictedType != null && restrictedType.IsAssignableFrom(targetType))
                return true;
        }
        return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CustomAssetMetadata Add<Metadata>(UnityEngine.Object target)
        where Metadata : CustomAssetMetadata
	{
		if (target == null || ReferenceEquals(target, null)) throw new NullReferenceException(nameof(target));
		EnsureInitialized();
		return Add(target, typeof(Metadata));
    }

    public static void Destroy<Metadata>(Metadata metadata)
        where Metadata : CustomAssetMetadata
	{
		if (metadata == null || ReferenceEquals(metadata, null)) throw new NullReferenceException(nameof(metadata));
		EnsureInitialized();

        var assetPath = AssetDatabase.GetAssetPath(metadata);
        if (string.IsNullOrEmpty(assetPath))
            return;

        // Unregister before removing the sub-asset, while its reference still resolves.
        MetadataLookup.Unregister(metadata.reference, metadata);

        undoAssetPaths.Add(assetPath);
        Undo.DestroyObjectImmediate(metadata);
        if (!AnyMetadataAtPath(assetPath))
            SetMetadataLabel(assetPath, false);
        SaveAndImport(assetPath);
    }

    // Assets whose metadata was added or removed with undo, and need saving again after an undo or redo
    static readonly HashSet<string> undoAssetPaths = new();

    [InitializeOnLoadMethod]
    static void RegisterUndo()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    static void OnUndoRedo()
    {
        if (undoAssetPaths.Count == 0)
            return;

        foreach (var assetPath in undoAssetPaths)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                continue;
            SetMetadataLabel(assetPath, AnyMetadataAtPath(assetPath));
            SaveAndImport(assetPath);
        }
        EditorHeaderHook.Reset();
    }

    static void SaveAndImport(string assetPath)
    {
        var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (mainAsset != null)
        {
            // Adding or removing a sub-asset does not dirty the main asset
            EditorUtility.SetDirty(mainAsset);
            AssetDatabase.SaveAssetIfDirty(mainAsset);
        }
        AssetDatabase.ImportAsset(assetPath);
    }

    static bool AnyMetadataAtPath(string assetPath)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (asset is CustomAssetMetadata)
                return true;
        }
        return false;
    }

    // Hidden sub-assets are invisible to AssetDatabase.FindAssets, so builds find metadata through this label
    static void SetMetadataLabel(string assetPath, bool hasMetadata)
    {
        var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (mainAsset == null)
            return;

        var labels = AssetDatabase.GetLabels(mainAsset);
        var index = Array.IndexOf(labels, kAssetLabel);
        if (hasMetadata == (index != -1))
            return;

        var newLabels = new List<string>(labels);
        if (hasMetadata)
            newLabels.Add(kAssetLabel);
        else
            newLabels.RemoveAt(index);
        AssetDatabase.SetLabels(mainAsset, newLabels.ToArray());
    }
}
