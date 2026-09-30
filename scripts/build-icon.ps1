# Draws the application icon and writes it as a multi-resolution .ico.
# Run by hand when the icon changes; the resulting .ico is committed.
param(
    [string]$Destino = "$PSScriptRoot/../src/ScreeningLoader.Shell/app.ico",
    [string]$Preview
)

Add-Type -AssemblyName System.Drawing

# The design system's primary color, the same one the rest of the tooling uses.
$Azul = [System.Drawing.Color]::FromArgb(0x48, 0x62, 0xFF)

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  if ($d -le 0) { $p.AddRectangle((New-Object System.Drawing.RectangleF $x, $y, $w, $h)); return $p }
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

function New-Icono([int]$S) {
  $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.Clear([System.Drawing.Color]::Transparent)

  # Background: the brand blue on a rounded tile like the design system's cards.
  $fondo = New-RoundedPath 0 0 $S $S ($S * 0.225)
  $g.FillPath((New-Object System.Drawing.SolidBrush $Azul), $fondo)
  $fondo.Dispose()

  # Below 40 px the two sheets blur into a blob, so a single, larger one goes there instead.
  $apilado = $S -ge 40

  if ($apilado) {
    $atras = New-RoundedPath ($S * 0.375) ($S * 0.215) ($S * 0.33) ($S * 0.44) ($S * 0.045)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 255, 255, 255))), $atras)
    $atras.Dispose()

    $frenteX = $S * 0.265
    $frenteY = $S * 0.315
    $frenteW = $S * 0.36
    $frenteH = $S * 0.47
  }
  else {
    $frenteW = $S * 0.42
    $frenteH = $S * 0.54
    $frenteX = ($S - $frenteW) / 2
    $frenteY = ($S - $frenteH) / 2
  }
  $frente = New-RoundedPath $frenteX $frenteY $frenteW $frenteH ($S * 0.05)
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), $frente)
  $frente.Dispose()

  # Text lines: at 16 px they turn to mush, so they only appear when there are pixels for them.
  if ($S -ge 48) {
    $alto = [Math]::Max(1.0, $S * 0.045)
    $pincel = New-Object System.Drawing.SolidBrush $Azul
    foreach ($i in 0..2) {
      if ($i -eq 2) { $ancho = $frenteW * 0.42 } else { $ancho = $frenteW * 0.62 }
      $y = $frenteY + $frenteH * (0.22 + $i * 0.235)
      $linea = New-RoundedPath ($frenteX + $frenteW * 0.19) $y $ancho $alto ($alto / 2)
      $g.FillPath($pincel, $linea)
      $linea.Dispose()
    }
    $pincel.Dispose()
  }

  $g.Dispose()
  return $bmp
}

# Small sizes go as DIB because every Windows version can read it; large ones as PNG.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
  $S = $bmp.Width
  $ms = New-Object System.IO.MemoryStream
  $w = New-Object System.IO.BinaryWriter $ms

  $w.Write([uint32]40); $w.Write([int32]$S); $w.Write([int32]($S * 2))
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
  $w.Write([uint32]($S * $S * 4)); $w.Write([int32]0); $w.Write([int32]0)
  $w.Write([uint32]0); $w.Write([uint32]0)

  for ($y = $S - 1; $y -ge 0; $y--) {
    for ($x = 0; $x -lt $S; $x++) {
      $c = $bmp.GetPixel($x, $y)
      $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
    }
  }

  $filaMascara = [Math]::Floor((($S + 31) / 32)) * 4
  $w.Write((New-Object byte[] ($filaMascara * $S)))

  $w.Flush()
  $bytes = $ms.ToArray()
  $w.Dispose(); $ms.Dispose()
  return ,$bytes
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bytes = $ms.ToArray()
  $ms.Dispose()
  return ,$bytes
}

$tamanos = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$imagenes = @{}
$payloads = @()

foreach ($S in $tamanos) {
  $bmp = New-Icono $S
  $imagenes[$S] = $bmp

  if ($S -ge 128) { $datos = [byte[]](Get-PngBytes $bmp) } else { $datos = [byte[]](Get-DibBytes $bmp) }

  $payloads += , @{ Size = $S; Data = $datos }
}

$fs = New-Object System.IO.FileStream $Destino, ([System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter $fs

$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$payloads.Count)

$offset = 6 + 16 * $payloads.Count
foreach ($p in $payloads) {
  if ($p.Size -ge 256) { $dim = 0 } else { $dim = $p.Size }
  $bw.Write([byte]$dim); $bw.Write([byte]$dim)
  $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32)
  $bw.Write([uint32]$p.Data.Length); $bw.Write([uint32]$offset)
  $offset += $p.Data.Length
}

foreach ($p in $payloads) { $bw.Write($p.Data) }

$bw.Flush(); $bw.Dispose(); $fs.Dispose()

if ($Preview) {
  $lienzo = New-Object System.Drawing.Bitmap 560, 300
  $g = [System.Drawing.Graphics]::FromImage($lienzo)
  $g.Clear([System.Drawing.Color]::FromArgb(0x0A, 0x0A, 0x0A))
  $g.DrawImage($imagenes[256], 20, 20, 256, 256)
  $x = 300
  foreach ($S in @(128, 64, 48, 32, 24, 16)) {
    $g.DrawImage($imagenes[$S], $x, 20, $S, $S)
    $x += $S + 16
    if ($x -gt 520) { $x = 300 }
  }
  $y = 170
  $x = 300
  foreach ($S in @(48, 32, 24, 16)) {
    $g.DrawImage($imagenes[$S], $x, $y, $S, $S)
    $x += $S + 20
  }
  $g.Dispose()
  $lienzo.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
  $lienzo.Dispose()
}

foreach ($bmp in $imagenes.Values) { $bmp.Dispose() }

"written: $Destino ($((Get-Item $Destino).Length) bytes, $($payloads.Count) sizes)"
