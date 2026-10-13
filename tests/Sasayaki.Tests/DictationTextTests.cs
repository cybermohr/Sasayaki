using Sasayaki.Core;
using Xunit;

namespace Sasayaki.Tests;

public sealed class DictationTextTests
{
    [Theory]
    [InlineData("Hello.", "Hello. ")]
    [InlineData("Ready?", "Ready? ")]
    [InlineData("Great!", "Great! ")]
    [InlineData("Wait…", "Wait… ")]
    [InlineData("He said \"hello.\"", "He said \"hello.\" ")]
    [InlineData("A fragment", "A fragment ")]
    [InlineData("Hello. ", "Hello. ")]
    [InlineData("Hello.   ", "Hello. ")]
    [InlineData("Hello.\u00a0", "Hello. ")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void LeavesExactlyOneTrailingSpace(string text, string expected)
        => Assert.Equal(expected, DictationText.ForInsertion(text));

    [Fact]
    public void ConsecutiveDictationsHaveASentenceSeparator()
        => Assert.Equal("Hello. Ready? Yes! ",
            DictationText.ForInsertion("Hello.") + DictationText.ForInsertion("Ready?") + DictationText.ForInsertion("Yes!"));

    [Theory]
    [InlineData("Hello.\n")]
    [InlineData("Hello.\t")]
    public void StillRejectsControlCharacters(string text)
        => Assert.Throws<ServiceException>(() => DictationText.ForInsertion(text));

    [Fact]
    public void PreservesTheExistingContentLengthLimit()
    {
        Assert.Equal(16001, DictationText.ForInsertion(new string('a', 16000)).Length);
        Assert.Throws<ServiceException>(() => DictationText.ForInsertion(new string('a', 16001)));
    }
}
