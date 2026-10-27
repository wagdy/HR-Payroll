import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { homePath } from '../../core/auth/guards';
import { errorMessage } from '../../core/http/error-message';

// A user's email and password: a manager or an accountant. Employees sign in to their own
// portal instead, with their mobile number and PIN.
@Component({
  selector: 'app-sign-in-page',
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, RouterLink],
  templateUrl: './sign-in-page.html',
  styleUrl: './sign-in-page.scss',
})
export class SignInPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  // ?reason=expired, when the API stopped accepting the last session.
  readonly reason = input<string>();

  protected email = '';
  protected password = '';
  protected readonly showPassword = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    if (this.busy()) {
      return;
    }
    if (!this.email.trim() || !this.password) {
      this.error.set('Enter your email and your password.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.signIn(this.email.trim(), this.password);
      await this.router.navigate([homePath]);
    } catch (error) {
      // Not found: this is the employee portal's own port (on the Wi-Fi), where no user signs
      // in. Its sign-in, then.
      if (error instanceof HttpErrorResponse && error.status === 404) {
        void this.router.navigate(['/portal']);
        return;
      }
      this.error.set(errorMessage(error, 'Sign-in failed.'));
      this.password = '';
    } finally {
      this.busy.set(false);
    }
  }
}
