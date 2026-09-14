QHH Desktop Storage Box recovery diagnostic collector

1. Close QHH Desktop Storage Box and any editor that may have files open.
2. Connect an external USB drive. Use a drive that is different from the data drives.
3. Copy Collect-QHHDesktopStorageBoxRecoveryBundle.ps1 and this file to the USB drive.
4. Open PowerShell and run, replacing E: with the USB drive letter:

   Set-ExecutionPolicy -Scope Process Bypass
   & "E:\Collect-QHHDesktopStorageBoxRecoveryBundle.ps1" -OutputRoot "E:\QHHDesktopStorageBox-Recovery" -DataDirectories "C:\Users\<USER>\AppData\Local\QHHDesktopStorageBox","D:\WD"

5. If the original source folder still exists, add its path:

   -SourceFolders "D:\path\to\original\folder"

6. Send the generated ZIP file to the developer. Do not edit or delete anything
   from the original computer before the recovery work is complete.

The bundle contains database copies, logs, volume information, and metadata-only
file inventories. It does not contain user file contents, but database and logs
may contain filenames and full paths.
