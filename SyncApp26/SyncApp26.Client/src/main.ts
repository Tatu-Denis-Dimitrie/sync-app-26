import '@angular/compiler';
import { bootstrapApplication } from '@angular/platform-browser';
import { broadcastResponseToMainFrame } from '@azure/msal-browser/redirect-bridge';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { MSAL_REDIRECT_PATH } from './app/auth/msal-redirect-path';

// MSAL v5 popups deliver their auth response over a BroadcastChannel, not by having
// the opener read the popup's URL. The redirect page must therefore run MSAL's bridge,
// which parses the response out of the URL and posts it to the main frame. Angular is
// deliberately not bootstrapped here: the router would rewrite the URL and destroy the
// response before the bridge could read it.
if (window.location.pathname === MSAL_REDIRECT_PATH) {
  broadcastResponseToMainFrame().catch((err) => console.error(err));
} else {
  bootstrapApplication(AppComponent, appConfig)
    .catch((err) => console.error(err));
}
