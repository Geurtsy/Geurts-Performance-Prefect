Adds a Windows firmware temperature fallback for motherboards and laptops.

- Prefect now reads ACPI thermal zones through Windows performance counters, including Lenovo laptops without a supported motherboard sensor chip. No new driver is needed for this source.
- Automatic selection prefers a dedicated Motherboard/System sensor, then a single firmware thermal zone. Select a zone in Settings when Windows exposes several; saved selections never silently switch sources.
- Firmware readings appear as System temperature (ACPI) with the zone name. Firmware does not identify the physical sensor location, so this is not a verified motherboard PCB temperature. Its reported value may update slowly or remain fixed.
- Invalid, missing or disconnected temperatures remain unavailable. Windows Kelvin values are converted to Celsius, and unavailable providers retry without displaying stale data.
- Validation on the Lenovo 83DV / LNVNB161216 found TZ00 reporting 301 K (27.85°C) without administrator access. Dedicated board sensor support and access requirements remain hardware dependent.

Windows x64 and the .NET 10 Desktop Runtime are required. Extract the release ZIP
into your application folder manually, or initiate the update yourself in Settings.
