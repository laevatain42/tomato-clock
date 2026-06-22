Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
appDir = fso.GetParentFolderName(WScript.ScriptFullName)
scriptPath = fso.BuildPath(appDir, "TomatoClock.exe")
command = Chr(34) & scriptPath & Chr(34)
shell.Run command, 0, False
