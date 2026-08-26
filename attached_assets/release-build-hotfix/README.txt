Release v1.3 build hotfix

Cause
The release tag is v1.3, but the project still declared version 1.3.1. Build.ps1 correctly rejected the tag before packaging.

The patch also makes Build.ps1 restore the separate RegressionTests project on a fresh runner before using --no-restore, preventing a subsequent NETSDK1004 assets-file failure.

Apply this fix from an up-to-date local clone:
  git apply fix-release-build-regression-restore.patch
  git add JudasEncodingManager/JudasEncodingManager/Build.ps1
  git commit -m "Align release version with v1.3"
  git push origin main

After the push, open GitHub Actions and rerun or wait for the new Windows queue regression workflow run. The Release build and packaging job should accept v1.3, restore both projects, and then package the application.
