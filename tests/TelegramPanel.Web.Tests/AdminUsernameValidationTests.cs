using Microsoft.AspNetCore.Http;
using TelegramPanel.Web.Api;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class AdminUsernameValidationTests
{
    [Theory]
    [InlineData("abc", "后台用户名长度应为 4-32 位")]
    [InlineData("admin", "请不要使用常见后台用户名")]
    [InlineData("ROOT", "请不要使用常见后台用户名")]
    public async Task ChangeAdminUsername_InvalidUsername_ReturnsBadRequest(
        string username,
        string expectedMessage)
    {
        var result = await PanelAdminApiEndpoints.ChangeAdminUsernameAsync(
            new ChangeAdminUsernameRequestDto("current-password", username),
            new DefaultHttpContext(),
            credentialStore: null!,
            CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var response = Assert.IsType<OperationResultDto>(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.False(response.Success);
        Assert.Equal(expectedMessage, response.Message);
    }
}
