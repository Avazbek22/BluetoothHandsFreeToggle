using BluetoothHandsFreeToggle.Ui;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed class ConsoleHelpersTests
{
    [Fact]
    public void ReadMenuChoiceReturnsNullAtEndOfInput()
    {
        var originalInput = Console.In;
        var originalOutput = Console.Out;

        try
        {
            Console.SetIn(new StringReader(string.Empty));
            Console.SetOut(TextWriter.Null);

            Assert.Null(ConsoleHelpers.ReadMenuChoice());
        }
        finally
        {
            Console.SetIn(originalInput);
            Console.SetOut(originalOutput);
        }
    }

    [Fact]
    public void ReadMenuChoiceTrimsProvidedInput()
    {
        var originalInput = Console.In;
        var originalOutput = Console.Out;

        try
        {
            Console.SetIn(new StringReader("  3  \r\n"));
            Console.SetOut(TextWriter.Null);

            Assert.Equal("3", ConsoleHelpers.ReadMenuChoice());
        }
        finally
        {
            Console.SetIn(originalInput);
            Console.SetOut(originalOutput);
        }
    }
}
