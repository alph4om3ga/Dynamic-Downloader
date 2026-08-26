GitHub update for Dynamic-Downloader

The current Replit main branch is four commits ahead of origin/main. These files contain the only file difference between those branches:

  .github/workflows/windows-queue-regression.yml

Option 1 - apply the patch from the repository root:
  git apply windows-queue-regression.patch

Option 2 - copy windows-queue-regression.yml into:
  .github/workflows/windows-queue-regression.yml

Then commit and push:
  git add .github/workflows/windows-queue-regression.yml
  git commit -m "Add Windows queue regression and release workflows"
  git push origin main

The patch includes the full file addition from origin/main.
