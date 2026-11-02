Add-Type -AssemblyName System.Drawing

$cell = 96
$pixel = 1
$columns = 8
$rows = 6
$output = Join-Path $PSScriptRoot "..\src\CodexTaskMonitor.App\Assets\PixelPetSpriteSheet.png"

$bitmap = New-Object System.Drawing.Bitmap ($cell * $columns), ($cell * $rows), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

$navy = [System.Drawing.Color]::FromArgb(255, 27, 50, 89)
$dark = [System.Drawing.Color]::FromArgb(255, 18, 31, 56)
$mint = [System.Drawing.Color]::FromArgb(255, 100, 205, 176)
$mintLight = [System.Drawing.Color]::FromArgb(255, 164, 239, 207)
$cyan = [System.Drawing.Color]::FromArgb(255, 114, 235, 248)
$coral = [System.Drawing.Color]::FromArgb(255, 255, 139, 115)
$cream = [System.Drawing.Color]::FromArgb(255, 244, 250, 243)
$shadow = [System.Drawing.Color]::FromArgb(65, 24, 51, 67)

function Block([int]$x, [int]$y, [int]$width, [int]$height, $color) {
    $brush = New-Object System.Drawing.SolidBrush $color
    $graphics.FillRectangle($brush, $x * $pixel, $y * $pixel, $width * $pixel, $height * $pixel)
    $brush.Dispose()
}

function DrawSpark([int]$x, [int]$y, $color) {
    Block $x ($y + 1) 1 3 $color
    Block ($x - 1) ($y + 2) 3 1 $color
}

function DrawRobot([int]$cellX, [int]$cellY, [int]$frame, [int]$row) {
    $x = $cellX * $cell
    $y = $cellY * $cell
    $pose = $frame % 8
    $bob = 0
    $armShift = 0
    $leftLeg = 0
    $rightLeg = 0
    $eyesClosed = $false
    $working = $row -eq 1
    $waiting = $row -eq 2
    $celebrating = $row -eq 3
    $walking = $row -eq 4
    $resting = $row -eq 5

    if ($row -eq 0) { $bob = if ($pose -in 2, 3) { -1 } else { 0 }; $eyesClosed = $pose -eq 6 }
    if ($working) { $bob = if ($pose % 2 -eq 0) { 0 } else { -1 }; $armShift = if ($pose % 2 -eq 0) { 1 } else { -1 } }
    if ($waiting) { $armShift = if ($pose % 2 -eq 0) { 2 } else { 0 }; $eyesClosed = $pose -eq 5 }
    if ($celebrating) { $bob = @(-1, -4, -7, -9, -7, -4, -1, 0)[$pose]; $armShift = 4 }
    if ($walking) { $leftLeg = @(0, 2, 3, 1, 0, -1, -3, -2)[$pose]; $rightLeg = @(0, -2, -3, -1, 0, 1, 3, 2)[$pose]; $bob = if ($pose % 2 -eq 0) { 0 } else { -1 } }
    if ($resting) { $bob = if ($pose % 4 -eq 0) { 1 } else { 0 }; $eyesClosed = $true }

    $cx = $x + 48
    $baseY = $y + 52 + $bob
    Block ($cx - 12) ($baseY + 20) 24 2 $shadow

    # antenna and head shell
    Block ($cx - 2) ($baseY - 23) 4 4 $navy
    Block ($cx - 4) ($baseY - 25) 8 3 $mint
    Block ($cx - 3) ($baseY - 26) 6 1 $mintLight
    Block ($cx - 15) ($baseY - 18) 30 20 $navy
    Block ($cx - 17) ($baseY - 14) 34 13 $navy
    Block ($cx - 14) ($baseY - 20) 28 3 $mint
    Block ($cx - 18) ($baseY - 15) 3 12 $mint
    Block ($cx + 15) ($baseY - 15) 3 12 $mint
    Block ($cx - 14) ($baseY + 0) 28 3 $mint
    Block ($cx - 11) ($baseY - 14) 22 13 $dark
    Block ($cx - 10) ($baseY - 13) 20 1 ([System.Drawing.Color]::FromArgb(255, 52, 82, 117))

    if ($eyesClosed) {
        Block ($cx - 7) ($baseY - 7) 4 1 $cyan
        Block ($cx + 3) ($baseY - 7) 4 1 $cyan
    } else {
        Block ($cx - 7) ($baseY - 9) 4 4 $cyan
        Block ($cx + 3) ($baseY - 9) 4 4 $cyan
        Block ($cx - 6) ($baseY - 8) 1 1 $cream
        Block ($cx + 4) ($baseY - 8) 1 1 $cream
    }

    # body and feet
    Block ($cx - 10) ($baseY + 4) 20 13 $navy
    Block ($cx - 8) ($baseY + 5) 16 8 $mint
    Block ($cx - 4) ($baseY + 7) 8 3 $dark
    Block ($cx - 2) ($baseY + 8) 4 1 $cyan
    Block ($cx - 8) ($baseY + 17 + $leftLeg) 7 6 $navy
    Block ($cx + 1) ($baseY + 17 + $rightLeg) 7 6 $navy
    Block ($cx - 10) ($baseY + 21 + $leftLeg) 9 3 $mint
    Block ($cx + 1) ($baseY + 21 + $rightLeg) 9 3 $mint

    # arms: working taps, waiting points, celebration lifts
    $leftArmY = $baseY + 7 - $armShift
    $rightArmY = $baseY + 7 - $armShift
    Block ($cx - 14) $leftArmY 5 7 $navy
    Block ($cx + 9) $rightArmY 5 7 $navy
    Block ($cx - 16) ($leftArmY + 4) 4 4 $mint
    Block ($cx + 12) ($rightArmY + 4) 4 4 $mint
    if ($waiting) { DrawSpark ($cx + 18) ($baseY - 17) $coral }
    if ($working) {
        Block ($cx - 18) ($baseY + 18) 36 2 $dark
        Block ($cx - 13) ($baseY + 17) 8 1 $cyan
        Block ($cx + 4) ($baseY + 17) 7 1 $cyan
    }
    if ($celebrating) {
        DrawSpark ($cx - 20) ($baseY - 13) $cyan
        DrawSpark ($cx + 20) ($baseY - 8) $coral
        DrawSpark ($cx - 14) ($baseY - 27) $mintLight
    }
    if ($resting -and $pose -in 3, 4, 5) {
        Block ($cx + 17) ($baseY - 15) 1 1 $cyan
        Block ($cx + 19) ($baseY - 17) 2 1 $cyan
        Block ($cx + 22) ($baseY - 19) 2 1 $cyan
    }
}

for ($row = 0; $row -lt $rows; $row++) {
    for ($frame = 0; $frame -lt $columns; $frame++) {
        DrawRobot $frame $row $frame $row
    }
}

$directory = Split-Path -Parent $output
New-Item -ItemType Directory -Force $directory | Out-Null
$bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
Write-Host "Created $output"
