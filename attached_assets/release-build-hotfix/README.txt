Release build hotfix

Cause
The GitHub Windows release-build job runs on a fresh runner. Build.ps1 restored only the WPF application, then tried to run the separate RegressionTests project with --no-restore. That project had no project.assets.json file, producing NETSDK1004 and stopping packaging.

Apply this fix from an up-to-date local clone:
  git apply fix-release-build-regression-restore.patch
  git add JudasEncodingManager/JudasEncodingManager/Build.ps1
  git commit -m "Restore regression project before release checks"
  git push origin main

After the push, open GitHub Actions and rerun or wait for the new Windows queue regression workflow run. The Release build and packaging job should now restore both projects before running the no-restore regression check.
