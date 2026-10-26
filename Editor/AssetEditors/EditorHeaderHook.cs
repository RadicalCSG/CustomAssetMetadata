using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

[InitializeOnLoad]
static class EditorHeaderHook
{
    // Destroyed editors compare equal to each other, so key by reference
    sealed class ByReference : IEqualityComparer<Editor>
    {
        public bool Equals(Editor a, Editor b) => ReferenceEquals(a, b);
        public int GetHashCode(Editor editor) => RuntimeHelpers.GetHashCode(editor);
    }

    private static Dictionary<Editor, (Object[] targets, MetadataEditor editor)> _targetsCache = new(new ByReference());
    private static List<Editor> _destroyedEditors = new();

    static EditorHeaderHook()
    {
        Editor.finishedDefaultHeaderGUI += DisplayMetadata;
    }

    static void DisplayMetadata(Editor editor)
    {
        if (!GUI.enabled)
            return;
        if (!AssetMetadataUtility.CanStoreMetadata(editor.target))
            return;

        if (_targetsCache.TryGetValue(editor, out var cache) == false || Equality(cache.targets, editor.targets) == false)
        {
            cache.editor?.Dispose();
            RemoveDestroyedEditors();
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

    static void RemoveDestroyedEditors()
    {
        foreach (var pair in _targetsCache)
        {
            if (pair.Key == null)
                _destroyedEditors.Add(pair.Key);
        }

        foreach (var editor in _destroyedEditors)
        {
            _targetsCache[editor].editor.Dispose();
            _targetsCache.Remove(editor);
        }
        _destroyedEditors.Clear();
    }
}
