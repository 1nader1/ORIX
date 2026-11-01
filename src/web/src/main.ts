import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { StudioComponent } from './app/studio/studio.component';

bootstrapApplication(StudioComponent, appConfig).catch((err) => console.error(err));
