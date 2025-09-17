param (
    [switch]$commit
)

cd discord-file-scraper
dotnet run
cd ../..

$source = "tools/discord-file-scraper/onnx"
$destination = "models"
$newFiles = $false
Get-ChildItem -Path $source -File | ForEach-Object {
    $destFile = Join-Path -Path $destination -ChildPath $_.Name
    if (-Not (Test-Path -Path $destFile)) {
        Move-Item -Path $_.FullName -Destination $destFile
        Write-Host "[*] Moved $($_.Name)"
        $newFiles = $true
    }
}

if ($commit -and $newFiles) {
    git add $destination
    git commit -m "chore(models): re-scrape models"
    Write-Host "[*] Commited new models to repo"
}