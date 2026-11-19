using Finlay.PharmaVigilance.Application.DTO;
using Finlay.PharmaVigilance.Application.DTO.Authentication;
using Finlay.PharmaVigilance.Application.IServices;
using Finlay.PharmaVigilance.Application.IServices.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Finlay.PharmaVigilance.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("Auth")]
public class AuthenticationController : ControllerBase
{
    private readonly IIdentityService _identityService;
    private readonly ICaptchaService _captchaService;
    private readonly bool _adminRegistrationEnabled;
    private readonly bool _crossSiteCookies;

    public AuthenticationController(
        IIdentityService identityService,
        ICaptchaService captchaService,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _identityService = identityService;
        _captchaService = captchaService;

        // This endpoint is anonymous (it is how the first administrator is bootstrapped), so outside
        // Development it stays closed unless Setup__AllowAdminRegistration=true is set explicitly.
        _adminRegistrationEnabled =
            configuration.GetValue<bool?>("Setup:AllowAdminRegistration") ?? environment.IsDevelopment();

        // Outside Development the frontend and the API live on different sites (e.g. *.workers.dev and
        // *.onrender.com), so the refresh-token cookie must be SameSite=None; Secure to be sent at all.
        _crossSiteCookies =
            configuration.GetValue<bool?>("Auth:CrossSiteCookies") ?? !environment.IsDevelopment();
    }

    private CookieOptions RefreshCookieOptions(DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true,
        Secure = _crossSiteCookies,
        SameSite = _crossSiteCookies ? SameSiteMode.None : SameSiteMode.Lax,
        Expires = expires
    };

    [HttpPost]
    [Route("login")]
    public async Task<IActionResult> LoginUser(LoginUserDto loginDto)
    {

        if (loginDto == null)
            throw new ArgumentNullException(nameof(loginDto), "Login data is required.");

        if (!await _captchaService.VerifyToken(loginDto.Token ?? string.Empty))
            return BadRequest(new { success = false });

        var authResult = await _identityService.LoginUserAsync(loginDto);

        Response.Cookies.Append("refreshToken", authResult.RefreshToken,
            RefreshCookieOptions(DateTimeOffset.UtcNow.AddDays(7)));

        return Ok(new
        {
            authResult.Id,
            authResult.UserName,
            authResult.UserRole,
            accessToken = authResult.Token
        });

    }

    [HttpPost]
    [Route("register/admins")]
    public async Task<IActionResult> RegisterAdmin(RegisterUserDto registerAdminDto)
    {
        if (!_adminRegistrationEnabled)
            return NotFound();

        var result = await _identityService.RegisterAdminAsync(registerAdminDto);
        return Ok(new
        {
            message = result,
            success = true
        });
    }

    [HttpPost("complete-registration")]
    public async Task<IActionResult> Create(
        CompleteRegistrationDto dto
    )
    {
        await _identityService.CompleteRegistrationAsync(dto);

        return Ok();
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken()
    {
        var refreshToken = Request.Cookies["refreshToken"];

        if (string.IsNullOrEmpty(refreshToken))
        {
            // return Unauthorized(new
            // {
            //     success = false,
            //     message = "Refresh token missing."
            // });
            return Ok(new
            {
                success = false,
                message = "Refresh token missing"
            });
        }

        var result = await _identityService.RefreshTokenAsync(refreshToken);

        Response.Cookies.Append("refreshToken", result.RefreshToken,
            RefreshCookieOptions(DateTimeOffset.UtcNow.AddDays(7)));

        return Ok(new
        {
            accessToken = result.AccessToken
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies["refreshToken"];

        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _identityService.LogoutAsync(refreshToken);
        }

        Response.Cookies.Delete("refreshToken", RefreshCookieOptions());

        return Ok(new
        {
            message = "Logged out successfully."
        });
    }

}