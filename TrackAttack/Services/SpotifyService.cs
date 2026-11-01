using System.Diagnostics;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using TrackAttack.Settings;

namespace TrackAttack.Services;

public static class SpotifyService
{
    private const int CallbackPort = 5543;
    private static PKCEAuthenticator? _authenticator;

    public static SpotifyClient? Client { get; private set; }
    public static bool IsConnected => Client is not null;
    public static string? DisplayName { get; private set; }

    public static async Task<bool> TryReconnectAsync()
    {
        var settings = AppSettings.Load();
        if (string.IsNullOrWhiteSpace(settings.SpotifyClientId)
            || string.IsNullOrWhiteSpace(settings.SpotifyRefreshToken))
            return false;

        var token = await new OAuthClient().RequestToken(
            new PKCETokenRefreshRequest(settings.SpotifyClientId, settings.SpotifyRefreshToken));
        await SetupClientAsync(settings.SpotifyClientId, token);
        settings.SpotifyRefreshToken = token.RefreshToken ?? settings.SpotifyRefreshToken;
        settings.SpotifyDisplayName = DisplayName ?? settings.SpotifyDisplayName;
        settings.Save();
        return true;
    }

    public static async Task ConnectWithBrowserAsync(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Enter a Spotify client ID.", nameof(clientId));

        var redirectUri = new Uri($"http://127.0.0.1:{CallbackPort}/callback");
        var (verifier, challenge) = PKCEUtil.GenerateCodes();
        var tokenCompletion = new TaskCompletionSource<PKCETokenResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var server = new EmbedIOAuthServer(redirectUri, CallbackPort);
        await server.Start();

        server.AuthorizationCodeReceived += async (_, response) =>
        {
            await server.Stop();
            try
            {
                var token = await new OAuthClient().RequestToken(
                    new PKCETokenRequest(clientId, response.Code, redirectUri, verifier));
                tokenCompletion.TrySetResult(token);
            }
            catch (Exception exception)
            {
                tokenCompletion.TrySetException(exception);
            }
        };
        server.ErrorReceived += (_, error, _) =>
        {
            tokenCompletion.TrySetException(new InvalidOperationException(error));
            return Task.CompletedTask;
        };

        var loginRequest = new LoginRequest(server.BaseUri, clientId, LoginRequest.ResponseType.Code)
        {
            CodeChallengeMethod = "S256",
            CodeChallenge = challenge,
            Scope =
            [
                Scopes.PlaylistReadPrivate,
                Scopes.PlaylistReadCollaborative,
                Scopes.UserReadPrivate,
                Scopes.UserModifyPlaybackState,
                Scopes.UserReadPlaybackState
            ]
        };

        Process.Start(new ProcessStartInfo
        {
            FileName = loginRequest.ToUri().ToString(),
            UseShellExecute = true
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var responseToken = await tokenCompletion.Task.WaitAsync(timeout.Token);
        await SetupClientAsync(clientId, responseToken);

        var updatedSettings = AppSettings.Load();
        updatedSettings.SpotifyClientId = clientId.Trim();
        updatedSettings.SpotifyRefreshToken = responseToken.RefreshToken ?? string.Empty;
        updatedSettings.SpotifyDisplayName = DisplayName ?? string.Empty;
        updatedSettings.Save();
    }

    public static void Disconnect()
    {
        Client = null;
        _authenticator = null;
        DisplayName = null;

        var settings = AppSettings.Load();
        settings.SpotifyRefreshToken = string.Empty;
        settings.SpotifyDisplayName = string.Empty;
        settings.Save();
    }

    private static async Task SetupClientAsync(string clientId, PKCETokenResponse token)
    {
        var authenticator = new PKCEAuthenticator(clientId, token);
        authenticator.TokenRefreshed += (_, refreshed) =>
        {
            var settings = AppSettings.Load();
            settings.SpotifyRefreshToken = refreshed.RefreshToken ?? settings.SpotifyRefreshToken;
            settings.Save();
        };

        var config = SpotifyClientConfig.CreateDefault().WithAuthenticator(authenticator);
        var client = new SpotifyClient(config);
        var profile = await client.UserProfile.Current();

        _authenticator = authenticator;
        Client = client;
        DisplayName = profile.DisplayName ?? profile.Id;
    }
}
