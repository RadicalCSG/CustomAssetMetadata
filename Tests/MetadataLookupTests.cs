using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

namespace AssetMetadata.Tests
{
	class TestMetadata : CustomAssetMetadata
	{
		public int value;
	}

	class OtherTestMetadata : CustomAssetMetadata
	{
	}

	[DisallowMultipleCustomAssetMetadata]
	class SingletonTestMetadata : CustomAssetMetadata
	{
	}

	[RestrictMetadataAssetTypes(typeof(Texture2D))]
	class TextureOnlyTestMetadata : CustomAssetMetadata
	{
	}

	[TestFixture]
	public class MetadataLookupTests
	{
		const string kTestFolderParent = "Assets";
		const string kTestFolderName   = "__AssetMetadataTests__";
		const string kTestFolder       = kTestFolderParent + "/" + kTestFolderName;

		Material material;
		string   materialPath;

		[SetUp]
		public void SetUp()
		{
			if (!AssetDatabase.IsValidFolder(kTestFolder))
				AssetDatabase.CreateFolder(kTestFolderParent, kTestFolderName);

			materialPath = AssetDatabase.GenerateUniqueAssetPath(kTestFolder + "/TestMaterial.mat");
			var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
			AssetDatabase.CreateAsset(new Material(shader), materialPath);
			material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
			Assert.That(material, Is.Not.Null, "could not create the material the tests run against");
		}

		[TearDown]
		public void TearDown()
		{
			material = null;
			AssetDatabase.DeleteAsset(kTestFolder);
			MetadataLookup.Clear();
		}

		static TestMetadata AddTestMetadata(Material target, int value = 0)
		{
			var metadata = AssetMetadataUtility.Add<TestMetadata>(target) as TestMetadata;
			Assert.That(metadata, Is.Not.Null, "AssetMetadataUtility.Add returned nothing");
			metadata.value = value;
			return metadata;
		}

		// --- discovery -------------------------------------------------------------------------

		[Test]
		public void AddedMetadata_IsFoundImmediately()
		{
			var metadata = AddTestMetadata(material, 42);
			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.SameAs(metadata));
		}

		// Hidden metadata must be loaded through LoadAllAssetsAtPath.
		[Test]
		public void Metadata_IsHidden_AndOnlyLoadAllAssetsAtPathReturnsIt()
		{
			var metadata = AddTestMetadata(material);

			Assert.That(metadata.hideFlags & HideFlags.HideInHierarchy, Is.EqualTo(HideFlags.HideInHierarchy),
				"metadata is expected to be a hidden sub-asset");

			var representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(materialPath);
			Assert.That(representations.OfType<CustomAssetMetadata>(), Is.Empty,
				"LoadAllAssetRepresentationsAtPath is not supposed to return hidden sub-assets - if it " +
				"now does, MetadataLookup could use it again, but until then it must not.");

			var allAssets = AssetDatabase.LoadAllAssetsAtPath(materialPath);
			Assert.That(allAssets.OfType<CustomAssetMetadata>(), Is.Not.Empty,
				"LoadAllAssetsAtPath must return hidden sub-assets; the whole lookup depends on it");
		}

		[Test]
		public void Metadata_IsRediscoveredFromDisk_AfterTheLookupIsCleared()
		{
			AddTestMetadata(material, 7);
			MetadataLookup.Clear();

			var found = material.GetMetadataOfType<TestMetadata>();
			Assert.That(found, Is.Not.Null, "metadata on disk was not rediscovered");
			Assert.That(found.value, Is.EqualTo(7));
		}

		// Adding metadata invalidates a cached miss.
		[Test]
		public void AddingMetadata_InvalidatesAnEarlierNegativeAnswer()
		{
			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.Null, "material started out with metadata");

