using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

[InitializeOnLoad]
static class EditorHeaderHook
{
    private static Dictionary<Editor, (Object[] targets, MetadataEditor editor)> _targetsCache = new();
    
    static EditorHeaderHook()
    {
        Editor.finishedDefaultHeaderGUI += DisplayMetadata;
    }

    static void DisplayMetadata(Editor editor)
    {
        if (!EditorUtility.IsPersistent(editor.target))
            return;
        if (!GUI.enabled)
            return;

        if (_targetsCache.TryGetValue(editor, out var cache) == false || Equality(cache.targets, editor.targets) == false)
        {
            cache.editor?.Dispose();
            _targetsCache[editor] = cache = (editor.targets, new MetadataEditor(editor.targets));
        }

        cache.editor.OnInspectorGUI();
        
        static bool Equality<T>(T[] a, T[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (ReferenceEquals(a[i], b[i]) == false)
                    return false;
            }

            return true;
        }
    }
}