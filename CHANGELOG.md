# Release notes

## Unreleased

No major bump: fixes only, no public API change.

### Fixed
- **The graph window no longer refreshes the asset database and saves every asset on each edit.**
  `GraphPanel.SaveGraphAsset` marks the container dirty and saves an asset container once, on the
  next editor tick (`AssetDatabase.SaveAssetIfDirty`); a scene container is saved with its scene.
- `Graph.Nodes` no longer replaces the serialized list on every read, and `TryGetContainer` no
  longer allocates: both are read for every node on each panel update.
- The graph window restores its last graph across editor sessions (the EditorPrefs key named the
  window's instance ID, which changes every session, and left a new key behind each time; it now
  names the project). A container that no longer resolves leaves the window empty instead of throwing.
- Entering or leaving Play mode no longer opens a graph window when none is open.

## v3.0.0

### Changed
- **The package is `SPACS-Graphs`, id `com.anotherealitysrl.spacs-graphs`** (was `Virtuademy-SDK-Graphs` /
  `com.anotherealitysrl.virtuademy-sdk-graphs`). The `Virtuademy-SDK-*` prefix is kept for the SDK
  proper — Core, Environments, Library; a package that carries no platform takes the `SPACS-*`
  prefix, as SPACS-Utility did. The assemblies and namespaces were already `SPACS.Graphs*` and are
  unchanged, so built bundles, Visual Scripting graphs and interpreted scripts are unaffected.

### Breaking
- A project that names `com.anotherealitysrl.virtuademy-sdk-graphs` in its `manifest.json` stops
  resolving once it pulls this version: the manifest key must match the id in `package.json`.
  Switch the key to `com.anotherealitysrl.spacs-graphs` and the URL to `SPACS-Graphs.git` (the package
  rename migrator in Virtuademy-SDK-Environments does both). Registry releases up to 2026.5.0 are
  unaffected: each pins the old repository URL at a tag, GitHub redirects that URL, and the tag
  still carries the id it was released with.

## v2.0.0

### Changed

- Changed package name, from Virtuademy-PLG-Graphs to Reflecits-SDK-Graphs, and updated namespaces according to new package name.

## v1.0.0

- Initial release
