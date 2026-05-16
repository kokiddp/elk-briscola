import { Component, inject } from '@angular/core';
import { ErrorToastService } from '../core/error-toast.service';

@Component({
  selector: 'bri-toast-host',
  standalone: true,
  imports: [],
  templateUrl: './toast-host.component.html',
  styleUrl: './toast-host.component.scss',
})
export class ToastHostComponent {
  private readonly service = inject(ErrorToastService);

  readonly toasts = this.service.toasts;

  dismiss(id: number): void {
    this.service.dismiss(id);
  }
}
