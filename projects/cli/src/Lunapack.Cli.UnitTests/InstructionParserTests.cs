namespace Lunapack.Cli.UnitTests;

public sealed class InstructionParserTests
{
    [Test]
    public async Task Parse_WhenIntroductionPrecedesHeading_PreservesIntroductionOnce()
    {
        var document = InstructionParser.Parse("Welcome.\n\n## Configure\nDetails.");

        await Assert.That(document.Introduction).IsEqualTo("Welcome.\n\n");
        await Assert.That(document.Steps).Count().IsEqualTo(1);
        await Assert.That(document.Steps[0].Content).IsEqualTo("Details.");
    }

    [Test]
    public async Task Parse_WhenMajorHeadingsDeclared_NumbersSequentially()
    {
        var document = InstructionParser.Parse("## First\nOne\n## Second\nTwo");

        await Assert
            .That(
                string.Join(
                    ",",
                    document.Steps.Select(step =>
                        $"{step.Number}.{step.SubstepNumber}:{step.Title}"
                    )
                )
            )
            .IsEqualTo("1.:First,2.:Second");
    }

    [Test]
    public async Task Parse_WhenNestedHeadingsDeclared_UsesMajorAndChildNumbers()
    {
        var document = InstructionParser.Parse(
            "## First\nOne\n### Child\nChild body\n### Next\nNext body"
        );

        await Assert
            .That(
                string.Join(
                    ",",
                    document.Steps.Select(step => $"{step.Number}.{step.SubstepNumber}")
                )
            )
            .IsEqualTo("1.,1.1,1.2");
    }

    [Test]
    public async Task Parse_WhenOrphanChildHeadingsDeclared_TreatsThemAsTopLevelSteps()
    {
        var document = InstructionParser.Parse("### First\nOne\n### Second\nTwo\n## Third\nThree");

        await Assert
            .That(
                string.Join(
                    ",",
                    document.Steps.Select(step => $"{step.Number}.{step.SubstepNumber}")
                )
            )
            .IsEqualTo("1.,2.,3.");
    }

    [Test]
    public async Task Parse_WhenNoStepHeadingExists_ReturnsOneUntitledStep()
    {
        var content = "# Setup\r\nUse this guide.";

        var document = InstructionParser.Parse(content);

        await Assert.That(document.Introduction).IsEmpty();
        await Assert.That(document.Steps).Count().IsEqualTo(1);
        await Assert.That(document.Steps[0]).IsEqualTo(new InstructionStep(1, null, null, content));
    }

    [Test]
    public async Task Parse_WhenCalledForMultipleDocuments_RestartsNumbering()
    {
        var first = InstructionParser.Parse("## First\n## Second");
        var second = InstructionParser.Parse("## Another");

        await Assert.That(first.Steps[^1].Number).IsEqualTo(2);
        await Assert.That(second.Steps[0].Number).IsEqualTo(1);
    }

    [Test]
    public async Task Parse_WhenLinesAreNotStepHeadings_PreservesThemLiterally()
    {
        var document = InstructionParser.Parse(
            "## Step\r\n# H1\r\n#### H4\r\n```\r\n##Not A Heading\r\n```\r\n"
        );

        await Assert
            .That(document.Steps[0].Content)
            .IsEqualTo("# H1\r\n#### H4\r\n```\r\n##Not A Heading\r\n```\r\n");
    }
}
