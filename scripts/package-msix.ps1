param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$architecture = if ($Runtime -eq 'win-arm64') { 'arm64' } else { 'x64' }
$staging = "artifacts/msix-$Runtime"
New-Item -ItemType Directory -Force "$staging/Assets" | Out-Null
Copy-Item "artifacts/desktop-$Runtime/*" $staging -Recurse -Force
Copy-Item 'packaging/Assets/*' "$staging/Assets" -Force
$manifest = Get-Content packaging/AppxManifest.xml -Raw
$manifest.Replace('ARCHITECTURE', $architecture) | Set-Content "$staging/AppxManifest.xml" -Encoding utf8
$sdkTool = Get-ChildItem 'C:/Program Files (x86)/Windows Kits/10/bin' -Filter makeappx.exe -Recurse | Where-Object FullName -Match '/x64/|\\x64\\' | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $sdkTool) { throw 'Windows SDK MakeAppx was not found.' }
& $sdkTool.FullName pack /d $staging /p "artifacts/RailWatch-$Runtime.msix" /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
