namespace TerraRuntime.Tests;

public sealed class StartupWorldCreationFailurePolicyTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("b\n")]
    [InlineData("BACK\n")]
    public void Failure_prompt_can_return_to_world_selection(string inputText)
    {
        using var input = new StringReader(inputText);
        using var output = new StringWriter();

        bool back = StartupWorldCreationFailurePolicy.PromptReturnToWorldSelection(input, output);

        Assert.True(back);
        Assert.Contains("return to world selection", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("q\n")]
    [InlineData("QUIT\n")]
    [InlineData("exit\n")]
    public void Failure_prompt_can_quit_without_throwing(string inputText)
    {
        using var input = new StringReader(inputText);
        using var output = new StringWriter();

        bool back = StartupWorldCreationFailurePolicy.PromptReturnToWorldSelection(input, output);

        Assert.False(back);
    }

    [Fact]
    public void Failure_prompt_retries_invalid_input()
    {
        using var input = new StringReader("wat\n\n");
        using var output = new StringWriter();

        bool back = StartupWorldCreationFailurePolicy.PromptReturnToWorldSelection(input, output);

        Assert.True(back);
        Assert.Contains("Choose Enter/B", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Recoverability_excludes_out_of_memory()
    {
        Assert.True(StartupWorldCreationFailurePolicy.IsRecoverable(new InvalidOperationException("boom")));
        Assert.False(StartupWorldCreationFailurePolicy.IsRecoverable(new OutOfMemoryException("oom")));
    }
}
