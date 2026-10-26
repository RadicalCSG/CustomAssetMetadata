using UnityEngine;

namespace AssetMetadata.Tests
{
	[RestrictMetadataAssetTypes(typeof(Texture))]
	class AnyTextureTestMetadata : CustomAssetMetadata
	{
	}
}
