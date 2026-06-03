# Microsoft Store Submission

Status: selected release path for MVP packaging.

The Microsoft Store path avoids buying and managing a public code-signing certificate for MSIX distribution. Microsoft signs the package during Store certification, but the package identity must match the app identity reserved in Partner Center.

## Partner Center Inputs

Create or sign in to a developer account at [Partner Center](https://partner.microsoft.com/dashboard), then reserve the app name.

Record these values from the reserved app identity:

- Package identity name.
- Package publisher.
- Publisher display name.
- Reserved Store app name.
- Hosted privacy policy URL.
- Hosted support URL.

The current local defaults are only development placeholders:

```text
Package identity name: SnapStudio
Package publisher: CN=SnapStudio
Package version: 1.0.0.0
```

For Store submission, use the Partner Center values rather than the local placeholders.

For a public Store release candidate, set these environment variables before running release readiness and package smoke:

```powershell
$env:SNAPSTUDIO_MSIX_PACKAGE_IDENTITY_NAME = "<Partner Center package identity name>"
$env:SNAPSTUDIO_MSIX_PACKAGE_PUBLISHER = "<Partner Center package publisher>"
$env:SNAPSTUDIO_STORE_PRIVACY_POLICY_URL = "https://..."
$env:SNAPSTUDIO_STORE_SUPPORT_URL = "https://..."
.\eng\run-release-readiness.ps1 -RequireStoreEnvironment -ReportPath artifacts\release\store-release-readiness.md
```

## Store Package Smoke

After reserving the app name, generate a Store-identity package:

```powershell
.\eng\run-release-smoke.ps1 `
  -Platform x64 `
  -Configuration Release `
  -PackageIdentityName "<Partner Center package identity name>" `
  -PackagePublisher "<Partner Center package publisher>" `
  -PackageVersion "1.0.0.0" `
  -RequireStoreIdentity
```

Do not pass `-CertificatePath` for the Store package smoke. Store-submitted MSIX packages do not require a CA-trusted signature because Microsoft re-signs accepted packages after certification.

The script output is:

```text
artifacts\packages\<IdentityName>_<Version>_x64.msix
```

Partner Center accepts `.msix`, `.msixupload`, `.msixbundle`, `.appx`, `.appxupload`, and `.appxbundle` files. Microsoft recommends `.msixupload` or `.appxupload` for Windows 10/11 submissions, so the final submission package should use Visual Studio's Store packaging wizard or a follow-up CI packaging step if Partner Center requires the upload container.

## Submission Checklist

- Reserve the app name in Partner Center.
- Update the package identity values used by `eng\run-release-smoke.ps1`.
- Run `eng\run-release-readiness.ps1 -RequireStoreEnvironment -ReportPath artifacts\release\store-release-readiness.md`.
- Build the Store-identity package and verify the unpacked manifest identity, payload, privacy notes, and signature status.
- Prepare Store listing text, screenshots, category, pricing, markets, support URL, and privacy policy URL. Use `docs\privacy.md` as the private-alpha privacy baseline before publishing a hosted privacy policy.
- Review restricted capabilities. SnapStudio currently declares `runFullTrust`, which is expected for a packaged desktop app but may be reviewed during certification.
- Upload the package in Partner Center and resolve validation warnings before certification.

## References

- [Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [Publish your first Windows app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app)
- [Upload MSIX app packages](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages)
