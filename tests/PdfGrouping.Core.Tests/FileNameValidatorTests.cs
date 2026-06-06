using PdfGrouping.Core;
using Xunit;

namespace PdfGrouping.Core.Tests;

public class FileNameValidatorTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("Часть1")]
    [InlineData("Глава 3")]
    [InlineData("12-15")]
    public void Valid_Labels_Pass(string name)
        => Assert.Null(FileNameValidator.Validate(name));

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a|b")]
    public void InvalidChars_Rejected(string name)
        => Assert.NotNull(FileNameValidator.Validate(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("CON")]
    [InlineData("nul")]
    public void Edge_Cases_Rejected(string name)
        => Assert.NotNull(FileNameValidator.Validate(name));
}
