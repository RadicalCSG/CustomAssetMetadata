# Contributing

Thanks for helping out! Bug reports, ideas and pull requests are all welcome.

## Working on the package

1. Clone the repository somewhere outside your Unity project.
2. In a Unity 6000.3 or newer project, add it as a local package and list it under `testables`, so its tests are compiled, in `Packages/manifest.json`:

   ```json
   {
     "dependencies": {
       "com.custom_asset_metadata": "file:C:/path/to/CustomAssetMetadata"
     },
     "testables": [ "com.custom_asset_metadata" ]
   }
   ```

3. Run the EditMode tests in **Window > General > Test Runner**.

A fix comes with a test that fails without it. Metadata classes used by tests need a file of their own name, like any ScriptableObject.

## Pull requests

- Describe what the change does and why, and add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
- Run the tests on Unity 6000.3, the oldest version the package supports, and on the newest version you have.
- Keep code comments short, and in plain ASCII.

Pull requests are squash-merged into a single commit, which keeps you as its author.
