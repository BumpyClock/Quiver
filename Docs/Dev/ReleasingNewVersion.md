# Releasing a new version

## Update version information

Release versions are set in these files:

| File                                                                           | Values                                                                                                                     |
| ------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------- |
| [Source/Meta.Shared.props](../../Source/Meta.Shared.props)                     | `Version` and `FileVersion` for the app                                                                                    |
| [Source/Quiver.Library/Constants.cs](../../Source/Quiver.Library/Constants.cs) | `VERSION`, the version displayed by Quiver                                                                                 |
| [Source/Quiver.App/Package.appxmanifest](../../Source/Quiver.App/Package.appxmanifest) | `Identity` `Version`. The Store requires `x.y.z.0` (last field must be 0), and each submission must increase it |

## Version format

In the format of `x.y.z.n` (windows) or `x.y.z-<type>-n` (human/git tags)
- The `x.y.z` mostly tries to follow semantic versioning (or maybe [zeroVer](https://0ver.org/) :D)
- `n` is release number.
  - Increments are done when a same version is released multiple times
  - like in the case of alpha, snapshot releases which also control the `<type>`, while it's semeantic version
  is the same.
  - Fox example, 
    - in case of `v0.10.0-alpha-2` it's windows format would be `0.10.0.002`
    - for the stable release, `v0.10.0`, windows format would be `0.10.0.100` 
- The Microsoft Store package version (`Package.appxmanifest`) always uses `x.y.z.0`. The Store reserves the last
  field, so `n` does not apply there.

## Before the first submission

Replace `Identity` `Name`, `Publisher` and `PublisherDisplayName` in `Package.appxmanifest` with the values from
Partner Center > Product identity.

## Release

Run the `release` GitHub workflow (or `./build.ps1` locally). Download the `Quiver_msixbundle` artifact
(`Quiver_<version>.msixbundle`) and upload it to Partner Center as a new submission.
