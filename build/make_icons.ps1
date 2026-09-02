param(
    [string]$Source = "C:\0-Personal Documents\2\assets\discnot.png",
    [string]$OutDir = "C:\0-Personal Documents\2\build"
)

Add-Type -AssemblyName System.Drawing

function New-RoundedSquareBmp {
    param([System.Drawing.Bitmap]$Src, [int]$Size)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $sw = [float]$Src.Width
    $sh = [float]$Src.Height
    $scale = [Math]::Min($Size / $sw, $Size / $sh)
    $dw = [int][Math]::Round($sw * $scale)
    $dh = [int][Math]::Round($sh * $scale)
    $dx = [int][Math]::Floor(($Size - $dw) / 2.0)
    $dy = [int][Math]::Floor(($Size - $dh) / 2.0)
    $g.DrawImage($Src, $dx, $dy, $dw, $dh)
    $g.Dispose()
    return $bmp
}

function Write-PngIcon {
    param([System.Drawing.Bitmap]$Src, [int[]]$Sizes, [string]$OutPath)
    $fs = [System.IO.File]::Create($OutPath)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$Sizes.Count)

    $entries = @()
    foreach ($s in $Sizes) {
        $bmp = New-RoundedSquareBmp -Src $Src -Size $s
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $png = $ms.ToArray()
        $bmp.Dispose()
        if ($s -ge 256) { $bw8 = 0 } else { $bw8 = $s }
        $entries += [pscustomobject]@{ W = $bw8; H = $bw8; Len = $png.Length; Data = $png }
    }

    $offset = 6 + 16 * $Sizes.Count
    foreach ($e in $entries) {
        $bw.Write([byte]$e.W); $bw.Write([byte]$e.H)
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$e.Len); $bw.Write([uint32]$offset)
        $offset += $e.Len
    }
    foreach ($e in $entries) { $bw.Write($e.Data) }
    $bw.Close(); $fs.Close()
}

function Write-BmpIcon {
    param([System.Drawing.Bitmap]$Src, [int[]]$Sizes, [string]$OutPath)
    $fs = [System.IO.File]::Create($OutPath)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$Sizes.Count)

    $entries = @()
    foreach ($s in $Sizes) {
        $bmp = New-RoundedSquareBmp -Src $Src -Size $s
        $w = $bmp.Width; $h = $bmp.Height
        $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
        $bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $stride = $bd.Stride
        $raw = New-Object byte[] ($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $raw, 0, $raw.Length)
        $bmp.UnlockBits($bd)
        $bmp.Dispose()

        $flip = New-Object byte[] ($stride * $h)
        for ($r = 0; $r -lt $h; $r++) {
            [Array]::Copy($raw, $r * $stride, $flip, ($h - 1 - $r) * $stride, $stride)
        }

        $pixels = New-Object System.IO.MemoryStream
        $pbw = New-Object System.IO.BinaryWriter($pixels)
        $pbw.Write([int32]40)
        $pbw.Write([int32]$w); $pbw.Write([int32]($h * 2))
        $pbw.Write([uint16]1); $pbw.Write([uint16]32)
        $pbw.Write([uint32]0)
        $pbw.Write([uint32]($stride * $h))
        $pbw.Write([int32]0); $pbw.Write([int32]0)
        $pbw.Write([uint32]0); $pbw.Write([uint32]0)
        $pbw.Write($flip)
        $pbw.Flush()
        $dib = $pixels.ToArray()

        if ($s -ge 256) { $bw8 = 0 } else { $bw8 = $s }
        $entries += [pscustomobject]@{ W = $bw8; H = $bw8; Len = $dib.Length; Data = $dib }
    }

    $offset = 6 + 16 * $Sizes.Count
    foreach ($e in $entries) {
        $bw.Write([byte]$e.W); $bw.Write([byte]$e.H)
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$e.Len); $bw.Write([uint32]$offset)
        $offset += $e.Len
    }
    foreach ($e in $entries) { $bw.Write($e.Data) }
    $bw.Close(); $fs.Close()
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$src = New-Object System.Drawing.Bitmap($Source)

$pngSizes = @(16, 24, 32, 48, 64, 128, 256)
$bmpSizes = @(16, 32, 48, 256)

Write-PngIcon -Src $src -Sizes $pngSizes -OutPath (Join-Path $OutDir "icon.ico")
Write-BmpIcon -Src $src -Sizes $bmpSizes -OutPath (Join-Path $OutDir "exe_icon.ico")
$src.Dispose()

Get-Item (Join-Path $OutDir "icon.ico"), (Join-Path $OutDir "exe_icon.ico") | Select-Object Name, Length
