# Script to clear database and restart application
Write-Host "Czyszczenie bazy danych..."

# Stop any running instances
$processes = Get-Process -Name "TyperBot.DiscordBot" -ErrorAction SilentlyContinue
if ($processes) {
    Write-Host "Zatrzymywanie działającej aplikacji..."
    $processes | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# Remove database files
$dbFiles = Get-ChildItem -Path . -Filter "*.db" -Recurse -ErrorAction SilentlyContinue
foreach ($db in $dbFiles) {
    try {
        Remove-Item $db.FullName -Force
        Write-Host "Usunięto: $($db.FullName)"
    } catch {
        Write-Host "Nie można usunąć $($db.FullName): $_"
    }
}

Write-Host "Baza danych wyczyszczona. Uruchom aplikację ponownie: dotnet run --project TyperBot.DiscordBot"

