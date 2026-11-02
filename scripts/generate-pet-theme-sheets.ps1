Add-Type -AssemblyName System.Drawing

$source = Join-Path $PSScriptRoot "..\src\CodexTaskMonitor.App\Assets\PixelPetSpriteSheet.png"
$assets = Split-Path -Parent $source

$themes = @(
    @{
        Name = "PixelPetOceanSpriteSheet.png"
        Colors = @{
            "27,50,89" = "31,58,112"; "18,31,56" = "17,36,76"; "100,205,176" = "73,158,218"
            "164,239,207" = "157,218,248"; "114,235,248" = "112,235,248"; "255,139,115" = "255,184,109"
        }
    },
    @{
        Name = "PixelPetCoralSpriteSheet.png"
        Colors = @{
            "27,50,89" = "94,48,72"; "18,31,56" = "58,31,52"; "100,205,176" = "229,128,111"
            "164,239,207" = "250,190,161"; "114,235,248" = "255,221,135"; "255,139,115" = "255,105,112"
        }
    }
)

foreach ($theme in $themes) {
    $bitmap = New-Object System.Drawing.Bitmap $source
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            if ($pixel.A -eq 0) {
                continue
            }

            $key = "$($pixel.R),$($pixel.G),$($pixel.B)"
            if (-not $theme.Colors.ContainsKey($key)) {
                continue
            }

            $components = $theme.Colors[$key].Split(',')
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(
                $pixel.A,
                [int]$components[0],
                [int]$components[1],
                [int]$components[2]))
        }
    }

    $output = Join-Path $assets $theme.Name
    $bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "Created $output"
}
