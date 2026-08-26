using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class NextStepRenderer(CliConsole console)
{
    public bool Suppress { get; set; }

    public void Render(
        IReadOnlyList<NextStepRecommendation> recommendations,
        string? heading = null
    )
    {
        if (Suppress || recommendations.Count == 0)
        {
            return;
        }

        console.Info(string.Empty);
        console.Info(heading ?? (recommendations.Count == 1 ? "Next step:" : "Next steps:"));
        console.Info(string.Empty);
        for (var index = 0; index < recommendations.Count; index++)
        {
            var recommendation = recommendations[index];
            console.Render(
                new Markup(
                    $"  {index + 1}. {Markup.Escape(recommendation.Label)}\n"
                        + $"     [bold]{Markup.Escape(recommendation.Command)}[/]\n"
                )
            );
        }
    }
}
