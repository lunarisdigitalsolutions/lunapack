[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Lunapack.Cli.UnitTests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Lunapack.Cli.SecurityTests")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "MA0004:Use Task.ConfigureAwait(false)",
    Justification = "The CLI executable has no synchronization context; decorating every await would obscure command control flow without changing behavior."
)]
