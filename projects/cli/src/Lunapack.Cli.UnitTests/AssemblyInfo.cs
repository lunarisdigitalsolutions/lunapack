[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "Test method names use underscores to separate scenario, condition, and expected outcome as required by the repository test convention."
)]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "MA0004:Use Task.ConfigureAwait(false)",
    Justification = "Microsoft Testing Platform runs without a synchronization context; decorating every test await would obscure test intent without changing behavior."
)]
