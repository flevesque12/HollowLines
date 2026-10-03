$folders = @("Assets\_Project\Scripts\Core", "Assets\_Project\Scripts\View")

foreach ($folder in $folders) {
    Get-ChildItem -Path $folder -Filter "*.cs" -Recurse | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        $newContent = $content -replace "using NUnit\.Framework;\r?\n", ""
        if ($content -ne $newContent) {
            Set-Content $_.FullName $newContent -NoNewline
            Write-Host "Cleaned: $($_.Name)"
        }
    }
}

Write-Host ""
Write-Host "Done! Retourne dans Unity et refais ton build."
