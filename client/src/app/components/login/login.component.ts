import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './login.component.html',
})
export class LoginComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected isRegistering = signal(false);
  protected loading = signal(false);
  protected errorMessage = signal<string | null>(null);
  protected successMessage = signal<string | null>(null);

  protected name = '';
  protected email = '';
  protected password = '';

  constructor() {
    if (this.auth.isLoggedIn()) {
      this.router.navigate(['/']);
    }
  }

  protected toggleMode(): void {
    this.isRegistering.update(v => !v);
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  protected async submit(): Promise<void> {
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.loading.set(true);

    if (this.isRegistering()) {
      const { error } = await this.auth.signUp(this.email, this.password, this.name);
      this.loading.set(false);

      if (error) {
        this.errorMessage.set(error);
        return;
      }

      this.successMessage.set('Sjekk e-posten din for bekreftelseslenke!');
      this.isRegistering.set(false);
    } else {
      const { error } = await this.auth.signInWithEmail(this.email, this.password);
      this.loading.set(false);

      if (error) {
        this.errorMessage.set(error);
        return;
      }

      this.router.navigate(['/']);
    }
  }
}
