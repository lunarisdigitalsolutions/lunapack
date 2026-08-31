# LunaPack testing pack

This draft pack supports manual lifecycle testing across a minor and major
update. Configure `projects/packs` as a local source, then use the exact pack
ID and version.

Available releases are `1.0.0`, `1.1.0`, and `2.0.0`. Each release exercises
string, Boolean, enum, and multi-select enum parameters; file, directory, and
glob selectors; Scriban templates; line, section, and JSON merges; conditions;
instructions; and PowerShell script hooks.

Run scripts only after reviewing `scripts/echo.ps1` in the selected release.

```powershell
luna discover --allow-draft --versions 3
luna search testing --allow-draft --versions 3
luna inspect lunapack-testing@1.0.0
luna install lunapack-testing@1.0.0 --scripts run
luna update lunapack-testing@1.1.0 --scripts run
luna update lunapack-testing@2.0.0 --scripts run
luna uninstall lunapack-testing --scripts run
```
