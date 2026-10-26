using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using System.Collections.Generic;

/// <summary>
/// An Editor for a metadata instance that's currently shown in the inspector
/// Holds a "metadata" for each selected "target" in the inspector
/// </summary>
sealed class MetadataEditorInstance : IDisposable
{
    string                  metadataKeyName;
    GUIContent              metadataContent;
    UnityEngine.Object[]    targets;
    bool                    showMetadata;
    CustomAssetMetadata[]   metadata;
    Editor                  metaDataEditor;
    bool                    isGenericInspector;
    SerializedObject        serializedObject;

    private MetadataEditorInstance() { }

    public static MetadataEditorInstance Create(UnityEngine.Object[] targets, CustomAssetMetadata[] metadata, Type type)
    {
        var metadataName = AssetMetadataUtility.GetDisplayName(type);
        var metadataKeyName = type.FullName;
        return new MetadataEditorInstance
        {
            targets         = targets,
            metadata        = metadata,
            metadataKeyName = metadataKeyName,
            metadataContent = new GUIContent(metadataName),
            showMetadata    = EditorPrefs.GetBool(metadataKeyName, false)
        };
    }

    public void Dispose()
    {
        EditorPrefs.SetBool(metadataKeyName, showMetadata);
        metadata = null;
        if (metaDataEditor != null)
            UnityEngine.Object.DestroyImmediate(metaDataEditor);
        metaDataEditor = null;
    }

    public bool Destroyed { get { return metadata == null; } }

    internal CustomAssetMetadata[] Metadata => metadata;

    public bool MixedValues
    {
        get
        {
            if (metadata == null ||
                metadata.Length != targets.Length)
                return true;

            if (metadata.Length == 0)
                return false;

            foreach (var item in metadata)
            {
                if (item == null)
                    return true;
            }
            return false;
        }
    }

    void ShowMetadataEditor()
    {
        if (metaDataEditor == null)
        {
            metaDataEditor = Editor.CreateEditor(metadata);
            isGenericInspector = metaDataEditor.GetType().FullName == "UnityEditor.GenericInspector";
            if (isGenericInspector)
            {
                serializedObject = new SerializedObject(metadata);
            }
        }

        if (metaDataEditor != null)
        {
            if (isGenericInspector)
            {
                serializedObject.UpdateIfRequiredOrScript();
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    if (iterator.propertyPath == "m_Script")
                        continue;

                    EditorGUILayout.PropertyField(iterator, true);
                    enterChildren = false;
                }
                serializedObject.ApplyModifiedProperties();
            } else
                metaDataEditor.OnInspectorGUI();
        }
    }

    public void OnInspectorGUI()
    {
        if (targets == null || targets.Length == 0 || metadata == null || metadata.Length == 0)
            return;

        var indentLevel = EditorGUI.indentLevel;
        var labelWidth = EditorGUIUtility.labelWidth;
        EditorGUI.indentLevel = 0;
        EditorGUIUtility.labelWidth = 0;
        try
        {
            DrawMetadata();
        }
        finally
        {
            EditorGUI.indentLevel = indentLevel;
            EditorGUIUtility.labelWidth = labelWidth;
        }
    }

    void DrawMetadata()
    {
        var rect = GUILayoutUtility.GetRect(GUIContent.none, InspectorLayout.InspectorTitlebar);
        showMetadata = InspectorLayout.FoldoutTitlebar(rect, showMetadata, metadataContent, false, InspectorLayout.InspectorTitlebar, InspectorLayout.InspectorTitlebarText);
        if (showMetadata)
        {
            if (MixedValues)
            {
                int count = 0;
                foreach (var item in metadata)
                {
                    if (item != null)
                        count++;
                }
                EditorGUILayout.HelpBox($"Only {count} of the {targets.Length} selected assets have this metadata. Select those on their own to edit it.", MessageType.Info);
            } else
            {
                EditorGUILayout.Space(3);
                ShowMetadataEditor();
                EditorGUILayout.Space();
            }
        }

        var evt = Event.current;
        if (evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Remove"), false, DestroyMetadata);
            menu.ShowAsContext();
            evt.Use();
        }
    }

    internal void DestroyMetadata()
    {
        if (metadata != null)
        {
            for (int i = metadata.Length - 1; i >= 0; i--)
            {
                if (metadata[i] != null)
                    AssetMetadataUtility.Destroy(metadata[i]);
            }
        }
        metadata = null;
    }
}


public sealed class MetadataEditor : IDisposable
{
    readonly bool                 canOpenForEdit;
    readonly UnityEngine.Object[] targets;
    MetadataEditorInstance[]      metadataEditors;

    public MetadataEditor(UnityEngine.Object[] targets)
    {
        this.targets = targets;
        canOpenForEdit = true;
        foreach (var target in targets)
        {
            if (!AssetDatabase.CanOpenForEdit(target))
                canOpenForEdit = false;
        }
    }

    public void Dispose()
    {
        DestroyEditors(ref metadataEditors);
        IsDisposed = true;
    }

