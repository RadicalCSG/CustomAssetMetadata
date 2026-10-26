using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AssetMetadata.Tests
{
	[DisallowMultipleCustomAssetMetadata]
	class BaseSingleTestMetadata : CustomAssetMetadata
	{
	}

	class DerivedSingleTestMetadata : BaseSingleTestMetadata
	{
	}

	[RestrictMetadataAssetTypes(typeof(Texture))]
	class AnyTextureTestMetadata : CustomAssetMetadata
	{
	}

	abstract class AbstractTestMetadata : CustomAssetMetadata
	{
	}

	[AddMetadataMenu("Order/Late", 10)]
	class LateTestMetadata : CustomAssetMetadata
	{
	}

	[AddMetadataMenu("Order/Early", -10)]
	class EarlyTestMetadata : CustomAssetMetadata
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

		static Material CreateMaterial(string name = "TestMaterial")
		{
			var path = AssetDatabase.GenerateUniqueAssetPath(kTestFolder + "/" + name + ".mat");
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
		public void MetadataMenu_LeavesOutAbstractTypes()
		{
			Assert.That(AssetMetadataUtility.AllMetadataTypes, Has.No.Member(typeof(AbstractTestMetadata)));
		}

		// --- build ----------------------------------------------------------------------------

		const string kBuildResourcesPath = "Assets/Resources/" + MetadataLookup.kResourcePath;

		[Test]
		public void FailedBuild_CleanupIsScheduledAndRemovesTheCopies()
		{
			CreateMaterial().AddMetadataOfType<ReloadableTestMetadata>();
			var hadResources = AssetDatabase.IsValidFolder("Assets/Resources");
			try
			{
				// A failing build never reaches the postprocess step
				new MetadataLookupPreprocessBuild().OnPreprocessBuild(null);
				Assert.That(AssetDatabase.IsValidFolder(kBuildResourcesPath), Is.True);
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
			var material = CreateMaterial();
			material.AddMetadataOfType<ReloadableTestMetadata>();
			try
			{
				new MetadataLookupPreprocessBuild().OnPreprocessBuild(null);
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
