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
        if (isDefault) Assert.Contains("尚未評估，可在設定中完成評估", before.Statement);
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
    [Fact]
    public async Task Questionnaire_OwnerOnly_DeleteActuallyRemovesAnswers_ManualReplacesThem()
    {
        await using var db = new RefactorTestDbContext(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = new AppUser { Id = Guid.NewGuid() }; var other = new AppUser { Id = Guid.NewGuid() };
        db.Users.AddRange(owner, other); await db.SaveChangesAsync();
        var a = new SettingsController(db, new CurrentUser(owner.Id));
        var b = new SettingsController(db, new CurrentUser(other.Id));
        var answers = new RiskQuestionnaireAnswers { C1 = 1, C2 = 3, C3 = 3, W1 = 3, W2 = 3, W3 = 0 };
        var profile = Assert.IsType<RiskPreferenceProfile>(Assert.IsType<OkObjectResult>((await a.SubmitQuestionnaire(answers, default)).Result).Value);
        Assert.Equal("Questionnaire", profile.Source); Assert.Equal("Conservative", profile.CapacityLevel);
        Assert.Equal("Aggressive", profile.WillingnessLevel); Assert.Equal("Conservative", profile.Value);
        Assert.NotNull(profile.AssessedAt);
        Assert.NotNull((await db.UserRiskProfiles.SingleAsync()).AnswersJson);
        Assert.DoesNotContain("C1", System.Text.Json.JsonSerializer.Serialize(profile));
        Assert.DoesNotContain("AnswersJson", System.Text.Json.JsonSerializer.Serialize(await db.UserRiskProfiles.SingleAsync()));
        Assert.Equal(nameof(RiskQuestionnaireAnswers), answers.ToString());
        var theirs = Assert.IsType<RiskPreferenceProfile>(Assert.IsType<OkObjectResult>((await b.GetRiskPreference(default)).Result).Value);
        Assert.True(theirs.IsDefault);
        await b.UpdateRiskPreference(new("Aggressive"), default); await b.DeleteRiskPreference(default);
        Assert.NotNull((await db.UserRiskProfiles.SingleAsync()).AnswersJson); // another user's writes/deletion cannot target owner
        await a.UpdateRiskPreference(new("Balanced"), default);
        var manual = await db.UserRiskProfiles.SingleAsync();
        Assert.Equal("Manual", manual.Source); Assert.Null(manual.AnswersJson); Assert.Null(manual.CapacityLevel);
        await a.SubmitQuestionnaire(answers, default);
        await a.DeleteRiskPreference(default);
        Assert.Empty(await db.UserRiskProfiles.ToListAsync()); Assert.Null(owner.RiskPreference);
        var reset = Assert.IsType<RiskPreferenceProfile>(Assert.IsType<OkObjectResult>((await a.GetRiskPreference(default)).Result).Value);
        Assert.True(reset.IsDefault); Assert.Equal("Balanced", reset.Value);
        Assert.Contains("預設", reset.Statement); Assert.Contains("可在設定中完成評估", reset.Statement);
        var anonymous = new SettingsController(db, new CurrentUser(Guid.Empty));
        Assert.IsType<UnauthorizedResult>((await anonymous.SubmitQuestionnaire(answers, default)).Result);
        Assert.IsType<UnauthorizedResult>((await anonymous.DeleteRiskPreference(default)).Result);
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<RiskQuestionnaireAnswers>("{\"C1\":1,\"C2\":3,\"C3\":3,\"W1\":3,\"W2\":3,\"W3\":0,\"userId\":\"other\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<RiskQuestionnaireAnswers>("{\"C1\":1,\"C2\":3,\"C3\":3,\"W1\":3,\"W2\":3}"));
    }

    [Fact]
    public async Task InvalidAnswersDoNotOverwriteCurrentProfile()
    {
        await using var db = new RefactorTestDbContext(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new AppUser { Id = Guid.NewGuid() }; db.Users.Add(user); await db.SaveChangesAsync();
        var controller = new SettingsController(db, new CurrentUser(user.Id));
        await controller.UpdateRiskPreference(new("Conservative"), default);
        Assert.IsType<BadRequestObjectResult>((await controller.SubmitQuestionnaire(new() { C1 = 4 }, default)).Result);
        Assert.Equal("Manual", (await db.UserRiskProfiles.SingleAsync()).Source);
    }

    private sealed class CurrentUser(Guid id) : ICurrentUserContext
    {
        public Guid UserId => id;
        public string Email => "";
        public string DisplayName => "";
        public bool IsAuthenticated => id != Guid.Empty;
    }
}
