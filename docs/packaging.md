# Packaging

## MSIX Baseline

`SnapStudio.App` remains unpackaged for normal local development. The MSIX package path is opt-in through the packaging script so Visual Studio and CLI debug builds can keep using `WindowsPackageType=None`.

Build an unsigned x64 MSIX package:

```powershell
.\eng\package-msix.ps1 -Platform x64 -Configuration Release
```

The script:

- Restores `SnapStudio.App` for the selected Windows runtime identifier.
- Publishes with `WindowsPackageType=MSIX` without changing the project file's normal development settings.
- Stages package payload from the generated `.appxrecipe`, or from the publish output and generated app resource index when the SDK does not emit a recipe.
- Adds runtime files that the current SDK output leaves outside the recipe, including project reference assemblies.
- Generates the package manifest when the SDK recipe points to a manifest file that was not materialized.
- Runs `makeappx pack` and then `makeappx unpack` as a package smoke test.

Default output:

```text
artifacts\packages\SnapStudio_1.1.0.0_x64.msix
artifacts\msix\layout\x64\
artifacts\msix\verify\x64\
```

`artifacts\` is ignored by git.

## Portable GitHub Release

Publish the x64 app with its .NET and Windows App SDK runtimes:

```powershell
dotnet publish src\SnapStudio.App\SnapStudio.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -p:SnapStudioPortable=true --output artifacts\portable\win-x64
.\eng\run-ui-smoke.ps1 -Configuration Release -Platform x64 -SkipBuild -AppExecutablePath artifacts\portable\win-x64\SnapStudio.App.exe
```

`SnapStudioPortable` enables Windows App SDK self-contained deployment only in the executable project. Applying `WindowsAppSDKSelfContained` on the command line also affects referenced libraries and can merge conflicting runtime resources. See [Microsoft's self-contained deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps).

The app project also includes its generated `SnapStudio.App.pri` in the portable publish. This resource index is required for the app window to open.

The offline user guide is copied from `docs/help` into `Assets/Help` for build,
portable publish, and MSIX output. Keep `index.html`, `help.css`, and `help.js`
together. The Help viewer and SVG designer require the Microsoft Edge WebView2
Runtime on the destination PC; it is not bundled in the portable ZIP.

Include `LICENSE` and `docs\privacy.md` in the distribution, then ZIP the entire publish directory as `SnapStudio-1.1.0-win-x64-portable.zip`. Users must extract all files before running `SnapStudio.App.exe`. This release is unsigned and uses the normal local Windows profile for captures and settings. OCR, scrolling capture, and screen recording remain experimental and disabled by default.

The stable GitHub release is tagged `v1.1.0` and is published with the prerelease
flag off. The additional `SnapStudio_1.1.0.0_x64.msix` is an unsigned,
framework-dependent package for packaging evaluation; it needs a trusted signing
certificate and the matching runtimes before installation. The portable ZIP is
the normal download. Microsoft Store certification and signed distribution are
separate release paths.

## Release Package Smoke

Use the Release smoke before handing a package to testers:

```powershell
.\eng\run-release-smoke.ps1 -Platform x64 -Configuration Release
```

The smoke wraps `eng\package-msix.ps1`, then checks the unpacked package manifest, required app payload, `runFullTrust` capability, bundled `Docs\privacy.md`, package path, and signature status. It is also run by CI and uploads the unsigned MSIX as a build artifact.

Use the release readiness check before declaring a release candidate:

```powershell
.\eng\run-release-readiness.ps1
```

To keep a release-candidate artifact with the exact readiness results:

```powershell
.\eng\run-release-readiness.ps1 -ReportPath artifacts\release\release-readiness.md
```

For a public Store release candidate, set `SNAPSTUDIO_MSIX_PACKAGE_IDENTITY_NAME`, `SNAPSTUDIO_MSIX_PACKAGE_PUBLISHER`, `SNAPSTUDIO_STORE_PRIVACY_POLICY_URL`, and `SNAPSTUDIO_STORE_SUPPORT_URL`, then run:

```powershell
.\eng\run-release-readiness.ps1 -RequireStoreEnvironment -ReportPath artifacts\release\store-release-readiness.md
```

For a Store-identity package smoke after reserving the app in Partner Center:

```powershell
.\eng\run-release-smoke.ps1 `
  -Platform x64 `
  -Configuration Release `
  -PackageIdentityName "<Partner Center package identity name>" `
  -PackagePublisher "<Partner Center package publisher>" `
  -PackageVersion "1.1.0.0" `
  -RequireStoreIdentity
```

For local signed-package validation, provide a trusted PFX and require a valid signature:

```powershell
.\eng\run-release-smoke.ps1 `
  -Platform x64 `
  -Configuration Release `
  -CertificatePath "C:\certs\SnapStudio.pfx" `
  -CertificatePassword "<password>" `
  -RequireSignedPackage
```

## OCR Package Smoke

OCR requires package identity. To prepare a packaged OCR smoke pass, use:

```powershell
.\eng\run-ocr-smoke.ps1 -Platform x64 -Configuration Release -RegisterLayout -Launch -KeepAppOpen
```

The OCR smoke script enables `V1.Ocr` in local settings with a restorable backup, creates `artifacts\ocr-smoke\ocr-sample.png`, builds the MSIX layout, optionally registers the loose package layout, and checks that the packaged app exposes the Copy Text command. Use the OCR checklist in `docs\project-plan.md` for the full manual pass.

## Signing

By default the script produces an unsigned MSIX. That is enough for packaging smoke tests, but Windows requires a signed package with a trusted certificate for local installation.

For Microsoft Store distribution, do not buy a public code-signing certificate for MSIX. Reserve the app name in Partner Center, build with the Store package identity, and submit the package. Microsoft signs the package during Store certification. See `docs\store-submission.md`.

To sign during packaging, provide a PFX certificate by parameter or environment variable:

```powershell
$env:SNAPSTUDIO_MSIX_CERTIFICATE_PATH = "C:\certs\SnapStudio.pfx"
$env:SNAPSTUDIO_MSIX_CERTIFICATE_PASSWORD = "<password>"
.\eng\package-msix.ps1 -Platform x64 -Configuration Release
```

The signing certificate subject must match the package publisher. The default publisher is `CN=SnapStudio`; override it with `-PackagePublisher` only when the certificate uses a different publisher identity.

Install a signed package:

```powershell
Add-AppxPackage -Path .\artifacts\packages\SnapStudio_1.1.0.0_x64.msix
```

If a self-signed development certificate is used, import the public certificate into `Cert:\CurrentUser\TrustedPeople` before installing the package.

## References

- [Windows App SDK deployment overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)
- [Windows App SDK framework-dependent packaged apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)
- [Create an app package with MakeAppx.exe](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
- [Sign an app package using SignTool](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool)
- [Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
