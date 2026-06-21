Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
appDir = fso.GetParentFolderName(WScript.ScriptFullName)
scriptPath = fso.BuildPath(appDir, "TomatoClock.ps1")
command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File " & Chr(34) & scriptPath & Chr(34)
shell.Run command, 0, False
