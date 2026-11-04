using EquityLens.Api.Controllers;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Controllers;

public sealed class SettingsControllerTests
{
    [Theory]
    [InlineData(null, "Balanced", true)]
    [InlineData("Balanced", "Balanced", false)]
    [InlineData("Conservative", "Conservative", false)]
    [InlineData("Aggressive", "Aggressive", false)]
    public async Task Preference_IsOwnerScopedAndExplicitlyIdentifiesDefault(string? saved, string expected, bool isDefault)
    {
        await using var db = new RefactorTestDbContext(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Id = Guid.NewGuid(), RiskPreference = saved };
        var other = new AppUser { Id = Guid.NewGuid(), RiskPreference = "Aggressive" };
        db.Users.AddRange(user, other); await db.SaveChangesAsync();
        var controller = new SettingsController(db, new CurrentUser(user.Id));
        var before = Assert.IsType<RiskPreferenceProfile>(Assert.IsType<OkObjectResult>((await controller.GetRiskPreference(default)).Result).Value);
        Assert.Equal(expected, before.Value); Assert.Equal(isDefault, before.IsDefault);
        if (isDefault) Assert.Contains("預設，可在設定修改", before.Statement);
        await controller.UpdateRiskPreference(new("Conservative"), default);
        db.ChangeTracker.Clear();
        var after = Assert.IsType<RiskPreferenceProfile>(Assert.IsType<OkObjectResult>((await controller.GetRiskPreference(default)).Result).Value);
        Assert.Equal("Conservative", after.Value); Assert.False(after.IsDefault);
        Assert.Equal("Aggressive", (await db.Users.SingleAsync(x => x.Id == other.Id)).RiskPreference);
        Assert.IsType<BadRequestObjectResult>((await controller.UpdateRiskPreference(new("invented"), default)).Result);
        Assert.Equal("Conservative", (await db.Users.SingleAsync(x => x.Id == user.Id)).RiskPreference);
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(SettingsController), typeof(AuthorizeAttribute)));
        var anonymous = new SettingsController(db, new CurrentUser(Guid.Empty));
        Assert.IsType<UnauthorizedResult>((await anonymous.GetRiskPreference(default)).Result);
        Assert.IsType<UnauthorizedResult>((await anonymous.UpdateRiskPreference(new("Balanced"), default)).Result);
    }
    private sealed class CurrentUser(Guid id) : ICurrentUserContext
    {
        public Guid UserId => id;
        public string Email => "";
        public string DisplayName => "";
        public bool IsAuthenticated => id != Guid.Empty;
    }
}
