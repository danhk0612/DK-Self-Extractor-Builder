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
function Invoke-Build([string[]]$paths, [string]$exe, [string]$destination, [string]$memo, [string]$secret, [int]$level) {
    $arguments = [object[]]::new(6)
    $arguments[0] = $paths
    $arguments[1] = $exe
    $arguments[2] = $destination
    $arguments[3] = $memo
    $arguments[4] = $secret
    $arguments[5] = $level
    Invoke-Internal $build $arguments | Out-Null
}
$builderType = $builder.GetType("DKSelfExtractor.BuilderForm")
$defaultExe = $builderType.GetMethod("DefaultExePath",$flags)
$package = $stub.GetType("DKSelfExtractor.Package")
$read = $package.GetMethod("Read",$flags)
$unpack = $package.GetMethod("UnpackPayload",$flags)
$extract = $package.GetMethod("Extract",$flags)
$folder = Join-Path $env:TEMP ("dksfx-smoke-" + [Guid]::NewGuid().ToString("N"))
$src = Join-Path $folder "input"
$out = Join-Path $folder "output"
$dst = Join-Path $folder "extracted"
$restored = Join-Path $dst "input"
$korean = ([string][char]0xD55C) + ([string][char]0xAE00)
try {
    New-Item -ItemType Directory -Force $src,$out,(Join-Path $src "sub"),(Join-Path $src "empty") | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $src "sample.txt"),"hello")
    [System.IO.File]::WriteAllText((Join-Path $src "sub\data.txt"),"nested")
    [System.IO.File]::WriteAllText((Join-Path $src ("sub\unicode-" + $korean + ".txt")),"unicode")
    $pathArgs = [object[]]::new(1)
    $pathArgs[0] = [string[]]@($src)
    $suggested = $defaultExe.Invoke($null,$pathArgs)
    $expectedPath = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($src)),"input.exe")
    if (-not [string]::Equals([string]$suggested,$expectedPath,[System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Default EXE folder path mismatch actual=[$suggested] length=$(([string]$suggested).Length) expected=[$expectedPath] length=$($expectedPath.Length)"
    }
    $pathArgs[0] = [string[]]@((Join-Path $src "sample.txt"))
    $suggestedFile = $defaultExe.Invoke($null,$pathArgs)
    if (-not [string]::Equals([string]$suggestedFile,$expectedPath,[System.StringComparison]::OrdinalIgnoreCase)) { throw "Default EXE file path mismatch: $suggestedFile expected $expectedPath" }
    Write-Host "PASS default EXE path for first file/folder"
    foreach ($level in @(0,1,2)) {
        $bundle = Join-Path $out ("bundle-" + $level + ".exe")
        Invoke-Build ([string[]]@($src)) $bundle $dst "test memo" "pass-123" $level
        if (-not (Test-Path $bundle)) { throw "Build missing level $level" }
        # The CLR cannot load the same assembly identity from multiple file paths in one AppDomain.
        if ($level -eq 0) {
            $managed = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($bundle)
            if ($managed.GetName().Name -ne "DKSelfExtractor.Stub") { throw "Generated EXE cannot be loaded as the SFX stub" }
        }
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
            if ([System.IO.File]::ReadAllText((Join-Path $restored "sample.txt")) -ne "hello") { throw "Content mismatch" }
            if (-not (Test-Path (Join-Path $restored "empty"))) { throw "Empty directory lost" }
            if ([System.IO.File]::ReadAllText((Join-Path $restored ("sub\unicode-" + $korean + ".txt"))) -ne "unicode") { throw "Unicode path failed" }
            [System.IO.File]::WriteAllText((Join-Path $restored "sample.txt"),"changed")
            Invoke-Internal $extract @($zip,$dst,$false,$null) | Out-Null
            if ([System.IO.File]::ReadAllText((Join-Path $restored "sample.txt")) -ne "changed") { throw "Non-overwrite failed" }
            Invoke-Internal $extract @($zip,$dst,$true,$null) | Out-Null
            if ([System.IO.File]::ReadAllText((Join-Path $restored "sample.txt")) -ne "hello") { throw "Overwrite failed" }
        } finally { Remove-Item $zip -Force -ErrorAction SilentlyContinue }
        Write-Host "PASS compression level $level encrypted roundtrip and overwrite toggle"
    }
    $plain = Join-Path $out "plain.exe"
    Invoke-Build ([string[]]@($src)) $plain $dst "" "" 2
    $plainInfo = Invoke-Internal $read @($plain)
    $plainZip = Invoke-Internal $unpack @($plain,$plainInfo,"")
    try { Invoke-Internal $extract @($plainZip,$dst,$true,$null) | Out-Null }
    finally { Remove-Item $plainZip -Force -ErrorAction SilentlyContinue }
    Write-Host "PASS unencrypted roundtrip"

    # A file and a second folder are both roots in one archive.
    $looseFile = Join-Path $folder "loose.txt"
    $otherFolder = Join-Path $folder "extras"
    New-Item -ItemType Directory -Force $otherFolder | Out-Null
    [System.IO.File]::WriteAllText($looseFile,"loose")
    [System.IO.File]::WriteAllText((Join-Path $otherFolder "extra.txt"),"other")
    $mixed = Join-Path $out "mixed.exe"
    Invoke-Build ([string[]]@($src,$looseFile,$otherFolder)) $mixed $dst "multi" "" 2
    $mixedInfo = Invoke-Internal $read @($mixed)
    $mixedZip = Invoke-Internal $unpack @($mixed,$mixedInfo,"")
    try {
        $mixedDst = Join-Path $folder "mixed-restored"
        Invoke-Internal $extract @($mixedZip,$mixedDst,$true,$null) | Out-Null
        if ([System.IO.File]::ReadAllText((Join-Path $mixedDst "loose.txt")) -ne "loose") { throw "Top-level file lost" }
        if ([System.IO.File]::ReadAllText((Join-Path $mixedDst "extras\extra.txt")) -ne "other") { throw "Second folder lost" }
        if ([System.IO.File]::ReadAllText((Join-Path $mixedDst "input\sub\data.txt")) -ne "nested") { throw "First folder tree lost" }
    } finally { Remove-Item $mixedZip -Force -ErrorAction SilentlyContinue }
    Write-Host "PASS mixed files/folders preserving directory roots"

    # UI defaults: first added source fills output; advanced remains collapsed,
    # and the memo is a multiline field.
    $ui = [Activator]::CreateInstance($builderType, $true)
    try {
        $instanceFlags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic
        $addSources = $builderType.GetMethod("AddSources",$instanceFlags)
        $addArguments = [object[]]::new(1)
        $addArguments[0] = [string[]]@($src,$looseFile)
        $addSources.Invoke($ui,$addArguments) | Out-Null
        $uiOutput = $builderType.GetField("output",$instanceFlags).GetValue($ui)
        $uiMemo = $builderType.GetField("note",$instanceFlags).GetValue($ui)
        $uiPanel = $builderType.GetField("advancedPanel",$instanceFlags).GetValue($ui)
        if (-not [string]::Equals([string]$uiOutput.Text,$expectedPath,[System.StringComparison]::OrdinalIgnoreCase)) { throw "First source did not auto-fill EXE path: $($uiOutput.Text)" }
        if (-not $uiMemo.Multiline) { throw "Memo does not allow multiple lines" }
        if ($uiPanel.Visible) { throw "Advanced options not collapsed" }
    } finally { $ui.Dispose() }
    Write-Host "PASS default output UI and collapsed multiline advanced options"

    # Verify the exact Windows Shell COM interface identity and that it can be
    # queried from the Windows FileOpenDialog coclass without opening a modal GUI.
    $pickerType = $builder.GetType("DKSelfExtractor.MultiFolderPicker")
    $nestedFlags = [System.Reflection.BindingFlags]::NonPublic
    $dialogInterface = $pickerType.GetNestedType("IFileOpenDialog",$nestedFlags)
    $dialogClass = $pickerType.GetNestedType("FileOpenDialog",$nestedFlags)
    $expectedIID = [Guid]"d57c7288-d4ad-4768-be02-9d969532d960"
    if ($dialogInterface.GUID -ne $expectedIID) { throw "Wrong native IFileOpenDialog IID" }
    $nativeDialog = [Activator]::CreateInstance($dialogClass,$true)
    $unknown = [IntPtr]::Zero
    $interfacePointer = [IntPtr]::Zero
    try {
        $unknown = [System.Runtime.InteropServices.Marshal]::GetIUnknownForObject($nativeDialog)
        $hr = [System.Runtime.InteropServices.Marshal]::QueryInterface($unknown,[ref]$expectedIID,[ref]$interfacePointer)
        if ($hr -ne 0 -or $interfacePointer -eq [IntPtr]::Zero) {
            throw ("Native IFileOpenDialog COM QueryInterface failed: 0x{0:X8}" -f $hr)
        }
    } finally {
        if ($interfacePointer -ne [IntPtr]::Zero) { [void][System.Runtime.InteropServices.Marshal]::Release($interfacePointer) }
        if ($unknown -ne [IntPtr]::Zero) { [void][System.Runtime.InteropServices.Marshal]::Release($unknown) }
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($nativeDialog)
    }
    Write-Host "PASS native Windows IFileOpenDialog COM interface activation"

    # Simulate an Explorer file-drop event, duplicate suppression, multi-item
    # keyboard removal, and multi-item removal through the button's shared action.
    $ui = [Activator]::CreateInstance($builderType,$true)
    try {
        $instanceFlags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic
        $addMethod = $builderType.GetMethod("AddSources",$instanceFlags)
        $list = $builderType.GetField("sources",$instanceFlags).GetValue($ui)
        if (-not $ui.AllowDrop -or -not $list.AllowDrop -or -not $list.MultiSelect) {
            throw "File-drop or multi-select not enabled on controls"
        }
        $dropData = [System.Windows.Forms.DataObject]::new()
        $dropFiles = [string[]]@($src,$looseFile,$otherFolder)
        $dropData.SetData([System.Windows.Forms.DataFormats]::FileDrop,[object]$dropFiles)
        $drag = [System.Windows.Forms.DragEventArgs]::new(
            $dropData,0,0,0,[System.Windows.Forms.DragDropEffects]::Copy,[System.Windows.Forms.DragDropEffects]::None)
        $builderType.GetMethod("OnSourceDragEnter",$instanceFlags).Invoke($ui,[object[]]@($list,$drag)) | Out-Null
        if ($drag.Effect -ne [System.Windows.Forms.DragDropEffects]::Copy) { throw "Explorer drag not accepted" }
        $builderType.GetMethod("OnSourceDragDrop",$instanceFlags).Invoke($ui,[object[]]@($list,$drag)) | Out-Null
        if ($list.Items.Count -ne 3) { throw "Explorer file and folder drop lost entries" }
        $builderType.GetMethod("OnSourceDragDrop",$instanceFlags).Invoke($ui,[object[]]@($list,$drag)) | Out-Null
        if ($list.Items.Count -ne 3) { throw "Duplicate paths were added on repeated drop" }
        [void]$list.Handle
        $list.Items[0].Selected = $true
        $list.Items[1].Selected = $true
        if ($list.SelectedItems.Count -ne 2) { throw "Ctrl/Shift-style multiple selection failed" }
        $key = [System.Windows.Forms.KeyEventArgs]::new([System.Windows.Forms.Keys]::Delete)
        $builderType.GetMethod("OnSourceKeyDown",$instanceFlags).Invoke($ui,[object[]]@($list,$key)) | Out-Null
        if ($list.Items.Count -ne 1 -or -not $key.Handled) { throw "Delete did not remove all selected items" }
        $addArgs = [object[]]::new(1)
        $addArgs[0] = [string[]]@($src,$looseFile)
        $addMethod.Invoke($ui,$addArgs) | Out-Null
        $list.Items[0].Selected = $true
        $list.Items[1].Selected = $true
        $builderType.GetMethod("RemoveSelectedSources",$instanceFlags).Invoke($ui,$null) | Out-Null
        if ($list.Items.Count -ne 1) { throw "Remove button did not remove all selected items" }
    } finally { $ui.Dispose() }
    Write-Host "PASS Explorer file/folder drop and Ctrl/Shift multiple removal via Delete/button"

    $tampered = Join-Path $out "tampered.exe"
    [System.IO.File]::Copy((Join-Path $out "bundle-2.exe"), $tampered)
    $tamperInfo = Invoke-Internal $read @($tampered)
    $offsetField = $tamperInfo.GetType().GetField("Offset",([System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Public))
    $offset = [long]$offsetField.GetValue($tamperInfo)
    $stream = [System.IO.File]::Open($tampered,[System.IO.FileMode]::Open,[System.IO.FileAccess]::ReadWrite)
    try {
        $stream.Position = $offset
        $first = $stream.ReadByte()
        $stream.Position = $offset
        $stream.WriteByte([byte]($first -bxor 1))
    } finally { $stream.Dispose() }
    try {
        Invoke-Internal $unpack @($tampered,$tamperInfo,"pass-123") | Out-Null
        throw "Tampered encrypted payload succeeded"
    } catch {
        $cause = $_.Exception
        while ($null -ne $cause.InnerException -and -not ($cause -is [System.IO.InvalidDataException])) { $cause = $cause.InnerException }
        if (-not ($cause -is [System.IO.InvalidDataException])) { throw }
    }
    Write-Host "PASS ciphertext tampering rejected"
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
    Write-Host ("Builder EXE bytes: " + (Get-Item $builderExe).Length)
    Write-Host ("Stub EXE bytes: " + (Get-Item $stubExe).Length)
}
finally { Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue }
