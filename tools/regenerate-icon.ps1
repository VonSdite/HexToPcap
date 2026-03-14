param(
    [string]$IconPath = "src/HexToPcap/Assets/HexToPcap.ico"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$resolvedIconPath = Resolve-Path -Path $IconPath
$iconBytes = [System.IO.File]::ReadAllBytes($resolvedIconPath)
if ($iconBytes.Length -lt 22) {
    throw "Icon file is too short: $resolvedIconPath"
}

$entryCount = [BitConverter]::ToUInt16($iconBytes, 4)
if ($entryCount -lt 1) {
    throw "Icon has no image entries: $resolvedIconPath"
}

$firstEntryOffset = 6
$imageSize = [BitConverter]::ToUInt32($iconBytes, $firstEntryOffset + 8)
$imageOffset = [BitConverter]::ToUInt32($iconBytes, $firstEntryOffset + 12)
if ($imageOffset + $imageSize -gt $iconBytes.Length) {
    throw "Icon entry points outside file bounds: $resolvedIconPath"
}

$sourceBytes = New-Object byte[] $imageSize
[Array]::Copy($iconBytes, [int]$imageOffset, $sourceBytes, 0, [int]$imageSize)

$tempPngPath = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), ".png")
[System.IO.File]::WriteAllBytes($tempPngPath, $sourceBytes)

try {
    $source = [System.Drawing.Image]::FromFile($tempPngPath)
    $sizes = @(16, 32, 48, 64, 128, 256)
    $entries = @()

    foreach ($size in $sizes) {
        $canvas = New-Object System.Drawing.Bitmap($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($source, 0, 0, $size, $size)

        $stream = New-Object System.IO.MemoryStream
        $canvas.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)

        $entries += [PSCustomObject]@{
            Size = $size
            Data = $stream.ToArray()
        }

        $stream.Dispose()
        $graphics.Dispose()
        $canvas.Dispose()
    }

    $source.Dispose()

    $output = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($output)

    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$entries.Count)

    $offset = 6 + (16 * $entries.Count)
    foreach ($entry in $entries) {
        $sizeByte = if ($entry.Size -eq 256) { [byte]0 } else { [byte]$entry.Size }
        $writer.Write($sizeByte)
        $writer.Write($sizeByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$entry.Data.Length)
        $writer.Write([UInt32]$offset)
        $offset += $entry.Data.Length
    }

    foreach ($entry in $entries) {
        $writer.Write($entry.Data)
    }

    [System.IO.File]::WriteAllBytes($resolvedIconPath, $output.ToArray())

    $writer.Dispose()
    $output.Dispose()
}
finally {
    if (Test-Path $tempPngPath) {
        Remove-Item -Path $tempPngPath -Force
    }
}
