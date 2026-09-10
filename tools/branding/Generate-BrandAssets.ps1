[CmdletBinding()]
param(
    [string]$OutputDirectory = '.\src\Gradient.PcHealthCheck\assets'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

function Resolve-SourceBase64Parts {
    param([string]$Directory)
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "Brand source directory not found: $Directory" }
    $parts = @(Get-ChildItem -LiteralPath $Directory -Filter 'part-*' -File | Sort-Object Name)
    if ($parts.Count -lt 1) { throw "Brand source chunks not found: $Directory" }
    $text = ($parts | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding ASCII }) -join ''
    $text = $text -replace '\s',''
    if ([string]::IsNullOrWhiteSpace($text)) { throw "Brand source is empty: $Directory" }
    try { return [Convert]::FromBase64String($text) }
    catch { throw "Invalid base64 brand source chunks: $Directory" }
}

function Write-Bytes {
    param([byte[]]$Bytes, [string]$Path)
    [IO.File]::WriteAllBytes($Path, $Bytes)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -le 0) {
        throw "Generated brand file is missing/empty: $Path"
    }
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    $stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash($stream)
        return ([BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

function Convert-RgbToHsv {
    param([int]$R,[int]$G,[int]$B)
    $r1=$R/255.0; $g1=$G/255.0; $b1=$B/255.0
    $max=[Math]::Max($r1,[Math]::Max($g1,$b1)); $min=[Math]::Min($r1,[Math]::Min($g1,$b1)); $d=$max-$min
    $h=0.0
    if ($d -gt 0) {
        if ($max -eq $r1) { $h=60.0*((($g1-$b1)/$d)%6) }
        elseif ($max -eq $g1) { $h=60.0*((($b1-$r1)/$d)+2) }
        else { $h=60.0*((($r1-$g1)/$d)+4) }
        if ($h -lt 0) { $h += 360.0 }
    }
    $s=if($max -eq 0){0.0}else{$d/$max}
    return @($h,$s,$max)
}

function Convert-HsvToColor {
    param([double]$H,[double]$S,[double]$V,[int]$A)
    $c=$V*$S; $x=$c*(1-[Math]::Abs((($H/60.0)%2)-1)); $m=$V-$c
    $r=0.0;$g=0.0;$b=0.0
    if($H -lt 60){$r=$c;$g=$x}
    elseif($H -lt 120){$r=$x;$g=$c}
    elseif($H -lt 180){$g=$c;$b=$x}
    elseif($H -lt 240){$g=$x;$b=$c}
    elseif($H -lt 300){$r=$x;$b=$c}
    else{$r=$c;$b=$x}
    return [Drawing.Color]::FromArgb($A,[int][Math]::Round(($r+$m)*255),[int][Math]::Round(($g+$m)*255),[int][Math]::Round(($b+$m)*255))
}

function Convert-ToGreenVariant {
    param([Drawing.Bitmap]$Source)
    $result = [Drawing.Bitmap]::new($Source.Width,$Source.Height,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for($y=0;$y -lt $Source.Height;$y++) {
        for($x=0;$x -lt $Source.Width;$x++) {
            $p=$Source.GetPixel($x,$y)
            if($p.A -eq 0){$result.SetPixel($x,$y,$p);continue}
            $hsv=Convert-RgbToHsv $p.R $p.G $p.B
            if($hsv[1] -ge 0.12 -and $hsv[2] -ge 0.10) {
                $s=[Math]::Min(1.0,[Math]::Max(0.58,$hsv[1]))
                $result.SetPixel($x,$y,(Convert-HsvToColor 133.0 $s $hsv[2] $p.A))
            } else {
                $result.SetPixel($x,$y,$p)
            }
        }
    }
    return $result
}

function Get-PngBytes {
    param([Drawing.Image]$Image)
    $ms=[IO.MemoryStream]::new()
    try {
        $Image.Save($ms,[Drawing.Imaging.ImageFormat]::Png)
        return $ms.ToArray()
    } finally { $ms.Dispose() }
}

function New-SquarePngBytes {
    param([Drawing.Image]$Source,[int]$Size)
    $bmp=[Drawing.Bitmap]::new($Size,$Size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $g=[Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear([Drawing.Color]::Transparent)
            $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale=[Math]::Min(($Size*0.90)/$Source.Width,($Size*0.90)/$Source.Height)
            $w=[int][Math]::Round($Source.Width*$scale)
            $h=[int][Math]::Round($Source.Height*$scale)
            $x=[int](($Size-$w)/2); $y=[int](($Size-$h)/2)
            $g.DrawImage($Source,[Drawing.Rectangle]::new($x,$y,$w,$h))
        } finally { $g.Dispose() }
        return Get-PngBytes $bmp
    } finally { $bmp.Dispose() }
}

function Write-MultiSizeIco {
    param([Drawing.Image]$Source,[string]$Path)
    $sizes=@(16,20,24,32,40,48,64,128,256)
    $images = foreach($size in $sizes){ [pscustomobject]@{ Size=$size; Bytes=(New-SquarePngBytes $Source $size) } }
    $ms=[IO.MemoryStream]::new(); $bw=[IO.BinaryWriter]::new($ms)
    try {
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)
        $offset=6+(16*$images.Count)
        foreach($image in $images){
            $dimension=if($image.Size -eq 256){0}else{$image.Size}
            $bw.Write([byte]$dimension); $bw.Write([byte]$dimension); $bw.Write([byte]0); $bw.Write([byte]0)
            $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$image.Bytes.Length); $bw.Write([uint32]$offset)
            $offset += $image.Bytes.Length
        }
        foreach($image in $images){ $bw.Write([byte[]]$image.Bytes) }
        $bw.Flush(); Write-Bytes $ms.ToArray() $Path
    } finally { $bw.Dispose(); $ms.Dispose() }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$shieldBytes=Resolve-SourceBase64Parts '.\tools\branding\source\g-shield'
if($shieldBytes.Length -ne 19842){ throw "Corporate shield size mismatch: $($shieldBytes.Length) bytes" }
$shieldPath=Join-Path $OutputDirectory 'g-shield.png'
Write-Bytes $shieldBytes $shieldPath

$sourceStream=[IO.MemoryStream]::new($shieldBytes,$false)
try {
    $sourceImage=[Drawing.Image]::FromStream($sourceStream,$true,$true)
    try {
        $sourceBitmap=[Drawing.Bitmap]::new($sourceImage)
        try {
            $green=Convert-ToGreenVariant $sourceBitmap
            try {
                Write-Bytes (Get-PngBytes $green) (Join-Path $OutputDirectory 'gpchc-icon-green.png')
                Write-MultiSizeIco $green (Join-Path $OutputDirectory 'gpchc.ico')
            } finally { $green.Dispose() }
        } finally { $sourceBitmap.Dispose() }
    } finally { $sourceImage.Dispose() }
} finally { $sourceStream.Dispose() }

$shieldHash=Get-Sha256Hex -Path $shieldPath
if($shieldHash -ne '5157c5cf83a6cfddb74c51701a693e401e36b400812505a1e46e15d760c72877') {
    throw "Corporate shield SHA-256 mismatch: $shieldHash"
}

$icon=Get-Item (Join-Path $OutputDirectory 'gpchc.ico')
if($icon.Length -lt 4096){ throw "Generated ICO unexpectedly small: $($icon.Length) bytes" }
Write-Host "Corporate shield: $shieldHash"
Write-Host "Green application icon: $($icon.FullName) ($($icon.Length) bytes)"
