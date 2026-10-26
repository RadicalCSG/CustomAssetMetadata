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

        EditorGUI.indentLevel = 0;
        EditorGUIUtility.labelWidth = 0;

        var rect = GUILayoutUtility.GetRect(GUIContent.none, InspectorLayout.InspectorTitlebar);
        showMetadata = InspectorLayout.FoldoutTitlebar(rect, showMetadata, metadataContent, false, InspectorLayout.InspectorTitlebar, InspectorLayout.InspectorTitlebarText);
        var controlId = InspectorLayout.LastControlId;
        if (showMetadata)
        {
            if (MixedValues)
            {
                EditorGUI.showMixedValue = true;
                EditorGUILayout.LabelField("\u2026");
                EditorGUI.showMixedValue = false;
            } else
            {
                EditorGUILayout.Space(3);
                ShowMetadataEditor();
                EditorGUILayout.Space();
            }
        }

        var evt = Event.current;
        var type = evt.GetTypeForControl(controlId);
        switch (type)
        {
            case EventType.MouseUp:
            {
                if (evt.button == 1 && rect.Contains(evt.mousePosition))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Remove"), false, DestroyMetadata);
                    menu.ShowAsContext();
                }
                break;
            }
        }
    }

    void DestroyMetadata()
    {
        if (metadata != null)
        {
            for (int i = metadata.Length - 1; i >= 0; i--)
            {
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
    }


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

    static MetadataEditorInstance[] CreateEditors(UnityEngine.Object[] targets)
    {
        if (targets == null)
        {
            Debug.Log("targets == null");
            return null;
        }

        var types               = ListPool<Type>.Get();
        var typeMetadata        = ListPool<List<CustomAssetMetadata>>.Get();
        var temporaryMetadata   = ListPool<CustomAssetMetadata>.Get();

        AssetMetadataUtility.GetAll(targets[0], temporaryMetadata);
        for (int i = 0; i < temporaryMetadata.Count; i++)
        {
            var metadataItem = temporaryMetadata[i];
            if (metadataItem == null)
                continue;
            types.Add(metadataItem.GetType());
            typeMetadata.Add(new List<CustomAssetMetadata>
            {
                metadataItem
            });
        }

        for (int i = 1; i < targets.Length; i++)
        {
            AssetMetadataUtility.GetAll(targets[i], temporaryMetadata);

            // TODO: do something clever to support metadata w/ multiple targets
        }

        var metadataEditors = new MetadataEditorInstance[typeMetadata.Count];
        for (int i = 0; i < typeMetadata.Count; i++)
        {
            metadataEditors[i] = MetadataEditorInstance.Create(targets, typeMetadata[i].ToArray(), types[i]);
        }

        ListPool<Type>.Release(types);
        ListPool<List<CustomAssetMetadata>>.Release(typeMetadata);
        ListPool<CustomAssetMetadata>.Release(temporaryMetadata);

        return metadataEditors;
    }

    private static GUIContent addMetadataButton = new GUIContent("+", "Add Metadata");
    
    public void OnInspectorGUI()
    {
        using (new EditorGUI.DisabledScope(!canOpenForEdit))
        {
            if (targets == null || !AssetMetadataUtility.HaveMetadataTypes)
                return;

            // TODO: support multiple targets
            if (targets.Length > 1)
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
                using (new EditorGUI.DisabledScope(!canAddMetadata))
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