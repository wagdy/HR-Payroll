import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { SignInPage } from './sign-in-page';

const text = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();

describe('signing in with an email and a password', () => {
  const signIn = vi.fn();

  async function render() {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { employeeActing: () => false, me: () => null, signIn, can: () => true } }],
    });
    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const type = (name: string, value: string) => {
      const input = el.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const send = async () => {
      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      await fixture.whenStable();
      fixture.detectChanges();
      // ngModel writes a field's new value on the turn after.
      await fixture.whenStable();
    };
    return { el, type, send };
  }

  // A block: a function handed back from beforeEach would be called as its teardown.
  beforeEach(() => {
    signIn.mockReset().mockResolvedValue(undefined);
  });

  it('signs in with what was typed, and goes to HR', async () => {
    const { type, send } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    type('email', '  omar@example.com ');
    type('password', 'change-me-now');
    await send();

    expect(signIn).toHaveBeenCalledWith('omar@example.com', 'change-me-now');
    expect(navigate).toHaveBeenCalledWith(['/hr']);
  });

  it('asks for both before sending anything', async () => {
    const { el, type, send } = await render();

    type('email', 'omar@example.com');
    await send();

    expect(signIn).not.toHaveBeenCalled();
    expect(text(el.querySelector('.error')!)).toBe('Enter your email and your password.');
  });

  it('says what the API said when refused, and clears the password', async () => {
    signIn.mockRejectedValue(new HttpErrorResponse({ status: 401, error: { title: 'Sign-in failed.', detail: 'Wrong email or password.' } }));
    const { el, type, send } = await render();

    type('email', 'omar@example.com');
    type('password', 'wrong-one');
    await send();

    expect(text(el.querySelector('.error')!)).toContain('Wrong email or password.');
    expect(el.querySelector<HTMLInputElement>('input[name="password"]')!.value).toBe('');
  });

  // On the portal's own port no user signs in: the API is not found there.
  it('opens the portal sign-in where the API is not found', async () => {
    signIn.mockRejectedValue(new HttpErrorResponse({ status: 404 }));
    const { type, send } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    type('email', 'omar@example.com');
    type('password', 'change-me-now');
    await send();

    expect(navigate).toHaveBeenCalledWith(['/portal']);
  });
});
