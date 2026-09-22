/** File purpose: Bootstraps Angular's root module and reports browser startup failures. */
import { platformBrowserDynamic } from '@angular/platform-browser-dynamic';
import { AppModule } from './app/app.module';

platformBrowserDynamic().bootstrapModule(AppModule).catch(error => console.error(error));
