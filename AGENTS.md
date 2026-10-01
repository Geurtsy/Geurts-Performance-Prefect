# Geurts Performance Prefect agent instructions

These instructions apply to the entire repository and all work on this project.

## Delivery rule: repository updates, manual local installation

The user installs Geurts Performance Prefect updates manually. Make and deliver
project changes through this Git repository. Local testing is allowed, but
testing and publishing changes do not authorize updating the user's installed app.

- Keep source, documentation and release changes in this repository. Commit and
  push completed, appropriately validated work to `main`. Publish GitHub release
  packages when the task calls for a release.
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
- Report what was changed, validated and published, with any material testing
  limitations. Leave local installation to the user; never describe a repository
  update as an installed update.
