using System;
using System.Linq;

if (args.Contains("--selftest") || args.Contains("--balance-only"))
{
    Environment.Exit(SERAAC.Core.SelfTest.Run());
}

using var game = new SERAAC.Game1(args);
game.Run();
