using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunapack.Cli.Application.Diagnostics;

internal sealed class CliRedirectedConsole : IAnsiConsole
{
    private readonly IAnsiConsole _outputConsole;

    public CliRedirectedConsole(IAnsiConsole inputConsole, IAnsiConsole outputConsole)
    {
        _outputConsole = outputConsole;
        _outputConsole.Profile.Capabilities.Interactive = inputConsole
            .Profile
            .Capabilities
            .Interactive;
        Input = inputConsole.Input;
    }

    public Profile Profile => _outputConsole.Profile;

    public IAnsiConsoleCursor Cursor => _outputConsole.Cursor;

    public IAnsiConsoleInput Input { get; }

    public IExclusivityMode ExclusivityMode => _outputConsole.ExclusivityMode;

    public RenderPipeline Pipeline => _outputConsole.Pipeline;

    public void Clear(bool home) => _outputConsole.Clear(home);

    public void Write(IRenderable renderable) => _outputConsole.Write(renderable);
}
