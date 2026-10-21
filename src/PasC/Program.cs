// pasc: command-line Pascal compiler.
using Studio.Pascal;
using Studio.Tooling;

return CliMain.Run(args, new PascalLanguage(), "pasc");
