$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Build an original small icon with Windows GDI+; no downloaded artwork.
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path "build" | Out-Null
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$shape = New-Object System.Drawing.Drawing2D.GraphicsPath
$shape.AddArc(6,6,16,16,180,90)
$shape.AddArc(42,6,16,16,270,90)
$shape.AddArc(42,42,16,16,0,90)
$shape.AddArc(6,42,16,16,90,90)
$shape.CloseFigure()
$blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(48,99,224))
$graphics.FillPath($blue,$shape)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 4
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawLine($pen,20,24,44,24)
$graphics.DrawLine($pen,32,29,32,43)
$graphics.DrawLine($pen,23,36,32,45)
$graphics.DrawLine($pen,41,36,32,45)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create((Join-Path $PSScriptRoot "build\app.ico"))
try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose(); $pen.Dispose(); $blue.Dispose(); $shape.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }

$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "Visual Studio Build Tools (MSBuild) not found." }
$install = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $install) { throw "MSBuild installation not found." }
$msbuild = Join-Path ($install | Select-Object -First 1) "MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) { throw "MSBuild.exe not found: $msbuild" }

& $msbuild ".\src\Stub\Stub.csproj" /m /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Stub compilation failed." }
& $msbuild ".\src\Builder\Builder.csproj" /m /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Builder compilation failed." }
Write-Host "Built src\Builder\bin\Release\DK-Self-Extractor-Builder.exe"
