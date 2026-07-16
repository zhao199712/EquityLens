using System.Security.Claims;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Auth;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.Users;
using EquityLens.Api.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Controllers;

/// <summary>
/// 認證控制器，提供註冊、登入、查詢目前使用者、刷新 Token 與登出功能。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// 初始化認證控制器。
    /// </summary>
    public AuthController(
        EquityLensDbContext dbContext,
        IUserRepository userRepository,
        ITokenService tokenService,
        IPasswordHasher<AppUser> passwordHasher,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _userRepository = userRepository;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
    }

    /// <summary>
    /// 註冊新使用者帳號。
    /// </summary>
    /// <param name="request">註冊請求資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>註冊成功時返回 JWT token 與使用者資訊。</returns>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new ApiError("auth.email_required", "Email is required."));

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new ApiError("auth.password_required", "Password is required."));

        if (request.Password.Length < 6)
            return BadRequest(new ApiError("auth.password_too_short", "Password must be at least 6 characters."));

        // 檢查 Email 是否已存在
        var existingUser = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existingUser != null)
            return Conflict(new ApiError("auth.email_exists", "Email is already registered."));

        // 建立使用者
        var now = DateTime.UtcNow;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            DisplayName = request.DisplayName ?? request.Email.Split('@')[0],
            Role = "User",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _userRepository.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 產生 tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = await CreateRefreshTokenAsync(user.Id, cancellationToken);

        return Ok(new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            TokenType: "Bearer",
            ExpiresIn: int.Parse(_configuration["Jwt:ExpireMinutes"]!),
            User: ToUserInfo(user)));
    }

    /// <summary>
    /// 使用 Email 與密碼登入。
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new ApiError("auth.credentials_required", "Email and password are required."));

        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user == null)
            return Unauthorized(new ApiError("auth.invalid_credentials", "Invalid email or password."));

        if (!user.IsActive)
            return Unauthorized(new ApiError("auth.account_disabled", "Account has been disabled."));

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
            return Unauthorized(new ApiError("auth.invalid_credentials", "Invalid email or password."));

        // 產生 tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = await CreateRefreshTokenAsync(user.Id, cancellationToken);

        return Ok(new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            TokenType: "Bearer",
            ExpiresIn: int.Parse(_configuration["Jwt:ExpireMinutes"]!),
            User: ToUserInfo(user)));
    }

    /// <summary>
    /// 取得目前登入使用者的資訊。
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> GetMe(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new ApiError("auth.user_not_found", "User not found."));

        return Ok(new MeResponse(User: ToUserInfo(user)));
    }

    /// <summary>
    /// 使用 Refresh Token 換取新的 Access Token。
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new ApiError("auth.refresh_token_required", "Refresh token is required."));

        // 找到 refresh token
        var refreshToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken, cancellationToken);

        if (refreshToken == null)
            return Unauthorized(new ApiError("auth.invalid_refresh_token", "Invalid refresh token."));

        if (refreshToken.IsRevoked)
            return Unauthorized(new ApiError("auth.refresh_token_revoked", "Refresh token has been revoked."));

        if (refreshToken.IsUsed)
            return Unauthorized(new ApiError("auth.refresh_token_used", "Refresh token has already been used."));

        if (refreshToken.ExpiresAtUtc < DateTime.UtcNow)
            return Unauthorized(new ApiError("auth.refresh_token_expired", "Refresh token has expired."));

        // 標記舊 token 為已使用（Token Rotation）
        refreshToken.IsUsed = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 找到使用者
        var user = await _dbContext.Users.FindAsync(refreshToken.UserId);
        if (user == null || !user.IsActive)
            return Unauthorized(new ApiError("auth.user_not_found", "User not found or inactive."));

        // 產生新 tokens
        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = await CreateRefreshTokenAsync(user.Id, cancellationToken);

        return Ok(new AuthResponse(
            AccessToken: newAccessToken,
            RefreshToken: newRefreshToken,
            TokenType: "Bearer",
            ExpiresIn: int.Parse(_configuration["Jwt:ExpireMinutes"]!),
            User: ToUserInfo(user)));
    }

    /// <summary>
    /// 登出：撤銷該使用者所有的 Refresh Token。
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        // 撤銷該使用者所有 Refresh Token
        await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.IsRevoked, true), cancellationToken);

        return NoContent();
    }

    private async Task<string> CreateRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var refreshExpireDays = int.Parse(_configuration["Jwt:RefreshExpireDays"] ?? "7");
        var token = _tokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(refreshExpireDays),
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString(),
            IsRevoked = false,
            IsUsed = false
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return token;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim != null ? Guid.Parse(claim) : Guid.Empty;
    }

    private static UserInfo ToUserInfo(AppUser user) =>
        new UserInfo(user.Id, user.Email, user.DisplayName, user.Role);
}

/// <summary>
/// 刷新 Token 的請求資料。
/// </summary>
public sealed record RefreshRequest(string RefreshToken);
