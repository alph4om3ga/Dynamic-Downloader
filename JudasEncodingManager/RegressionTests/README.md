# Regression fixture

Run the focused safety checks with:

```bash
dotnet run --project JudasEncodingManager/RegressionTests/JudasEncodingManager.RegressionTests.csproj --framework net8.0
```

The fixture uses simulated qBittorrent WebUI state responses and controlled
file operations. It verifies accepted paused/stopped state names, rejects an
unconfirmed stop, prevents a file move before exclusive access succeeds,
revalidates access on move retries, and continues the queue after one item
fails. It also verifies Nyaa session expiry states, one-day warning
deduplication, reset eligibility, and settings round-tripping. `Build.ps1`
runs these checks before creating a release build.

On Windows, run the QueueManager completion regression against the WPF
assembly with:

```powershell
dotnet run --project JudasEncodingManager/RegressionTests/JudasEncodingManager.RegressionTests.csproj --framework net8.0-windows
```