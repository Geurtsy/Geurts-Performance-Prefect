# Geurts Performance Prefect 1.26.0

- Added BitLocker drive decryption under Settings → Security: explicit drive selection, Turn off BitLocker and decrypt drive, conversion status, remaining encrypted percentage and resume for paused decryption.
- Requires administrator access, checks the selected volume identity and current status again before starting, and confirms the drive and loss of encryption protection. Normal sessions offer an administrator restart that reopens Security without starting decryption.
- Windows owns the background conversion; closing Prefect after acceptance does not cancel it. The visible tab refreshes an actively decrypting selected drive every 15 seconds. App close/restart/update waits while Windows acknowledges the initial request. Percentage and suspended protection alone never imply completion.
- Locked drives require authorized Windows unlocking. No recovery keys are collected or auto-unlock keys cleared; organization policy and Windows errors are respected. Separate EFS file encryption is unaffected. Re-enabling BitLocker requires a separate setup through Windows management.
- Validated supported conversion states, locked/unknown/unreadable drives, identity changes, confirmation cancellation, partial status failures, progress, completion, privilege gating, request lifecycle and normal/narrow layouts using isolated fixtures. No real drive decryption, key-protector changes, unlocking or administrator restart was run during validation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.
