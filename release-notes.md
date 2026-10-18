# Geurts Performance Prefect 1.25.0

- Added Windows Firewall controls under Settings → Security: Disable Windows Firewall, Restore / enable Windows Firewall, and status for the Domain, Private and Public profiles.
- Disabling requires confirmation explaining the computer-wide scope and reduced network protection, then administrator approval when needed. The change normally applies without restarting Windows. No firewall changes run automatically on startup or from opening Settings.
- Saves each profile's original enabled/disabled state in an administrator-protected backup before changing profiles. Restore recovers those exact states, or enables all three profiles without a backup. Includes readback verification, rollback on partial failure and retained recovery data for retries.
- Preserves firewall rules, the firewall service, antivirus and UAC settings. Status reports local settings; organization policies and other security products can override them.
- Validated every original profile-state combination, confirmation/elevation cancellation, backup failures, partial and ignored writes, recovery retries, helper arguments, and the scrolled Security tab at normal and narrow widths. Native firewall getters were read only; real firewall changes and elevated helpers were not run during validation.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.
