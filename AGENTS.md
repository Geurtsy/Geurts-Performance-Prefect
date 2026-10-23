# Geurts Performance Prefect agent instructions

These instructions apply to the entire repository and all work on this project.

## Delivery rule: repository updates, manual local installation

The user installs Geurts Performance Prefect updates manually. Make and deliver
project changes through this Git repository. Local testing is allowed, but
testing and publishing changes do not authorize updating the user's installed app.

- Keep source, documentation and release changes in this repository. Commit and
  push completed, appropriately validated work to `main`.
- Every completed application change must include a published GitHub release
  package unless the user explicitly requests otherwise. Bug fixes, features
  and behavior changes authorize this release workflow without a separate
  publication request. Increment the version, update release notes, build from
  clean committed source, validate the package, and publish the Windows ZIP and
  checksum with `scripts/Build-Release.ps1` and `scripts/Publish-Release.ps1`.
  Verify that GitHub's latest stable release exposes the new version and that
  its asset checksum matches the validated package. Verify the updater using
  an older disposable packaged fixture when available. Report any publication
  blocker; do not claim delivery is complete until the package is published.
- Check delivery access at the start of application work. If local Git metadata
  or publishing network access is restricted, use the hosted Windows release
  workflow in `.github/workflows/release.yml` after committing to `main`.
  Follow its run through completion; a pushed source commit alone is not an
  application update. If the workflow cannot run or fails, lead the delivery
  report with **Release pending**, the concrete blocker and the remaining step.
- Release builds must pass the clean-source checks in `Build-Release.ps1` and
  `Test-Release.ps1`. Publishing requires the matching successful test receipt.
  Confirm `published.json` records the expected version, commit, public download
  checksum and disposable updater result before saying an update is available.
  Never edit a manifest or fabricate a receipt to bypass a failed gate.
- Never copy or publish build output into a local installation folder, replace
  installed executables or libraries, or change an installed update manifest.
  This includes the ignored `Geurts Performance Prefect/` and
  `Geurts Performance Prefect - PawnIO default/` folders in this checkout, any
  legacy HardwareOverlay installation, and installations elsewhere on the machine.
- Never invoke the installed app's updater or run an installation/deployment helper
  targeting the user's installation. Never stop, close or restart the user's
  running app to deliver a change.
- Preserve the installed app's settings, startup configuration, drivers and
  system tuning. Do not change them as a side effect of development or delivery.
- A request to fix a bug, add a feature, build, test or publish a release does not
  grant permission to install it locally. Override this boundary only when the
  user explicitly asks for that specific local installation or change.

## Local validation

- Build and test on the user's machine using separate output directories under
  `Build/`, with reports and previews under `Verification/`, or another isolated
  temporary location. Keep these generated files out of Git.
- Use isolated test settings and fixtures so tests preserve the live app and its
  preferences. The app's `--self-test` mode supports this workflow. Test processes
  may be started and stopped; the user's running installed app must be preserved.
- Updater installation/restart tests are allowed inside a disposable packaged
  fixture, including `--update-smoke`. Check that the executable and replacement
  targets belong to that fixture before running them. Prefer dedicated test modes;
  normal startup uses real preferences and can request driver installation.
- `scripts/Build-Release.ps1` creates a package in a fresh output directory.
  `scripts/Publish-Release.ps1` publishes that package to GitHub. Neither step
  requires deploying it to the user's installation.
- GitHub Actions builds and tests releases on a disposable Windows runner.
  It uses a short-lived repository token, not credentials copied from the user's
  machine. Retain the workflow's verification artifact when investigating a
  failed release. Never replace an already published version's assets.
- Report what was changed, validated and published, with any material testing
  limitations. Leave local installation to the user; never describe a repository
  update as an installed update.
