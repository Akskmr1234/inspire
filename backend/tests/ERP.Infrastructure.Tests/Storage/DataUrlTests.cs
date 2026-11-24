using System.Text;
using ERP.Infrastructure.Storage;

namespace ERP.Infrastructure.Tests.Storage;

/// <summary>Reading the data URLs pictures arrive as, before they go to file storage.</summary>
public sealed class DataUrlTests
{
    [Fact]
    public void A_base64_picture_is_read_with_its_type_and_extension()
    {
        DataUrl.TryParse("data:image/png;base64,iVBORw0K", out DataUrl? file).ShouldBeTrue();

        file.MediaType.ShouldBe("image/png");
        file.Extension.ShouldBe("png");
        file.Content.ShouldBe(Convert.FromBase64String("iVBORw0K"));
        file.ToString().ShouldBe("data:image/png;base64,iVBORw0K");
    }

    [Fact]
    public void A_percent_encoded_picture_is_read_too()
    {
        DataUrl.TryParse("data:image/svg+xml,%3Csvg%2F%3E", out DataUrl? file).ShouldBeTrue();

        file.Extension.ShouldBe("svg");
        Encoding.UTF8.GetString(file.Content).ShouldBe("<svg/>");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("data:image/png;base64")]
    [InlineData("data:image/png;base64,***")]
    public void Anything_else_is_not_a_data_url(string? value) =>
        DataUrl.TryParse(value, out _).ShouldBeFalse();
}
