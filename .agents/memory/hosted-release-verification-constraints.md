---
name: Hosted release verification constraints
description: Access and platform prerequisites for validating tagged Windows releases from this workspace.
---

Do not create a deliberately mismatched release tag until a credential has successfully updated the hosted workflow file under `.github/workflows`; a release is not fully verified until the hosted jobs, release asset list, and Windows x64 binary launch have been checked.

**Why:** A repository credential may read and update ordinary files while lacking GitHub's separate `workflow` permission. A failed update can leave the hosted workflow invalid or stale, making a mismatch-tag test unsafe and inconclusive. The packaged output is a Windows GUI executable, so Linux-only validation cannot prove the release launches.

**How to apply:** First perform and verify a protected workflow-file update on the target branch. If the OAuth connector remains scoped to `repo` after reauthorization, or the configured Git remote cannot authenticate a push, obtain a repository credential that explicitly supports workflow writes before publishing tags. Do not treat successful unreferenced Git blob uploads as proof of workflow-write access; later tree or commit operations may still be denied. A validated patch applied from an authenticated checkout is a reliable fallback. Then confirm the tagged hosted workflow, exactly one release asset, and a Windows x64 launch.