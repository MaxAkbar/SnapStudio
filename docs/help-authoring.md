# HTML help

The user guide is authored directly in [help/index.html](help/index.html), with
[help/help.css](help/help.css) and [help/help.js](help/help.js). Open `index.html`
in a browser to preview it. All resources are local; no documentation generator,
server, CDN, or internet connection is needed.

## App integration

- The toolbar Help button and F1 open one reusable `HelpWindow`. It is separate
  from the editor, works during first-run setup, and closes with the main window.
- `SnapStudio.App.csproj` links `docs/help/**` into `Assets/Help` in build and
  publish output. The MSIX staging process takes those published assets too.
- The WinUI WebView2 control maps `https://snapstudio-help.invalid/index.html`
  to that local folder using virtual host mapping. This address never needs DNS
  resolution or a web server. See [Microsoft's local-content documentation](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content).
- Navigation is restricted to the guide and its fragment links. Popups,
  downloads, host objects, web messages, and permission requests are disabled.
  The HTML content security policy allows only bundled styles, scripts, and images.
- The WebView2 profile uses `HelpBrowser` beneath `AppServices.ResolveAppDataRoot()`
  (normally `%LOCALAPPDATA%\SnapStudio`), keeping its cache writable for installed
  apps. `SNAPSTUDIO_DATA_ROOT` also isolates this cache for development previews.
- CSS follows the WebView2 preferred color scheme, which the window updates from
  the app's theme. Browser Ctrl+F and keyboard zoom remain available.
- Missing files and initialization/navigation failures show an error with Retry.
  Closing the window disposes WebView2, including when initialization is pending.

## Updating the guide

Keep user instructions aligned with the visible controls and current behavior.
Experimental features must stay identified as disabled by default. Avoid network
resources and external links in the guide: its viewer intentionally stays local.

Each topic is a `main section` with a unique `id` and a heading referenced by
`aria-labelledby`. Add its matching anchor to the topic navigation in the same
order. Search matches all typed words against each section, hides nonmatching
topics, and announces the result count. Escape or Clear restores the guide.
History navigation reveals a topic even if the current search had hidden it.

## Verification

```powershell
dotnet format SnapStudio.sln --verify-no-changes --no-restore
dotnet build SnapStudio.sln --configuration Debug -p:Platform=x64
dotnet test SnapStudio.sln --configuration Debug --no-build -p:Platform=x64
```

`HelpContentTests` exercises the navigation boundary without loading WinUI into
the test host. For visual and integration checks:

1. Open Help with the toolbar and F1, including during first-run setup. Repeat
   opening it and confirm that only one Help window exists.
2. Follow topic links and use Back, Forward, and Help home.
3. Search for `layers`, try a phrase with no matches, then clear it using Escape
   and the Clear button. Check topic history after filtering.
4. Switch the app's theme and resize Help to a narrow window. Check contrast,
   keyboard focus, readable tables, and the topic navigation.
5. Close and reopen Help; close the main window with Help open and confirm both
   windows exit. Also check closing Help immediately while it is starting.
6. Verify `Assets/Help/index.html`, `help.css`, and `help.js` in publish/package
   output. Test an extracted portable copy. In a disposable copy, omit a help
   asset and confirm the error and Retry UI appear.

The Microsoft Edge WebView2 Runtime is required, as it is for the SVG designer.
