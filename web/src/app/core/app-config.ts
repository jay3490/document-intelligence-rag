import { InjectionToken } from '@angular/core';

/** Settings read at start-up from `config.json`, so one build can target any API. */
export interface AppConfig {
  /** Origin of the API, for example `http://localhost:5080`. Empty means same origin. */
  apiBaseUrl: string;
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');

const defaults: AppConfig = { apiBaseUrl: '' };

/** Loads `config.json`; a missing or unreadable file falls back to same-origin requests. */
export async function loadAppConfig(url = 'config.json'): Promise<AppConfig> {
  try {
    const response = await fetch(url, { cache: 'no-store' });
    if (!response.ok) {
      return defaults;
    }
    const config = (await response.json()) as Partial<AppConfig>;
    return { apiBaseUrl: (config.apiBaseUrl ?? '').replace(/\/+$/, '') };
  } catch {
    return defaults;
  }
}
