# qBittorrent hand-off regression fixture

Run the focused safety checks with:

```bash
dotnet run --project JudasEncodingManager/RegressionTests/JudasEncodingManager.RegressionTests.csproj
```

The fixture uses simulated qBittorrent WebUI state responses and controlled
file operations. It verifies accepted paused/stopped state names, rejects an
unconfirmed stop, prevents a file move before exclusive access succeeds,
revalidates access on move retries, and continues the queue after one item
fails. `Build.ps1` runs these checks before creating a release build.