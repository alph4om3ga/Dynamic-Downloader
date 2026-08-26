---
name: Hosted release verification constraints
description: External prerequisites for validating tagged Windows releases from this workspace.
---

Tagged-release verification cannot be completed solely in this Linux workspace when GitHub push credentials and a Windows x64 machine are unavailable.

**Why:** The hosted GitHub Actions workflow must be triggered by a tag, and the output is a Windows GUI executable whose launch cannot be validated on Linux. The available GitHub connector may also be blocked when attempting to write workflow files.

**How to apply:** Before treating a Windows release task as complete, confirm an authenticated tag push, successful hosted workflow jobs, the release asset list, and an actual Windows x64 launch check. Keep the task open if any of those external checks cannot be performed.