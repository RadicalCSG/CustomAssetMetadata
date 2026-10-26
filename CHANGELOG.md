# Changelog

## Unreleased
- Metadata title bars look and work like those of components: a foldout arrow, an icon, and a menu with Remove; the fields are indented under them
- When an asset has metadata, its own inspector gets a title bar too

## 1.3.0 (2026-10-26)
- Metadata on any asset Unity stores in its own format, not only materials; the add button is in the inspector header (thanks to Eideren)
- Assets holding metadata get the "AssetMetadata" label, which builds use to find their metadata
- GetMetadataOfType, HasMetadataOfType and AddMetadataOfType work on any asset
- Several selected assets show their metadata together: metadata they all have is edited at once, metadata only some have says so
- Fixed metadata editors leaking when the inspector changes selection
- Fixed added or removed metadata only reaching disk when the project was saved
- Fixed a failed build leaving metadata copies in Assets/Resources, where the editor found them as extra metadata
- DisallowMultipleCustomAssetMetadata counts subtypes, as documented; RestrictMetadataAssetTypes accepts subclasses of the listed types
- The metadata menu follows the order given to AddMetadataMenu, and leaves out abstract metadata classes
- Builds only include the metadata of assets they include anyway; every asset with metadata used to be pulled into every build
- Adding and removing metadata can be undone
- Metadata classes that are not in a file of their own name are no longer offered: Unity cannot load them back or restore them on undo
- The remove menu opens on a context click instead of reading a private Unity field
- Requires Unity 6000.3 or newer, which the package already needed since 1.2.3
- The sample's AudioClip-only metadata is now for animation clips, as audio clips cannot hold metadata

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
