# Geurts Performance Prefect 1.24.0

- Added a Security tab and moved Prefect's existing Start as administrator preference there.
- Added an explicit Windows-wide action to run apps from administrator accounts without UAC prompts by disabling UAC. It explains the reduced protection, requires confirmation and administrator approval when needed, and requires you to restart Windows yourself. Standard accounts remain limited.
- Added Restore / enable UAC protection, with an administrator-protected backup of the original policy, readback verification, rollback on failure and recovery information retained for retries. Other UAC policies are preserved. No security changes run automatically or merely from opening Settings.
- Validated policy apply/restore, missing original values, repeated actions, cancellation, backup failures, failed and ignored writes, rollback, recovery retries, helper launch arguments, and all eight Settings tabs with isolated fixtures. Live Windows security policy was read only; real UAC changes and Windows restarts were not performed during validation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.
