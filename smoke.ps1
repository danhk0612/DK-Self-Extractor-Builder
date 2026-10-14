$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$flags = [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::NonPublic
function Invoke-Internal([System.Reflection.MethodInfo]$method, [object[]]$arguments) {
    $raw = [object[]]::new($arguments.Length)
    for ($i = 0; $i -lt $arguments.Length; $i++) {
        if ($null -eq $arguments[$i]) { $raw[$i] = $null }
        else { $raw[$i] = $arguments[$i].PSObject.BaseObject }
    }
    return $method.Invoke($null, $raw)
}
$builderExe = Join-Path $PSScriptRoot "src\Builder\bin\Release\DK-Self-Extractor-Builder.exe"
$stubExe = Join-Path $PSScriptRoot "src\Stub\bin\Release\DKSelfExtractor.Stub.exe"
$builder = [System.Reflection.Assembly]::LoadFrom($builderExe)
$stub = [System.Reflection.Assembly]::LoadFrom($stubExe)
$build = $builder.GetType("DKSelfExtractor.BuilderForm").GetMethod("Build",$flags)
$package = $stub.GetType("DKSelfExtractor.Package")
$read = $package.GetMethod("Read",$flags)
$unpack = $package.GetMethod("UnpackPayload",$flags)
$extract = $package.GetMethod("Extract",$flags)
$folder = Join-Path $env:TEMP ("dksfx-smoke-" + [Guid]::NewGuid().ToString("N"))
$src = Join-Path $folder "input"
$out = Join-Path $folder "output"
$dst = Join-Path $folder "extracted"
$korean = ([string][char]0xD55C) + ([string][char]0xAE00)
try {
    New-Item -ItemType Directory -Force $src,$out,(Join-Path $src "sub"),(Join-Path $src "empty") | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $src "sample.txt"),"hello")
    [System.IO.File]::WriteAllText((Join-Path $src "sub\data.txt"),"nested")
    [System.IO.File]::WriteAllText((Join-Path $src ("sub\unicode-" + $korean + ".txt")),"unicode")
    foreach ($level in @(0,1,2)) {
        $bundle = Join-Path $out ("bundle-" + $level + ".exe")
        Invoke-Internal $build @($src,$bundle,$dst,"test memo","pass-123", $level) | Out-Null
        if (-not (Test-Path $bundle)) { throw "Build missing level $level" }
        $info = (Invoke-Internal $read @($bundle))
        try {
            Invoke-Internal $unpack @($bundle,$info,"wrong-password") | Out-Null
            throw "Wrong password succeeded"
        } catch {
            $cause = $_.Exception
            while ($null -ne $cause.InnerException -and -not ($cause -is [System.IO.InvalidDataException])) { $cause = $cause.InnerException }
            if (-not ($cause -is [System.IO.InvalidDataException])) { throw }
        }
        $zip = Invoke-Internal $unpack @($bundle,$info,"pass-123")
        try {
            Invoke-Internal $extract @($zip,$dst,$true,$null) | Out-Null
            if ([System.IO.File]::ReadAllText((Join-Path $dst "sample.txt")) -ne "hello") { throw "Content mismatch" }
            if (-not (Test-Path (Join-Path $dst "empty"))) { throw "Empty directory lost" }
            if ([System.IO.File]::ReadAllText((Join-Path $dst ("sub\unicode-" + $korean + ".txt"))) -ne "unicode") { throw "Unicode path failed" }
            [System.IO.File]::WriteAllText((Join-Path $dst "sample.txt"),"changed")
            Invoke-Internal $extract @($zip,$dst,$false,$null) | Out-Null
            if ([System.IO.File]::ReadAllText((Join-Path $dst "sample.txt")) -ne "changed") { throw "Non-overwrite failed" }
            Invoke-Internal $extract @($zip,$dst,$true,$null) | Out-Null
            if ([System.IO.File]::ReadAllText((Join-Path $dst "sample.txt")) -ne "hello") { throw "Overwrite failed" }
        } finally { Remove-Item $zip -Force -ErrorAction SilentlyContinue }
        Write-Host "PASS compression level $level encrypted roundtrip and overwrite toggle"
    }
    $plain = Join-Path $out "plain.exe"
    Invoke-Internal $build @($src,$plain,$dst,"","",2) | Out-Null
    $plainInfo = Invoke-Internal $read @($plain)
    $plainZip = Invoke-Internal $unpack @($plain,$plainInfo,"")
    try { Invoke-Internal $extract @($plainZip,$dst,$true,$null) | Out-Null }
    finally { Remove-Item $plainZip -Force -ErrorAction SilentlyContinue }
    Write-Host "PASS unencrypted roundtrip"
    $evil = Join-Path $folder "evil.zip"
    Add-Type -AssemblyName System.IO.Compression
    $file = [System.IO.File]::Create($evil)
    $archive = [System.IO.Compression.ZipArchive]::new($file,[System.IO.Compression.ZipArchiveMode]::Create)
    $entry = $archive.CreateEntry("../escape.txt")
    $writer = [System.IO.StreamWriter]::new($entry.Open())
    $writer.Write("escape")
    $writer.Dispose()
    $archive.Dispose()
    $file.Dispose()
    try {
        Invoke-Internal $extract @($evil,$dst,$true,$null) | Out-Null
        throw "Traversal succeeded"
    } catch {
        $cause = $_.Exception
        while ($null -ne $cause.InnerException -and -not ($cause -is [System.IO.InvalidDataException])) { $cause = $cause.InnerException }
        if (-not ($cause -is [System.IO.InvalidDataException])) { throw }
    }
    Write-Host "PASS traversal rejected"
}
finally { Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue }
