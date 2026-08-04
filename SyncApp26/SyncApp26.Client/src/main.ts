import '@angular/compiler';
import { bootstrapApplication } from '@angular/platform-browser';
import { broadcastResponseToMainFrame } from '@azure/msal-browser/redirect-bridge';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { MSAL_REDIRECT_PATH } from './app/auth/msal-redirect-path';

// MSAL popups need this page to run the bridge, which forwards the auth response to the
// main frame. Angular must not bootstrap here - the router would rewrite the URL first.
if (window.location.pathname === MSAL_REDIRECT_PATH) {
  broadcastResponseToMainFrame().catch((err) => console.error(err));
} else {
  bootstrapApplication(AppComponent, appConfig)
    .catch((err) => console.error(err));
}
