# Installers

Large Unity installer payloads are intentionally not tracked in Git. Clone the
project first, then download the installer files on demand:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\download-installers.ps1
```

Download only one artifact:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\download-installers.ps1 -ArtifactId unity-editor-6000.6.0a2-windows-x64
```

If Unity's CDN redirects to a regional mirror that does not have the alpha
editor, pass a proxy:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\download-installers.ps1 -Proxy http://127.0.0.1:10808
```

Downloaded files are written to `installers/downloads/` and verified against
`installers/installers.json`.