    internal bool IsDisposed { get; private set; }


    static void DestroyEditors(ref MetadataEditorInstance[] metadataEditors)
    {
        if (metadataEditors != null)
        {
            for (int i = 0; i < metadataEditors.Length; i++)
            {
                metadataEditors[i].Dispose();
            }
            metadataEditors = null;
        }
	}

	public bool CanAddMetadataType(Type type)
	{
		if (type == null || targets == null || targets.Length == 0)
			return false;

		for (int i = 0; i < targets.Length; i++)
		{
            if (!AssetMetadataUtility.CanAddMetadataType(targets[i], type))
                return false;
		}
        return true;
	}

	public void AddMetadata(Type type)
    {
        if (type == null || targets == null || targets.Length == 0)
            return;

        for (int i = 0; i < targets.Length; i++)
        {
            AssetMetadataUtility.Add(targets[i], type);
        }
    }

    internal static MetadataEditorInstance[] CreateEditors(UnityEngine.Object[] targets)
    {
        if (targets == null)
        {
            Debug.Log("targets == null");
            return null;
        }

        // The n-th metadata of a type on one target is edited together with the n-th of that type on the others
        var keys                = new List<(Type type, int occurrence)>();
        var keyMetadata         = new Dictionary<(Type type, int occurrence), CustomAssetMetadata[]>();
        var occurrences         = new Dictionary<Type, int>();
        var temporaryMetadata   = ListPool<CustomAssetMetadata>.Get();
        try
        {
            for (int t = 0; t < targets.Length; t++)
            {
                temporaryMetadata.Clear();
                occurrences.Clear();
                AssetMetadataUtility.GetAll(targets[t], temporaryMetadata);
                foreach (var metadataItem in temporaryMetadata)
                {
                    if (metadataItem == null)
                        continue;
                    var type = metadataItem.GetType();
                    occurrences.TryGetValue(type, out var occurrence);
                    occurrences[type] = occurrence + 1;

                    var key = (type, occurrence);
                    if (!keyMetadata.TryGetValue(key, out var perTarget))
                    {
                        perTarget = new CustomAssetMetadata[targets.Length];
                        keyMetadata[key] = perTarget;
                        keys.Add(key);
                    }
                    perTarget[t] = metadataItem;
                }
            }
        }
        finally
        {
            ListPool<CustomAssetMetadata>.Release(temporaryMetadata);
        }

        var metadataEditors = new MetadataEditorInstance[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            metadataEditors[i] = MetadataEditorInstance.Create(targets, keyMetadata[keys[i]], keys[i].type);
        }
        return metadataEditors;
    }

    private static GUIContent addMetadataButton = new GUIContent("+", "Add Metadata");
    
    public void OnInspectorGUI()
    {
        using (new EditorGUI.DisabledScope(!canOpenForEdit))
        {
            if (targets == null || !AssetMetadataUtility.HaveMetadataTypes)
                return;

			metadataEditors ??= CreateEditors(targets);
            if (metadataEditors == null)
                return;

            // TODO: make it possible to re-order metadata like Components on GameObjects

            GUILayout.BeginVertical();

            List<Type> filteredMetadata = ListPool<Type>.Get();
            try
            {
                var canAddMetadata = AssetMetadataUtility.HaveMetadataTypes;
                if (canAddMetadata)
                {
                    foreach (var type in AssetMetadataUtility.AllMetadataTypes)
                    {
                        if (!CanAddMetadataType(type))
                            continue;
                        filteredMetadata.Add(type);
                    }
                    canAddMetadata = filteredMetadata.Count > 0;
                }
                if (canAddMetadata)
                {
                    // Embed the button inside the previous rect
                    var rect = GUILayoutUtility.GetRect(0,0,0,0);
                    rect.width = rect.height = EditorGUIUtility.singleLineHeight;
                    rect.y -= rect.height;
                    if (GUI.Button(rect, addMetadataButton))
                    {
                        // TODO: have a nicer dropdownmenu, more like the "add components" menu
                        var menu = new GenericMenu();
                        foreach (var type in filteredMetadata)
                        {
                            if (!CanAddMetadataType(type))
                                continue;

                            var menuName = AssetMetadataUtility.GetMenuName(type);
                            menu.AddItem(new GUIContent(menuName), false, delegate ()
                            {
                                AddMetadata(type);
                                DestroyEditors(ref metadataEditors);
                                metadataEditors = CreateEditors(targets);
                            });
                        }
                        menu.ShowAsContext();
                    }
                }
            }
            finally
            {
                ListPool<Type>.Release(filteredMetadata);
            }

            if (metadataEditors.Length > 0)
            {
                // We seem to get the metadata in reverse order from the assetdatabase, compared to the order we add them
                // So to make things feel more consistent, we reverse the order
                for (int i = metadataEditors.Length - 1; i >= 0; i--)
                {
                    metadataEditors[i].OnInspectorGUI();
                }
            }

            GUILayout.EndVertical();
        }
    }
}