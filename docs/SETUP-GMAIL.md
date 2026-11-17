# Gmail OAuth setup

LeadPilot sends approved emails from your own Gmail via the official Gmail API with the minimum scope **`gmail.send`** (plus `openid email` to display which account is connected). It cannot read your mailbox.

## Google Cloud console
1. Same project as Places (or a new one). APIs & Services → Library → enable **Gmail API**.
2. **OAuth consent screen**: User type *External*; app name "LeadPilot AI"; your support email; authorised domain = your API domain; scopes: `.../auth/gmail.send`, `openid`, `email`.
   - While in **Testing** status, add your Gmail as a *Test user*. Note: refresh tokens for apps in Testing expire after ~7 days, so you would reconnect weekly.
   - For long-lived tokens, **publish** the app. `gmail.send` is a *sensitive* scope; Google may require verification (privacy policy URL, app homepage, demo video). For a personal single-user tool, staying in Testing and reconnecting weekly is the pragmatic option until verified. Check Google's current requirements.
3. Credentials → Create credentials → **OAuth client ID** → type **Web application**.
   - Authorised redirect URI: `https://api.yourdomain.in/api/v1/integrations/google/callback` (must match exactly).
4. Server `.env`:
```
GoogleOAuth__ClientId=xxxx.apps.googleusercontent.com
GoogleOAuth__ClientSecret=GOCSPX-...
GoogleOAuth__RedirectUri=https://api.yourdomain.in/api/v1/integrations/google/callback
```
5. Restart api/worker. In the app: More → Integrations → **Connect Gmail** → sign in → approve → *Return to LeadPilot AI*.

## Security properties
- Authorization-code flow with **PKCE (S256)** and a single-use, hashed, 10-minute **state**; redirect URI validated.
- Refresh token encrypted at rest (ASP.NET Data Protection; keys in the `dpkeys` volume, outside the DB), never sent to the app.
- Access tokens kept only in server memory; expired/revoked refresh tokens mark the account *Revoked* and prompt reconnection.
- Disconnect revokes the token at Google and deletes it locally.

## Test mode
`Email__TestMode=true` (default) runs the full approval/outbox workflow but never calls Gmail. Turn it off only after testing.
