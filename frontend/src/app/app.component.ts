// src/app/app.component.ts
import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector:   'sp-root',
  standalone: true,
  imports:    [RouterOutlet],
  template:   `<router-outlet />`,
  styles:     [`:host { display: block; }`]
})
export class AppComponent {}