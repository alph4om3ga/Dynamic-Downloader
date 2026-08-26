Release v1.3 version-only patch

The previous package accidentally contained an empty patch because an automatic
checkpoint had already recorded the version change. This package contains an
explicit one-line patch.

From the repository root:
  git apply fix-release-v1.3-version-only.patch
  git add JudasEncodingManager/JudasEncodingManager/JudasEncodingManager.csproj
  git commit -m "Align release version with v1.3"
  git push origin main

Fallback if needed:
  sed -i 's/<Version>1\.3\.1<\/Version>/<Version>1.3<\/Version>/' JudasEncodingManager/JudasEncodingManager/JudasEncodingManager.csproj
