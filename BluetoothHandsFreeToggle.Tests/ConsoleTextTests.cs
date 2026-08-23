using BluetoothHandsFreeToggle.Ui;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed class ConsoleTextTests
{
    [Theory]
    [InlineData("Status", 6)]
    [InlineData("Français", 8)]
    [InlineData("状態", 4)]
    [InlineData("한국어", 6)]
    [InlineData("e\u0301", 1)]
    public void GetDisplayWidthHandlesUnicodeConsoleCells(string value, int expected)
        => Assert.Equal(expected, ConsoleText.GetDisplayWidth(value));

    [Fact]
    public void PadRightUsesDisplayWidthInsteadOfUtf16Length()
        => Assert.Equal("状態  ", ConsoleText.PadRight("状態", 6));
}
