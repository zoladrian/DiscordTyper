# PowerShell script to help verify modal fixes
# Run after making changes to check compilation

Write-Host "Building Discord Bot project..." -ForegroundColor Cyan
dotnet build ./TyperBot.DiscordBot/TyperBot.DiscordBot.csproj

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ Build succeeded!" -ForegroundColor Green
    Write-Host "`nChecking modal handler registrations in code..." -ForegroundColor Cyan
    
    # Count IModal classes
    $modalClasses = (Select-String -Path "TyperBot.DiscordBot/Modules/AdminModule.cs" -Pattern "^public class.*: IModal" | Measure-Object).Count
    Write-Host "Found $modalClasses IModal classes in AdminModule" -ForegroundColor Yellow
    
    # Count [ModalInteraction] attributes  
    $modalHandlers = (Select-String -Path "TyperBot.DiscordBot/Modules/*.cs" -Pattern "\[ModalInteraction\(" | Measure-Object).Count
    Write-Host "Found $modalHandlers [ModalInteraction] handlers" -ForegroundColor Yellow
    
    Write-Host "`nNext: Run the bot and check startup logs for 'Registered X modal handler(s)'" -ForegroundColor Cyan
} else {
    Write-Host "`n❌ Build failed! Check errors above." -ForegroundColor Red
}

