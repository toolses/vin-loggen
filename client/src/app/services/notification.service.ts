import { Injectable, signal } from '@angular/core';

export type ToastType = 'success' | 'error' | 'warning' | 'info';

export interface Toast {
  id: number;
  message: string;
  type: ToastType;
}

const ERROR_MESSAGES: Record<string, string> = {
  ImageUnreadable: 'Kunne ikke lese etiketten. Prøv å ta et nytt bilde med bedre lys.',
  QuotaExceeded: 'Dagens kvote for Pro-analyse er nådd.',
  ExternalServiceDown: 'Vindatabasen er midlertidig utilgjengelig, viser basisinformasjon.',
  Unauthorized: 'Du er ikke logget inn. Logg inn og prøv igjen.',
  UnknownError: 'Noe gikk galt. Prøv igjen senere.',
};

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 0;
  private readonly _toasts = signal<Toast[]>([]);
  readonly toasts = this._toasts.asReadonly();

  show(message: string, type: ToastType = 'info', durationMs = 4000): void {
    const id = this.nextId++;
    this._toasts.update(list => [...list, { id, message, type }]);
    setTimeout(() => this.dismiss(id), durationMs);
  }

  dismiss(id: number): void {
    this._toasts.update(list => list.filter(t => t.id !== id));
  }

  success(message: string): void {
    this.show(message, 'success', 3000);
  }

  error(message: string): void {
    this.show(message, 'error', 6000);
  }

  warning(message: string): void {
    this.show(message, 'warning', 5000);
  }

  showApiError(errorCode: string): void {
    const message = ERROR_MESSAGES[errorCode] ?? ERROR_MESSAGES['UnknownError'];
    this.error(message);
  }
}
