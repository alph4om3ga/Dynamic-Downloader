Release v1.3 version-only patch

Use this patch if the earlier Build.ps1 regression-restore hotfix has already been applied.
It changes only the project version from 1.3.1 to 1.3.

From the repository root:
  git apply fix-release-v1.3-version-only.patch
  git add JudasEncodingManager/JudasEncodingManager/JudasEncodingManager.csproj
  git commit -m "Align release version with v1.3"
  git push origin main
