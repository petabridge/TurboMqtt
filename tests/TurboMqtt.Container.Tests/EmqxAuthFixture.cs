// -----------------------------------------------------------------------
// <copyright file="EmqxAuthFixture.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using TestContainers.Emqx;

namespace TurboMqtt.Container.Tests;

[CollectionDefinition(nameof(EmqxAuthCollection))]
public class EmqxAuthCollection : ICollectionFixture<EmqxAuthFixture>
{
    // Collection fixture for EMQX tests that require username/password authentication.
}

/// <summary>
/// TestContainers fixture that starts EMQX with anonymous access disabled and
/// configures a built-in database authenticator with a known test credential.
///
/// EMQX 5.x uses API key authentication for the management REST API (not dashboard
/// credentials). The API key is bootstrapped via a file bind-mounted into the container
/// before startup, using the EMQX_MANAGEMENT__API_KEY__BOOTSTRAP_FILE env var.
/// File format: {AppID}:{ApiKey}:{ApiSecret}  (one entry per line)
/// HTTP Basic auth: Authorization: Basic base64({ApiKey}:{ApiSecret})
/// </summary>
public class EmqxAuthFixture : IAsyncLifetime
{
    public const string ValidUserName = "mqtt-user";
    public const string ValidPassword = "mqtt-password";

    // API key bootstrapped at container start for management API access.
    // EMQX 5.5.1 bootstrap file format (per-line): {ApiKey}:{ApiSecret}:{Role}
    // HTTP Basic auth: Authorization: Basic base64({ApiKey}:{ApiSecret})
    private const string ApiKey = "emqx-test-api-key";
    private const string ApiSecret = "emqx-test-api-secret-1234";
    private const string ApiBootstrapContainerPath = "/tmp/api_bootstrap.txt";

    public readonly EmqxContainer Container;
    private readonly string _apiBootstrapHostPath;

    public EmqxAuthFixture()
    {
        // Create a temp file on the host with the API key bootstrap content.
        // WithBindMount requires the host path to exist at builder time.
        // EMQX 5.5.1 bootstrap file format (per-line): {ApiKey}:{ApiSecret}:{Role}
        _apiBootstrapHostPath = Path.GetTempFileName();
        File.WriteAllText(_apiBootstrapHostPath, $"{ApiKey}:{ApiSecret}:administrator\n");

        Container = new EmqxBuilder()
            .WithEnvironment("EMQX_SESSION__UPGRADE_QOS", "true")
            // Disable anonymous MQTT connections so authentication is enforced.
            .WithEnvironment("EMQX_MQTT__ALLOW_ANONYMOUS", "false")
            // Bootstrap the management API key so HTTP calls can authenticate.
            // Root-level config path: [api_key, bootstrap_file] → env var EMQX_API_KEY__BOOTSTRAP_FILE
            .WithEnvironment("EMQX_API_KEY__BOOTSTRAP_FILE", ApiBootstrapContainerPath)
            .WithBindMount(_apiBootstrapHostPath, ApiBootstrapContainerPath)
            .Build();
    }

    public int MqttPort => Container.BrokerTcpPort;

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        await SetupAuthenticationAsync();
    }

    /// <summary>
    /// After EMQX starts, uses the management HTTP API to:
    /// 1. Create a built-in database password authenticator.
    /// 2. Add the test user.
    ///
    /// The allow_anonymous=false env var ensures unauthenticated connections are rejected.
    /// </summary>
    private async Task SetupAuthenticationAsync()
    {
        var baseUrl = $"http://localhost:{Container.BrokerDashboardPort}/api/v5";
        // EMQX 5.x management API: Basic auth with ApiKey:ApiSecret (not dashboard credentials)
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ApiKey}:{ApiSecret}"));

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        // Step 1: Create the built-in database authenticator.
        // sha256 + suffix-salt is the EMQX 5.x default; EMQX handles hashing transparently.
        var createAuthResp = await http.PostAsJsonAsync(
            $"{baseUrl}/authentication",
            new
            {
                mechanism = "password_based",
                backend = "built_in_database",
                password_hash_algorithm = new { name = "sha256", salt_position = "suffix" },
                user_id_type = "username",
                enable = true
            });

        // 200/201 = created; 409 = already exists — all acceptable.
        if (!createAuthResp.IsSuccessStatusCode && (int)createAuthResp.StatusCode != 409)
        {
            var body = await createAuthResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Failed to create EMQX built-in database authenticator: HTTP {createAuthResp.StatusCode} — {body}");
        }

        // Step 2: Add the test user to the built-in database.
        var addUserResp = await http.PostAsJsonAsync(
            $"{baseUrl}/authentication/password_based:built_in_database/users",
            new { user_id = ValidUserName, password = ValidPassword, is_superuser = false });

        if (!addUserResp.IsSuccessStatusCode && (int)addUserResp.StatusCode != 409)
        {
            var body = await addUserResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Failed to add EMQX test user '{ValidUserName}': HTTP {addUserResp.StatusCode} — {body}");
        }
    }

    public async Task DisposeAsync()
    {
        await Container.StopAsync();
        await Container.DisposeAsync();

        // Clean up the temp bootstrap file.
        try { File.Delete(_apiBootstrapHostPath); } catch { /* best effort */ }
    }
}
