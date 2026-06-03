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
- Stages package payload from the generated `.appxrecipe`.
- Adds runtime files that the current SDK output leaves outside the recipe, including project reference assemblies.
- Generates the package manifest when the SDK recipe points to a manifest file that was not materialized.
- Runs `makeappx pack` and then `makeappx unpack` as a package smoke test.

Default output:

```text
artifacts\packages\SnapStudio_1.0.0.0_x64.msix
artifacts\msix\layout\x64\
artifacts\msix\verify\x64\
```

`artifacts\` is ignored by git.

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
  -PackageVersion "1.0.0.0" `
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
Add-AppxPackage -Path .\artifacts\packages\SnapStudio_1.0.0.0_x64.msix
```

If a self-signed development certificate is used, import the public certificate into `Cert:\CurrentUser\TrustedPeople` before installing the package.

## References

- [Windows App SDK deployment overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)
- [Windows App SDK framework-dependent packaged apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)
- [Create an app package with MakeAppx.exe](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
- [Sign an app package using SignTool](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool)
- [Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
