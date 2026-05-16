import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { render, screen, fireEvent } from '@testing-library/angular';
import { describe, expect, it } from 'vitest';
import { correlationIdInterceptor } from '../../core/correlation-id.interceptor';
import { httpTokenInterceptor } from '../../core/http-token.interceptor';
import { LoginComponent } from './login.component';

async function setup() {
  const r = await render(LoginComponent, {
    providers: [
      provideHttpClient(
        withInterceptors([correlationIdInterceptor, httpTokenInterceptor]),
      ),
      provideHttpClientTesting(),
      provideRouter([{ path: '**', component: LoginComponent }]),
    ],
  });
  const ctrl = r.fixture.debugElement.injector.get(HttpTestingController);
  return { ...r, ctrl };
}

describe('LoginComponent', () => {
  it('renders the form', async () => {
    await setup();
    expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument();
    expect(screen.getByTestId('usernameOrEmail')).toBeInTheDocument();
    expect(screen.getByTestId('password')).toBeInTheDocument();
  });

  it('disables submit while the form is invalid', async () => {
    await setup();
    const submit = screen.getByTestId('submit') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
  });

  it('enables submit when both fields are filled', async () => {
    await setup();
    fireEvent.input(screen.getByTestId('usernameOrEmail'), { target: { value: 'alice' } });
    fireEvent.input(screen.getByTestId('password'), { target: { value: 'hunter2hunter' } });
    const submit = screen.getByTestId('submit') as HTMLButtonElement;
    expect(submit.disabled).toBe(false);
  });

  it('disables submit and shows the "submitting" label while the request is in flight', async () => {
    const { ctrl, fixture } = await setup();

    fireEvent.input(screen.getByTestId('usernameOrEmail'), { target: { value: 'alice' } });
    fireEvent.input(screen.getByTestId('password'), { target: { value: 'hunter2hunter' } });

    const submit = screen.getByTestId('submit') as HTMLButtonElement;
    fireEvent.click(submit);
    await fixture.whenStable();
    fixture.detectChanges();

    // The login request is queued; do NOT flush it.
    const loginReq = ctrl.expectOne('/api/v1/auth/login');
    expect(submit.disabled).toBe(true);
    expect(submit.textContent ?? '').toMatch(/signing in/i);

    // Let the in-flight request resolve cleanly so afterEach.verify is happy.
    loginReq.flush({}, { status: 500, statusText: 'fail' });
    ctrl.verify();
  });

  it('shows an error message on 401', async () => {
    const { ctrl } = await setup();

    fireEvent.input(screen.getByTestId('usernameOrEmail'), { target: { value: 'alice' } });
    fireEvent.input(screen.getByTestId('password'), { target: { value: 'wrongpassword' } });

    fireEvent.click(screen.getByTestId('submit'));

    const loginReq = ctrl.expectOne('/api/v1/auth/login');
    loginReq.flush({ code: 'InvalidCredentials' }, { status: 401, statusText: 'Unauthorized' });

    expect(await screen.findByRole('alert')).toHaveTextContent(/invalid/i);
    ctrl.verify();
  });
});
