using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
public enum AuthState
{
NotAuthenticated,
Authenticating,
Authenticated,
Error,
Timeout
}

public static class AuthenticationWrapper
{
    public static AuthState authState { get; private set; } = AuthState.NotAuthenticated;
    
    public static async Task<AuthState> DoAuth(int maxRetries = 5)
    {
        // If already authenticated, just return current state
        if (authState == AuthState.Authenticated)
            return authState;

        // If already authenticating, wait for the other process to finish
        if (authState == AuthState.Authenticating)
        {
            return await Authenticating();
        }

        // Start the sign-in process
        await SignInAnonymouslyAsync(maxRetries);
        return authState;
    }

    private static async Task SignInAnonymouslyAsync(int maxRetries)
    {
        // moved sign-in logic lives here
        authState = AuthState.Authenticating;
        int tries = 0;
        while (authState == AuthState.Authenticating && tries < maxRetries)
        {
            // Try to sign in anonymously
            try
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            catch (AuthenticationException authEx)
            {
                Debug.LogError(authEx);
                authState = AuthState.Error;
            }
            catch (RequestFailedException reqEx)
            {
                Debug.LogError(reqEx);
                authState = AuthState.Error;
            }

            if (AuthenticationService.Instance.IsSignedIn && AuthenticationService.Instance.IsAuthorized)
            {
                authState = AuthState.Authenticated;
                break;
            }
            tries++;
            await Task.Delay(1000); // wait 1 second before retry
        }
        if (authState != AuthState.Authenticated)
        {
            authState = AuthState.Timeout;
            Debug.LogWarning($"Player was not signed in successfully after {maxRetries} retries");
        }
    }

    private static async Task<AuthState> Authenticating()
    {
        Debug.LogWarning("Already authenticating...");
        while (authState == AuthState.Authenticating || authState == AuthState.NotAuthenticated)
        {
            await Task.Delay(200);
        }
        return authState;
    }
}