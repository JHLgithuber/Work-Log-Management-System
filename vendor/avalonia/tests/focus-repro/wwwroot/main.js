const {dotnet}=await import('./_framework/dotnet.js');const r=await dotnet.create();const e=await r.getAssemblyExports(r.getConfig().mainAssemblyName);window.qaRead=e.Program.Read;window.qaFocus=e.Program.Focus;window.qaClear=e.Program.Clear;window.qaSeed=e.Program.Seed;window.qaFlags=e.Program.Flags;await r.runMain(r.getConfig().mainAssemblyName,[]);
await import('./regression.js');
