using Rebelgent.Telegram.Commands;

namespace Rebelgent.Telegram.Tests;

public class TelegramCommandParserTests
{
    // --- Known commands are recognized ---

    [Theory]
    [InlineData("/start")]
    [InlineData("/help")]
    [InlineData("/status")]
    [InlineData("/tasks")]
    public void KnownCommands_AreRecognized(string input)
    {
        var result = TelegramCommandParser.TryParse(input, out var command);

        Assert.True(result);
        Assert.Equal(input, command);
    }

    // --- @botname suffix is stripped ---

    [Theory]
    [InlineData("/status@RebelgentBot", "/status")]
    [InlineData("/tasks@RebelgentBot", "/tasks")]
    [InlineData("/start@RebelgentBot", "/start")]
    [InlineData("/help@RebelgentBot", "/help")]
    public void BotnameSuffix_IsStripped(string input, string expected)
    {
        var result = TelegramCommandParser.TryParse(input, out var command);

        Assert.True(result);
        Assert.Equal(expected, command);
    }

    // --- Surrounding whitespace is ignored ---

    [Theory]
    [InlineData("  /status  ")]
    [InlineData("  /tasks  ")]
    [InlineData("  /status@RebelgentBot  ")]
    public void SurroundingWhitespace_IsIgnored(string input)
    {
        var result = TelegramCommandParser.TryParse(input, out _);

        Assert.True(result);
    }

    [Fact]
    public void LeadingWhitespace_CommandNormalizesCorrectly()
    {
        var result = TelegramCommandParser.TryParse("  /status  ", out var command);

        Assert.True(result);
        Assert.Equal("/status", command);
    }

    // --- Case normalization ---

    [Theory]
    [InlineData("/Status", "/status")]
    [InlineData("/TASKS", "/tasks")]
    [InlineData("/Help", "/help")]
    [InlineData("/START", "/start")]
    [InlineData("/STATUS@RebelgentBot", "/status")]
    public void Commands_AreLowercased(string input, string expected)
    {
        var result = TelegramCommandParser.TryParse(input, out var command);

        Assert.True(result);
        Assert.Equal(expected, command);
    }

    // --- Unknown slash commands are still recognized as commands ---

    [Fact]
    public void UnknownCommand_IsRecognizedAsCommand()
    {
        var result = TelegramCommandParser.TryParse("/whatever", out var command);

        Assert.True(result);
        Assert.Equal("/whatever", command);
    }

    // --- Non-command text returns false ---

    [Theory]
    [InlineData("Add memory expiration support to BlazorMemory")]
    [InlineData("Build something")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("just text")]
    public void NonCommandText_ReturnsFalse(string input)
    {
        var result = TelegramCommandParser.TryParse(input, out var command);

        Assert.False(result);
        Assert.Equal(string.Empty, command);
    }

    // --- Text with inline argument after space ---

    [Fact]
    public void CommandWithArgument_ExtractsCommandOnly()
    {
        var result = TelegramCommandParser.TryParse("/tasks 10", out var command);

        Assert.True(result);
        Assert.Equal("/tasks", command);
    }
}
