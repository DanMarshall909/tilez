$tilez_root = "C:\code\tilez"
$project_path = "Tilez\Tilez.csproj"
$generated_dir = "generated\images"
$base_url = "http://localhost:1234"
$model = "flux.1-schnell"

Write-Host "TILEZ TILE GENERATION DEBUG"
Write-Host "Working: $tilez_root"
Write-Host "Model: $model"
Write-Host ""

$full_project = Join-Path $tilez_root $project_path
$full_gen_dir = Join-Path $tilez_root $generated_dir

Write-Host "Checking directories..."
if (Test-Path $full_project) {
    Write-Host "Project OK"
} else {
    Write-Host "ERROR: Project not found"
    exit 1
}

if (Test-Path $full_gen_dir) {
    Write-Host "Gen dir OK"
} else {
    Write-Host "ERROR: Gen dir not found"
    exit 1
}

Write-Host ""
Write-Host "Running test generation..."

Push-Location $tilez_root

$cmd = "dotnet run --project $project_path -- render --image-backend openai --ai-base-url $base_url --ai-model $model --x 77 --y 77 --zoom 3.0 --seed 888888"
Write-Host "Cmd: $cmd"

$output = & cmd.exe /C $cmd
Write-Host $output

Write-Host ""
Write-Host "Checking for generated files..."

$files = Get-ChildItem -Path $full_gen_dir -Filter "tile_77_77_*.png" 2>/dev/null
Write-Host "Found: $($files.Count) files"
if ($files.Count -gt 0) {
    foreach ($file in $files) {
        Write-Host "  $($file.Name)"
    }
}

Pop-Location
Write-Host "Done"
