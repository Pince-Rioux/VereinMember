using Microsoft.Playwright;

namespace VereinMember.E2ETests;

[Trait("Category", "E2E")]
public sealed class MembershipEndToEndTests(MembershipTestHost host) : IClassFixture<MembershipTestHost>
{
    [Fact]
    public async Task Profile_RequiresAuthentication()
    {
        await using var page = await host.NewPageAsync();

        var response = await page.GotoAsync("/Membership/Profile");

        Assert.NotNull(response);
        Assert.True(response.Ok, $"Expected the login page, got HTTP {response.Status}.");
        Assert.Equal(host.BaseUrl + "/Membership/Login?ReturnUrl=%2FMembership%2FProfile", page.Url);
        Assert.Equal("Anmelden", await page.Locator("h1").InnerTextAsync());
    }

    [Fact]
    public async Task Registration_Activation_Login_ProfileUpdate_And_Logout()
    {
        await using var page = await host.NewPageAsync();
        var email = $"member-{Guid.NewGuid():N}@example.test";
        const string password = "TestPassword123";

        var response = await page.GotoAsync("/Membership/Register");
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Expected the registration page, got HTTP {response.Status}.");

        await page.Locator("#Input_FirstName").FillAsync("Playwright");
        await page.Locator("#Input_LastName").FillAsync("Testmitglied");
        await page.Locator("#Input_Email").FillAsync(email);
        await page.Locator("#Input_BirthDay").SelectOptionAsync("15");
        await page.Locator("#Input_BirthMonth").SelectOptionAsync("6");
        await page.Locator("#Input_BirthYear").SelectOptionAsync("1990");
        await page.Locator("#Input_MembershipFeeId").SelectOptionAsync(host.AdultFeeId.ToString());
        await page.Locator("#Input_Street").FillAsync("Testweg 1");
        await page.Locator("#Input_ZipCode").FillAsync("12345");
        await page.Locator("#Input_City").FillAsync("Teststadt");
        await page.Locator("#Input_Country").FillAsync("Deutschland");
        // IBAN from the public ISO 13616 example, used only as synthetic test data.
        await page.Locator("#Input_BankIban").FillAsync("DE89 3704 0044 0532 0130 00");
        await page.Locator("#Input_PrivacyConsented").CheckAsync();
        await page.Locator("#Input_SepaMandateConsented").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Registrieren", Exact = true }).ClickAsync();

        await page.WaitForURLAsync("**/Membership/RegisterConfirmation**");
        Assert.Contains(email, await page.Locator("main").InnerTextAsync());

        var invitationLink = await host.WaitForInvitationLinkAsync(email);
        response = await page.GotoAsync(invitationLink);
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Expected the activation page, got HTTP {response.Status}.");
        Assert.Contains("Registrierung abschließen", await page.Locator("h1").InnerTextAsync());
        await page.Locator("#Input_Password").FillAsync(password);
        await page.Locator("#Input_ConfirmPassword").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Registrierung abschließen", Exact = true }).ClickAsync();
        await page.Locator(".success-message").WaitForAsync();

        await page.GotoAsync("/Membership/Login");
        await page.Locator("#Input_Email").FillAsync(email);
        await page.Locator("#Input_Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(host.BaseUrl + "/Membership/Profile");

        await page.Locator("#Input_Phone").FillAsync("01234 567890");
        await page.Locator("#Input_City").FillAsync("Neue Teststadt");
        await page.GetByRole(AriaRole.Button, new() { Name = "Speichern", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(host.BaseUrl + "/Membership/Profile");
        Assert.Contains("Profil erfolgreich aktualisiert.", await page.Locator("main").InnerTextAsync());
        Assert.Equal("Neue Teststadt", await page.Locator("#Input_City").InputValueAsync());

        await page.Locator("form").GetByRole(AriaRole.Link, new() { Name = "Abmelden", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(host.BaseUrl + "/Membership/Login");
        await page.GotoAsync("/Membership/Profile");
        Assert.StartsWith(host.BaseUrl + "/Membership/Login", page.Url);
    }
}
