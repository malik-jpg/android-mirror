# app.ico  : ikon bulat "X" (cincin emas + navy, isi krem, silang emas) digambar vektor
# logo.png : logo lengkap untuk header aplikasi, dari logo-malixnet.png
param([string]$Source = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'assets\logo-malixnet.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$here = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'assets'

function ToPng($bmp) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); return ,$ms.ToArray() }

$gold1 = [Drawing.Color]::FromArgb(236, 200, 110)
$gold2 = [Drawing.Color]::FromArgb(176, 132, 52)
$navy  = [Drawing.Color]::FromArgb(30, 50, 76)
$cream = [Drawing.Color]::FromArgb(244, 238, 224)

function RenderX([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)
    $f = [float]$s
    $pad = [Math]::Max(0.5, $f * 0.02)
    $full = New-Object Drawing.RectangleF $pad, $pad, ($f - 2 * $pad), ($f - 2 * $pad)
    $goldBrush = New-Object Drawing.Drawing2D.LinearGradientBrush $full, $gold1, $gold2, 45.0

    # cincin emas luar
    $g.FillEllipse($goldBrush, $full)
    # cincin navy
    $ring = $f * 0.075; if ($s -le 24) { $ring = $f * 0.09 }
    $navyR = New-Object Drawing.RectangleF ($full.X + $ring), ($full.Y + $ring), ($full.Width - 2 * $ring), ($full.Height - 2 * $ring)
    $g.FillEllipse((New-Object Drawing.SolidBrush $navy), $navyR)
    # isi krem (di ukuran kecil cincin navy ditipiskan supaya tetap terbaca)
    $nav = $f * 0.06; if ($s -le 24) { $nav = $f * 0.07 }
    $inner = New-Object Drawing.RectangleF ($navyR.X + $nav), ($navyR.Y + $nav), ($navyR.Width - 2 * $nav), ($navyR.Height - 2 * $nav)
    $g.FillEllipse((New-Object Drawing.SolidBrush $cream), $inner)

    # silang emas, dipotong di dalam cincin emas luar
    $clip = New-Object Drawing.Drawing2D.GraphicsPath
    $clip.AddEllipse($navyR)
    $g.SetClip($clip)
    $w = $f * 0.12; if ($s -le 24) { $w = $f * 0.15 }
    $pen = New-Object Drawing.Pen $goldBrush, $w
    $pen.StartCap = 'Flat'; $pen.EndCap = 'Flat'
    $g.DrawLine($pen, 0, 0, $f, $f)
    $g.DrawLine($pen, $f, 0, 0, $f)
    $g.ResetClip()
    $g.Dispose()
    return ,(ToPng $bmp)
}

function PngToDib([byte[]]$png) {
    # ICO entry 32bpp BGRA (bottom-up) + AND mask, untuk kompatibilitas penuh di ukuran < 256
    $bmp = [Drawing.Bitmap]::FromStream((New-Object IO.MemoryStream (,$png)))
    $s = $bmp.Width
    $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
    $w.Write([UInt32]40); $w.Write([Int32]$s); $w.Write([Int32]($s * 2))
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $w.Write([UInt32]0); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) {
        $c = $bmp.GetPixel($x, $y); $w.Write([Byte]$c.B); $w.Write([Byte]$c.G); $w.Write([Byte]$c.R); $w.Write([Byte]$c.A) } }
    $rowBytes = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($rowBytes * $s)))
    $w.Flush(); $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$imgs = @(); foreach ($s in $sizes) { $p = RenderX $s; if ($s -lt 256) { $p = PngToDib $p }; $imgs += ,$p }

$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([Byte]$b); $w.Write([Byte]$b); $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$imgs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $imgs[$i].Length
}
foreach ($img in $imgs) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $here 'app.ico'), $ms.ToArray())

# logo lengkap (bulat) untuk header
if (Test-Path $Source) {
    $src = [Drawing.Image]::FromFile($Source)
    $r = $src.Width * 0.452
    $crop = New-Object Drawing.RectangleF ($src.Width / 2 - $r), ($src.Height / 2 - $r), (2 * $r), (2 * $r)
    $bmp = New-Object Drawing.Bitmap 96, 96, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
    $p = New-Object Drawing.Drawing2D.GraphicsPath; $p.AddEllipse(0, 0, 95, 95); $g.SetClip($p)
    $g.DrawImage($src, (New-Object Drawing.RectangleF 0, 0, 96, 96), $crop, 'Pixel')
    $g.Dispose(); $src.Dispose()
    [IO.File]::WriteAllBytes((Join-Path $here 'logo.png'), (ToPng $bmp))
}
"icon ok"
