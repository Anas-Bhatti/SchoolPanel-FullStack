// src/main.ts
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig }            from './app/app.config';
import { AppComponent }         from './app/app.component';

bootstrapApplication(AppComponent, appConfig).catch(err => {
  console.error('[SchoolPanel] Bootstrap failed:', err);
  document.body.innerHTML = `
    <div style="
      display:flex; align-items:center; justify-content:center;
      height:100vh; background:#0F172A; color:#F1F5F9;
      flex-direction:column; gap:14px; font-family:sans-serif;
    ">
      <h2 style="margin:0;font-size:20px">SchoolPanel failed to start</h2>
      <p  style="margin:0;color:#94A3B8;font-size:14px">
        Please refresh the page. If the problem persists, contact your administrator.
      </p>
      <button onclick="location.reload()" style="
        margin-top:8px; padding:10px 22px;
        background:#2563EB; color:#fff;
        border:none; border-radius:8px; cursor:pointer;
        font-size:14px; font-weight:500;
      ">
        Refresh Page
      </button>
    </div>`;
});