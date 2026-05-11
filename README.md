full rights go to https://github.com/Frosthaven/voicemeeter-windows-volume

modifications made:
- ported to c# (windows only)
- fixed the annoying tray icon dupe bug (happend on the js version because it tried to periodically check if vm isnt open, and if youre vm crashed it will fail and reopen the app)
- one exe (selfcontained .net runtime), no installer 
- autorun registers over the registry now
