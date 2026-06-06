using Dominatus.MonoGameRtsDemo;

var options = RtsDemoOptions.Parse(args);
using var game = new RtsDemoGame(options);
game.Run();
