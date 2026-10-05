Adds an optional Start with Windows setting.

- Enable Settings → Startup → Start with Windows to open the overlay when you sign in to your Windows account. It is off by default, saves immediately and does not require administrator approval.
- Turning it off removes only this application's startup entry. Settings reports access failures and restores the checkbox to the actual registered state.
- Keep the app in the same folder. If you move it, turn the option off and on from the new location. Updates in the same folder keep the entry. Windows Settings → Apps → Startup must also allow the app.
- Startup launches use your normal overlay preferences and exit quietly if the app is already running. Downloads auto-wipe also runs on these launches if enabled.
- Automated startup checks use an isolated registry fixture outside Windows startup keys and preserve real startup configuration.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.
