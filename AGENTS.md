# Dialogue Machine repository instructions

## Project and scope

- This repository is the offline C#/.NET 10 and Avalonia desktop editor, separate from the website. The GitHub remote is `origin`, currently `https://github.com/xLostPro/DialogueMachine.git`; the primary branch is `master`.
- Read `ARCHITECTURE.md` before substantial implementation work and `ROADMAP.md` when selecting the next feature. Keep the roadmap's completed-work status accurate.
- Preserve website JSON field names, numeric visibility ordering, unknown-field round trips, and the separation between Core and UI.

## Verify completed work

- Run the relevant regression checks for behavior changes:

  ```powershell
  $env:AVALONIA_TELEMETRY_OPTOUT = '1'
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  dotnet run --project tests/DialogueMachine.Tests.csproj -c Release -- artifacts
  ```

- For UI or packaging changes, review the generated renders, build Windows, and run `Packaging/Test-WindowsPackage.ps1`. An existing restore can be reused with `--no-restore`; report any verification that cannot be completed.
- Keep the project version, `Packaging/Info.plist`, About text and release notes aligned when preparing a version change. Update README test counts when the harness changes.
- Run `git diff --check` and review the complete intended diff before committing.

## GitHub delivery

- **The user-established default is to commit verified changes, push a `codex/` feature branch, and open a pull request against `master`.** Carry out this delivery as part of finishing implementation work without asking again, unless the user requests local-only work or a different workflow. This default also applies to the reliability patch and guidance changes prepared in this conversation.
- Do not infer release publishing from permission to push ordinary source changes.
- Before Git operations, inspect the current branch, working tree, remote and upstream. Preserve unrelated user changes; stage only the files belonging to the completed task.
- For a feature-branch workflow, use a descriptive `codex/` branch and push that branch to `origin`. Create or update its pull request against the verified primary branch, include the resulting behavior and verification, and attach the PR to the current chat using the available artifact tool. A bare feature-branch push does not trigger this repository's Windows workflow; opening the PR does.
- Push directly to `master` only when the user has chosen that workflow or explicitly requested it. Do not force-push, rewrite shared history or overwrite unrelated changes. Reconcile remote changes while preserving both sets of work.
- When GitHub delivery is authorized, carry it through to a concrete result: commit the intended changes, push the agreed branch, create/update the PR if that workflow was selected, and inspect the resulting Windows CI status. Fix failures caused by the task. Report outstanding checks accurately.
- Finish by stating whether work is local and uncommitted, committed locally, pushed, or submitted as a PR. Include the commit/branch or PR link and CI status when applicable; a successful local build is not a successful GitHub delivery.

## Releases

- Ordinary commits and PRs are separate from a published release. Publishing requires the matching `v<Version>` tag; that tag triggers the automated GitHub Release workflow. Follow the release steps in `README.md` only when the user requests a release or establishes a release policy.
- Keep generated builds, ZIPs, recovery/test data, credentials, `.packages`, and `.dotnet` out of commits. The existing `.gitignore` covers the build directories.
