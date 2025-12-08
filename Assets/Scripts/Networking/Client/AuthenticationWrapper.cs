using System.Threading.Tasks;
using Unity.Services.Authentication;
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
    public static AuthState AuthState { get; private set; } = AuthState.NotAuthenticated;
    
    public static async Task<AuthState> DoAuth(int maxTries = 5)
    {
        // If already authenticated, just return current state
        if (AuthState == AuthState.Authenticated)
            return AuthState;

        AuthState = AuthState.Authenticating;
        int tries = 0;
        while (AuthState == AuthState.Authenticating && tries < maxTries)
        { 
            // Try to sign in anonymously
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

            if (AuthenticationService.Instance.IsSignedIn &&
            AuthenticationService.Instance.IsAuthorized)
            {
                AuthState = AuthState.Authenticated;
                break;
            }
            tries++;
            await Task.Delay(1000); // wait 1 second before retry
        }
        // For now, if we exit the loop without success, we just
        // return whatever AuthState is (could still be Authenticating).
        // In a later lecture, error/timeout handling will be improved.
        return AuthState;
    }
}