			var metadata = AddTestMetadata(material);
			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.SameAs(metadata),
				"the 'no metadata' answer was cached and never revisited");
		}

		[Test]
		public void MaterialWithoutMetadata_ReportsNone()
		{
			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.Null);
			Assert.That(material.HasMetadataOfType<TestMetadata>(), Is.False);
			Assert.That(MetadataLookup.GetAllMetadata(material), Is.Null);
		}

		[Test]
		public void MetadataIsNotSharedBetweenAssets()
		{
			var metadata = AddTestMetadata(material);

			var otherPath = AssetDatabase.GenerateUniqueAssetPath(kTestFolder + "/OtherMaterial.mat");
			AssetDatabase.CreateAsset(new Material(material.shader), otherPath);
			var other = AssetDatabase.LoadAssetAtPath<Material>(otherPath);

			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.SameAs(metadata));
			Assert.That(other.GetMetadataOfType<TestMetadata>(), Is.Null);
		}

		// --- removal ---------------------------------------------------------------------------

		[Test]
		public void DestroyedMetadata_IsNoLongerFound()
		{
			var metadata = AddTestMetadata(material);
			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.SameAs(metadata));

			AssetMetadataUtility.Destroy(metadata);

			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.Null);
			Assert.That(material.HasMetadataOfType<TestMetadata>(), Is.False);
		}

		[Test]
		public void DestroyedMetadata_IsNotLeftInTheLookup()
		{
			var metadata = AddTestMetadata(material);
			AssetMetadataUtility.Destroy(metadata);

			var all = MetadataLookup.GetAllMetadata(material);
			if (all == null)
				return;
			foreach (var item in all)
				Assert.That(item != null, Is.True, "the lookup handed back a destroyed metadata object");
		}

		[Test]
		public void MetadataCanBeRediscovered_AfterBeingDestroyedAndAddedAgain()
		{
			AssetMetadataUtility.Destroy(AddTestMetadata(material, 1));
			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.Null);

			var metadata = AddTestMetadata(material, 2);
			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.SameAs(metadata));
			Assert.That(material.GetMetadataOfType<TestMetadata>().value, Is.EqualTo(2));
		}

		// --- unloading -------------------------------------------------------------------------

		// Reloaded metadata may reuse the EntityId of the unloaded object.
		[Test]
		public void Metadata_IsFoundAgain_AfterUnityUnloadedAndReimportedIt()
		{
			var metadata = AssetMetadataUtility.Add<ReloadableTestMetadata>(material) as ReloadableTestMetadata;
			Assert.That(metadata, Is.Not.Null, "AssetMetadataUtility.Add returned nothing");
			metadata.value = 5;
			EditorUtility.SetDirty(metadata);
			AssetDatabase.SaveAssets();
			var path = materialPath;
			var fullPath = Path.GetFullPath(path);
			Assert.That(File.ReadAllText(fullPath), Does.Contain("value: 5"), "the metadata was not saved");
			MetadataLookup.Clear();
			material = null;
			metadata = null;
			EditorUtility.UnloadUnusedAssetsImmediate();

			// Simulate an external file update.
			File.WriteAllText(fullPath, File.ReadAllText(fullPath).Replace("value: 5", "value: 6"));
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

			material = AssetDatabase.LoadAssetAtPath<Material>(path);
			var found = material.GetMetadataOfType<ReloadableTestMetadata>();
			Assert.That(found != null, Is.True, "the lookup handed back metadata Unity had unloaded");
			Assert.That(found.value, Is.EqualTo(6));
		}

		[Test]
		public void Build_FindsTheMetadataOfEveryMaterial()
		{
			var metadata = AddTestMetadata(material, 3);
			AssetDatabase.SaveAssets();
			Assert.That(MetadataLookupPreprocessBuild.MaterialPaths(), Has.Member(materialPath));
			Assert.That(MetadataLookupPreprocessBuild.FindMetadata(new[] { materialPath }), Has.Member(metadata));
		}

		[Test]
		public void Build_CreatesPersistentLookupAsset()
		{
			// Only assets the build includes get their metadata copied, and Resources is always included
			AssetDatabase.CreateFolder(kTestFolder, "Resources");
			var resourcesPath = kTestFolder + "/Resources/BuiltMaterial.mat";
			Assert.That(AssetDatabase.MoveAsset(materialPath, resourcesPath), Is.Empty);
			material = AssetDatabase.LoadAssetAtPath<Material>(resourcesPath);
			AddTestMetadata(material, 3);
			AssetDatabase.SaveAssets();

			var preprocess = new MetadataLookupPreprocessBuild();
			try
			{
				preprocess.OnPreprocessBuild(null);
				var lookupPath = $"Assets/Resources/{MetadataLookup.kResourcePath}/{MetadataLookup.kAssetName}.asset";
				var lookup = AssetDatabase.LoadAssetAtPath<MetadataLookupAsset>(lookupPath);
				Assert.That(lookup, Is.Not.Null, "the generated lookup was not saved as an asset");
				Assert.That(lookup.allMetadata, Has.Some.Matches<CustomAssetMetadata>(item =>
					item != null && item.reference.entityId == material.GetEntityId()));
			}
			finally
			{
				new MetadataLookupPostprocessBuild().OnPostprocessBuild(null);
				AssetDatabase.Refresh();
			}
		}

		// --- values ----------------------------------------------------------------------------

		[Test]
		public void EditingMetadataValues_NeedsNoInvalidation()
		{
			var metadata = AddTestMetadata(material, 1);
			metadata.value = 99;
			Assert.That(material.GetMetadataOfType<TestMetadata>().value, Is.EqualTo(99));
		}

		// --- multiple metadata -----------------------------------------------------------------

		[Test]
		public void MetadataOfDifferentTypes_CoexistAndAreFilteredByType()
		{
			var metadata = AddTestMetadata(material);
			var other = AssetMetadataUtility.Add<OtherTestMetadata>(material) as OtherTestMetadata;
			Assert.That(other, Is.Not.Null);

			Assert.That(material.GetMetadataOfType<TestMetadata>(), Is.SameAs(metadata));
			Assert.That(material.GetMetadataOfType<OtherTestMetadata>(), Is.SameAs(other));

			var results = new List<TestMetadata>();
			Assert.That(MetadataLookup.GetAllMetadataOfType(material, results), Is.True);
			Assert.That(results, Is.EquivalentTo(new[] { metadata }));

			Assert.That(MetadataLookup.GetAllMetadata(material).Count, Is.EqualTo(2));
		}

		[Test]
		public void HasMetadataOfType_AgreesWithGetMetadataOfType()
		{
			Assert.That(material.HasMetadataOfType<TestMetadata>(), Is.False);
			AddTestMetadata(material);
			Assert.That(material.HasMetadataOfType<TestMetadata>(), Is.True);
			Assert.That(MetadataLookup.HasMetadataOfType(material, typeof(TestMetadata)), Is.True);
			Assert.That(MetadataLookup.HasMetadataOfType(material, typeof(OtherTestMetadata)), Is.False);
		}

		// --- what may be added to what ---------------------------------------------------------

		[Test]
		public void DisallowMultiple_PreventsASecondOfTheSameType()
		{
			Assert.That(AssetMetadataUtility.CanAddMetadataType(material, typeof(SingletonTestMetadata)), Is.True);
			Assert.That(AssetMetadataUtility.Add<SingletonTestMetadata>(material), Is.Not.Null);

			Assert.That(AssetMetadataUtility.CanAddMetadataType(material, typeof(SingletonTestMetadata)), Is.False);
			Assert.That(AssetMetadataUtility.Add<SingletonTestMetadata>(material), Is.Null);
		}

		[Test]
		public void RestrictedMetadata_CannotBeAddedToAnUnrelatedAssetType()
		{
			Assert.That(AssetMetadataUtility.CanAddMetadataType(material, typeof(TextureOnlyTestMetadata)), Is.False);
			Assert.That(AssetMetadataUtility.Add<TextureOnlyTestMetadata>(material), Is.Null);
		}

		[Test]
		public void UnrestrictedMetadata_CanBeAddedToAnyAssetType()
		{
			Assert.That(AssetMetadataUtility.CanAddMetadataType(material, typeof(TestMetadata)), Is.True);
		}

		// --- utility agreement -----------------------------------------------------------------

		[Test]
		public void GetAll_AgreesWithTheLookup()
		{
			AddTestMetadata(material);
			AssetMetadataUtility.Add<OtherTestMetadata>(material);
			MetadataLookup.Clear();

			var viaUtility = new List<CustomAssetMetadata>();
			AssetMetadataUtility.GetAll(material, viaUtility);

			var viaLookup = MetadataLookup.GetAllMetadata(material);
			Assert.That(viaLookup, Is.Not.Null, "the lookup found no metadata where GetAll found some");
			Assert.That(viaLookup.OrderBy(m => m.GetEntityId()),
						Is.EquivalentTo(viaUtility.OrderBy(m => m.GetEntityId())));
		}

		// --- assets that cannot keep metadata --------------------------------------------------

		[Test]
		public void ImportedTexture_CannotStoreMetadata()
		{
			var texturePath = kTestFolder + "/TestTexture.png";
			File.WriteAllBytes(texturePath, new Texture2D(4, 4).EncodeToPNG());
			AssetDatabase.ImportAsset(texturePath);
			var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

			Assert.That(AssetMetadataUtility.CanStoreMetadata(texture), Is.False);
			Assert.That(AssetMetadataUtility.CanStoreMetadata(AssetImporter.GetAtPath(texturePath)), Is.False);
			Assert.That(AssetMetadataUtility.Add<TestMetadata>(texture), Is.Null);
		}

		[Test]
		public void Folder_CannotStoreMetadata()
		{
			var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(kTestFolder);
			Assert.That(AssetMetadataUtility.CanStoreMetadata(folder), Is.False);
			Assert.That(AssetMetadataUtility.Add<TestMetadata>(folder), Is.Null);
		}

		[Test]
		public void Scene_GetAllFindsNothing()
		{
			var scenePath = kTestFolder + "/TestScene.unity";
			File.WriteAllText(scenePath, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
			AssetDatabase.ImportAsset(scenePath);
			var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);

			var found = new List<CustomAssetMetadata>();
			AssetMetadataUtility.GetAll(sceneAsset, found);
			Assert.That(found, Is.Empty);
			Assert.That(AssetMetadataUtility.CanStoreMetadata(sceneAsset), Is.False);
		}

		// --- other native assets and the build label -------------------------------------------

		AnimationClip CreateClip()
		{
			var clipPath = AssetDatabase.GenerateUniqueAssetPath(kTestFolder + "/TestClip.anim");
			AssetDatabase.CreateAsset(new AnimationClip(), clipPath);
			return AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
		}

		[Test]
		public void AnimationClip_KeepsMetadataAcrossImport()
		{
			var clip = CreateClip();
			var clipPath = AssetDatabase.GetAssetPath(clip);
			var metadata = clip.AddMetadataOfType<ReloadableTestMetadata>();
			Assert.That(metadata, Is.Not.Null);

			AssetDatabase.ImportAsset(clipPath, ImportAssetOptions.ForceUpdate);
			MetadataLookup.Clear();

			Assert.That(clip.HasMetadataOfType<ReloadableTestMetadata>(), Is.True);
			Assert.That(clip.GetMetadataOfType<ReloadableTestMetadata>(), Is.Not.Null);
		}

		[Test]
		public void AddedMetadata_LabelsTheAsset_AndRemovingTheLastClearsIt()
		{
			var first  = AddTestMetadata(material);
			var second = AssetMetadataUtility.Add<OtherTestMetadata>(material);
			Assert.That(AssetDatabase.GetLabels(material), Has.Member(AssetMetadataUtility.kAssetLabel));

			AssetMetadataUtility.Destroy(first);
			Assert.That(AssetDatabase.GetLabels(material), Has.Member(AssetMetadataUtility.kAssetLabel));

			AssetMetadataUtility.Destroy(second);
			Assert.That(AssetDatabase.GetLabels(material), Has.No.Member(AssetMetadataUtility.kAssetLabel));
		}

		[Test]
		public void Build_FindsMetadataOnLabelledAssets()
		{
			var clip = CreateClip();
			var clipPath = AssetDatabase.GetAssetPath(clip);
			var metadata = AssetMetadataUtility.Add<ReloadableTestMetadata>(clip);

			var paths = MetadataLookupPreprocessBuild.MetadataPaths();
			Assert.That(paths, Has.Member(clipPath));
			Assert.That(paths, Has.Member(materialPath), "unlabelled materials must still be searched");
			Assert.That(MetadataLookupPreprocessBuild.FindMetadata(new[] { clipPath }), Has.Member(metadata));
		}

		[Test]
		public void Metadata_CannotStoreMetadata()
		{
			var metadata = AddTestMetadata(material);
			Assert.That(AssetMetadataUtility.CanStoreMetadata(metadata), Is.False);
		}
	}
}
