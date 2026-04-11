import { Component, inject } from '@angular/core';
import { NotificationService } from '../../services/notification.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  templateUrl: './toast-container.component.html',
})
export class ToastContainerComponent {
  protected readonly notifications = inject(NotificationService);

  protected getTypeClasses(type: string): string {
    switch (type) {
      case 'error':
        return 'border-red-500/30 bg-red-500/10 text-red-300';
      case 'warning':
        return 'border-gold/30 bg-gold/10 text-gold';
      case 'success':
        return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300';
      default:
        return 'border-white/10 bg-white/5 text-cream';
    }
  }

  protected getIconPath(type: string): string {
    switch (type) {
      case 'error':
        return 'M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z';
      case 'warning':
        return 'M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z';
      case 'success':
        return 'M9 12.75 11.25 15 15 9.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z';
      default:
        return 'M11.25 11.25l.041-.02a.75.75 0 0 1 1.063.852l-.708 2.836a.75.75 0 0 0 1.063.853l.041-.021M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9-3.75h.008v.008H12V8.25Z';
    }
  }
}
