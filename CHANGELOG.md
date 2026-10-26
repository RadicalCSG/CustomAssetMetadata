# Changelog

## Unreleased
- Metadata on any asset Unity stores in its own format, not only materials; the add button is in the inspector header (thanks to Eideren)
- Assets holding metadata get the "AssetMetadata" label, which builds use to find their metadata
- GetMetadataOfType, HasMetadataOfType and AddMetadataOfType work on any asset
- Fixed metadata editors leaking when the inspector changes selection

## 1.2.3 (2026-10-26)
- Metadata kept as a hidden sub-asset is found, in the editor and in builds
- Unity 6.5 forward compatibility
- Fixed a build error

## 1.2.2 (2024-10-30)
- Fixed Resources.FindObjectsOfTypeAll in builds

## 1.2.1 (2024-10-29)
- CustomAssetMetadata.AssociatedAsset: the asset a metadata object belongs to

## 1.2.0 (2024-08-31)
- Default values for metadata
- HasMetadataOfType on materials, and AddMetadataOfType (editor only)
- An improved metadata menu; the metadata lookup uses LazyLoadReference
- Fixed a package GUID conflict

## Earlier
- Metadata is only looked for on materials, as looking on every object was too slow
- Inspector fixes
- First release
