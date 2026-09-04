using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Protos.AdminOps;

namespace OpsConsole.UnitTests.TestHelpers;

// Spins up the real OpsConsole host (real ApiKeyMiddleware, real JwtBearer + OpsViewer/
// OpsAdmin policies, real endpoint routing) with only the three outbound gRPC clients
// swapped for NSubstitute fakes — everything else in Program.cs runs exactly as it does
// in production, so a test proves the actual auth pipeline, not a stand-in for it.
public sealed class OpsConsoleWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminApiKey = "test-admin-api-key";

    // Program.cs reads Jwt:PublicKeyBase64/Audience/Issuer into local variables at the top
    // of the script, before builder.Build() runs. Tests mint tokens with the dev RSA PRIVATE
    // key below; the host validates them with the matching PUBLIC key from
    // appsettings.Development.json — same verify-only split as production.
    public static readonly (string PrivateKeyBase64, string Audience, string Issuer) JwtDevConfig = LoadJwtDevConfig();

    private const string DevSigningPrivateKeyBase64 =
        "LS0tLS1CRUdJTiBQUklWQVRFIEtFWS0tLS0tCk1JSUV2UUlCQURBTkJna3Foa2lHOXcwQkFRRUZBQVNDQktjd2dnU2pBZ0VBQW9JQkFRQ2hYa2U3VHl3U2NjYm4KdDhkK21IbEZxS3pqaDdLNmJkbTZ1Z1N5b1BQRWlCWWFJREo0NEdyTjNFYkYrV0l2Z0gwUml6cFR5bUFxd1lXLwpkL1EzNk83Y2VocWNyaGpQL2RkRXN5UU5FSTNtQjhod1A3NXZGSGNxcitoNTQ2L1ZZbmVwMTRkWEN3aU9iME9UCnJLMkgwZndOWnRhQ2Qxczd0aGZJUG91NUIvWThnT3IwYkR3cXlwVkR3YStxbnRVbG1XOHV6SGFHSnR0NnBtajUKYXVQdTE4endvTXRyTm5wckZKUHVyVGVMTC9NSEdXa29vNTJlM1p3djF4NkRSNGdGZ2M4L2hvbTdxNStwWTVPVQpJeldaWlBVckhhdTF3Z0MvWUlnNnJudFE4ZHhOeWtGOVpXQ25vRkZBVW5URnhDQ0F2a0tLaUtKZmhwZlpKVmpDCk16TWwwSTl6QWdNQkFBRUNnZ0VBSU8xNUc2cWJKcVJhM3h1c05KUHVZeDE1TWZDVnN0OEpoOFcvZ2FmQU5rRkMKcVZBYW5IbkdzWDBhWC9sMFpKY0dibGNIcnVOajNqV2hFaUhyRHFHVVpCN3lZVGhSVGRmUlhtNWprOXJsNmFONgo3aFREeWl6VjZEcis2Q2hpejlzSTZmcFYzcGdjeGR2RVlWVGlFQTMwTGRQblA3WVZRc2owYjJMNzVlVFBCU2RCCjU0MGEybUFRZkhTUGhzSXduQzNsN3hRVHVGZXI5SWNjL0hpZ3h4ZURUTm5NSFRKUGlDTzVjK1BUUWdxSXBPUUIKeVhrbm5tTzFyOUNOZ0MxY3g4R1hMQldydDI2MlM5ckRLSHk1UysxQUkzTk9adW0xUXlJNWNvM3dsT3crelhjRgoxZlR0QndkTlFZajV4eFlFZTkzMUM5SG9vcGRPQ2RjMlZYZXhWSUUwV1FLQmdRRGhPa29UbktVdmFXOCs1NkdyCmxKcjRobU5IZlJzOS8vNitGTnhEUzQ1LzFCSnVMSU8vcTdydGZWdmpVRVJNOG1Ua3JDWi9JcGVlZ1QwWVo4Q0gKekhLazlPOUxNOE1RUXgrYXRLdXphOEE4aXB1Q01LZFVlYkFkbkZxNldpR1l6cUh1YmZMdEt3U1JFZ3h2RW1UWAovK1EyRjVrUGtsd2dtYjhDU3FGbFBSOVRCd0tCZ1FDM2FtY0hBZWZiQ1YvdnlmRjZZWnJ2eHNDZEtFS2JnT25WCjBja0NUNkFxTlBGRzdKckpad1R1U3p5aDd6SFFaZVhDZGxoT0pDYlRHUTB6bUxtN0UyK2czcUNEWjlYcmhpMEUKOTIrcCs3OEZ3cE1XWkU5ZTBvMnk4ZlNxNmZqTGRNa0xNcDRQTFpSZ0RDZjdHQ0JBM2hIbzRxeTNtcHc0alNkdQpMQnR0ZG1YcE5RS0JnQzZkWGMrSlVEYnIzM1pwZ25COHBVWmlxaEdWdHhteDdndHhUZFV2d2lKNnhnVy9lTlVtCnVkMkZZSXMvaGFOWFY4SnNUdHRwVVhBZzE0QkJtUHVDT1FnakdaTzY5dGhhekNPODJQeWRoSUFEUUFSR0JadmEKUTdVZE16bjJoWldXenJVR1ZJejVwa3hRSy9xaEYvWU1wREw5MTFQOXVzdVVoby8yMmtpVnlmSHBBb0dBWG83cApmTEJiMHcyN093azJpQ3huenpQOU8waDFSbXdvb1laZEJlYjlJS1ZZdW9MaXJmQ0JsMFNNaHNPbFA5WTRwSStVCnFQeDBVNkozcnVFTzU4WjJaMDQvSEYvYzVtYXZNUDlMdnl1OWFIL09pdDIrR1ptZFdlTHBpMi9DUjBuM0YrSEoKb1BPVHFneTZVL1kxTXB3S1NiRUs4RUV5UnVsbXFhTHRwUHBFUWYwQ2dZRUFueXVWcTJmN3h6akZoMWNheTNuQQpzaVdvcm4ydHl0dGNSd1MxaVJXa2dVRUdxWnc3Rm5GZjc0YnB1dXRWL3dGYnZSVWUxU1FFR1p6NzFESmlpNmZhCllTeFFVMk9PRFlDSVpqVVJPT2htZ29GbHBJN2hrSU9jWVdKYlhIMmRlN0ZkamRtZDRacG95WjAvR2tNTVV0S1QKMWZKSEptSjVlZ29HMFlxRFhOa1pvUXM9Ci0tLS0tRU5EIFBSSVZBVEUgS0VZLS0tLS0K";

    private static (string PrivateKeyBase64, string Audience, string Issuer) LoadJwtDevConfig([CallerFilePath] string thisFile = "")
    {
        var opsConsoleDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(opsConsoleDir, "appsettings.Development.json"), optional: false)
            .Build();

        return (DevSigningPrivateKeyBase64, config["Jwt:Audience"]!, config["Jwt:Issuer"]!);
    }

    public AdminOpsService.AdminOpsServiceClient OrderClient { get; } =
        Substitute.For<AdminOpsService.AdminOpsServiceClient>();

    public AdminPaymentService.AdminPaymentServiceClient PaymentClient { get; } =
        Substitute.For<AdminPaymentService.AdminPaymentServiceClient>();

    public AdminInventoryService.AdminInventoryServiceClient InventoryClient { get; } =
        Substitute.For<AdminInventoryService.AdminInventoryServiceClient>();

    public AdminAccountingService.AdminAccountingServiceClient AccountingClient { get; } =
        Substitute.For<AdminAccountingService.AdminAccountingServiceClient>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // AdminApiKey and InternalServices:OpsConsoleApiKey are read live from
            // IConfiguration at request time, so overriding them here works fine. Jwt:*
            // is deliberately left untouched — see JwtDevConfig above for why.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminApiKey"] = AdminApiKey,
                ["InternalServices:OpsConsoleApiKey"] = "test-internal-key"
            });
        });

        builder.ConfigureServices(services =>
        {
            ReplaceSingleton(services, OrderClient);
            ReplaceSingleton(services, PaymentClient);
            ReplaceSingleton(services, InventoryClient);
            ReplaceSingleton(services, AccountingClient);
        });
    }

    private static void ReplaceSingleton<T>(IServiceCollection services, T instance) where T : class
    {
        services.RemoveAll(typeof(T));
        services.AddSingleton(instance);
    }

    public HttpClient CreateAuthorizedClient(params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTokenFactory.CreateToken(JwtDevConfig.PrivateKeyBase64, JwtDevConfig.Audience, JwtDevConfig.Issuer, roles));
        return client;
    }

    public HttpClient CreateClientWithoutApiKey(params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTokenFactory.CreateToken(JwtDevConfig.PrivateKeyBase64, JwtDevConfig.Audience, JwtDevConfig.Issuer, roles));
        return client;
    }

    public HttpClient CreateClientWithJwtIssuer(string issuer, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTokenFactory.CreateToken(JwtDevConfig.PrivateKeyBase64, JwtDevConfig.Audience, issuer, roles));
        return client;
    }

    public HttpClient CreateClientWithoutJwt()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", AdminApiKey);
        return client;
    }
}
