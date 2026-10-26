using UnityEngine;

namespace AssetMetadata.Tests
{
	[RestrictMetadataAssetTypes(typeof(Texture2D))]
	class TextureOnlyTestMetadata : CustomAssetMetadata
	{
	}
}
