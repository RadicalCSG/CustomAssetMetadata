using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AssetMetadata.Tests
{
	abstract class AbstractTestMetadata : CustomAssetMetadata
	{
	}

	// Deliberately not in a file of its own name, so Unity has no script asset for it
	class NoScriptFileTestMetadata : CustomAssetMetadata
	{
	}

	[TestFixture]
	public class AssetMetadataEditorTests
	{
		const string kTestFolderParent = "Assets";
		const string kTestFolderName   = "__AssetMetadataEditorTests__";
		const string kTestFolder       = kTestFolderParent + "/" + kTestFolderName;

		readonly List<Object> toDestroy = new();

		[SetUp]
		public void SetUp()
		{
			if (!AssetDatabase.IsValidFolder(kTestFolder))
				AssetDatabase.CreateFolder(kTestFolderParent, kTestFolderName);
		}

		[TearDown]
		public void TearDown()
		{
			foreach (var obj in toDestroy)
			{
				if (obj)
					Object.DestroyImmediate(obj);
			}
			toDestroy.Clear();
			AssetDatabase.DeleteAsset(kTestFolder);
			MetadataLookup.Clear();
		}

		static Material CreateMaterial(string name = "TestMaterial", string folder = kTestFolder)
		{
			var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".mat");
			var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
			AssetDatabase.CreateAsset(new Material(shader), path);
			return AssetDatabase.LoadAssetAtPath<Material>(path);
		}

		// --- persistence -----------------------------------------------------------------------

		[Test]
		public void Add_WritesTheMetadataAndTheLabelToDisk()
		{
			var material = CreateMaterial();
			var path = AssetDatabase.GetAssetPath(material);
			material.AddMetadataOfType<ReloadableTestMetadata>();

			Assert.That(File.ReadAllText(path), Does.Contain("m_Name: " + nameof(ReloadableTestMetadata)));
			Assert.That(File.ReadAllText(path + ".meta"), Does.Contain(AssetMetadataUtility.kAssetLabel));
		}

		[Test]
		public void Destroy_RemovesTheMetadataAndTheLabelFromDisk()
		{
			var material = CreateMaterial();
			var path = AssetDatabase.GetAssetPath(material);
			AssetMetadataUtility.Destroy(material.AddMetadataOfType<ReloadableTestMetadata>());

			Assert.That(File.ReadAllText(path), Does.Not.Contain("m_Name: " + nameof(ReloadableTestMetadata)));
			Assert.That(File.ReadAllText(path + ".meta"), Does.Not.Contain(AssetMetadataUtility.kAssetLabel));
		}

		[Test]
		public void Destroy_OfMetadataThatIsNotAnAsset_DoesNothing()
		{
			var metadata = ScriptableObject.CreateInstance<TestMetadata>();
			toDestroy.Add(metadata);
			AssetMetadataUtility.Destroy(metadata);
			LogAssert.NoUnexpectedReceived();
		}

		// Metadata that arrives through an import (version control, another editor) must be found
		// even when the asset was looked up, and found to have none, before.
		[Test]
		public void MetadataArrivingThroughAnImport_IsFound()
		{
			var material = CreateMaterial();
			var path = AssetDatabase.GetAssetPath(material);
			var metadata = material.AddMetadataOfType<ReloadableTestMetadata>();
			var withMetadata = File.ReadAllText(path);
			var metaWithLabel = File.ReadAllText(path + ".meta");
			AssetMetadataUtility.Destroy(metadata);

			Assert.That(material.HasMetadataOfType<ReloadableTestMetadata>(), Is.False);

			File.WriteAllText(path, withMetadata);
			File.WriteAllText(path + ".meta", metaWithLabel);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

			Assert.That(material.HasMetadataOfType<ReloadableTestMetadata>(), Is.True);
		}

		// --- undo ------------------------------------------------------------------------------

		static bool OnDisk(string path) => File.ReadAllText(path).Contains("m_Name: " + nameof(ReloadableTestMetadata));
		static bool Labelled(string path) => File.ReadAllText(path + ".meta").Contains(AssetMetadataUtility.kAssetLabel);

		[Test]
		public void Add_CanBeUndoneAndRedone()
		{
			var material = CreateMaterial();
			var path = AssetDatabase.GetAssetPath(material);
			Undo.ClearAll();
			Undo.IncrementCurrentGroup();
			material.AddMetadataOfType<ReloadableTestMetadata>();

			// Like any undone edit, the asset is written on the next save
			Undo.PerformUndo();
			AssetDatabase.SaveAssets();
			Assert.That(material.HasMetadataOfType<ReloadableTestMetadata>(), Is.False);
			Assert.That(OnDisk(path), Is.False);
			Assert.That(Labelled(path), Is.False);

			Undo.PerformRedo();
			AssetDatabase.SaveAssets();
			Assert.That(material.HasMetadataOfType<ReloadableTestMetadata>(), Is.True);
			Assert.That(OnDisk(path), Is.True);
			Assert.That(Labelled(path), Is.True);
		}

		[Test]
		public void Destroy_CanBeUndoneAndRedone()
		{
			var material = CreateMaterial();
			var path = AssetDatabase.GetAssetPath(material);
			var metadata = material.AddMetadataOfType<ReloadableTestMetadata>();
			metadata.value = 7;
			Undo.ClearAll();
			Undo.IncrementCurrentGroup();
			AssetMetadataUtility.Destroy(metadata);

			Undo.PerformUndo();
			AssetDatabase.SaveAssets();
			var restored = material.GetMetadataOfType<ReloadableTestMetadata>();
			Assert.That(restored, Is.Not.Null);
			Assert.That(restored.value, Is.EqualTo(7));
			Assert.That(OnDisk(path), Is.True);
			Assert.That(Labelled(path), Is.True);

			Undo.PerformRedo();
			AssetDatabase.SaveAssets();
			Assert.That(material.HasMetadataOfType<ReloadableTestMetadata>(), Is.False);
			Assert.That(OnDisk(path), Is.False);
			Assert.That(Labelled(path), Is.False);
		}

		static int[] Values(Material material)
		{
			var found = new List<CustomAssetMetadata>();
			AssetMetadataUtility.GetAll(material, found);
			return found.OfType<ReloadableTestMetadata>().Select(m => m.value).OrderBy(v => v).ToArray();
		}

		[Test]
		public void UndoAfterSeveralAddsAndARemove_StepsBackOneOperationAtATime()
		{
			var material = CreateMaterial();
			Undo.ClearAll();
			var added = new List<ReloadableTestMetadata>();
			for (int i = 1; i <= 3; i++)
			{
				Undo.IncrementCurrentGroup();
				var metadata = material.AddMetadataOfType<ReloadableTestMetadata>();
				metadata.value = i;
				added.Add(metadata);
			}
			Undo.IncrementCurrentGroup();
			AssetMetadataUtility.Destroy(added[2]);
			Assert.That(Values(material), Is.EqualTo(new[] { 1, 2 }));

			Undo.PerformUndo();
			Assert.That(Values(material), Is.EqualTo(new[] { 1, 2, 3 }), "undoing the remove");

			Undo.PerformUndo();
			Assert.That(Values(material), Is.EqualTo(new[] { 1, 2 }), "undoing the third add");

			Undo.PerformUndo();
			Assert.That(Values(material), Is.EqualTo(new[] { 1 }), "undoing the second add");
		}

		// --- attributes ------------------------------------------------------------------------

		[Test]
		public void DisallowMultiple_AlsoCountsSubtypes()
		{
			var material = CreateMaterial();
			Assert.That(AssetMetadataUtility.Add<DerivedSingleTestMetadata>(material), Is.Not.Null);
			Assert.That(AssetMetadataUtility.CanAddMetadataType(material, typeof(BaseSingleTestMetadata)), Is.False);
			Assert.That(MetadataLookup.HasMetadataOfType(material, typeof(BaseSingleTestMetadata)), Is.True);
		}

		[Test]
		public void RestrictedMetadata_AllowsSubclassesOfTheRestrictedType()
		{
			var path = kTestFolder + "/TestRenderTexture.renderTexture";
			AssetDatabase.CreateAsset(new RenderTexture(4, 4, 0), path);
			var renderTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);

			Assert.That(AssetMetadataUtility.CanAddMetadataType(renderTexture, typeof(AnyTextureTestMetadata)), Is.True);
			Assert.That(AssetMetadataUtility.CanAddMetadataType(CreateMaterial(), typeof(AnyTextureTestMetadata)), Is.False);
		}

		[Test]
		public void MetadataMenu_FollowsTheOrderOfTheAttribute()
		{
			var types = AssetMetadataUtility.AllMetadataTypes;
			Assert.That(types.IndexOf(typeof(EarlyTestMetadata)), Is.LessThan(types.IndexOf(typeof(LateTestMetadata))));
			Assert.That(types.IndexOf(typeof(LateTestMetadata)), Is.GreaterThan(types.IndexOf(typeof(TestMetadata))));
		}

		[Test]
		public void MetadataWithoutAScriptFile_CannotBeAdded()
		{
			var material = CreateMaterial();
			Assert.That(AssetMetadataUtility.CanAddMetadataType(material, typeof(NoScriptFileTestMetadata)), Is.False);
			LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(nameof(NoScriptFileTestMetadata) + @"\.cs"));
			Assert.That(AssetMetadataUtility.Add<NoScriptFileTestMetadata>(material), Is.Null);
			Assert.That(AssetMetadataUtility.CanAddMetadataType(material, typeof(ReloadableTestMetadata)), Is.True);
		}

		[Test]
		public void MetadataMenu_LeavesOutAbstractTypes()
		{
			Assert.That(AssetMetadataUtility.AllMetadataTypes, Has.No.Member(typeof(AbstractTestMetadata)));
		}

		// --- build ----------------------------------------------------------------------------

		const string kBuildResourcesPath = "Assets/Resources/" + MetadataLookup.kResourcePath;

		static Material CreateBuiltMaterial()
		{
			var folder = kTestFolder + "/Resources";
			if (!AssetDatabase.IsValidFolder(folder))
				AssetDatabase.CreateFolder(kTestFolder, "Resources");
			return CreateMaterial("Built", folder);
		}

		static MetadataLookupAsset LoadBuildLookup()
		{
			return AssetDatabase.LoadAssetAtPath<MetadataLookupAsset>($"{kBuildResourcesPath}/{MetadataLookup.kAssetName}.asset");
		}

		[Test]
		public void Build_OnlyCopiesMetadataOfAssetsTheBuildIncludes()
		{
			var built = CreateBuiltMaterial();
			var notBuilt = CreateMaterial("NotBuilt");
			built.AddMetadataOfType<ReloadableTestMetadata>();
			notBuilt.AddMetadataOfType<ReloadableTestMetadata>();
			try
			{
				MetadataLookupPreprocessBuild.BuildScenes = new string[0];
				new MetadataLookupPreprocessBuild().OnPreprocessBuild(null);
				var copiedFor = LoadBuildLookup().allMetadata.Select(m => m.reference.entityId).ToArray();
				Assert.That(copiedFor, Has.Member(built.GetEntityId()));
				Assert.That(copiedFor, Has.No.Member(notBuilt.GetEntityId()));
				Assert.That(MetadataLookupPreprocessBuild.BuildScenes, Is.Null, "the scenes of one build must not leak into the next");
			}
			finally
			{
				MetadataLookupPreprocessBuild.RemoveBuildResources();
			}
		}

		[Test]
		public void Build_WithoutMetadataWritesNothing()
		{
			CreateMaterial("NotBuilt").AddMetadataOfType<ReloadableTestMetadata>();
			var hadResources = AssetDatabase.IsValidFolder("Assets/Resources");
			try
			{
				MetadataLookupPreprocessBuild.BuildScenes = new string[0];
				new MetadataLookupPreprocessBuild().OnPreprocessBuild(null);
				Assert.That(Directory.Exists(kBuildResourcesPath), Is.False);
				Assert.That(AssetDatabase.IsValidFolder("Assets/Resources"), Is.EqualTo(hadResources));
				Assert.That(MetadataLookupPreprocessBuild.BuildScenes, Is.Null);
			}
			finally
			{
				MetadataLookupPreprocessBuild.RemoveBuildResources();
			}
		}

		[Test]
		public void BuiltAssetPaths_FollowsDependencies()
		{
			var material = CreateMaterial("Referenced");
			var prefabFolder = kTestFolder + "/Resources";
			AssetDatabase.CreateFolder(kTestFolder, "Resources");
			var go = new GameObject("Referencing");
			go.AddComponent<MeshRenderer>().sharedMaterial = material;
			PrefabUtility.SaveAsPrefabAsset(go, prefabFolder + "/Referencing.prefab");
			Object.DestroyImmediate(go);

			var paths = MetadataLookupPreprocessBuild.BuiltAssetPaths(new string[0]);
			Assert.That(paths, Has.Member(AssetDatabase.GetAssetPath(material)));
			Assert.That(paths, Has.No.Member(AssetDatabase.GetAssetPath(CreateMaterial("Unreferenced"))));
		}

		[Test]
		public void FailedBuild_CleanupIsScheduledAndRemovesTheCopies()
		{
			CreateBuiltMaterial().AddMetadataOfType<ReloadableTestMetadata>();
			var hadResources = AssetDatabase.IsValidFolder("Assets/Resources");
			try
			{
				// A failing build never reaches the postprocess step
				new MetadataLookupPreprocessBuild().OnPreprocessBuild(null);
				Assert.That(LoadBuildLookup().allMetadata, Is.Not.Empty);
				Assert.That(EditorApplication.delayCall.GetInvocationList().Select(d => d.Method.Name),
							Has.Member(nameof(MetadataLookupPreprocessBuild.RemoveBuildResources)));
			}
			finally
			{
				MetadataLookupPreprocessBuild.RemoveBuildResources();
			}

			Assert.That(System.IO.Directory.Exists(kBuildResourcesPath), Is.False);
			Assert.That(AssetDatabase.IsValidFolder("Assets/Resources"), Is.EqualTo(hadResources));
		}

		[Test]
		public void LeftoverBuildCopies_AreNotFoundAsMetadata()
		{
			var material = CreateBuiltMaterial();
			material.AddMetadataOfType<ReloadableTestMetadata>();
			try
			{
				new MetadataLookupPreprocessBuild().OnPreprocessBuild(null);
				Assert.That(LoadBuildLookup().allMetadata, Is.Not.Empty);
				MetadataLookupPreprocessBuild.RemoveBuildResources();
				MetadataLookup.Clear();
				Assert.That(MetadataLookup.GetAllMetadata(material).Count, Is.EqualTo(1));
			}
			finally
			{
				MetadataLookupPreprocessBuild.RemoveBuildResources();
			}
		}

		// --- inspector header ------------------------------------------------------------------

		[Test]
		public void HeaderCache_DisposesTheEditorsOfDestroyedInspectors()
		{
			var material = CreateMaterial();
			var first = Editor.CreateEditor(material);
			var firstMetadataEditor = EditorHeaderHook.GetMetadataEditor(first);
			Assert.That(EditorHeaderHook.GetMetadataEditor(first), Is.SameAs(firstMetadataEditor));

			Object.DestroyImmediate(first);
			var second = Editor.CreateEditor(material);
			toDestroy.Add(second);
			EditorHeaderHook.GetMetadataEditor(second);

			Assert.That(EditorHeaderHook.IsCached(first), Is.False);
			Assert.That(firstMetadataEditor.IsDisposed, Is.True);
		}

		[Test]
		public void Header_IsOnlyShownWhenEverySelectedAssetCanStoreMetadata()
		{
			var texturePath = kTestFolder + "/TestTexture.png";
			File.WriteAllBytes(texturePath, new Texture2D(4, 4).EncodeToPNG());
			AssetDatabase.ImportAsset(texturePath);
			var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
			var material = CreateMaterial();

			Assert.That(EditorHeaderHook.ShowsMetadata(new Object[] { material }), Is.True);
			Assert.That(EditorHeaderHook.ShowsMetadata(new Object[] { material, texture }), Is.False);
		}

		// --- multiple selected assets (issues 1 and 2) ----------------------------------------

		[Test]
		public void MultipleTargets_SharedMetadataIsEditedTogether()
		{
			var a = CreateMaterial("A");
			var b = CreateMaterial("B");
			var metadataA = a.AddMetadataOfType<ReloadableTestMetadata>();
			var metadataB = b.AddMetadataOfType<ReloadableTestMetadata>();

			var editors = MetadataEditor.CreateEditors(new Object[] { a, b });
			Assert.That(editors.Length, Is.EqualTo(1));
			Assert.That(editors[0].MixedValues, Is.False);
			Assert.That(editors[0].Metadata, Is.EqualTo(new CustomAssetMetadata[] { metadataA, metadataB }));
			foreach (var editor in editors) editor.Dispose();
		}

		[Test]
		public void MultipleTargets_MetadataOnSomeOfThemIsMixed()
		{
			var a = CreateMaterial("A");
			var b = CreateMaterial("B");
			var metadataA = a.AddMetadataOfType<ReloadableTestMetadata>();

			var editors = MetadataEditor.CreateEditors(new Object[] { a, b });
			Assert.That(editors.Length, Is.EqualTo(1));
			Assert.That(editors[0].MixedValues, Is.True);
			Assert.That(editors[0].Metadata, Is.EqualTo(new CustomAssetMetadata[] { metadataA, null }));

			editors[0].DestroyMetadata();
			LogAssert.NoUnexpectedReceived();
			Assert.That(a.HasMetadataOfType<ReloadableTestMetadata>(), Is.False);
		}

		[Test]
		public void MultipleTargets_RepeatedMetadataIsMatchedInOrder()
		{
			var a = CreateMaterial("A");
			var b = CreateMaterial("B");
			AssetMetadataUtility.Add<OtherTestMetadata>(a);
			AssetMetadataUtility.Add<OtherTestMetadata>(a);
			AssetMetadataUtility.Add<OtherTestMetadata>(b);

			var editors = MetadataEditor.CreateEditors(new Object[] { a, b });
			Assert.That(editors.Length, Is.EqualTo(2));
			Assert.That(editors.Count(e => e.MixedValues), Is.EqualTo(1));
			foreach (var editor in editors) editor.Dispose();
		}

		[Test]
		public void MultipleTargets_AddMetadataAddsToEveryTarget()
		{
			var a = CreateMaterial("A");
			var b = CreateMaterial("B");
			var metadataEditor = new MetadataEditor(new Object[] { a, b });
			metadataEditor.AddMetadata(typeof(ReloadableTestMetadata));
			metadataEditor.Dispose();

			Assert.That(a.HasMetadataOfType<ReloadableTestMetadata>(), Is.True);
			Assert.That(b.HasMetadataOfType<ReloadableTestMetadata>(), Is.True);
		}
	}
}
