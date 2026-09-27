import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SelectFilterComponent } from './shared/components/select-filter/select-filter.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, SelectFilterComponent],
  template: '<router-outlet></router-outlet><app-select-filter></app-select-filter>'
})
export class AppComponent {}
